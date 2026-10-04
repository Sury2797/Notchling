using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Notch.Core.Commerce;

namespace Notch.Billing;

public sealed class BillingEngine(BillingOptions options, BillingStore store, IStripeBilling stripe, ILoginEmailSender email)
{
    public async Task RequestLoginAsync(LoginRequest input, CancellationToken token)
    {
        var address = NormalizeEmail(input.Email); var device = NormalizeDevice(input.DeviceId);
        await store.TransactionAsync(async state =>
        {
            var now = DateTimeOffset.UtcNow;
            state.Challenges.RemoveAll(item => item.ExpiresAt <= now);
            foreach (var old in state.LoginRequests.Where(pair => pair.Value < now.AddDays(-1)).Select(pair => pair.Key).ToArray()) state.LoginRequests.Remove(old);
            if (state.LoginRequests.TryGetValue(address, out var sentAt) && sentAt > now.AddMinutes(-1)) return false;
            var code = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);
            state.Challenges.RemoveAll(item => item.Email == address);
            state.Challenges.Add(new(address, device, CodeHash(address, device, code), now.AddMinutes(10)));
            await email.SendAsync(address, code, token);
            state.LoginRequests[address] = now;
            return true;
        }, token);
    }
    public async Task<LoginResponse> VerifyLoginAsync(VerifyLoginRequest input, CancellationToken token)
    {
        var address = NormalizeEmail(input.Email); var device = NormalizeDevice(input.DeviceId);
        if (input.Code is null || input.Code.Length != 8 || !input.Code.All(char.IsAsciiDigit)) throw new BillingAuthenticationException();
        var result = await store.TransactionAsync(async state =>
        {
            var now = DateTimeOffset.UtcNow;
            var challenge = state.Challenges.SingleOrDefault(item => item.Email == address && item.DeviceId == device);
            if (challenge is null || challenge.ExpiresAt <= now || challenge.Attempts >= 5) return null;
            var index = state.Challenges.IndexOf(challenge);
            state.Challenges[index] = challenge with { Attempts = challenge.Attempts + 1 };
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(challenge.CodeHash),
                Encoding.ASCII.GetBytes(CodeHash(address, device, input.Code)))) return null;
            state.Challenges.RemoveAt(index);
            var account = state.Accounts.SingleOrDefault(item => item.Email == address);
            if (account is null) { account = new(Guid.NewGuid().ToString("N"), address); state.Accounts.Add(account); }
            if (account.CustomerId is not null) account = await RefreshAccountAsync(state, account, token);
            state.Sessions.RemoveAll(item => item.ExpiresAt <= now || (item.AccountId == account.Id && item.DeviceId == device));
            var devices = state.Sessions.Where(item => item.AccountId == account.Id).OrderBy(item => item.ExpiresAt).ToList();
            while (devices.Count >= options.MaximumDevices) { state.Sessions.Remove(devices[0]); devices.RemoveAt(0); }
            var sessionToken = EntitlementTokens.Encode(RandomNumberGenerator.GetBytes(32));
            state.Sessions.Add(new(BillingStore.Hash(sessionToken), account.Id, device, now.AddDays(30)));
            return new LoginResponse(sessionToken, Issue(account, device, now), now);
        }, token);
        return result ?? throw new BillingAuthenticationException();
    }
    public Task<EntitlementResponse> GetEntitlementAsync(string session, CancellationToken token) => store.TransactionAsync(async state =>
    {
        var (account, login) = Authorize(state, session);
        account = await RefreshAccountAsync(state, account, token);
        var now = DateTimeOffset.UtcNow;
        return new EntitlementResponse(Issue(account, login.DeviceId, now), now);
    }, token);
    public Task<string> CheckoutAsync(string session, CancellationToken token) => store.TransactionAsync(async state =>
    {
        var (account, _) = Authorize(state, session);
        if (account.CustomerId is null)
        {
            var id = await stripe.CreateCustomerAsync(account, token);
            account = account with { CustomerId = id }; ReplaceAccount(state, account);
        }
        return await stripe.CheckoutAsync(account.CustomerId!, account.Id, token);
    }, token);
    public Task<string> PortalAsync(string session, CancellationToken token) => store.TransactionAsync(async state =>
    {
        var (account, _) = Authorize(state, session);
        if (account.CustomerId is null) throw new BillingValidationException("No subscription has been created for this account.");
        return await stripe.PortalAsync(account.CustomerId, token);
    }, token);
    public Task<bool> SignOutAsync(string session, CancellationToken token) => store.TransactionAsync(state =>
    {
        var (_, login) = Authorize(state, session); state.Sessions.Remove(login); return Task.FromResult(true);
    }, token);
    public Task<bool> RequirePremiumAsync(string session, CancellationToken token) => store.TransactionAsync(async state =>
    {
        var (account, _) = Authorize(state, session); account = await RefreshAccountAsync(state, account, token);
        return account.PaidThrough > DateTimeOffset.UtcNow;
    }, token);
    public async Task ProcessWebhookAsync(byte[] body, string signature, CancellationToken token)
    {
        if (!options.Ready) throw new BillingUnavailableException();
        if (!StripeWebhook.Verify(body, signature, options.StripeWebhookSecret, DateTimeOffset.UtcNow))
            throw new BillingAuthenticationException();
        using var document = JsonDocument.Parse(body);
        var item = document.RootElement;
        var id = item.GetProperty("id").GetString();
        if (id is null || !id.StartsWith("evt_", StringComparison.Ordinal) || id.Length > 255) throw new BillingValidationException("Invalid event.");
        var type = item.GetProperty("type").GetString() ?? "";
        var payload = item.GetProperty("data").GetProperty("object");
        var customer = payload.TryGetProperty("customer", out var value)
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ValueKind == JsonValueKind.Object ? value.GetProperty("id").GetString() : null)
            : type.StartsWith("customer.", StringComparison.Ordinal) && !type.StartsWith("customer.subscription.", StringComparison.Ordinal)
                ? payload.GetProperty("id").GetString() : null;
        var chargeId = payload.TryGetProperty("charge", out var charge) && charge.ValueKind == JsonValueKind.String ? charge.GetString() : null;
        await store.TransactionAsync(async state =>
        {
            if (state.ProcessedEvents.ContainsKey(id)) return false;
            if (customer is null && chargeId is not null) customer = await stripe.CustomerForChargeAsync(chargeId, token);
            if (customer is not null)
            {
                var account = state.Accounts.SingleOrDefault(candidate => candidate.CustomerId == customer);
                if (account is not null)
                {
                    if (type == "customer.deleted") ReplaceAccount(state, account with { CustomerId = null, PaidThrough = null, RefreshedAt = DateTimeOffset.UtcNow });
                    else await RefreshAccountAsync(state, account, token);
                }
            }
            // Event ordering never determines access: every event re-reads the provider's authoritative state.
            state.ProcessedEvents[id] = DateTimeOffset.UtcNow;
            foreach (var expired in state.ProcessedEvents.Where(pair => pair.Value < DateTimeOffset.UtcNow.AddDays(-90)).Select(pair => pair.Key).ToArray())
                state.ProcessedEvents.Remove(expired);
            return true;
        }, token);
    }
    private async Task<Account> RefreshAccountAsync(BillingState state, Account account, CancellationToken token)
    {
        var paid = account.CustomerId is null ? null : await stripe.ReadPaidThroughAsync(account.CustomerId, token);
        account = account with { PaidThrough = paid, RefreshedAt = DateTimeOffset.UtcNow }; ReplaceAccount(state, account); return account;
    }
    private string Issue(Account account, string device, DateTimeOffset now)
    {
        var premium = account.PaidThrough > now;
        var refresh = now + EntitlementTokens.RefreshInterval;
        var expiry = refresh + EntitlementTokens.OfflineGrace;
        if (premium && expiry > account.PaidThrough) expiry = account.PaidThrough!.Value;
        if (refresh > expiry) refresh = expiry;
        using var privateKey = RSA.Create(); privateKey.ImportFromPem(options.SigningPrivateKeyPem);
        return EntitlementTokens.Sign(new(EntitlementTokens.Issuer, EntitlementTokens.Audience, account.Id, device,
            premium ? PlanTier.Premium : PlanTier.Free, now, refresh, expiry, premium ? account.PaidThrough : null), privateKey);
    }
    private static void ReplaceAccount(BillingState state, Account account) => state.Accounts[state.Accounts.FindIndex(item => item.Id == account.Id)] = account;
    private static (Account, LoginSession) Authorize(BillingState state, string session)
    {
        if (string.IsNullOrWhiteSpace(session) || session.Length > 256) throw new BillingAuthenticationException();
        var hash = BillingStore.Hash(session);
        var login = state.Sessions.SingleOrDefault(item => item.TokenHash == hash && item.ExpiresAt > DateTimeOffset.UtcNow)
            ?? throw new BillingAuthenticationException();
        return (state.Accounts.Single(item => item.Id == login.AccountId), login);
    }
    private string CodeHash(string email, string device, string code) => Convert.ToHexString(HMACSHA256.HashData(
        Encoding.UTF8.GetBytes(options.SigningPrivateKeyPem), Encoding.UTF8.GetBytes(email + "\n" + device + "\n" + code)));
    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || email.Contains('\r') || email.Contains('\n'))
            throw new BillingValidationException("Enter a valid email address.");
        try
        {
            var value = email.Trim().ToLowerInvariant(); var address = new MailAddress(value);
            if (address.Address != value || !address.Host.Contains('.')) throw new FormatException();
            return value;
        }
        catch (FormatException) { throw new BillingValidationException("Enter a valid email address."); }
    }
    public static string NormalizeDevice(string device) => Guid.TryParse(device, out var id) && id != Guid.Empty
        ? id.ToString("D") : throw new BillingValidationException("Invalid device identity.");
}
