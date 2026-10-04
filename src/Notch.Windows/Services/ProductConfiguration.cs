using System.Text.Json;

namespace Notch.Windows.Services;

/// <summary>Public deployment configuration. Never contains Stripe, SMTP, signing, or weather secrets.</summary>
public sealed record ProductConfiguration(string? BillingUrl = null, string? EntitlementPublicKeyPem = null)
{
    public static ProductConfiguration Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "product-config.json");
        if (!File.Exists(path)) return new();
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) return new();
            return JsonSerializer.Deserialize<ProductConfiguration>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
}
