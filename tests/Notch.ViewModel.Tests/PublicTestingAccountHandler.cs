using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Notch.Core.Commerce;

internal sealed class PublicTestingAccountHandler(RSA key) : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string _device = "";
    public int LoginRequests { get; private set; }
    public int VerifyRequests { get; private set; }
    public int RestoreRequests { get; private set; }
    public bool PauseVerify { get; set; }
    public bool VerifyCancelled { get; private set; }
    public TaskCompletionSource VerifyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource VerifyRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string Proof() => EntitlementTokens.Sign(new(EntitlementTokens.Issuer, EntitlementTokens.Audience,
            "testing-account", _device, PlanTier.Free, now, now.AddHours(6), now.AddHours(30), null), key);
        if (request.RequestUri!.AbsolutePath == "/v1/auth/request")
        {
            LoginRequests++;
            return new(HttpStatusCode.Accepted);
        }
        if (request.RequestUri.AbsolutePath == "/v1/auth/verify")
        {
            VerifyRequests++;
            if (PauseVerify)
            {
                VerifyStarted.TrySetResult();
                try { await VerifyRelease.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { VerifyCancelled = true; throw; }
            }
            var input = await request.Content!.ReadFromJsonAsync<VerifyLoginRequest>(Json, cancellationToken)
                ?? throw new InvalidDataException("Missing fixture verification request.");
            _device = input.DeviceId;
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new LoginResponse("testing-session", Proof(), now)) };
        }
        if (request.RequestUri.AbsolutePath == "/v1/entitlement")
        {
            RestoreRequests++;
            if (request.Headers.Authorization?.Parameter != "testing-session")
                return new(HttpStatusCode.Unauthorized);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new EntitlementResponse(Proof(), now)) };
        }
        throw new InvalidOperationException("Unexpected account fixture request: " + request.RequestUri.AbsolutePath);
    }
}
