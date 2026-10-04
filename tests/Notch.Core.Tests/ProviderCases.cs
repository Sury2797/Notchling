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

internal sealed class ProviderClock(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = utcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class StallingProviderStream : Stream
{
    public bool WasDisposed { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
}

internal sealed class StallingProviderContent(StallingProviderStream stream) : HttpContent
{
    protected override bool TryComputeLength(out long length) { length = 0; return false; }
    protected override Task SerializeToStreamAsync(Stream target, TransportContext? context) => throw new NotSupportedException();
    protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(stream);
    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult<Stream>(stream);
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
    private static object Location(string name = "Bengaluru") => new { latitude = 12.97, longitude = 77.59, name, country = "India" };
    private static WeatherClient Weather(HttpClient http, TimeProvider? clock = null) => new(http,
        WeatherServiceConfiguration.CommercialProxy("https://billing.example.test/v1/weather"),
        () => "synthetic-session-token", clock);

    private static object Forecast(bool mismatched = false, string zoneId = "Asia/Kolkata", int offsetSeconds = 19800, DateTimeOffset? current = null)
    {
        var instant = (current ?? new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero)).ToUnixTimeSeconds();
        return new
        {
            utc_offset_seconds = offsetSeconds, timezone = zoneId,
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
        suite.AddAsync("Weather uses the licensed authenticated proxy and preserves city-local forecast metadata", async () =>
        {
            using var handler = new FixtureHandler((request, _) =>
            {
                Check.Equal("https://billing.example.test/v1/weather?city=Bengaluru", request.RequestUri!.AbsoluteUri);
                Check.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Check.Equal("synthetic-session-token", request.Headers.Authorization?.Parameter);
                return Json(new { location = Location(), forecast = Forecast() });
            });
            using var http = new HttpClient(handler);
            var weather = Weather(http);
            var result = await weather.ReadAsync(" Bengaluru ");
            Check.Equal("Bengaluru, India", result.City);
            Check.Near(29, result.Temperature);
            Check.Equal(TimeSpan.FromMinutes(330), result.Hours[0].Time.Offset);
            Check.Equal(11, result.Hours[0].Time.Hour);
            Check.Equal(new DateOnly(2026, 10, 3), result.Days[0].Date);
            Check.Equal("Asia/Kolkata", result.TimeZoneId);
            Check.Equal<TimeSpan?>(TimeSpan.FromMinutes(330), result.UtcOffset);
            Check.Equal(result, await weather.ReadAsync("bengaluru"));
            Check.Equal(1, handler.Calls);
        });
        suite.AddAsync("Weather reports unknown cities and inconsistent forecast arrays", async () =>
        {
            using var unknown = new HttpClient(new FixtureHandler((_, _) => new(HttpStatusCode.NotFound)));
            var error = await Check.ThrowsAsync<HttpRequestException>(() => Weather(unknown).ReadAsync("Unknown"));
            Check.Equal(HttpStatusCode.NotFound, error.StatusCode);
            using var mismatch = new HttpClient(new FixtureHandler((_, _) => Json(new { location = Location("Fixture"), forecast = Forecast(mismatched: true) })));
            await Check.ThrowsAsync<InvalidDataException>(() => Weather(mismatch).ReadAsync("Fixture"));
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
        suite.AddAsync("Provider deadline covers a body that stalls after successful response headers", async () =>
        {
            var stream = new StallingProviderStream();
            using var http = new HttpClient(new FixtureHandler((_, _) => new(HttpStatusCode.OK) { Content = new StallingProviderContent(stream) }))
            { Timeout = TimeSpan.FromMilliseconds(120) };
            var started = System.Diagnostics.Stopwatch.StartNew();
            var error = await Check.ThrowsAsync<TimeoutException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                .ReadAsync("https://example.test/data?private=hidden", "Site"));
            Check.True(started.Elapsed < TimeSpan.FromSeconds(5), "The body read did not honor its complete-request deadline.");
            Check.True(stream.WasDisposed);
            Check.True(error.Message.Contains("try again", StringComparison.OrdinalIgnoreCase));
            Check.False(error.Message.Contains("hidden", StringComparison.Ordinal));
        });
        suite.AddAsync("Provider cancellation remains cancellation instead of becoming a timeout", async () =>
        {
            var stream = new StallingProviderStream();
            using var http = new HttpClient(new FixtureHandler((_, _) => new(HttpStatusCode.OK) { Content = new StallingProviderContent(stream) }))
            { Timeout = TimeSpan.FromSeconds(3) };
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Check.ThrowsAsync<OperationCanceledException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                .ReadAsync("https://example.test/data", "Site", cancel.Token));
            Check.True(stream.WasDisposed);
        });
        suite.AddAsync("Provider connection diagnostics omit private endpoint and transport exception details", async () =>
        {
            using var http = new HttpClient(new FixtureHandler((_, _) => throw new HttpRequestException("TLS failure private.example.test?key=synthetic-private")));
            var error = await Check.ThrowsAsync<HttpRequestException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                .ReadAsync("https://private.example.test/data?key=synthetic-private", "Site"));
            Check.True(error.Message.Contains("connection", StringComparison.OrdinalIgnoreCase));
            Check.False(error.Message.Contains("synthetic-private", StringComparison.Ordinal));
            Check.True(error.InnerException is null);
        });
        suite.AddAsync("Provider authorization and rate limits preserve actionable safe status messages", async () =>
        {
            foreach (var item in new[] { (HttpStatusCode.Unauthorized, "credential"), (HttpStatusCode.Forbidden, "permission"), (HttpStatusCode.TooManyRequests, "rate limit") })
            {
                using var http = new HttpClient(new FixtureHandler((_, _) => new(item.Item1) { Content = new StringContent("private response details") }));
                var error = await Check.ThrowsAsync<HttpRequestException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"))
                    .ReadAsync("https://example.test/data", "Site"));
                Check.Equal(item.Item1, error.StatusCode);
                Check.True(error.Message.Contains(item.Item2, StringComparison.OrdinalIgnoreCase));
                Check.False(error.Message.Contains("private response", StringComparison.Ordinal));
            }
        });
        suite.AddAsync("Revenue snapshots bind UTC reporting boundaries to the requested day count", async () =>
        {
            var now = new DateTimeOffset(2026, 10, 4, 0, 0, 30, TimeSpan.Zero);
            var clock = new ProviderClock(now);
            using var handler = new FixtureHandler((request, call) =>
            {
                var expectedStart = call == 1 ? new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero) : new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
                Check.True(request.RequestUri!.Query.Contains("created%5Bgte%5D=" + expectedStart.ToUnixTimeSeconds(), StringComparison.Ordinal));
                return Json(Page(false));
            });
            using var http = new HttpClient(handler);
            var result = await new RevenueClient(http, FixtureVault.With("stripe"), clock).ReadAsync(RevenueProvider.Stripe, 7);
            Check.Equal(7, result.RequestedDays);
            Check.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), result.RangeStart);
            Check.Equal<DateTimeOffset?>(now, result.RangeEnd);
            Check.Equal(7, result.DailyAmounts.Count);
            clock.Now = now.AddMinutes(1);
            var today = await new RevenueClient(http, FixtureVault.With("stripe"), clock).ReadAsync(RevenueProvider.Stripe, 1);
            Check.Equal(1, today.RequestedDays);
            Check.Equal<DateTimeOffset?>(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero), today.RangeStart);
        });
        suite.AddAsync("Weather fails closed without licensed configuration or an authenticated session", async () =>
        {
            using var handler = new FixtureHandler((_, _) => throw new InvalidOperationException("Unexpected request"));
            using var http = new HttpClient(handler);
            await Check.ThrowsAsync<InvalidOperationException>(() => new WeatherClient(http).ReadAsync("Bengaluru"));
            await Check.ThrowsAsync<InvalidOperationException>(() => new WeatherClient(http,
                WeatherServiceConfiguration.CommercialProxy("https://billing.example.test/v1/weather")).ReadAsync("Bengaluru"));
            await Check.ThrowsAsync<InvalidOperationException>(() => new WeatherClient(http,
                WeatherServiceConfiguration.CommercialProxy("https://billing.example.test/v1/weather"), () => "synthetic\r\ninvalid").ReadAsync("Bengaluru"));
            Check.Equal(0, handler.Calls);
        });
        suite.Add("Commercial weather configuration rejects insecure, credential-bearing, or development endpoints", () =>
        {
            foreach (var endpoint in new[] { "http://example.test/weather", "https://user:secret@example.test/weather", "https://example.test/weather?key=secret", "https://example.test/weather#section", "https://api.open-meteo.com/v1/forecast", "https://geocoding-api.open-meteo.com/v1/search" })
                Check.Throws<ArgumentException>(() => WeatherServiceConfiguration.CommercialProxy(endpoint));
#if !DEBUG
            Check.Throws<InvalidOperationException>(() => WeatherServiceConfiguration.Development());
#endif
        });
        suite.AddAsync("Weather cache expires and malformed proxy objects cannot replace good data", async () =>
        {
            var clock = new ProviderClock(new DateTimeOffset(2026, 10, 3, 6, 0, 0, TimeSpan.Zero));
            using var handler = new FixtureHandler((_, call) => call == 2 ? Text("{\"location\":null,\"forecast\":{}}") : Json(new { location = Location(), forecast = Forecast() }));
            using var http = new HttpClient(handler);
            var weather = Weather(http, clock);
            var first = await weather.ReadAsync("Bengaluru");
            clock.Now += TimeSpan.FromMinutes(6);
            await Check.ThrowsAsync<InvalidDataException>(() => weather.ReadAsync("Bengaluru"));
            var third = await weather.ReadAsync("Bengaluru");
            Check.Equal(first.City, third.City);
            Check.Near(first.Temperature, third.Temperature);
            Check.Equal(first.UpdatedAt, third.UpdatedAt);
            Check.Equal(3, handler.Calls);
        });
        suite.AddAsync("Weather forecast hours retain distinct offsets across a city daylight-saving transition", async () =>
        {
            using var http = new HttpClient(new FixtureHandler((_, _) => Json(new
            {
                location = Location("New York"),
                forecast = Forecast(zoneId: "America/New_York", offsetSeconds: -4 * 3600,
                    current: new DateTimeOffset(2026, 11, 1, 5, 0, 0, TimeSpan.Zero)),
            })));
            var result = await Weather(http).ReadAsync("New York");
            Check.Equal("America/New_York", result.TimeZoneId);
            Check.Equal(1, result.Hours[0].Time.Hour);
            Check.Equal(1, result.Hours[1].Time.Hour);
            Check.Equal(TimeSpan.FromHours(-4), result.Hours[0].Time.Offset);
            Check.Equal(TimeSpan.FromHours(-5), result.Hours[1].Time.Offset);
            Check.Equal(new DateOnly(2026, 11, 1), result.Days[0].Date);
        });
#if DEBUG
        suite.AddAsync("Hosted weather development endpoints require explicit debug configuration", async () =>
        {
            using var handler = new FixtureHandler((request, call) =>
            {
                Check.True(request.Headers.Authorization is null);
                Check.Equal(call == 1 ? "geocoding-api.open-meteo.com" : "api.open-meteo.com", request.RequestUri!.Host);
                return call == 1 ? Json(new { results = new[] { Location() } }) : Json(Forecast());
            });
            using var http = new HttpClient(handler);
            var snapshot = await new WeatherClient(http, WeatherServiceConfiguration.Development()).ReadAsync("Bengaluru");
            Check.Equal("Bengaluru, India", snapshot.City);
            Check.Equal(2, handler.Calls);
        });
#endif
        suite.AddAsync("Analytics requires its documented credential and rejects a future source clock", async () =>
        {
            using var handler = new FixtureHandler((_, _) => Text("""{"activeUsers":3,"pageViews":100,"newUsers":12,"timeline":[],"pages":[],"updatedAt":"2026-10-03T01:10:00Z"}"""));
            using var http = new HttpClient(handler);
            await Check.ThrowsAsync<InvalidOperationException>(() => new AnalyticsClient(http, new FixtureVault()).ReadAsync("https://example.test/data", "Site"));
            Check.Equal(0, handler.Calls);
            var clock = new ProviderClock(new DateTimeOffset(2026, 10, 3, 1, 2, 0, TimeSpan.Zero));
            await Check.ThrowsAsync<InvalidDataException>(() => new AnalyticsClient(http, FixtureVault.With("analytics"), clock).ReadAsync("https://example.test/data", "Site"));
        });
    }
}
