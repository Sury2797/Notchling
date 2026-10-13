namespace Notch.Core.AiUsage;

public enum AiUsageProvider { Codex, Claude }
public enum AiUsageState { Available, Partial, NotFound, NoUsage, Unavailable, Stale }

/// <summary>Observed token counts. Cached input and reasoning output are subsets, never added again.</summary>
public sealed record AiTokenUsage(long InputTokens, long OutputTokens, long CachedInputTokens, long CacheWriteInputTokens, long ReasoningOutputTokens)
{
    public long TotalTokens => checked(InputTokens + OutputTokens);
}

/// <summary>A provider-reported window, not a quota inferred from token counts.</summary>
public sealed record AiUsageWindow(string Name, double UsedPercent, TimeSpan? Duration, DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => 100 - UsedPercent;
}

public sealed record AiUsageQuota(string LimitId, string? Name, IReadOnlyList<AiUsageWindow> Windows, DateTimeOffset ObservedAt, bool IsStale);

public sealed record AiProviderUsage(
    AiUsageProvider Provider, string Name, AiUsageState State, AiTokenUsage? Tokens, int Sessions,
    DateTimeOffset? LastActivityAt, DateTimeOffset CheckedAt, DateTimeOffset? LastSuccessfulRead,
    string Source, string Detail, IReadOnlyList<AiUsageQuota> Quotas);

public sealed record AiUsageSnapshot(IReadOnlyList<AiProviderUsage> Providers, DateTimeOffset CheckedAt);

public interface IAiUsageService
{
    Task<AiUsageSnapshot> ReadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
}

/// <summary>Paths are local read-only telemetry roots. Credentials and account databases are never opened.</summary>
public sealed record LocalAiUsageOptions(string? CodexHome, string? ClaudeHome)
{
    public int MaximumFiles { get; init; } = 256;
    public int MaximumDirectories { get; init; } = 512;
    public long MaximumFileBytes { get; init; } = 8 * 1024 * 1024;
    public long MaximumReadBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumLineCharacters { get; init; } = 256 * 1024;
    public TimeSpan RefreshInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan QuotaFreshness { get; init; } = TimeSpan.FromMinutes(5);

    public static LocalAiUsageOptions Discover()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(profile, ".codex"),
            Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(profile, ".claude"));
    }
}
