using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Notch.Core;
using Notch.Core.Commerce;

namespace Notch.Windows.Services;

/// <summary>Only public configuration enters the desktop. Login sessions and proofs use the OS credential vault.</summary>
public sealed class SubscriptionService
{
    private readonly HttpClient _http;
    private readonly ISecretVault _vault;
    private readonly Uri _server;
    private readonly string _publicKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private long _generation;
    private string? _session;
    private VerifiedEntitlement? _verified;
    private DateTimeOffset? _serverTime;
    private const string SessionKey = "notch-billing-session", ProofKey = "notch-billing-proof",
        DeviceKey = "notch-billing-device", TimeKey = "notch-billing-time";
    public string DeviceId { get; }
    public string? SessionToken { get { lock (_stateGate) return _session; } }
    public EntitlementDecision Current
    {
        get { lock (_stateGate) return _verified?.Evaluate(DeviceId, DateTimeOffset.UtcNow, _serverTime) ?? EntitlementDecision.Free(); }
    }

    public SubscriptionService(HttpClient http, ISecretVault vault, Uri serverBase, string publicKeyPem)
    {
        if (!IsAllowedServer(serverBase)) throw new ArgumentException("Billing requires HTTPS.", nameof(serverBase));
        if (publicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new ArgumentException("Desktop configuration must contain a public key only.", nameof(publicKeyPem));
        using var rsa = System.Security.Cryptography.RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        if (rsa.KeySize < 2048) throw new ArgumentException("Billing public key must use at least 2048 bits.", nameof(publicKeyPem));
        _http = http; _vault = vault;
        _server = new Uri(serverBase.AbsoluteUri.TrimEnd('/') + "/"); _publicKey = publicKeyPem;
        var device = vault.Read(DeviceKey);
        if (!Guid.TryParse(device, out var parsed) || parsed == Guid.Empty) { parsed = Guid.NewGuid(); vault.Save(DeviceKey, parsed.ToString("D")); }
        DeviceId = parsed.ToString("D");
        _session = vault.Read(SessionKey);
        _verified = EntitlementTokens.ReadVerified(vault.Read(ProofKey), publicKeyPem);
        _serverTime = DateTimeOffset.TryParse(vault.Read(TimeKey), out var savedTime) ? savedTime : null;
    }

    public static bool TryCreateFromConfiguration(HttpClient http, ISecretVault vault, string? serverUrl, string? publicKeyPem,
        out SubscriptionService? service, out string reason)
    {
        service = null;
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(publicKeyPem))
        { reason = "Subscriptions are not configured in this build. Free tools are available."; return false; }
        try
        {
            service = new(http, vault, new Uri(serverUrl, UriKind.Absolute), publicKeyPem);
            reason = "Subscriptions configured."; return true;
        }
        catch (Exception error) when (error is ArgumentException or UriFormatException or System.Security.Cryptography.CryptographicException)
        { reason = "Subscription configuration is invalid. Free tools are available."; return false; }
        catch (Exception error) when (IsVaultFailure(error))
        { reason = "Windows secure storage is unavailable for subscriptions. Free tools are available."; return false; }
    }

    public async Task RequestLoginAsync(string email, CancellationToken token = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "v1/auth/request", new LoginRequest(email, DeviceId), false, token);
    }
    public async Task VerifyLoginAsync(string email, string code, CancellationToken token = default)
    {
        var generation = Interlocked.Read(ref _generation);
        await _gate.WaitAsync(token);
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "v1/auth/verify", new VerifyLoginRequest(email, DeviceId, code), false, token);
            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(token)
                ?? throw new InvalidOperationException("The billing service returned no login.");
            ValidateProof(result.Entitlement, result.ServerTime);
            lock (_stateGate)
            {
                if (generation != _generation) throw new OperationCanceledException("Login was canceled by sign-out.");
                _vault.Save(SessionKey, result.SessionToken);
                _session = result.SessionToken;
                SaveProof(result.Entitlement, result.ServerTime);
            }
        }
        finally { _gate.Release(); }
    }
    public async Task RefreshAsync(CancellationToken token = default)
    {
        var generation = Interlocked.Read(ref _generation);
        await _gate.WaitAsync(token);
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "v1/entitlement", null, true, token);
            var result = await response.Content.ReadFromJsonAsync<EntitlementResponse>(token)
                ?? throw new InvalidOperationException("The billing service returned no entitlement.");
            ValidateProof(result.Entitlement, result.ServerTime);
            lock (_stateGate)
            {
                if (generation != _generation) throw new OperationCanceledException("Refresh was canceled by sign-out.");
                SaveProof(result.Entitlement, result.ServerTime);
            }
        }
        finally { _gate.Release(); }
    }
    public Task<Uri> CreateCheckoutAsync(CancellationToken token = default) => GetLinkAsync("v1/checkout", token);
    public Task<Uri> CreatePortalAsync(CancellationToken token = default) => GetLinkAsync("v1/portal", token);
    public void SignOut()
    {
        lock (_stateGate)
        {
            Interlocked.Increment(ref _generation); _session = null; _verified = null; _serverTime = null;
            Exception? failure = null;
            foreach (var name in new[] { SessionKey, ProofKey, TimeKey })
                try { _vault.Delete(name); } catch (Exception error) when (IsVaultFailure(error)) { failure ??= error; }
            if (failure is not null) throw new IOException("This device is signed out, but Windows secure storage could not remove all cached credentials.", failure);
        }
    }
    public async Task SignOutAsync(CancellationToken token = default)
    {
        var session = SessionToken;
        IOException? localFailure = null;
        try { SignOut(); } catch (IOException error) { localFailure = error; }
        if (string.IsNullOrWhiteSpace(session)) { if (localFailure is not null) throw localFailure; return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_server, "v1/auth/signout"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        using var response = await _http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("This device is signed out. Server revocation could not be confirmed.");
        if (localFailure is not null) throw localFailure;
    }
    private async Task<Uri> GetLinkAsync(string path, CancellationToken token)
    {
        using var response = await SendAsync(HttpMethod.Post, path, new { }, true, token);
        var link = await response.Content.ReadFromJsonAsync<BillingLinkResponse>(token);
        if (link is null || !Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !(uri.Host == "checkout.stripe.com" || uri.Host == "billing.stripe.com"))
            throw new InvalidOperationException("The service returned an invalid billing link.");
        return uri;
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, bool authenticated, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(method, new Uri(_server, path));
        if (body is not null) request.Content = JsonContent.Create(body);
        string? session = null;
        long requestGeneration = 0;
        if (authenticated)
        {
            lock (_stateGate) { session = _session; requestGeneration = _generation; }
            if (string.IsNullOrWhiteSpace(session)) throw new InvalidOperationException("Verify your email to restore or upgrade.");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session);
        }
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
                using var memory = new MemoryStream(); var buffer = new byte[8192];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, deadline.Token); if (read == 0) break;
                    if (memory.Length + read > 65536) throw new InvalidOperationException("The billing response exceeds the supported size.");
                    memory.Write(buffer, 0, read);
                }
                var contentType = response.Content.Headers.ContentType;
                response.Content.Dispose(); response.Content = new ByteArrayContent(memory.ToArray());
                response.Content.Headers.ContentType = contentType;
                return response;
            }
            catch { response.Dispose(); throw; }
        }
        var status = response.StatusCode;
        response.Dispose();
        if (status == HttpStatusCode.Unauthorized)
        {
            if (!authenticated) throw new InvalidOperationException("The verification code is invalid or expired. Request a new code.");
            lock (_stateGate) { if (_generation == requestGeneration && _session == session) SignOut(); }
            throw new InvalidOperationException("Your login expired. Verify your email again.");
        }
        throw new InvalidOperationException(status switch
        {
            HttpStatusCode.ServiceUnavailable => "Subscriptions are not available yet. Free tools remain available.",
            HttpStatusCode.TooManyRequests => "Too many attempts. Wait a minute and try again.",
            HttpStatusCode.Forbidden => "This action requires a current Premium subscription.",
            _ => "The billing service could not complete this request. Please try again."
        });
    }
    private void ValidateProof(string proof, DateTimeOffset serverTime)
    {
        if (string.IsNullOrWhiteSpace(proof)) throw new InvalidOperationException("The service returned no subscription proof.");
        var result = EntitlementTokens.Validate(proof, _publicKey, DeviceId, serverTime);
        // Signed Free proofs are valid too; invalid proof diagnostics do not equal an authenticated downgrade.
        if (result.Message != "Free plan" && !result.IsPremium)
            throw new InvalidOperationException("The subscription response could not be verified.");
    }
    private void SaveProof(string proof, DateTimeOffset serverTime)
    {
        try
        {
            _vault.Save(ProofKey, proof); _vault.Save(TimeKey, serverTime.ToString("O"));
            _verified = EntitlementTokens.ReadVerified(proof, _publicKey); _serverTime = serverTime;
        }
        catch (Exception error) when (IsVaultFailure(error)) { _verified = null; _serverTime = null; throw; }
    }
    private static bool IsVaultFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or System.Runtime.InteropServices.COMException or System.Security.SecurityException;
    private static bool IsAllowedServer(Uri uri) => uri.IsAbsoluteUri && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback));
}
