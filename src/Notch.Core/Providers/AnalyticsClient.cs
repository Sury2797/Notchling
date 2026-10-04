using System.Net.Http.Headers;
using System.Text.Json;

namespace Notch.Core.Providers;

/// <summary>Consumes an explicitly configured read-only endpoint with the normalized Notchling analytics contract.</summary>
public sealed class AnalyticsClient(HttpClient client, ISecretVault vault, TimeProvider? timeProvider = null)
{
    public async Task<AnalyticsSnapshot> ReadAsync(string endpoint, string site, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use an absolute HTTPS endpoint without credentials or a fragment in its URL.", nameof(endpoint));
        if (string.IsNullOrWhiteSpace(site) || site.Trim().Length > 200) throw new ArgumentException("Enter a site label up to 200 characters.", nameof(site));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ProviderHttp.RequireSecret(vault, "analytics"));
        using var document = await ProviderHttp.ReadJsonAsync(client, request, ct).ConfigureAwait(false);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The analytics endpoint must return a JSON object.");
        var active = (int)ProviderHttp.Integer(root, "activeUsers", int.MaxValue);
        var pageViews = ProviderHttp.Integer(root, "pageViews");
        var newUsers = ProviderHttp.Integer(root, "newUsers");
        if (!root.TryGetProperty("timeline", out var timeline) || timeline.ValueKind != JsonValueKind.Array || timeline.GetArrayLength() > 1440)
            throw new InvalidDataException("The analytics timeline must contain at most 1440 counts.");
        var counts = new List<int>();
        foreach (var value in timeline.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < 0) throw new InvalidDataException("The analytics timeline contains an invalid count.");
            counts.Add(number);
        }
        if (!root.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() > 100)
            throw new InvalidDataException("The analytics pages list must contain at most 100 entries.");
        var pageCounts = new List<KeyValuePair<string, int>>();
        foreach (var page in pages.EnumerateArray())
            pageCounts.Add(new(ProviderHttp.Text(page, "path", 500), (int)ProviderHttp.Integer(page, "users", int.MaxValue)));
        var updatedText = ProviderHttp.Text(root, "updatedAt", 100);
        if (updatedText.Length < 20 || updatedText[10] != 'T' || !(updatedText.EndsWith('Z') || updatedText.Length >= 6 && (updatedText[^6] == '+' || updatedText[^6] == '-')) ||
            !root.GetProperty("updatedAt").TryGetDateTimeOffset(out var updated))
            throw new InvalidDataException("The analytics updatedAt timestamp is invalid.");
        if (updated > (timeProvider ?? TimeProvider.System).GetUtcNow().AddMinutes(5))
            throw new InvalidDataException("The analytics timestamp is in the future. Check the endpoint's clock before refreshing.");
        return new(site.Trim(), active, pageViews, newUsers, counts, pageCounts, updated);
    }
}
