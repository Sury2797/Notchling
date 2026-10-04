using System.Net.Http.Headers;
using System.Text.Json;

namespace Notch.Core.Providers;

/// <summary>City forecasts through a licensed, authenticated server-side proxy.</summary>
public sealed class WeatherClient(HttpClient client, WeatherServiceConfiguration? configuration = null,
    Func<string?>? accessTokenProvider = null, TimeProvider? timeProvider = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly WeatherServiceConfiguration service = configuration ?? WeatherServiceConfiguration.Disabled;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private string? cachedQuery;
    private WeatherSnapshot? cached;
    private DateTimeOffset fetchedAt;

    public async Task<WeatherSnapshot> ReadAsync(string city, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(city) || city.Trim().Length > 80)
            throw new ArgumentException("Enter a city name up to 80 characters.", nameof(city));
        if (!service.IsDevelopment && service.ProxyEndpoint is null)
            throw new InvalidOperationException("Weather is unavailable until the licensed service is configured.");
        city = city.Trim();
        string? token = null;
        if (service.ProxyEndpoint is not null)
        {
            token = accessTokenProvider?.Invoke();
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Sign in to Premium before refreshing weather.");
            if (token.Contains('\r') || token.Contains('\n')) throw new InvalidOperationException("The weather session credential is invalid. Sign in again.");
        }
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cached is not null && string.Equals(cachedQuery, city, StringComparison.OrdinalIgnoreCase) &&
                clock.GetUtcNow() - fetchedAt is var age && age >= TimeSpan.Zero && age < TimeSpan.FromMinutes(5)) return cached;
            WeatherSnapshot snapshot;
            if (service.ProxyEndpoint is { } endpoint)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.AbsoluteUri + "?city=" + Uri.EscapeDataString(city));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var document = await ProviderHttp.ReadJsonAsync(client, request, cancellationToken).ConfigureAwait(false);
                var root = Object(document.RootElement);
                snapshot = ParseForecast(root.GetProperty("location"), root.GetProperty("forecast"));
            }
            else
            {
                // This path can only be configured by the DEBUG-only factory.
                using var geocodeRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json");
                using var geocode = await ProviderHttp.ReadJsonAsync(client, geocodeRequest, cancellationToken).ConfigureAwait(false);
                var geocodingRoot = Object(geocode.RootElement);
                if (!geocodingRoot.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                    throw new InvalidOperationException("No matching city was found. Try its full name.");
                var location = Object(results[0]);
                var latitude = Number(location, "latitude", -90, 90);
                var longitude = Number(location, "longitude", -180, 180);
                var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={latitude}&longitude={longitude}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min,weather_code&timezone=auto&forecast_days=7&timeformat=unixtime&temperature_unit=celsius&wind_speed_unit=kmh");
                using var forecastRequest = new HttpRequestMessage(HttpMethod.Get, url);
                using var forecast = await ProviderHttp.ReadJsonAsync(client, forecastRequest, cancellationToken).ConfigureAwait(false);
                snapshot = ParseForecast(location, forecast.RootElement);
            }
            cached = snapshot;
            cachedQuery = city;
            fetchedAt = clock.GetUtcNow();
            return snapshot;
        }
        catch (KeyNotFoundException) { throw new InvalidDataException("The forecast response is missing required fields."); }
        finally { gate.Release(); }
    }

    private static WeatherSnapshot ParseForecast(JsonElement location, JsonElement root)
    {
        location = Object(location);
        root = Object(root);
        _ = Number(location, "latitude", -90, 90);
        _ = Number(location, "longitude", -180, 180);
        var locationName = ProviderHttp.Text(location, "name", 200);
        if (location.TryGetProperty("country", out var country) && country.ValueKind == JsonValueKind.String)
            locationName += ", " + ProviderHttp.Text(location, "country", 200);
        if (!root.TryGetProperty("utc_offset_seconds", out var utc) || utc.ValueKind != JsonValueKind.Number ||
            !utc.TryGetInt32(out var seconds) || seconds < -14 * 3600 || seconds > 14 * 3600 || seconds % 60 != 0)
            throw new InvalidDataException("The forecast timezone offset is invalid.");
        var offset = TimeSpan.FromSeconds(seconds);
        string? zoneId = null;
        TimeZoneInfo? zone = null;
        if (root.TryGetProperty("timezone", out var zoneElement) && zoneElement.ValueKind == JsonValueKind.String)
        {
            zoneId = ProviderHttp.Text(root, "timezone", 200);
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
            if (zone is null && TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out var windowsId))
            {
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(windowsId); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
        }
        DateTimeOffset At(JsonElement time)
        {
            if (time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out var epoch) || epoch < 0 || epoch > 253402300799)
                throw new InvalidDataException("The forecast timestamp is invalid.");
            try
            {
                var instant = DateTimeOffset.FromUnixTimeSeconds(epoch);
                return zone is null ? instant.ToOffset(offset) : TimeZoneInfo.ConvertTime(instant, zone);
            }
            catch (ArgumentException) { throw new InvalidDataException("The forecast timestamp is outside the supported range."); }
        }
        var current = Object(root.GetProperty("current"));
        var currentTime = At(current.GetProperty("time"));
        var hourly = Object(root.GetProperty("hourly"));
        var times = Array(hourly, "time", 400);
        var temperatures = Array(hourly, "temperature_2m", 400);
        var codes = Array(hourly, "weather_code", 400);
        if (times.Length != temperatures.Length || times.Length != codes.Length)
            throw new InvalidDataException("Forecast hourly arrays have different lengths.");
        var hours = new List<WeatherHour>();
        for (var index = 0; index < times.Length; index++)
        {
            var time = At(times[index]);
            if (time < currentTime.AddHours(-1) || hours.Count == 24) continue;
            hours.Add(new(time, Number(temperatures[index], -120, 80), Code(codes[index])));
        }
        var daily = Object(root.GetProperty("daily"));
        var dates = Array(daily, "time", 16);
        var highs = Array(daily, "temperature_2m_max", 16);
        var lows = Array(daily, "temperature_2m_min", 16);
        var dailyCodes = Array(daily, "weather_code", 16);
        if (dates.Length != highs.Length || dates.Length != lows.Length || dates.Length != dailyCodes.Length)
            throw new InvalidDataException("Forecast daily arrays have different lengths.");
        var days = new List<WeatherDay>();
        for (var index = 0; index < dates.Length; index++)
        {
            var high = Number(highs[index], -120, 80);
            var low = Number(lows[index], -120, 80);
            if (low > high) throw new InvalidDataException("Forecast minimum exceeds its maximum.");
            days.Add(new(DateOnly.FromDateTime(At(dates[index]).DateTime), high, low, Code(dailyCodes[index])));
        }
        return new(locationName, Number(current, "temperature_2m", -120, 80), Number(current, "apparent_temperature", -150, 100),
            (int)Number(current, "relative_humidity_2m", 0, 100), Number(current, "wind_speed_10m", 0, 500),
            Code(current.GetProperty("weather_code")), hours, days, currentTime, TimeZoneId: zoneId, UtcOffset: currentTime.Offset);
    }

    private static JsonElement Object(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The forecast response contains an invalid object.");
        return element;
    }
    private static double Number(JsonElement element, string field, double minimum, double maximum) => Number(element.GetProperty(field), minimum, maximum);
    private static double Number(JsonElement element, double minimum, double maximum)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value) || !double.IsFinite(value) || value < minimum || value > maximum)
            throw new InvalidDataException("The forecast contains an invalid measurement.");
        return value;
    }
    private static int Code(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var code) || code < 0 || code > 99)
            throw new InvalidDataException("The forecast weather code is invalid.");
        return code;
    }
    private static JsonElement[] Array(JsonElement element, string name, int maximum)
    {
        var array = element.GetProperty(name);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > maximum)
            throw new InvalidDataException("The forecast series is invalid.");
        return array.EnumerateArray().ToArray();
    }
}
