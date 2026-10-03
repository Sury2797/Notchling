using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Notch.Core.Providers;

/// <summary>Imports usage metadata from one explicitly selected local JSONL file. No prompts or source text are retained.</summary>
public static class CodingImporter
{
    private sealed record Usage(string Session, DateOnly Day, long Input, long Output);
    private sealed record CodexTotal(long Input, long Output);

    public static async Task<CodingSnapshot> ReadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        path = Path.GetFullPath(path);
        const long maximumBytes = 50L * 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > maximumBytes) throw new InvalidDataException("Choose a coding JSONL file no larger than 50 MB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false, 64 * 1024, leaveOpen: true);
        var claude = new Dictionary<string, Usage>(StringComparer.Ordinal);
        var codexTotals = new Dictionary<string, CodexTotal>(StringComparer.Ordinal);
        var codexUsage = new List<Usage>();
        var fallbackSession = Path.GetFileName(path);
        var codexSession = fallbackSession;
        var sawClaude = false; var sawCodex = false;
        var lines = 0;
        long characters = 0;
        string? line;
        try
        {
            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (++lines > 500_000 || stream.Position > maximumBytes || line.Length > 2 * 1024 * 1024 || (characters += line.Length + 1) > maximumBytes)
                    throw new InvalidDataException("The coding log exceeds the import limits.");
                if (lines == 1) line = line.TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 64 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var type = String(root, "type");
                if (type == "session_meta" && root.TryGetProperty("payload", out var metadata))
                {
                    codexSession = String(metadata, "id") ?? fallbackSession;
                    if (string.IsNullOrWhiteSpace(codexSession) || codexSession.Length > 500) throw new InvalidDataException("The coding session identifier is too long.");
                    continue;
                }
                if (type == "assistant" && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
                    message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    var input = checked(Token(usage, "input_tokens") + Token(usage, "cache_creation_input_tokens", optional: true) + Token(usage, "cache_read_input_tokens", optional: true));
                    var output = Token(usage, "output_tokens");
                    var session = String(root, "sessionId") ?? fallbackSession;
                    var messageId = String(message, "id") ?? String(root, "uuid") ?? throw new InvalidDataException("Claude usage is missing a message identifier for deduplication.");
                    if (string.IsNullOrWhiteSpace(session) || string.IsNullOrWhiteSpace(messageId) || session.Length > 500 || messageId.Length > 500) throw new InvalidDataException("The coding identifier is too long.");
                    var id = session + ":" + messageId;
                    var day = DateOnly.FromDateTime(Timestamp(root).UtcDateTime);
                    if (claude.TryGetValue(id, out var previous))
                        claude[id] = previous with { Input = Math.Max(previous.Input, input), Output = Math.Max(previous.Output, output) };
                    else claude[id] = new("Claude:" + session, day, input, output);
                    sawClaude = true;
                }
                else if (type == "event_msg" && root.TryGetProperty("payload", out var payload) && String(payload, "type") == "token_count" &&
                    payload.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
                {
                    if (!info.TryGetProperty("total_token_usage", out var codexTokens) || codexTokens.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException("This Codex log lacks cumulative token usage needed for reliable deduplication.");
                    var input = Token(codexTokens, "input_tokens"); var output = Token(codexTokens, "output_tokens");
                    var previous = codexTotals.GetValueOrDefault(codexSession) ?? new(0, 0);
                    var inputDelta = Math.Max(0, input - previous.Input); var outputDelta = Math.Max(0, output - previous.Output);
                    var day = DateOnly.FromDateTime(Timestamp(root).UtcDateTime);
                    codexTotals[codexSession] = new(Math.Max(previous.Input, input), Math.Max(previous.Output, output));
                    if (inputDelta != 0 || outputDelta != 0 || previous.Input == 0 && previous.Output == 0)
                        codexUsage.Add(new("Codex:" + codexSession, day, inputDelta, outputDelta));
                    sawCodex = true;
                }
            }
            var all = claude.Values.Concat(codexUsage).ToArray();
            if (!sawClaude && !sawCodex) throw new InvalidDataException("No supported Claude or Codex token-usage metadata was found in the selected JSONL file.");
            var days = all.GroupBy(item => item.Day).OrderBy(group => group.Key)
                .Select(group => new CodingDay(group.Key, group.Sum(item => item.Input), group.Sum(item => item.Output), group.Select(item => item.Session).Distinct(StringComparer.Ordinal).Count())).ToArray();
            return new(sawClaude && sawCodex ? "Claude + Codex" : sawClaude ? "Claude" : "Codex", all.Sum(item => item.Input), all.Sum(item => item.Output),
                all.Select(item => item.Session).Distinct(StringComparer.Ordinal).Count(), days, path);
        }
        catch (JsonException) { throw new InvalidDataException("The selected coding log contains invalid JSON."); }
        catch (OverflowException) { throw new InvalidDataException("The coding log contains token counts outside supported bounds."); }
        catch (DecoderFallbackException) { throw new InvalidDataException("The selected coding log is not valid UTF-8."); }
    }

    private static string? String(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static long Token(JsonElement element, string name, bool optional = false)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            if (optional) return 0;
            throw new InvalidDataException($"Coding usage is missing {name}.");
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < 0) throw new InvalidDataException("Coding usage contains an invalid token count.");
        return number;
    }
    private static DateTimeOffset Timestamp(JsonElement element)
    {
        var text = String(element, "timestamp");
        if (text is null || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value))
            throw new InvalidDataException("Coding usage is missing a valid timestamp.");
        return value;
    }
}
