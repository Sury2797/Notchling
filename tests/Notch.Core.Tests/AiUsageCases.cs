using System.Text;
using Notch.Core.AiUsage;

namespace Notch.Core.Tests;

internal static class AiUsageCases
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-10-13T12:00:00Z");
    }
    private static Task<AiUsageFileSnapshot> Parse(string text, AiUsageProvider provider, int limit = 256 * 1024)
        => AiUsageLogParser.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), provider, limit);
    private const string Codex = """
        {"type":"session_meta","payload":{"id":"s1"}}
        {"type":"event_msg","timestamp":"2026-10-13T11:59:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":120,"cached_input_tokens":50,"output_tokens":20,"reasoning_output_tokens":5}},"rate_limits":{"limit_id":"codex","primary":{"used_percent":42.5,"window_minutes":300,"resets_at":1791896400},"secondary":{"used_percent":17,"window_minutes":10080,"resets_at":1792404000}}}}
        """;
    private const string Claude = """
        {"type":"assistant","timestamp":"2026-10-13T11:59:00Z","sessionId":"s1","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":20,"cache_creation_input_tokens":10,"cache_read_input_tokens":50}}}
        """;

    public static void Register(TestSuite suite)
    {
        suite.AddAsync("AI Codex reports observed counters and real quota windows without double-counting subsets", async () =>
        {
            var parsed = await Parse(Codex, AiUsageProvider.Codex);
            Check.False(parsed.Partial);
            var sample = parsed.Samples.Single();
            Check.Equal(140L, sample.Tokens.TotalTokens);
            Check.Equal(50L, sample.Tokens.CachedInputTokens);
            Check.Equal(5L, sample.Tokens.ReasoningOutputTokens);
            var quota = parsed.Quotas.Single();
            Check.Equal(42.5, quota.Windows[0].UsedPercent);
            Check.Equal(57.5, quota.Windows[0].RemainingPercent);
            Check.Equal(TimeSpan.FromHours(5), quota.Windows[0].Duration);
            Check.Equal(DateTimeOffset.FromUnixTimeSeconds(1791896400), quota.Windows[0].ResetsAt);
        });
        suite.AddAsync("AI Claude streaming retransmissions deduplicate and account for actual cache tokens", async () =>
        {
            var parsed = await Parse(Claude + "\n" + Claude.Replace("\"output_tokens\":20", "\"output_tokens\":25"), AiUsageProvider.Claude);
            Check.Equal(1, parsed.Samples.Count);
            Check.Equal(160L, parsed.Samples[0].Tokens.InputTokens);
            Check.Equal(25L, parsed.Samples[0].Tokens.OutputTokens);
            Check.Equal(0, parsed.Quotas.Count);
        });
        suite.AddAsync("AI unsupported records never manufacture zero usage", async () =>
        {
            var parsed = await Parse("{\"type\":\"user\",\"usage\":{\"input_tokens\":999}}", AiUsageProvider.Codex);
            Check.Equal(0, parsed.Samples.Count);
            Check.Equal(0, parsed.Quotas.Count);
        });
        suite.AddAsync("AI incomplete append is retried while malformed completed lines mark partial coverage", async () =>
        {
            var incomplete = await Parse(Claude + "\n{\"type\":", AiUsageProvider.Claude);
            Check.Equal(1, incomplete.Samples.Count);
            Check.False(incomplete.Partial);
            var bad = await Parse("{broken}\n" + Claude, AiUsageProvider.Claude);
            Check.Equal(1, bad.Samples.Count);
            Check.True(bad.Partial);
        });
        suite.AddAsync("AI negative, invalid and excessive counters are rejected without losing valid records", async () =>
        {
            foreach (var bad in new[] { "-1", "\"unknown\"", "1000000000001", "null" })
            {
                var parsed = await Parse(Claude.Replace("\"input_tokens\":100", "\"input_tokens\":" + bad) + "\n" + Claude, AiUsageProvider.Claude);
                Check.True(parsed.Partial);
                Check.Equal(1, parsed.Samples.Count);
                Check.Equal(160L, parsed.Samples[0].Tokens.InputTokens);
            }
        });
        suite.AddAsync("AI quota rejects impossible percentages and does not infer missing duration or reset", async () =>
        {
            var invalid = await Parse(Codex.Replace("42.5", "142.5"), AiUsageProvider.Codex);
            Check.True(invalid.Partial);
            Check.Equal(0, invalid.Quotas.Count);
            Check.Equal(140L, invalid.Samples.Single().Tokens.TotalTokens);
            var absent = await Parse(Codex.Replace("\"window_minutes\":300,\"resets_at\":1791896400", "\"window_minutes\":null,\"resets_at\":null"), AiUsageProvider.Codex);
            Check.Equal<TimeSpan?>(null, absent.Quotas.Single().Windows[0].Duration);
            Check.Equal<DateTimeOffset?>(null, absent.Quotas.Single().Windows[0].ResetsAt);
        });
        suite.AddAsync("AI overlong lines are bounded and subsequent valid records remain readable", async () =>
        {
            var parsed = await Parse("{\"content\":\"" + new string('x', 4096) + "\"}\n" + Claude, AiUsageProvider.Claude, 1024);
            Check.True(parsed.Partial);
            Check.Equal(1, parsed.Samples.Count);
        });
        suite.AddAsync("AI missing Codex session identity prevents invented deduplication", async () =>
        {
            var parsed = await Parse(Codex[(Codex.IndexOf('\n') + 1)..], AiUsageProvider.Codex);
            Check.True(parsed.Partial);
            Check.Equal(0, parsed.Samples.Count);
            Check.Equal(1, parsed.Quotas.Count); // Independent quota evidence is still usable.
        });
        suite.AddAsync("AI discovery finds both providers and deduplicates copied session and message records", () => WithRoots(async (options, _, codex, claude) =>
        {
            await File.WriteAllTextAsync(Path.Combine(codex, "a.jsonl"), Codex);
            await File.WriteAllTextAsync(Path.Combine(codex, "b.jsonl"), Codex);
            await File.WriteAllTextAsync(Path.Combine(claude, "a.jsonl"), Claude);
            await File.WriteAllTextAsync(Path.Combine(claude, "b.jsonl"), Claude.Replace("\"sessionId\":\"s1\"", "\"sessionId\":\"fork\""));
            var result = await new LocalAiUsageService(options, new Clock()).ReadAsync();
            Check.Equal(140L, result.Providers[0].Tokens!.TotalTokens);
            Check.Equal(180L, result.Providers[1].Tokens!.TotalTokens);
            Check.Equal(1, result.Providers[1].Sessions);
            Check.Equal(AiUsageState.Available, result.Providers[0].State);
        }));
        suite.AddAsync("AI discovery distinguishes missing installations from empty usage and excludes credentials", () => WithRoots(async (options, root, codex, _) =>
        {
            await File.WriteAllTextAsync(Path.Combine(options.CodexHome!, "auth.jsonl"), Codex);
            await File.WriteAllTextAsync(Path.Combine(root, "conversation.jsonl"), Claude);
            var result = await new LocalAiUsageService(options with { ClaudeHome = Path.Combine(root, "missing") }, new Clock()).ReadAsync();
            Check.Equal(AiUsageState.NoUsage, result.Providers[0].State);
            Check.Equal<AiTokenUsage?>(null, result.Providers[0].Tokens);
            Check.Equal(AiUsageState.NotFound, result.Providers[1].State);
            Check.Equal<AiTokenUsage?>(null, result.Providers[1].Tokens);
        }));
        suite.AddAsync("AI cached refresh updates append counters and removes deleted records", () => WithRoots(async (options, _, codex, _) =>
        {
            var path = Path.Combine(codex, "a.jsonl");
            await File.WriteAllTextAsync(path, Codex);
            var clock = new Clock(); var service = new LocalAiUsageService(options, clock);
            var first = await service.ReadAsync();
            await File.AppendAllTextAsync(path, "\n" + Codex[(Codex.IndexOf('\n') + 1)..].Replace("\"input_tokens\":120", "\"input_tokens\":150"));
            Check.True(ReferenceEquals(first, await service.ReadAsync()));
            clock.UtcNow += TimeSpan.FromSeconds(31);
            Check.Equal(170L, (await service.ReadAsync()).Providers[0].Tokens!.TotalTokens);
            File.Delete(path);
            clock.UtcNow += TimeSpan.FromSeconds(31);
            Check.Equal<AiTokenUsage?>(null, (await service.ReadAsync()).Providers[0].Tokens);
        }));
        suite.AddAsync("AI historical or reset quota observations are explicitly stale", () => WithRoots(async (options, _, codex, _) =>
        {
            await File.WriteAllTextAsync(Path.Combine(codex, "a.jsonl"), Codex.Replace("11:59:00", "11:40:00"));
            var result = await new LocalAiUsageService(options, new Clock()).ReadAsync();
            Check.True(result.Providers[0].Quotas.Single().IsStale);
            Check.Equal(42.5, result.Providers[0].Quotas.Single().Windows[0].UsedPercent);
        }));
        suite.AddAsync("AI scan bounds expose partial coverage rather than claiming complete usage", () => WithRoots(async (options, _, codex, _) =>
        {
            await File.WriteAllTextAsync(Path.Combine(codex, "a.jsonl"), Codex);
            await File.WriteAllTextAsync(Path.Combine(codex, "b.jsonl"), Codex.Replace("\"s1\"", "\"s2\""));
            var result = await new LocalAiUsageService(options with { MaximumFiles = 1 }, new Clock()).ReadAsync();
            Check.Equal(AiUsageState.Partial, result.Providers[0].State);
            Check.Equal(140L, result.Providers[0].Tokens!.TotalTokens);
        }));
        suite.AddAsync("AI cancellation is honored before local discovery", async () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => new LocalAiUsageService().ReadAsync(cancellationToken: cancellation.Token));
        });
    }

    private static async Task WithRoots(Func<LocalAiUsageOptions, string, string, string, Task> run)
    {
        var root = Path.Combine(Path.GetTempPath(), "notch-ai-usage-fixture-" + Guid.NewGuid().ToString("N"));
        var codexHome = Path.Combine(root, "codex"); var claudeHome = Path.Combine(root, "claude");
        var codex = Path.Combine(codexHome, "sessions"); var claude = Path.Combine(claudeHome, "projects", "fixture");
        Directory.CreateDirectory(codex); Directory.CreateDirectory(claude);
        try { await run(new(codexHome, claudeHome), root, codex, claude); }
        finally { Directory.Delete(root, true); }
    }
}
