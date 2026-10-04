namespace Notch.Core.Providers;

/// <summary>
/// Selects a licensed server-side proxy. Shared weather-provider credentials stay
/// on that server; the desktop supplies only its current customer session token.
/// </summary>
public sealed class WeatherServiceConfiguration
{
    private WeatherServiceConfiguration(Uri? proxyEndpoint, bool development)
    {
        ProxyEndpoint = proxyEndpoint;
        IsDevelopment = development;
    }

    internal Uri? ProxyEndpoint { get; }
    internal bool IsDevelopment { get; }
    public static WeatherServiceConfiguration Disabled { get; } = new(null, false);

    public static WeatherServiceConfiguration CommercialProxy(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.Query))
            throw new ArgumentException("Use an absolute HTTPS weather proxy endpoint without credentials, query parameters, or a fragment.", nameof(endpoint));
        if (uri.Host.Equals("api.open-meteo.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("geocoding-api.open-meteo.com", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A commercial weather proxy cannot use the hosted development endpoints.", nameof(endpoint));
        return new(uri, false);
    }

    public static WeatherServiceConfiguration Development()
    {
#if DEBUG
        return new(null, true);
#else
        throw new InvalidOperationException("Hosted development weather endpoints are unavailable in release builds. Configure the licensed weather proxy.");
#endif
    }
}
