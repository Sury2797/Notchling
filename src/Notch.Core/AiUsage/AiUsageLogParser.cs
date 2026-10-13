using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Notch.Core.AiUsage;

/// <summary>Retains only identifiers, timestamps and usage metadata, never message text.</summary>
public sealed record AiUsageSample(string SessionId, string MessageId, DateTimeOffset Timestamp, AiTokenUsage Tokens);
public sealed record AiUsageFileSnapshot(IReadOnlyList<AiUsageSample> Samples, IReadOnlyList<AiUsageQuota> Quotas, bool Partial, long BytesRead);

public static class AiUsageLogParser
{
    public static async Task<AiUsageFileSnapshot> ReadAsync(Stream stream, AiUsageProvider provider,
        int maximumLineCharacters = 256 * 1024, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (maximumLineCharacters < 1024 || maximumLineCharacters > 2 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumLineCharacters));
        var samples = new Dictionary<string, AiUsageSample>(StringComparer.Ordinal);
        var quotas = new Dictionary<string, AiUsageQuota>(StringComparer.Ordinal);
        var partial = false;
        string? codexSession = null;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 16 * 1024, leaveOpen: true);
        var buffer = new char[16 * 1024];
        var line = new StringBuilder();
        var overlong = false;
        var lines = 0;
        void Consume(bool terminated)
        {
            if (++lines > 100_000) { partial = true; return; }
            if (overlong) { partial = true; overlong = false; line.Clear(); return; }
            var text = line.ToString().TrimEnd('\r');
            line.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;
                var type = Text(root, "type");
                if (provider == AiUsageProvider.Codex && type == "session_meta")
                {
                    codexSession = root.TryGetProperty("payload", out var metadata) ? Identifier(Text(metadata, "id")) : null;
                    if (codexSession is null) partial = true;
                    return;
                }
                if (provider == AiUsageProvider.Codex && type == "event_msg" && root.TryGetProperty("payload", out var payload) && Text(payload, "type") == "token_count")
                {
                    var timestamp = Timestamp(root);
                    // A quota can be observed even when a token-count event has no info object.
                    if (payload.TryGetProperty("rate_limits", out var limits) && limits.ValueKind == JsonValueKind.Object)
                    {
                        try
                        {
                            var quota = Quota(limits, timestamp);
                            if (quota is not null && (!quotas.TryGetValue(quota.LimitId, out var previous) || quota.ObservedAt > previous.ObservedAt)) quotas[quota.LimitId] = quota;
                        }
                        catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException) { partial = true; }
                    }
                    if (!payload.TryGetProperty("info", out var info) || info.ValueKind == JsonValueKind.Null) return;
                    if (codexSession is null || info.ValueKind != JsonValueKind.Object || !info.TryGetProperty("total_token_usage", out var usage)) throw new InvalidDataException();
                    var counts = CodexTokens(usage);
                    // Repeated cumulative counters are merged across files at the session level later.
                    var sample = new AiUsageSample(codexSession, codexSession, timestamp, counts);
                    samples[codexSession] = samples.TryGetValue(codexSession, out var old) ? Maximum(old, sample) : sample;
                }
                else if (provider == AiUsageProvider.Claude && type == "assistant" && root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
                    && message.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    var session = Identifier(Text(root, "sessionId"));
                    var messageId = Identifier(Text(message, "id")) ?? Identifier(Text(root, "uuid"));
                    if (session is null || messageId is null) throw new InvalidDataException();
                    var cacheRead = Count(usage, "cache_read_input_tokens", true);
                    var cacheWrite = Count(usage, "cache_creation_input_tokens", true);
                    var counts = new AiTokenUsage(checked(Count(usage, "input_tokens") + cacheRead + cacheWrite), Count(usage, "output_tokens"), cacheRead, cacheWrite, 0);
                    var sample = new AiUsageSample(session, messageId, Timestamp(root), counts);
                    // Message IDs survive copied/forked transcript files; a new session does not bill the same message twice.
                    samples[messageId] = samples.TryGetValue(messageId, out var old) ? Maximum(old, sample) : sample;
                }
            }
            catch (JsonException) { if (terminated) partial = true; /* A writer's incomplete final line is retried next refresh. */ }
            catch (Exception error) when (error is InvalidDataException or OverflowException or ArgumentOutOfRangeException or InvalidOperationException) { partial = true; }
        }
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var character in buffer.AsSpan(0, count))
                {
                    if (character == '\n') Consume(true);
                    else if (!overlong)
                    {
                        if (line.Length == maximumLineCharacters) { overlong = true; line.Clear(); }
                        else line.Append(character);
                    }
                }
                if (lines > 100_000) break;
            }
            if (line.Length != 0 || overlong) Consume(false);
        }
        catch (DecoderFallbackException) { partial = true; }
        return new(samples.Values.ToArray(), quotas.Values.ToArray(), partial, stream.CanSeek ? stream.Position : 0);
    }

    public static AiUsageSample Maximum(AiUsageSample first, AiUsageSample second)
    {
        // A streaming message/cumulative counter can be written repeatedly; compare each reported component once.
        var a = first.Tokens; var b = second.Tokens;
        return first with { Timestamp = first.Timestamp > second.Timestamp ? first.Timestamp : second.Timestamp,
            Tokens = new(Math.Max(a.InputTokens, b.InputTokens), Math.Max(a.OutputTokens, b.OutputTokens),
                Math.Max(a.CachedInputTokens, b.CachedInputTokens), Math.Max(a.CacheWriteInputTokens, b.CacheWriteInputTokens), Math.Max(a.ReasoningOutputTokens, b.ReasoningOutputTokens)) };
    }

    private static AiTokenUsage CodexTokens(JsonElement usage)
    {
        var input = Count(usage, "input_tokens"); var output = Count(usage, "output_tokens");
        var cached = Count(usage, "cached_input_tokens", true); var write = Count(usage, "cache_write_input_tokens", true); var reasoning = Count(usage, "reasoning_output_tokens", true);
        if (cached > input || write > input || reasoning > output) throw new InvalidDataException();
        return new(input, output, cached, write, reasoning);
    }
    private static AiUsageQuota? Quota(JsonElement limits, DateTimeOffset timestamp)
    {
        var windows = new List<AiUsageWindow>();
        foreach (var name in new[] { "primary", "secondary" })
        {
            if (!limits.TryGetProperty(name, out var window) || window.ValueKind == JsonValueKind.Null) continue;
            if (window.ValueKind != JsonValueKind.Object || !window.TryGetProperty("used_percent", out var percent)
                || percent.ValueKind != JsonValueKind.Number || !percent.TryGetDouble(out var used) || !double.IsFinite(used) || used < 0 || used > 100) throw new InvalidDataException();
            TimeSpan? duration = null; DateTimeOffset? reset = null;
            if (window.TryGetProperty("window_minutes", out var minutes) && minutes.ValueKind != JsonValueKind.Null)
            {
                if (minutes.ValueKind != JsonValueKind.Number || !minutes.TryGetInt64(out var number) || number <= 0 || number > 525_600) throw new InvalidDataException();
                duration = TimeSpan.FromMinutes(number);
            }
            if (window.TryGetProperty("resets_at", out var resets) && resets.ValueKind != JsonValueKind.Null)
            {
                if (resets.ValueKind != JsonValueKind.Number || !resets.TryGetInt64(out var number)) throw new InvalidDataException();
                reset = DateTimeOffset.FromUnixTimeSeconds(number);
            }
            windows.Add(new(name, used, duration, reset));
        }
        if (windows.Count == 0) return null;
        return new(Identifier(Text(limits, "limit_id")) ?? "codex", Identifier(Text(limits, "limit_name")), windows, timestamp, false);
    }
    private static long Count(JsonElement element, string name, bool optional = false)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        { if (optional) return 0; throw new InvalidDataException(); }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var count) || count < 0 || count > 1_000_000_000_000) throw new InvalidDataException();
        return count;
    }
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 ? value : null;
    private static DateTimeOffset Timestamp(JsonElement element)
    {
        var text = Text(element, "timestamp");
        if (text is null || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)) throw new InvalidDataException();
        return timestamp;
    }
}
