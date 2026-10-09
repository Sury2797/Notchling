using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Notch.Core.Providers;

namespace Notch.Core.Tests;

internal sealed class DelayedConnectionHandler(Func<int, object> response, TimeSpan delay) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = ++Calls;
        await Task.Delay(delay, cancellationToken);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response(call)), Encoding.UTF8, "application/json") };
    }
}

internal static class ConnectionCases
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 6, 0, 0, TimeSpan.Zero);
    private static object Page(string id, bool more = true) => new
    {
        has_more = more,
        data = new[] { new { id, paid = true, captured = true, status = "succeeded", amount_captured = 100, amount_refunded = 0,
            currency = "usd", created = Now.AddMinutes(-1).ToUnixTimeSeconds(), description = "Fixture payment" } }
    };

    public static void Register(TestSuite suite)
    {
        suite.AddAsync("Revenue deadline bounds the entire paginated report and permits a fresh retry", async () =>
        {
            using var handler = new DelayedConnectionHandler(call => Page("ch_" + call), TimeSpan.FromMilliseconds(90));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(240) };
            var client = new RevenueClient(http, FixtureVault.With("stripe"), new ProviderClock(Now));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Check.ThrowsAsync<TimeoutException>(() => client.ReadAsync(RevenueProvider.Stripe, 7));
            Check.True(handler.Calls is >= 2 and <= 4, "Each pagination page restarted the full operation deadline.");
            Check.True(watch.Elapsed < TimeSpan.FromSeconds(5));
            await Check.ThrowsAsync<TimeoutException>(() => client.ReadAsync(RevenueProvider.Stripe, 7));
            Check.True(handler.Calls >= 4, "A timed-out request poisoned its next refresh.");
        });
        suite.AddAsync("Cancelling a paginated report remains cancellation", async () =>
        {
            using var handler = new DelayedConnectionHandler(call => Page("ch_" + call), TimeSpan.FromMilliseconds(90));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Check.ThrowsAsync<OperationCanceledException>(() => new RevenueClient(http, FixtureVault.With("stripe"), new ProviderClock(Now))
                .ReadAsync(RevenueProvider.Stripe, 7, cancellationToken: cancel.Token));
        });
        suite.AddAsync("Stripe detects a cycling pagination cursor before the report cap", async () =>
        {
            using var handler = new FixtureHandler((_, call) => new(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(Page(call % 2 == 1 ? "ch_a" : "ch_b")), Encoding.UTF8, "application/json") });
            using var http = new HttpClient(handler);
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(http, FixtureVault.With("stripe"), new ProviderClock(Now)).ReadAsync(RevenueProvider.Stripe, 7));
            Check.Equal(3, handler.Calls);
        });
        suite.AddAsync("Connection errors explain missing endpoints and proxy authentication without exposing bodies", async () =>
        {
            foreach (var item in new[] { (HttpStatusCode.NotFound, "HTTPS address"), (HttpStatusCode.ProxyAuthenticationRequired, "proxy settings"),
                (HttpStatusCode.UnprocessableEntity, "data contract"), (HttpStatusCode.GatewayTimeout, "timed out") })
            {
                using var http = new HttpClient(new FixtureHandler((_, _) => new(item.Item1) { Content = new StringContent("private-provider-body") }));
                var error = await Check.ThrowsAsync<HttpRequestException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                    .ReadAsync("https://example.test/data", "Site"));
                Check.Equal(item.Item1, error.StatusCode);
                Check.True(error.Message.Contains(item.Item2, StringComparison.Ordinal));
                Check.False(error.Message.Contains("private-provider-body", StringComparison.Ordinal));
            }
        });
        suite.AddAsync("A rate limit gives its safe retry delay and does not repeat provider requests", async () =>
        {
            using var handler = new FixtureHandler((_, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
                return response;
            });
            using var http = new HttpClient(handler);
            var error = await Check.ThrowsAsync<HttpRequestException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                .ReadAsync("https://example.test/data", "Site"));
            Check.True(error.Message.Contains("60 seconds", StringComparison.Ordinal));
            Check.Equal(1, handler.Calls);
        });
        suite.Add("Reporting credentials reject corrupted control characters before storage or network use", () =>
        {
            Check.Equal("fixture-key", ProviderCredential.Normalize(" fixture-key "));
            foreach (var value in new[] { "fixture\r\nkey", "fixture\tkey", "fixture\0key", new string('x', 8193) })
                Check.Throws<ArgumentException>(() => ProviderCredential.Normalize(value));
        });
        suite.AddAsync("Invalid saved credentials never reach the analytics endpoint", async () =>
        {
            using var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
            using var http = new HttpClient(handler);
            var vault = new FixtureVault(); vault.Save("analytics", "corrupted\tcredential");
            await Check.ThrowsAsync<InvalidOperationException>(() => new AnalyticsClient(http, vault).ReadAsync("https://example.test/data", "Site"));
            Check.Equal(0, handler.Calls);
        });
        suite.AddAsync("Calendar import supports UTF-8 and BOM-marked UTF-16 while another app holds the export", async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "notchling-calendar-" + Guid.NewGuid().ToString("N") + ".ics");
            const string calendar = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nDTSTART:20261009T060000Z\r\nSUMMARY:Design café\r\nEND:VEVENT\r\nEND:VCALENDAR";
            try
            {
                foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true) })
                {
                    await File.WriteAllTextAsync(path, calendar, encoding);
                    using var heldExport = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                    var text = await CalendarImporter.ReadTextAsync(path);
                    Check.Equal(calendar, text);
                    Check.Equal("Design café", IcsCalendar.Parse(text, Now.AddDays(-1), Now.AddDays(1))[0].Title);
                }
            }
            finally { File.Delete(path); }
        });
        suite.AddAsync("Calendar importer rejects oversized files and malformed text and honors cancellation", async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "notchling-calendar-" + Guid.NewGuid().ToString("N") + ".ics");
            try
            {
                await File.WriteAllBytesAsync(path, new byte[5 * 1024 * 1024 + 1]);
                await Check.ThrowsAsync<InvalidDataException>(() => CalendarImporter.ReadTextAsync(path));
                foreach (var invalid in new[] { new byte[] { 0xC3, 0x28 }, new byte[] { 0xFF, 0xFE, 0x00, 0xD8 } })
                {
                    await File.WriteAllBytesAsync(path, invalid);
                    await Check.ThrowsAsync<InvalidDataException>(() => CalendarImporter.ReadTextAsync(path));
                }
                await File.WriteAllTextAsync(path, "BEGIN:VCALENDAR\nEND:VCALENDAR");
                using var cancel = new CancellationTokenSource(); cancel.Cancel();
                await Check.ThrowsAsync<OperationCanceledException>(() => CalendarImporter.ReadTextAsync(path, cancel.Token));
            }
            finally { File.Delete(path); }
        });
        suite.Add("An unrelated or empty file cannot be reported as a successfully connected calendar", () =>
        {
            foreach (var text in new[] { "", "TITLE:Not a calendar", "BEGIN:VEVENT\nDTSTART:20261009T060000Z\nEND:VEVENT" })
                Check.Throws<InvalidDataException>(() => IcsCalendar.Parse(text, Now.AddDays(-1), Now.AddDays(1)));
            Check.Equal(0, IcsCalendar.Parse("BEGIN:VCALENDAR\nVERSION:2.0\nEND:VCALENDAR", Now.AddDays(-1), Now.AddDays(1)).Count);
        });
    }
}
