using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Notch.Billing;
using Notch.Core;
using Notch.Core.Commerce;
using Notch.Windows.Services;

using var key = RSA.Create(2048);
var publicKey = key.ExportSubjectPublicKeyInfoPem();
var device = Guid.NewGuid().ToString("D");
var now = DateTimeOffset.UtcNow;
var cases = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); cases++; Console.WriteLine("PASS " + name); }
async Task Throws<T>(Func<Task> work, string name) where T : Exception
{ try { await work(); } catch (T) { Check(true, name); return; } throw new Exception(name); }
string Proof(PlanTier plan = PlanTier.Premium, string? boundDevice = null, DateTimeOffset? expiry = null)
    => EntitlementTokens.Sign(new(EntitlementTokens.Issuer, EntitlementTokens.Audience, "account", boundDevice ?? device,
        plan, now, now.AddHours(6), expiry ?? now.AddHours(30), now.AddDays(30)), key);

Check(EntitlementTokens.Validate(Proof(), publicKey, device, now).IsPremium, "valid RSA proof grants Premium");
Check(!EntitlementTokens.Validate(Proof() + "A", publicKey, device, now).IsPremium, "modified signature fails closed");
Check(!EntitlementTokens.Validate(Proof(), publicKey, Guid.NewGuid().ToString("D"), now).IsPremium, "proof cannot transfer between devices");
Check(EntitlementTokens.Validate(Proof(), publicKey, device, now.AddHours(7)).RequiresRefresh, "offline grace requests refresh");
Check(!EntitlementTokens.Validate(Proof(), publicKey, device, now.AddHours(31)).IsPremium, "offline grace expires");
Check(!EntitlementTokens.Validate(Proof(expiry: now.AddDays(10)), publicKey, device, now).IsPremium, "excessive offline lifetime rejected");
Check(!EntitlementTokens.Validate(Proof(), publicKey, device, now, now.AddMinutes(10)).IsPremium, "clock rollback requires online restore");
Check(!EntitlementTokens.Validate(Proof(PlanTier.Free), publicKey, device, now).IsPremium, "signed downgrade returns Free");
Check(FeaturePolicy.CanUse(ModuleId.Scratchpad, EntitlementDecision.Free()), "scratchpad remains Free");
Check(!FeaturePolicy.CanUse(ModuleId.Revenue, EntitlementDecision.Free()), "revenue requires verified Premium");
Check(FeaturePolicy.CanReadStoredData(ModuleId.Notes), "downgrade leaves records accessible");

var body = Encoding.UTF8.GetBytes("{\"id\":\"evt_test\"}");
string Header(byte[] content, string secret, long? timestamp = null)
{
    var time = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    return "t=" + time + ",v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
        Encoding.UTF8.GetBytes(time + ".").Concat(content).ToArray())).ToLowerInvariant();
}
Check(StripeWebhook.Verify(body, Header(body, "whsec_test"), "whsec_test", now), "verified webhook accepted");
Check(!StripeWebhook.Verify(body, Header(body, "wrong"), "whsec_test", now), "forged webhook rejected");
Check(!StripeWebhook.Verify(body, Header(body, "whsec_test", now.AddMinutes(-6).ToUnixTimeSeconds()), "whsec_test", now), "old signed webhook rejected");
using (var invoice = JsonDocument.Parse("{\"status\":\"paid\",\"paid\":true,\"payment_intent\":{\"latest_charge\":{\"paid\":true,\"amount\":200,\"amount_refunded\":200,\"refunded\":true}}}"))
    Check(!StripeClient.InvoiceGrantsAccess(invoice.RootElement), "full refund revokes access");
using (var invoice = JsonDocument.Parse("{\"status\":\"paid\",\"paid\":true,\"payment_intent\":{\"latest_charge\":{\"paid\":true,\"amount\":200,\"amount_refunded\":50,\"refunded\":false}}}"))
    Check(StripeClient.InvoiceGrantsAccess(invoice.RootElement), "partial goodwill refund retains remaining subscription");
using (var invoice = JsonDocument.Parse("{\"status\":\"open\",\"paid\":false}"))
    Check(!StripeClient.InvoiceGrantsAccess(invoice.RootElement), "unpaid invoice grants no access");


using (var invoice = JsonDocument.Parse("{\"status\":\"paid\",\"paid\":true}"))
    Check(!StripeClient.InvoiceGrantsAccess(invoice.RootElement), "paid invoice without confirmed charge fails closed");
using (var invoice = JsonDocument.Parse("{\"status\":\"paid\",\"paid\":true,\"payment_intent\":{\"latest_charge\":{\"paid\":true,\"amount\":200,\"amount_refunded\":0,\"disputed\":true}}}"))
{
    Check(!StripeClient.InvoiceGrantsAccess(invoice.RootElement), "open dispute denies access");
    Check(StripeClient.InvoiceGrantsAccess(invoice.RootElement, true), "confirmed won dispute restores paid access");
}

var temporary = Path.Combine(Path.GetTempPath(), "notch-commerce-" + Guid.NewGuid().ToString("N"));
var options = new BillingOptions
{
    StripeSecretKey = "sk_test_fixture", StripeWebhookSecret = "whsec_test", StripePriceId = "price_fixture",
    SigningPrivateKeyPem = key.ExportPkcs8PrivateKeyPem(), PublicOrigin = "https://billing.example.test",
    DataDirectory = temporary, SmtpHost = "smtp.example.test", SmtpUsername = "fixture", SmtpPassword = "fixture",
    MailFrom = "notch@example.test"
};
var store = new BillingStore(options); var stripe = new FakeStripe(); var sender = new FakeEmail();
var engine = new BillingEngine(options, store, stripe, sender);
try
{
    Check(!new BillingOptions().Ready, "missing production configuration disables billing");
    options.StripeSecretKey = "rk_test_fixture";
    Check(options.Ready, "restricted Stripe server key is accepted");
    options.StripeSecretKey = "sk_live_fixture";
    Check(!options.Ready, "live commerce remains disabled until release policy approval");
    options.CommercialReleaseApproved = true;
    Check(options.Ready, "live configuration requires explicit completed-policy approval");
    options.CommercialReleaseApproved = false;
    options.StripeSecretKey = "sk_test_fixture";
    await Throws<BillingUnavailableException>(() => new BillingStore(new()).TransactionAsync(state => Task.FromResult(true)), "unconfigured store cannot create accounts");
    await engine.RequestLoginAsync(new("owner@example.test", device), default);
    var login = await engine.VerifyLoginAsync(new("owner@example.test", device, sender.Code), default);
    Check(!EntitlementTokens.Validate(login.Entitlement, publicKey, device, login.ServerTime).IsPremium, "verified email alone does not purchase Premium");
    await Throws<BillingAuthenticationException>(() => engine.VerifyLoginAsync(new("owner@example.test", device, sender.Code), default), "OTP is single use");
    await engine.CheckoutAsync(login.SessionToken, default);
    stripe.PaidThrough = now.AddDays(30);
    var purchased = await engine.GetEntitlementAsync(login.SessionToken, default);
    Check(EntitlementTokens.Validate(purchased.Entitlement, publicKey, device, purchased.ServerTime).IsPremium, "authoritative paid subscription restores Premium");
    stripe.PaidThrough = now.AddMinutes(20);
    var ending = await engine.GetEntitlementAsync(login.SessionToken, default);
    Check(EntitlementTokens.Validate(ending.Entitlement, publicKey, device, ending.ServerTime).ValidUntil <= stripe.PaidThrough, "cancellation-period end caps offline lifetime");
    var webhook = Encoding.UTF8.GetBytes("{\"id\":\"evt_new\",\"type\":\"customer.subscription.updated\",\"data\":{\"object\":{\"customer\":\"cus_fixture\"}}}");
    await engine.ProcessWebhookAsync(webhook, Header(webhook, options.StripeWebhookSecret), default);
    var count = stripe.ReadCount;
    await engine.ProcessWebhookAsync(webhook, Header(webhook, options.StripeWebhookSecret), default);
    Check(stripe.ReadCount == count, "duplicate webhook is idempotent");
    stripe.PaidThrough = null;
    var delayed = Encoding.UTF8.GetBytes("{\"id\":\"evt_delayed\",\"created\":1,\"type\":\"invoice.paid\",\"data\":{\"object\":{\"customer\":\"cus_fixture\"}}}");
    await engine.ProcessWebhookAsync(delayed, Header(delayed, options.StripeWebhookSecret), default);
    var downgraded = await engine.GetEntitlementAsync(login.SessionToken, default);
    Check(!EntitlementTokens.Validate(downgraded.Entitlement, publicKey, device, downgraded.ServerTime).IsPremium, "delayed paid event cannot overwrite current cancellation or payment failure");
    await engine.RequestLoginAsync(new("attempts@example.test", device), default);
    var validCode = sender.Code;
    for (var attempt = 0; attempt < 5; attempt++)
        await Throws<BillingAuthenticationException>(() => engine.VerifyLoginAsync(new("attempts@example.test", device, validCode == "00000000" ? "11111111" : "00000000"), default), "wrong OTP attempt " + (attempt + 1));
    await Throws<BillingAuthenticationException>(() => engine.VerifyLoginAsync(new("attempts@example.test", device, validCode), default), "five wrong attempts burn challenge durably");
    await Throws<IOException>(() => store.TransactionAsync<bool>(state => { state.Accounts.Clear(); throw new IOException("fixture"); }), "failed transaction preserves prior state");
    var preserved = await engine.GetEntitlementAsync(login.SessionToken, default);
    Check(preserved is not null, "failed transaction retained customer records");
    await engine.SignOutAsync(login.SessionToken, default);
    await Throws<BillingAuthenticationException>(() => engine.GetEntitlementAsync(login.SessionToken, default), "server signout revokes session");
    var deviceLogins = new List<LoginResponse>();
    for (var index = 0; index < 4; index++)
    {
        var nextDevice = Guid.NewGuid().ToString("D");
        await store.TransactionAsync(state => { state.LoginRequests.Remove("devices@example.test"); return Task.FromResult(true); });
        await engine.RequestLoginAsync(new("devices@example.test", nextDevice), default);
        deviceLogins.Add(await engine.VerifyLoginAsync(new("devices@example.test", nextDevice, sender.Code), default));
    }
    await Throws<BillingAuthenticationException>(() => engine.GetEntitlementAsync(deviceLogins[0].SessionToken, default), "fourth verified device revokes oldest login");
    Check(await engine.GetEntitlementAsync(deviceLogins[3].SessionToken, default) is not null, "newest device can restore");
}
finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }

var vault = new FakeVault();
var desktopHandler = new DesktopHandler(key);
using var desktopHttp = new HttpClient(desktopHandler);
Check(!SubscriptionService.TryCreateFromConfiguration(desktopHttp, vault, null, null, out _, out _), "desktop has safe disabled default");
Check(!SubscriptionService.TryCreateFromConfiguration(desktopHttp, vault, "https://billing.example.test", key.ExportPkcs8PrivateKeyPem(), out _, out _), "desktop rejects accidental private signing key");
var desktop = new SubscriptionService(desktopHttp, vault, new("https://billing.example.test"), publicKey);
await desktop.RequestLoginAsync("owner@example.test");
await desktop.VerifyLoginAsync("owner@example.test", "12345678");
Check(desktop.Current.IsPremium && desktop.SessionToken == "fixture-session", "desktop persists signed proof and session in credential vault");
desktopHandler.InvalidVerify = true;
await Throws<InvalidOperationException>(() => desktop.VerifyLoginAsync("owner@example.test", "00000000"), "invalid OTP is rejected");
Check(desktop.Current.IsPremium && desktop.SessionToken == "fixture-session", "mistyped OTP preserves existing verified Premium");
desktopHandler.InvalidVerify = false;
await desktop.RefreshAsync();
Check(desktop.Current.IsPremium, "desktop restores server entitlement");
Check((await desktop.CreateCheckoutAsync()).Host == "checkout.stripe.com", "desktop accepts only trusted HTTPS Stripe checkout");
desktopHandler.PauseRefresh = true;
var refreshing = desktop.RefreshAsync();
await desktopHandler.Entered.Task;
desktop.SignOut();
desktopHandler.Release.SetResult();
await Throws<OperationCanceledException>(() => refreshing, "signout fences in-flight entitlement refresh");
Check(!desktop.Current.IsPremium && desktop.SessionToken is null, "desktop logout returns Free and removes session");
var paidUntil = DateTimeOffset.FromUnixTimeSeconds(now.AddDays(30).ToUnixTimeSeconds());
foreach (var scenario in new[] { "paid", "refunded", "disputed", "won", "wrong-price", "unpaid" })
{
    using var stripeHttp = new HttpClient(new StripeApiFixture(scenario, paidUntil));
    var actualStripe = new StripeClient(stripeHttp, options);
    var result = await actualStripe.ReadPaidThroughAsync("cus_fixture", default);
    Check(result == (scenario is "paid" or "won" ? paidUntil : null), "actual Stripe HTTP mapping: " + scenario);
}
using (var stripeHttp = new HttpClient(new StripeApiFixture("incomplete", paidUntil)))
    await Throws<InvalidDataException>(() => new StripeClient(stripeHttp, options).ReadPaidThroughAsync("cus_fixture", default), "incomplete authoritative subscription listing never grants access");
Console.WriteLine($"{cases} commerce checks passed; no live purchases, emails, or provider requests.");

sealed class FakeEmail : ILoginEmailSender
{
    public string Code { get; private set; } = "";
    public Task SendAsync(string email, string code, CancellationToken token) { Code = code; return Task.CompletedTask; }
}
sealed class FakeStripe : IStripeBilling
{
    public DateTimeOffset? PaidThrough { get; set; }
    public int ReadCount { get; private set; }
    public Task<string> CreateCustomerAsync(Account account, CancellationToken token) => Task.FromResult("cus_fixture");
    public Task<DateTimeOffset?> ReadPaidThroughAsync(string customer, CancellationToken token) { ReadCount++; return Task.FromResult(PaidThrough); }
    public Task<string> CheckoutAsync(string customer, string accountId, CancellationToken token) => Task.FromResult("https://checkout.stripe.com/fixture");
    public Task<string> PortalAsync(string customer, CancellationToken token) => Task.FromResult("https://billing.stripe.com/fixture");
    public Task<string?> CustomerForChargeAsync(string charge, CancellationToken token) => Task.FromResult<string?>("cus_fixture");
}
sealed class FakeVault : ISecretVault
{
    private readonly Dictionary<string, string> _values = [];
    public void Save(string name, string value) => _values[name] = value;
    public string? Read(string name) => _values.GetValueOrDefault(name);
    public void Delete(string name) => _values.Remove(name);
}
sealed class DesktopHandler(RSA key) : HttpMessageHandler
{
    private string _device = "";
    public bool PauseRefresh { get; set; }
    public bool InvalidVerify { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        object result;
        var now = DateTimeOffset.UtcNow;
        string Proof() => EntitlementTokens.Sign(new(EntitlementTokens.Issuer, EntitlementTokens.Audience, "fixture", _device,
            PlanTier.Premium, now, now.AddHours(6), now.AddHours(30), now.AddDays(30)), key);
        if (request.RequestUri!.AbsolutePath == "/v1/auth/request") return new(HttpStatusCode.Accepted);
        if (request.RequestUri.AbsolutePath == "/v1/auth/verify")
        {
            if (InvalidVerify) return new(HttpStatusCode.Unauthorized);
            var input = await request.Content!.ReadFromJsonAsync<VerifyLoginRequest>(token);
            _device = input!.DeviceId; result = new LoginResponse("fixture-session", Proof(), now);
        }
        else
        {
            if (request.Headers.Authorization?.Parameter != "fixture-session") throw new Exception("Missing session authentication");
            if (PauseRefresh && request.RequestUri.AbsolutePath == "/v1/entitlement")
            { Entered.SetResult(); await Release.Task.WaitAsync(token); }
            result = request.RequestUri.AbsolutePath == "/v1/checkout" ? new BillingLinkResponse("https://checkout.stripe.com/fixture")
                : new EntitlementResponse(Proof(), now);
        }
        return new(HttpStatusCode.OK) { Content = JsonContent.Create(result) };
    }
}

sealed class StripeApiFixture(string scenario, DateTimeOffset paidUntil) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (request.RequestUri?.Host != "api.stripe.com" || request.Headers.Authorization?.Parameter != "sk_test_fixture") throw new Exception("Expected fixture server authentication");
        object payload = request.RequestUri.AbsolutePath switch
        {
            "/v1/prices/price_fixture" => new { currency = "usd", unit_amount = 200, type = "recurring", active = true, recurring = new { interval = "month", interval_count = 1 } },
            "/v1/subscriptions" => new { has_more = scenario == "incomplete", data = new[] { new { status = "active", current_period_end = paidUntil.ToUnixTimeSeconds(), latest_invoice = "in_fixture", items = new { data = new[] { new { price = new { id = scenario == "wrong-price" ? "price_other" : "price_fixture" } } } } } } },
            "/v1/invoices/in_fixture" => new { status = scenario == "unpaid" ? "open" : "paid", paid = scenario != "unpaid", payment_intent = new { latest_charge = new { id = "ch_fixture", paid = true, amount = 200, amount_refunded = scenario == "refunded" ? 200 : 0, refunded = scenario == "refunded", disputed = scenario is "disputed" or "won" } } },
            "/v1/disputes" => new { has_more = false, data = new[] { new { status = scenario == "won" ? "won" : "needs_response" } } },
            _ => throw new Exception("Unexpected fixture Stripe path")
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
    }
}
