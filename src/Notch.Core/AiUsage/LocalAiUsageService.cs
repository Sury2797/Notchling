namespace Notch.Core.AiUsage;

/// <summary>
/// Reads supported local CLI telemetry on a background worker. It never opens credentials, sends
/// conversations to a server, or converts local tokens into subscription allowance.
/// </summary>
public sealed class LocalAiUsageService : IAiUsageService
{
    private sealed record CachedFile(long Length, long LastWriteTicks, AiUsageFileSnapshot Data);
    private sealed record Discovery(IReadOnlyList<FileInfo> Files, bool Found, bool Limited, bool Failed);
    private readonly LocalAiUsageOptions _options;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CachedFile> _files = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private AiUsageSnapshot? _current;

    public LocalAiUsageService(LocalAiUsageOptions? options = null, IClock? clock = null)
    {
        _options = options ?? LocalAiUsageOptions.Discover();
        _clock = clock ?? new SystemClock();
        if (_options.MaximumFiles is < 1 or > 1024 || _options.MaximumDirectories is < 1 or > 4096
            || _options.MaximumFileBytes is < 1024 or > 50 * 1024 * 1024 || _options.MaximumReadBytes < _options.MaximumFileBytes
            || _options.MaximumReadBytes > 128 * 1024 * 1024 || _options.RefreshInterval < TimeSpan.FromSeconds(5)
            || _options.QuotaFreshness < TimeSpan.FromSeconds(30)) throw new ArgumentOutOfRangeException(nameof(options));
    }

    public async Task<AiUsageSnapshot> ReadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _clock.UtcNow;
            var minimum = forceRefresh ? TimeSpan.FromSeconds(5) : _options.RefreshInterval;
            if (_current is { } current && now >= current.CheckedAt && now - current.CheckedAt < minimum) return current;
            // Directory discovery and JSON parsing never run on the calling UI thread.
            _current = await Task.Run(async () =>
            {
                var keep = new HashSet<string>(_files.Comparer);
                var codex = await ReadProviderAsync(AiUsageProvider.Codex, _options.CodexHome, ["sessions", "archived_sessions"], now, keep, cancellationToken).ConfigureAwait(false);
                var claude = await ReadProviderAsync(AiUsageProvider.Claude, _options.ClaudeHome, ["projects"], now, keep, cancellationToken).ConfigureAwait(false);
                foreach (var path in _files.Keys.Where(path => !keep.Contains(path)).ToArray()) _files.Remove(path);
                return new AiUsageSnapshot([codex, claude], now);
            }, cancellationToken).ConfigureAwait(false);
            return _current;
        }
        finally { _gate.Release(); }
    }

    private async Task<AiProviderUsage> ReadProviderAsync(AiUsageProvider provider, string? home, string[] relativeRoots,
        DateTimeOffset now, HashSet<string> keep, CancellationToken ct)
    {
        var name = provider == AiUsageProvider.Codex ? "Codex" : "Claude";
        var source = provider == AiUsageProvider.Codex ? "Local Codex session logs · provider-reported metadata" : "Local Claude Code transcripts · usage metadata";
        var discovery = Discover(home, relativeRoots, ct);
        if (!discovery.Found && !discovery.Failed)
            return new(provider, name, AiUsageState.NotFound, null, 0, null, now, null, source, "No supported local logs found. Install or use the provider's CLI on this device; account quota is unavailable.", []);
        var samples = new Dictionary<string, AiUsageSample>(StringComparer.Ordinal);
        var quotas = new Dictionary<string, AiUsageQuota>(StringComparer.Ordinal);
        var partial = discovery.Limited;
        var failed = discovery.Failed;
        long budget = 0;
        var readFiles = 0;
        foreach (var file in discovery.Files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                file.Refresh();
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0) { partial = true; continue; }
                if (file.Length > _options.MaximumFileBytes || file.Length > _options.MaximumReadBytes - budget) { partial = true; continue; }
                budget += file.Length;
                var length = file.Length; var write = file.LastWriteTimeUtc.Ticks;
                AiUsageFileSnapshot data;
                if (_files.TryGetValue(file.FullName, out var cached) && cached.Length == length && cached.LastWriteTicks == write) data = cached.Data;
                else
                {
                    await using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                        16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    // Freeze the read to this observed length: a growing log cannot overrun the refresh budget.
                    using var bounded = new BoundedReadStream(stream, Math.Min(length, _options.MaximumFileBytes));
                    data = await AiUsageLogParser.ReadAsync(bounded, provider, _options.MaximumLineCharacters, ct).ConfigureAwait(false);
                    file.Refresh();
                    if (file.Exists && file.Length == length && file.LastWriteTimeUtc.Ticks == write)
                        _files[file.FullName] = new(length, write, data);
                    else _files.Remove(file.FullName); // A concurrent append must be reread next time.
                }
                keep.Add(file.FullName);
                readFiles++;
                partial |= data.Partial;
                foreach (var sample in data.Samples)
                {
                    var key = provider == AiUsageProvider.Codex ? sample.SessionId : sample.MessageId;
                    samples[key] = samples.TryGetValue(key, out var previous) ? AiUsageLogParser.Maximum(previous, sample) : sample;
                }
                foreach (var quota in data.Quotas)
                    if (!quotas.TryGetValue(quota.LimitId, out var previous) || quota.ObservedAt > previous.ObservedAt) quotas[quota.LimitId] = quota;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            { failed = true; partial = true; }
        }
        var windows = quotas.Values.OrderByDescending(value => value.ObservedAt).Take(8)
            .Select(value => value with { IsStale = value.ObservedAt > now + TimeSpan.FromMinutes(1) || now - value.ObservedAt > _options.QuotaFreshness
                || value.Windows.Any(window => window.ResetsAt is { } reset && reset <= now) }).ToArray();
        if (samples.Count == 0)
        {
            // Keep previously measured data visibly stale on an I/O failure, never silently replace it with zero.
            var previous = _current?.Providers.FirstOrDefault(value => value.Provider == provider);
            if (failed && previous?.Tokens is not null)
                return previous with { State = AiUsageState.Stale, CheckedAt = now, Detail = "Local logs could not be refreshed. Showing the last successful measurement.",
                    Quotas = previous.Quotas.Select(value => value with { IsStale = true }).ToArray() };
            return new(provider, name, failed || partial ? AiUsageState.Unavailable : AiUsageState.NoUsage, null, 0, null, now,
                readFiles > 0 ? now : null, source, failed || partial ? "Local records could not be fully read; usage is unavailable. No allowance is estimated."
                    : "No supported token-usage records yet. Missing records are not zero usage.", windows);
        }
        try
        {
            var totals = new AiTokenUsage(samples.Values.Sum(value => value.Tokens.InputTokens), samples.Values.Sum(value => value.Tokens.OutputTokens),
                samples.Values.Sum(value => value.Tokens.CachedInputTokens), samples.Values.Sum(value => value.Tokens.CacheWriteInputTokens), samples.Values.Sum(value => value.Tokens.ReasoningOutputTokens));
            _ = totals.TotalTokens;
            var detail = $"Observed across {readFiles:N0} local log files. " + (partial || failed ? "Partial coverage: unreadable, oversized or scan-limited records were omitted. " : "")
                + (provider == AiUsageProvider.Claude ? "Subscription quota is not supplied by these transcripts." : "Allowance is shown only when a log includes a provider-reported quota; it is not a live account query.");
            return new(provider, name, partial || failed ? AiUsageState.Partial : AiUsageState.Available, totals,
                samples.Values.Select(value => value.SessionId).Distinct(StringComparer.Ordinal).Count(), samples.Values.Max(value => value.Timestamp), now, now, source, detail, windows);
        }
        catch (OverflowException)
        { return new(provider, name, AiUsageState.Unavailable, null, 0, null, now, null, source, "The source token counters exceed supported bounds. No usage estimate is shown.", windows); }
    }

    private Discovery Discover(string? home, IEnumerable<string> relativeRoots, CancellationToken ct)
    {
        var files = new List<FileInfo>(); var found = false; var limited = false; var failed = false;
        if (string.IsNullOrWhiteSpace(home)) return new(files, false, false, false);
        try
        {
            var root = Path.GetFullPath(home);
            // Telemetry discovery must stay local and must not crawl an entire profile or follow junctions.
            if (root.StartsWith("\\\\", StringComparison.Ordinal) || !Directory.Exists(root)) return new(files, false, false, false);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return new(files, false, true, false);
            var pending = new Stack<(string Path, int Depth)>();
            foreach (var child in relativeRoots)
            {
                var path = Path.Combine(root, child);
                if (Directory.Exists(path)) { pending.Push((path, 0)); found = true; }
            }
            var directories = 0;
            var entries = 0;
            while (pending.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                if (++directories > _options.MaximumDirectories) { limited = true; break; }
                try
                {
                    if ((File.GetAttributes(directory.Path) & FileAttributes.ReparsePoint) != 0) { limited = true; continue; }
                    foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (++entries > _options.MaximumDirectories * 32) { limited = true; pending.Clear(); break; }
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { limited = true; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        { if (directory.Depth < 10) pending.Push((entry, directory.Depth + 1)); else limited = true; }
                        else if (Path.GetExtension(entry).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)) files.Add(new FileInfo(entry));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { failed = true; }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException) { failed = true; }
        if (files.Count > _options.MaximumFiles) limited = true;
        return new(files.OrderByDescending(file => file.LastWriteTimeUtc).Take(_options.MaximumFiles).ToArray(), found, limited, failed);
    }

    private sealed class BoundedReadStream(Stream stream, long remaining) : Stream
    {
        private long _read;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => remaining;
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { var read = stream.Read(buffer, offset, (int)Math.Min(count, remaining - _read)); _read += read; return read; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { var read = await stream.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining - _read)], cancellationToken).ConfigureAwait(false); _read += read; return read; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
