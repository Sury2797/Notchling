using System.Net;
using System.Text.Json;

namespace Notch.Core.Providers;

internal static class ProviderHttp
{
    public static async Task<JsonDocument> ReadJsonAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var reason = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "Access was refused. Check the configured credential and its read permissions.",
                HttpStatusCode.TooManyRequests => "The provider rate limit was reached. Try again later.",
                _ => "The provider request failed."
            };
            throw new HttpRequestException($"{reason} HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }
        const int maximumBytes = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("The provider response exceeds the 8 MB limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var block = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(block, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > maximumBytes)
                throw new InvalidDataException("The provider response exceeds the 8 MB limit.");
            await buffer.WriteAsync(block.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        try { return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { throw new InvalidDataException("The provider returned invalid JSON."); }
    }

    public static string Text(JsonElement element, string name, int limit = 500)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"The provider response is missing {name}.");
        var text = value.GetString() ?? string.Empty;
        if (text.Length > limit) throw new InvalidDataException($"The provider field {name} is too long.");
        return text;
    }

    public static long Integer(JsonElement element, string name, long maximum = long.MaxValue)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0 || number > maximum)
            throw new InvalidDataException($"The provider field {name} is invalid.");
        return number;
    }

    public static string RequireSecret(ISecretVault vault, string name)
    {
        var secret = vault.Read(name);
        if (string.IsNullOrWhiteSpace(secret)) throw new InvalidOperationException($"Connect {name} in Settings before refreshing.");
        if (secret.Contains('\r') || secret.Contains('\n')) throw new InvalidOperationException("The saved credential is invalid.");
        return secret;
    }
}
