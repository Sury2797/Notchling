using System.Net;
using System.Text;
using System.Text.Json;
using Notch.Core;
using Notch.Core.Providers;

namespace Notch.Core.Tests;

internal sealed class FixtureVault : ISecretVault
{
    private readonly Dictionary<string, string> _entries = new(StringComparer.Ordinal);
    public void Save(string name, string value) => _entries[name] = value;
    public string? Read(string name) => _entries.GetValueOrDefault(name);
    public void Delete(string name) => _entries.Remove(name);
    public static FixtureVault With(string name)
    {
        var vault = new FixtureVault();
        vault.Save(name, "synthetic-fixture-credential");
        return vault;
    }
}

internal sealed class FixtureHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        return Task.FromResult(respond(request, Calls));
    }
}

internal static class ProviderCases
{
    private static HttpResponseMessage Json(object value) => Text(JsonSerializer.Serialize(value));
    private static HttpResponseMessage Text(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
    private static object Charge(string id, long amount, long refund = 0, string currency = "usd", bool paid = true) => new
    {
        id, paid, captured = true, status = "succeeded", amount_captured = amount,
        amount_refunded = refund, currency, created = DateTimeOffset.UtcNow.AddSeconds(-10).ToUnixTimeSeconds(),
        description = "Fixture payment",
    };
    private static object Page(bool more, params object[] charges) => new { has_more = more, data = charges };

    private static object Forecast(bool mismatched = false)
    {
        var instant = new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        return new
        {
            utc_offset_seconds = 19800, timezone = "Asia/Kolkata",
            current = new { time = instant, temperature_2m = 29d, apparent_temperature = 30d, relative_humidity_2m = 63, weather_code = 2, wind_speed_10m = 8d },
            hourly = new { time = new[] { instant, instant + 3600 }, temperature_2m = mismatched ? new[] { 29d } : new[] { 29d, 30d }, weather_code = new[] { 2, 3 } },
            daily = new { time = new[] { instant }, temperature_2m_max = new[] { 32d }, temperature_2m_min = new[] { 23d }, weather_code = new[] { 3 } },
        };
    }

    public static void Register(TestSuite suite)
    {
        suite.AddAsync("Stripe nets captured charges and refunds, filters unpaid records and deduplicates pagination", async () =>
        {
            using var handler = new FixtureHandler((request, call) =>
            {
                Check.Equal("api.stripe.com", request.RequestUri!.Host);
                Check.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Check.Equal("synthetic-fixture-credential", request.Headers.Authorization?.Parameter);
                if (call == 1) return Json(Page(true, Charge("ch_1", 4900, 400), Charge("ch_unpaid", 9999, paid: false)));
                Check.True(request.RequestUri.Query.Contains("starting_after=ch_unpaid", StringComparison.Ordinal));
                return Json(Page(false, Charge("ch_1", 4900, 400), Charge("ch_2", 1000)));
            });
            using var http = new HttpClient(handler);
            var result = await new RevenueClient(http, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7);
            Check.Equal(55m, result.Total);
            Check.Equal(2, result.Payments.Count);
            Check.Equal(55m, result.DailyAmounts.Sum());
            Check.Equal("USD", result.Currency);
            Check.True(result.Complete);
            Check.Equal(2, handler.Calls);
        });
        suite.AddAsync("Stripe uses currency precision and refuses mixed currencies", async () =>
        {
            foreach (var fixture in new[] { (Code: "jpy", Amount: 1500L, Expected: 1500m), (Code: "kwd", Amount: 1500L, Expected: 1.5m), (Code: "isk", Amount: 1500L, Expected: 15m) })
            {
                using var handler = new FixtureHandler((_, _) => Json(Page(false, Charge("ch_1", fixture.Amount, currency: fixture.Code))));
                using var http = new HttpClient(handler);
                var result = await new RevenueClient(http, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7);
                Check.Equal(fixture.Expected, result.Total);
            }
            using var mixed = new HttpClient(new FixtureHandler((_, _) => Json(Page(false, Charge("usd", 100), Charge("eur", 100, currency: "eur")))));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(mixed, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
        });
        suite.AddAsync("Stripe marks a capped report incomplete rather than presenting a full total", async () =>
        {
            using var handler = new FixtureHandler((_, call) => Json(Page(true, Charge("ch_" + call, 100))));
            using var http = new HttpClient(handler);
            var result = await new RevenueClient(http, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7);
            Check.False(result.Complete);
            Check.Equal(100, handler.Calls);
            Check.Equal(100m, result.Total);
        });
        suite.AddAsync("Missing credentials and unsupported providers do not make network requests", async () =>
        {
            using var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("Unexpected network request"));
            using var http = new HttpClient(handler);
            var revenue = new RevenueClient(http, new FixtureVault());
            await Check.ThrowsAsync<InvalidOperationException>(() => revenue.ReadAsync(RevenueProvider.Stripe, 7));
            await Check.ThrowsAsync<NotSupportedException>(() => revenue.ReadAsync(RevenueProvider.Polar, 7));
            Check.Equal(0, handler.Calls);
        });
        suite.AddAsync("Provider failures keep HTTP status while omitting private response bodies", async () =>
        {
            using var http = new HttpClient(new FixtureHandler((_, _) => new(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("provider_private_details_do_not_display"),
            }));
            var error = await Check.ThrowsAsync<HttpRequestException>(() => new RevenueClient(http, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
            Check.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
            Check.False(error.Message.Contains("provider_private_details", StringComparison.Ordinal));
        });
        suite.AddAsync("Malformed provider JSON and oversized responses fail explicitly", async () =>
        {
            using var malformed = new HttpClient(new FixtureHandler((_, _) => Text("{unfinished")));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(malformed, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
            using var huge = new HttpClient(new FixtureHandler((_, _) => Text(new string(' ', 8 * 1024 * 1024 + 1))));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(huge, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
        });
        suite.AddAsync("Stripe rejects impossible refunds and nonadvancing pagination", async () =>
        {
            using var badRefund = new HttpClient(new FixtureHandler((_, _) => Json(Page(false, Charge("ch_1", 100, 200)))));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(badRefund, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
            using var repeating = new HttpClient(new FixtureHandler((_, _) => Json(Page(true, Charge("same", 100)))));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(repeating, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
        });
        suite.AddAsync("Weather geocodes an explicit city and preserves local forecast time", async () =>
        {
            using var handler = new FixtureHandler((request, call) =>
            {
                if (call == 1)
                {
                    Check.Equal("geocoding-api.open-meteo.com", request.RequestUri!.Host);
                    return Json(new { results = new[] { new { latitude = 12.97, longitude = 77.59, name = "Bengaluru", country = "India" } } });
                }
                Check.Equal("api.open-meteo.com", request.RequestUri!.Host);
                return Json(Forecast());
            });
            using var http = new HttpClient(handler);
            var weather = new WeatherClient(http);
            var result = await weather.ReadAsync(" Bengaluru ");
            Check.Equal("Bengaluru, India", result.City);
            Check.Near(29, result.Temperature);
            Check.Equal(TimeSpan.FromMinutes(330), result.Hours[0].Time.Offset);
            Check.Equal(11, result.Hours[0].Time.Hour);
            Check.Equal(new DateOnly(2026, 10, 3), result.Days[0].Date);
            Check.Equal(result, await weather.ReadAsync("bengaluru"));
            Check.Equal(2, handler.Calls);
        });
        suite.AddAsync("Weather reports unknown cities and inconsistent forecast arrays", async () =>
        {
            using var unknown = new HttpClient(new FixtureHandler((_, _) => Json(new { results = Array.Empty<object>() })));
            await Check.ThrowsAsync<InvalidOperationException>(() => new WeatherClient(unknown).ReadAsync("Unknown"));
            using var mismatch = new HttpClient(new FixtureHandler((_, call) => call == 1
                ? Json(new { results = new[] { new { latitude = 1d, longitude = 1d, name = "Fixture" } } })
                : Json(Forecast(mismatched: true))));
            await Check.ThrowsAsync<InvalidDataException>(() => new WeatherClient(mismatch).ReadAsync("Fixture"));
        });
        suite.AddAsync("Analytics reads the normalized endpoint without changing its query", async () =>
        {
            using var handler = new FixtureHandler((request, _) =>
            {
                Check.Equal("https://example.test/metrics?period=day", request.RequestUri!.AbsoluteUri);
                Check.Equal("Bearer", request.Headers.Authorization?.Scheme);
                return Text("""{"activeUsers":3,"pageViews":100,"newUsers":12,"timeline":[1,3],"pages":[{"path":"/","users":3}],"updatedAt":"2026-10-03T01:02:00Z"}""");
            });
            using var http = new HttpClient(handler);
            var result = await new AnalyticsClient(http, FixtureVault.With("analytics")).ReadAsync("https://example.test/metrics?period=day", " My workspace ");
            Check.Equal("My workspace", result.Site);
            Check.Equal(3, result.ActiveUsers);
            Check.Equal(100L, result.PageViews);
            Check.Equal("/", result.Pages[0].Key);
            Check.Equal(2, result.Timeline.Count);
        });
        suite.AddAsync("Analytics rejects insecure or credential-bearing endpoints before network access", async () =>
        {
            using var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("Unexpected network request"));
            using var http = new HttpClient(handler);
            var analytics = new AnalyticsClient(http, FixtureVault.With("analytics"));
            foreach (var endpoint in new[] { "http://example.test/data", "https://user:fixture@example.test/data", "https://example.test/data#fragment", "relative" })
                await Check.ThrowsAsync<ArgumentException>(() => analytics.ReadAsync(endpoint, "Site"));
            Check.Equal(0, handler.Calls);
        });
        suite.AddAsync("Analytics refuses malformed and negative count data", async () =>
        {
            using var http = new HttpClient(new FixtureHandler((_, _) => Text("""{"activeUsers":-1,"pageViews":100,"newUsers":12,"timeline":[],"pages":[],"updatedAt":"2026-10-03T01:02:00Z"}""")));
            await Check.ThrowsAsync<InvalidDataException>(() => new AnalyticsClient(http, FixtureVault.With("analytics")).ReadAsync("https://example.test/data", "Site"));
        });
        suite.AddAsync("Provider schemas reject wrong numeric types and unknown currency codes", async () =>
        {
            using var unknown = new HttpClient(new FixtureHandler((_, _) => Json(Page(false, Charge("ch_1", 100, currency: "xyz")))));
            await Check.ThrowsAsync<InvalidDataException>(() => new RevenueClient(unknown, FixtureVault.With("stripe")).ReadAsync(RevenueProvider.Stripe, 7));
            using var malformed = new HttpClient(new FixtureHandler((_, _) => Text("""{"activeUsers":"3","pageViews":100,"newUsers":12,"timeline":[],"pages":[],"updatedAt":"2026-10-03T01:02:00Z"}""")));
            await Check.ThrowsAsync<InvalidDataException>(() => new AnalyticsClient(malformed, FixtureVault.With("analytics")).ReadAsync("https://example.test/data", "Site"));
        });
        suite.AddAsync("Analytics rejects timestamps without an explicit zone", async () =>
        {
            using var malformed = new HttpClient(new FixtureHandler((_, _) => Text("""{"activeUsers":3,"pageViews":100,"newUsers":12,"timeline":[],"pages":[],"updatedAt":"2026-10-03T01:02:00"}""")));
            await Check.ThrowsAsync<InvalidDataException>(() => new AnalyticsClient(malformed, FixtureVault.With("analytics")).ReadAsync("https://example.test/data", "Site"));
        });
    }
}
