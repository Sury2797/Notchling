using System.Text.Json;

namespace Notch.Billing;

public sealed class WeatherOptions
{
    public string ApiKey { get; set; } = "";
    public string ForecastBase { get; set; } = "";
    public string GeocodingBase { get; set; } = "";
    public bool Ready => !string.IsNullOrWhiteSpace(ApiKey) && Valid(ForecastBase) && Valid(GeocodingBase)
        && !new Uri(ForecastBase).Host.Equals("api.open-meteo.com", StringComparison.OrdinalIgnoreCase);
    private static bool Valid(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
}
public sealed class WeatherProxy(HttpClient http, WeatherOptions options)
{
    public async Task<object> ReadAsync(string city, CancellationToken token)
    {
        if (!options.Ready) throw new BillingUnavailableException();
        if (string.IsNullOrWhiteSpace(city) || city.Length > 80) throw new BillingValidationException("Enter a city with at most 80 characters.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var key = Uri.EscapeDataString(options.ApiKey);
        using var geo = await ReadJsonAsync(options.GeocodingBase.TrimEnd('/') + "/v1/search?name=" + Uri.EscapeDataString(city.Trim())
            + "&count=1&language=en&format=json&apikey=" + key, deadline.Token);
        if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            throw new BillingValidationException("No matching city was found.");
        var location = results[0];
        var latitude = location.GetProperty("latitude").GetDouble(); var longitude = location.GetProperty("longitude").GetDouble();
        using var forecast = await ReadJsonAsync(options.ForecastBase.TrimEnd('/') + "/v1/forecast?latitude="
            + latitude.ToString(System.Globalization.CultureInfo.InvariantCulture) + "&longitude="
            + longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m"
            + "&hourly=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min"
            + "&timezone=auto&timeformat=unixtime&forecast_days=7&apikey=" + key, deadline.Token);
        return new
        {
            location = new { name = location.GetProperty("name").GetString(), country = location.TryGetProperty("country", out var country) ? country.GetString() : "", latitude, longitude },
            forecast = forecast.RootElement.Clone()
        };
    }
    private async Task<JsonDocument> ReadJsonAsync(string url, CancellationToken token)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new BillingUnavailableException();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token); if (read == 0) break;
            if (memory.Length + read > 1024 * 1024) throw new InvalidDataException("Weather response exceeds limit.");
            memory.Write(buffer, 0, read);
        }
        return JsonDocument.Parse(memory.ToArray());
    }
}
