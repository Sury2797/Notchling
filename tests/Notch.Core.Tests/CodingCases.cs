using System.Text;
using Notch.Core.Providers;

namespace Notch.Core.Tests;

internal static class CodingCases
{
    private static async Task WithLog(string text, Func<string, Task> run)
    {
        var path = Path.Combine(Path.GetTempPath(), "notch-coding-fixture-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            await run(path);
        }
        finally { File.Delete(path); }
    }

    public static void Register(TestSuite suite)
    {
        suite.AddAsync("Claude usage deduplicates repeated messages and includes cache input tokens", () => WithLog("""
            {"type":"assistant","timestamp":"2026-10-03T01:00:00Z","sessionId":"s1","message":{"id":"m1","usage":{"input_tokens":100,"output_tokens":5,"cache_creation_input_tokens":10}}}
            {"type":"assistant","timestamp":"2026-10-03T01:01:00Z","sessionId":"s1","message":{"id":"m1","usage":{"input_tokens":120,"output_tokens":7,"cache_creation_input_tokens":10,"cache_read_input_tokens":20}}}
            {"type":"assistant","timestamp":"2026-10-03T01:02:00Z","sessionId":"s1","message":{"id":"m2","usage":{"input_tokens":20,"output_tokens":3}}}
            """, async path =>
            {
                var result = await CodingImporter.ReadAsync(path);
                Check.Equal("Claude", result.Provider);
                Check.Equal(170L, result.InputTokens);
                Check.Equal(10L, result.OutputTokens);
                Check.Equal(1, result.Sessions);
                Check.Equal(1, result.Days.Count);
            }));
        suite.AddAsync("Codex cumulative token counters contribute only positive deltas", () => WithLog("""
            {"type":"session_meta","payload":{"id":"s1"}}
            {"type":"event_msg","timestamp":"2026-10-03T01:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":10}}}}
            {"type":"event_msg","timestamp":"2026-10-03T01:01:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"output_tokens":10}}}}
            {"type":"event_msg","timestamp":"2026-10-03T01:02:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":140,"output_tokens":15}}}}
            {"type":"event_msg","timestamp":"2026-10-03T01:03:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":120,"output_tokens":14}}}}
            """, async path =>
            {
                var result = await CodingImporter.ReadAsync(path);
                Check.Equal("Codex", result.Provider);
                Check.Equal(140L, result.InputTokens);
                Check.Equal(15L, result.OutputTokens);
                Check.Equal(1, result.Sessions);
            }));
        suite.AddAsync("Coding import rejects malformed JSON and unsupported logs", async () =>
        {
            await WithLog("{unfinished", path => Check.ThrowsAsync<InvalidDataException>(() => CodingImporter.ReadAsync(path)));
            await WithLog("{\"type\":\"unrelated\"}", path => Check.ThrowsAsync<InvalidDataException>(() => CodingImporter.ReadAsync(path)));
        });
        suite.AddAsync("Coding import rejects negative counts and supports cancellation", async () =>
        {
            var bad = """{"type":"assistant","timestamp":"2026-10-03T01:00:00Z","sessionId":"s1","message":{"id":"m1","usage":{"input_tokens":-1,"output_tokens":5}}}""";
            await WithLog(bad, path => Check.ThrowsAsync<InvalidDataException>(() => CodingImporter.ReadAsync(path)));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await WithLog("{\"type\":\"unrelated\"}", path => Check.ThrowsAsync<OperationCanceledException>(() => CodingImporter.ReadAsync(path, canceled.Token)));
        });
    }
}
