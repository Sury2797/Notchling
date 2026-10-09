using System.Threading.RateLimiting;
using Notch.Billing;
using Notch.Core;
using Notch.Core.Commerce;

var builder = WebApplication.CreateBuilder(args);
var billing = builder.Configuration.GetSection("Billing").Get<BillingOptions>() ?? new();
var weather = builder.Configuration.GetSection("Weather").Get<WeatherOptions>() ?? new();
builder.Services.AddSingleton(billing);
builder.Services.AddSingleton(weather);
builder.Services.AddSingleton<BillingStore>();
builder.Services.AddSingleton<ILoginEmailSender, SmtpLoginEmailSender>();
builder.Services.AddHttpClient<IStripeBilling, StripeClient>();
builder.Services.AddSingleton<BillingEngine>();
// Suppress HttpClient request-URI logging: commercial weather URLs contain a server-only API key.
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
builder.Services.AddHttpClient<WeatherProxy>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    try { await next(context); }
    catch (Exception error) when (error is BillingUnavailableException or BillingAuthenticationException or BillingValidationException
        or HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException or System.Net.Mail.SmtpException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.StatusCode = error switch
        {
            BillingAuthenticationException => 401,
            BillingValidationException => 400,
            _ => 503
        };
        var message = error is BillingValidationException validation ? validation.Message
            : error is BillingAuthenticationException ? "Authentication could not be verified."
            : "Service is not configured or is temporarily unavailable.";
        await context.Response.WriteAsJsonAsync(new BillingError(message));
    }
});
app.UseRateLimiter();
app.MapGet("/health/ready", () => billing.Ready ? Results.Ok(new { ready = true }) : Results.Json(new { ready = false }, statusCode: 503));
app.MapGet("/billing/return", () => Results.Content($"<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>{ProductIdentity.DisplayName} subscription</title><body><h1>Return to {ProductIdentity.DisplayName}</h1><p>Use Restore / refresh in {ProductIdentity.DisplayName} to verify your subscription. A browser return does not activate Premium.</p></body></html>", "text/html"));
app.MapPost("/v1/auth/request", async (LoginRequest input, BillingEngine engine, CancellationToken token) =>
{ await engine.RequestLoginAsync(input, token); return Results.Accepted(); }).RequireRateLimiting("login");
app.MapPost("/v1/auth/verify", async (VerifyLoginRequest input, BillingEngine engine, CancellationToken token) =>
    Results.Ok(await engine.VerifyLoginAsync(input, token))).RequireRateLimiting("login");
app.MapPost("/v1/auth/signout", async (HttpContext context, BillingEngine engine, CancellationToken token) =>
{ await engine.SignOutAsync(Session(context), token); return Results.NoContent(); }).RequireRateLimiting("api");
app.MapGet("/v1/entitlement", async (HttpContext context, BillingEngine engine, CancellationToken token) =>
    Results.Ok(await engine.GetEntitlementAsync(Session(context), token))).RequireRateLimiting("api");
app.MapPost("/v1/checkout", async (HttpContext context, BillingEngine engine, CancellationToken token) =>
    Results.Ok(new BillingLinkResponse(await engine.CheckoutAsync(Session(context), token)))).RequireRateLimiting("api");
app.MapPost("/v1/portal", async (HttpContext context, BillingEngine engine, CancellationToken token) =>
    Results.Ok(new BillingLinkResponse(await engine.PortalAsync(Session(context), token)))).RequireRateLimiting("api");
app.MapPost("/v1/stripe/webhook", async (HttpContext context, BillingEngine engine, CancellationToken token) =>
{
    using var body = new MemoryStream();
    var buffer = new byte[8192];
    while (true)
    {
        var read = await context.Request.Body.ReadAsync(buffer, token); if (read == 0) break;
        if (body.Length + read > 1024 * 1024) return Results.StatusCode(413);
        body.Write(buffer, 0, read);
    }
    await engine.ProcessWebhookAsync(body.ToArray(), context.Request.Headers["Stripe-Signature"].ToString(), token);
    return Results.Ok();
});
app.MapGet("/v1/weather", async (string city, HttpContext context, BillingEngine engine, WeatherProxy proxy, CancellationToken token) =>
    await engine.RequireWeatherAccessAsync(Session(context), token) ? Results.Ok(await proxy.ReadAsync(city, token)) : Results.StatusCode(403))
    .RequireRateLimiting("api");
app.Run();

static string Session(HttpContext context)
{
    var value = context.Request.Headers.Authorization.ToString();
    if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) throw new BillingAuthenticationException();
    return value[7..];
}

public partial class Program;
