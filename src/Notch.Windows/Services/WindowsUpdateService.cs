using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Notch.Windows.Services;

public enum UpdateTrustPolicy { SignedPublisher, OfficialUnsignedEvaluation }
public sealed record ReleaseProvenance(long ReleaseId, long AssetId, string Tag, string SourceCommit, bool Immutable = false);
public sealed record AvailableUpdate(string Version, string InstallerUrl, string Sha256, long SizeBytes, int MinimumWindowsBuild,
    string Architecture = "x64", UpdateTrustPolicy Trust = UpdateTrustPolicy.SignedPublisher, ReleaseProvenance? Provenance = null);
public sealed record PreparedUpdate(string Version, string InstallerPath, string Sha256, string Architecture = "x64",
    UpdateTrustPolicy Trust = UpdateTrustPolicy.SignedPublisher, ReleaseProvenance? Provenance = null, long SizeBytes = 0);
public sealed record UpdateInstallationResult(string State, string Version, string Architecture, string Message, DateTimeOffset CheckedAtUtc, int? InstallerExitCode);
public enum UpdateChannel { Stable, Evaluation }
public enum UpdateDownloadStage { Downloading, Verifying, Ready }
public sealed record UpdateDownloadProgress(UpdateDownloadStage Stage, long ReceivedBytes, long TotalBytes)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)ReceivedBytes / TotalBytes, 0, 1) : 0;
}
public sealed record ReleaseUpdate(string Version, string ReleasePageUrl, string Architecture, UpdateChannel Channel,
    bool CanInstallVerified, AvailableUpdate? VerifiedUpdate, string ReleaseNotes = "")
{
    public bool RequiresEvaluationConsent => VerifiedUpdate?.Trust == UpdateTrustPolicy.OfficialUnsignedEvaluation;
    public string TrustDescription => RequiresEvaluationConsent
        ? "Unsigned public evaluation from the official GitHub repository. Release, source commit, installer size and SHA-256 are checked again before Setup. This is not publisher signing."
        : CanInstallVerified ? "Installer integrity and the trusted installed publisher are verified before Setup."
        : "In-app installation needs an official evaluation digest and pinned source commit, or a trusted signed publisher.";
}

public interface IWindowsUpdateService
{
    Task<bool> CanVerifyPublisherAsync(CancellationToken cancellationToken = default);
    Task<ReleaseUpdate?> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default);
    Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken = default);
    Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default);
    Task OpenInstallerAsync(PreparedUpdate update, CancellationToken cancellationToken = default);
    void CancelPendingInstallation() { }
    Task<UpdateInstallationResult?> ReadLastInstallationResultAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<UpdateInstallationResult?>(null);
}

/// <summary>
/// Official release discovery and installation; signed stable installers additionally
/// require an exact official asset, bounded bytes, SHA-256 and the installed publisher.
/// An unsigned evaluation can install only an explicitly accepted, source-pinned
/// official GitHub evaluation release with an API-supplied asset digest.
/// </summary>
public sealed class WindowsUpdateService : IWindowsUpdateService
{
    private const string Repository = "https://github.com/Sury2797/Notchling";
    private const string Channel = Repository + "/releases/latest/download/notchling-update.json";
    private const string ReleasesApi = "https://api.github.com/repos/Sury2797/Notchling/releases?per_page=30";
    private const string ReleasesFeed = Repository + "/releases.atom";
    private const long MaximumInstallerBytes = 256 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly WindowsUpdateEnvironment _environment;
    private readonly TimeSpan _requestTimeout;
    private readonly object _flightGate = new();
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private readonly Dictionary<string, PreparedUpdate> _prepared = new(StringComparer.OrdinalIgnoreCase);
    private Task<ReleaseUpdate?>? _discoveryFlight;
    private Task<AvailableUpdate?>? _checkFlight;
    private readonly Dictionary<AvailableUpdate, ReleaseProvenance> _evaluations = [];
    private string? _pendingHandoffCancelPath;

    public WindowsUpdateService(HttpClient http) : this(http, WindowsUpdateEnvironment.Installed, TimeSpan.FromSeconds(20)) { }
    internal WindowsUpdateService(HttpClient http, WindowsUpdateEnvironment environment, TimeSpan? requestTimeout = null)
    {
        _http = http; _environment = environment;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(20);
    }

    public void CancelPendingInstallation()
    {
        var path = Interlocked.Exchange(ref _pendingHandoffCancelPath, null);
        if (path is null) return;
        try { File.WriteAllText(path, "cancel"); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task<UpdateInstallationResult?> ReadLastInstallationResultAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchling", "Updates", "latest-result.json");
        try
        {
            if (!File.Exists(path)) return null;
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            if (file.Length > 32768) return null;
            using var bytes = new MemoryStream();
            var buffer = new byte[4096]; int count;
            while ((count = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            { if (bytes.Length + count > 32768) return null; bytes.Write(buffer, 0, count); }
            var result = JsonSerializer.Deserialize<UpdateInstallationResult>(bytes.ToArray(), JsonOptions);
            if (result is null || result.State is not ("WaitingForApp" or "Installing" or "Completed" or "Failed" or "Cancelled")
                || result.Architecture != ArchitectureName || result.Message is null or { Length: > 1000 }
                || result.CheckedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) || result.CheckedAtUtc < DateTimeOffset.UtcNow.AddDays(-30)) return null;
            _ = ParseVersion(result.Version);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { return null; }
    }

    public async Task<bool> CanVerifyPublisherAsync(CancellationToken cancellationToken = default)
        => await ReadPublisherKeyAsync(_environment.InstalledPublisherKey, cancellationToken).ConfigureAwait(false) is not null;

    private async Task<byte[]?> ReadPublisherKeyAsync(Func<byte[]?> verify, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        try { return await Task.Run(verify, deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Publisher verification timed out. Try again or open the release page.", error); }
    }

    // A concurrent check joins the same bounded operation, rather than duplicating
    // release requests. The caller that starts it owns network cancellation; other
    // callers may cancel their wait without cancelling that operation.
    public Task<ReleaseUpdate?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_flightGate)
        {
            if (_discoveryFlight is null || _discoveryFlight.IsCompleted)
                return _discoveryFlight = DiscoverCoreAsync(cancellationToken);
            return _discoveryFlight.WaitAsync(cancellationToken);
        }
    }
    public Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_flightGate)
        {
            if (_checkFlight is null || _checkFlight.IsCompleted)
                return _checkFlight = CheckCoreAsync(cancellationToken);
            return _checkFlight.WaitAsync(cancellationToken);
        }
    }

    private async Task<ReleaseUpdate?> DiscoverCoreAsync(CancellationToken token)
    {
        RequireSupportedPlatform();
        if (await CanVerifyPublisherAsync(token))
        {
            var signed = await CheckAsync(token).ConfigureAwait(false);
            return signed is null ? null : new(signed.Version, Repository + "/releases/tag/v" + signed.Version,
                signed.Architecture, UpdateChannel.Stable, true, signed);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_requestTimeout);
        try
        {
            try
            {
                var bytes = await ReadBoundedAsync(ReleasesApi, 1024 * 1024, deadline.Token).ConfigureAwait(false);
                return await DiscoverApiAsync(bytes, deadline.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.NotFound)
            {
                // Public release feeds need no GitHub token and remain usable when
                // the anonymous API quota is exhausted. No feed text is executed.
                return await DiscoverFeedAsync(deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("The release check timed out. Try again or open the release page.", error); }
    }

    private async Task<AvailableUpdate?> CheckCoreAsync(CancellationToken token)
    {
        RequireSupportedPlatform();
        if (!await CanVerifyPublisherAsync(token))
            throw new InvalidOperationException("Verified updates require an official signed application. Use the release page for evaluation builds.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(_requestTimeout);
        try
        {
            var bytes = await ReadBoundedAsync(Channel, 32 * 1024, deadline.Token).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes, JsonOptions)
                ?? throw new InvalidDataException("The update manifest is empty.");
            if (manifest.SignerPublicKeySha256 is { } declaredPublisher)
            {
                var installedPublisher = await ReadPublisherKeyAsync(_environment.InstalledPublisherKey, deadline.Token).ConfigureAwait(false);
                if (declaredPublisher.Length != 64 || !declaredPublisher.All(Uri.IsHexDigit) || installedPublisher is null
                    || !CryptographicOperations.FixedTimeEquals(installedPublisher, Convert.FromHexString(declaredPublisher)))
                    throw new InvalidDataException("The update manifest publisher does not match the trusted installed application.");
            }
            var update = ValidateManifest(manifest);
            return ParseVersion(update.Version) > CurrentVersion ? update : null;
        }
        catch (OperationCanceledException error) when (!token.IsCancellationRequested)
        { throw new TimeoutException("The update check timed out. Try again or open the release page.", error); }
    }

    private async Task<ReleaseUpdate?> DiscoverApiAsync(byte[] bytes, CancellationToken token)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The release response is invalid.");
        ReleaseUpdate? newest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !release.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !release.TryGetProperty("tag_name", out var tagProperty) || tagProperty.ValueKind != JsonValueKind.String
                || !TryReleaseTag(tagProperty.GetString(), out var version, out var releaseChannel, out var tag)
                || (releaseChannel == UpdateChannel.Stable && prerelease.GetBoolean())
                || (releaseChannel == UpdateChannel.Evaluation && !prerelease.GetBoolean())
                || !release.TryGetProperty("html_url", out var page) || page.ValueKind != JsonValueKind.String || page.GetString() != Repository + "/releases/tag/" + tag
                || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                continue;
            var expected = InstallerUrl(version, ArchitectureName, releaseChannel);
            var expectedName = expected[(expected.LastIndexOf('/') + 1)..];
            var compatibleAsset = assets.EnumerateArray().FirstOrDefault(asset => asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                && name.GetString() == expectedName && asset.TryGetProperty("browser_download_url", out var uri) && uri.ValueKind == JsonValueKind.String
                && uri.GetString() == expected && asset.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var count)
                && count > 0 && count <= MaximumInstallerBytes
                && asset.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded");
            if (compatibleAsset.ValueKind == JsonValueKind.Object && ParseVersion(version) > CurrentVersion
                && (newest is null || ParseVersion(version) > ParseVersion(newest.Version)))
            {
                AvailableUpdate? evaluation = null;
                if (releaseChannel == UpdateChannel.Evaluation && TryEvaluationAsset(release, compatibleAsset, version, tag, out var asset))
                {
                    var commit = await ReadTagCommitAsync(tag, token).ConfigureAwait(false);
                    evaluation = asset with { Provenance = asset.Provenance! with { SourceCommit = commit } };
                    lock (_evaluations) _evaluations[evaluation] = evaluation.Provenance!;
                }
                var notes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                    ? LimitNotes(body.GetString()) : "";
                newest = new(version, Repository + "/releases/tag/" + tag, ArchitectureName, releaseChannel,
                    evaluation is not null, evaluation, notes);
            }
        }
        return newest;
    }

    private bool TryEvaluationAsset(JsonElement release, JsonElement asset, string version, string tag, out AvailableUpdate update)
    {
        update = null!;
        if (!release.TryGetProperty("id", out var releaseId) || (releaseId.ValueKind != JsonValueKind.Number || !releaseId.TryGetInt64(out var releaseNumber)) || releaseNumber <= 0
            || !asset.TryGetProperty("id", out var assetId) || (assetId.ValueKind != JsonValueKind.Number || !assetId.TryGetInt64(out var assetNumber)) || assetNumber <= 0
            || !asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String
            || digest.GetString() is not { } digestText || !digestText.StartsWith("sha256:", StringComparison.Ordinal)
            || digestText.Length != 71 || !digestText[7..].All(Uri.IsHexDigit)) return false;
        update = new(version, InstallerUrl(version, ArchitectureName, UpdateChannel.Evaluation), digestText[7..],
            asset.GetProperty("size").GetInt64(), 19045, ArchitectureName, UpdateTrustPolicy.OfficialUnsignedEvaluation,
            new(releaseNumber, assetNumber, tag, "", release.TryGetProperty("immutable", out var immutable) && immutable.ValueKind == JsonValueKind.True));
        return true;
    }
    private async Task<string> ReadTagCommitAsync(string tag, CancellationToken token)
    {
        var bytes = await ReadBoundedAsync("https://api.github.com/repos/Sury2797/Notchling/git/ref/tags/" + tag,
            32 * 1024, token).ConfigureAwait(false);
        using var reference = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        if (reference.RootElement.ValueKind != JsonValueKind.Object
            || !reference.RootElement.TryGetProperty("ref", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != "refs/tags/" + tag
            || !reference.RootElement.TryGetProperty("object", out var target) || target.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The evaluation source tag did not match the official release.");
        for (var depth = 0; depth < 3; depth++)
        {
            if (!target.TryGetProperty("sha", out var shaProperty) || shaProperty.ValueKind != JsonValueKind.String
                || !target.TryGetProperty("type", out var typeProperty) || typeProperty.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("The evaluation source reference is invalid.");
            var sha = shaProperty.GetString();
            var type = typeProperty.GetString();
            if (sha is null || sha.Length != 40 || !sha.All(Uri.IsHexDigit))
                throw new InvalidDataException("The evaluation source commit is invalid.");
            if (type == "commit") return sha.ToLowerInvariant();
            if (type != "tag") break;
            var annotation = await ReadBoundedAsync("https://api.github.com/repos/Sury2797/Notchling/git/tags/" + sha,
                32 * 1024, token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(annotation, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("object", out var annotatedTarget)
                || annotatedTarget.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The annotated evaluation tag is invalid.");
            target = annotatedTarget.Clone();
        }
        throw new InvalidDataException("The evaluation tag did not resolve to a pinned source commit.");
    }
    private static string LimitNotes(string? value)
    {
        var bounded = new string((value ?? "").Where(character => !char.IsControl(character) || character is '\n' or '\t').Take(6000).ToArray());
        // Release notes are text, not executable/rendered HTML. Keep a concise
        // native summary instead of exposing GitHub Markdown/table syntax.
        var lines = bounded.Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith('|') && !line.StartsWith("```", StringComparison.Ordinal));
        var text = string.Join("\n", lines.Take(12));
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]\r\n]{1,200})\]\([^\)\r\n]{1,2048}\)", "$1",
            System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^#{1,6}\s+", "",
            System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        text = text.Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal);
        return text.Length > 1200 ? text[..1200].TrimEnd() + "…" : text;
    }

    private async Task RequireEvaluationProvenanceAsync(AvailableUpdate update, CancellationToken token)
    {
        lock (_evaluations)
            if (!_evaluations.TryGetValue(update, out var provenance) || provenance != update.Provenance)
                throw new InvalidDataException("This evaluation was not discovered and pinned by this update operation.");
        var bytes = await ReadBoundedAsync("https://api.github.com/repos/Sury2797/Notchling/releases/" + update.Provenance!.ReleaseId,
            1024 * 1024, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        // Apply the same strict release/architecture checks used during discovery.
        var array = System.Text.Encoding.UTF8.GetBytes("[" + document.RootElement.GetRawText() + "]");
        var refreshed = await DiscoverApiAsync(array, token).ConfigureAwait(false);
        if (refreshed?.VerifiedUpdate != update)
            throw new InvalidDataException("The official evaluation release or installer identity changed. Check for updates again.");
    }

    private async Task<ReleaseUpdate?> DiscoverFeedAsync(CancellationToken token)
    {
        var bytes = await ReadBoundedAsync(ReleasesFeed, 1024 * 1024, token).ConfigureAwait(false);
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var feed = XDocument.Load(reader);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        if (feed.Root?.Name != atom + "feed") throw new InvalidDataException("The release feed is invalid.");
        var candidates = feed.Root.Elements(atom + "entry").Take(30)
            .Select(entry => entry.Elements(atom + "link").FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?.Attribute("href")?.Value)
            .Where(page => page is not null && page.StartsWith(Repository + "/releases/tag/", StringComparison.Ordinal))
            .Select(page => page![(Repository.Length + "/releases/tag/".Length)..])
            .Where(tag => TryReleaseTag(tag, out _, out _, out _))
            .Select(tag => { TryReleaseTag(tag, out var version, out var channel, out _); return (Tag: tag, Version: version, Channel: channel); })
            .Where(candidate => ParseVersion(candidate.Version) > CurrentVersion)
            .OrderByDescending(candidate => ParseVersion(candidate.Version));
        // Only inspect a bounded number of candidates, and require their exact
        // architecture asset before telling users a compatible update exists.
        foreach (var candidate in candidates.Take(3))
        {
            var assets = await ReadBoundedAsync(Repository + "/releases/expanded_assets/" + candidate.Tag, 256 * 1024, token).ConfigureAwait(false);
            var expectedPath = new Uri(InstallerUrl(candidate.Version, ArchitectureName, candidate.Channel)).AbsolutePath;
            var html = System.Text.Encoding.UTF8.GetString(assets);
            if (html.Contains("href=\"" + expectedPath + "\"", StringComparison.Ordinal))
                return new(candidate.Version, Repository + "/releases/tag/" + candidate.Tag, ArchitectureName, candidate.Channel, false, null);
        }
        return null;
    }

    public Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
        => DownloadAsync(update, null, cancellationToken);
    public async Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        ValidateUpdate(update, requireNewer: true);
        CancelPendingInstallation();
        if (!await _downloadGate.WaitAsync(0, cancellationToken)) throw new InvalidOperationException("An update download is already running.");
        string? directory = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            byte[]? publisher;
            if (update.Trust == UpdateTrustPolicy.OfficialUnsignedEvaluation)
            {
                if (await CanVerifyPublisherAsync(deadline.Token).ConfigureAwait(false))
                    throw new InvalidOperationException("A signed installation cannot switch to unsigned evaluation updates.");
                await RequireEvaluationProvenanceAsync(update, deadline.Token).ConfigureAwait(false);
                publisher = null;
            }
            else publisher = await ReadPublisherKeyAsync(_environment.InstalledPublisherKey, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The installed publisher signature cannot be verified.");
            directory = Path.Combine(Path.GetTempPath(), "Notchling.Update", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Notchling-setup.exe");
            using var response = await SendAsync(update.InstallerUrl, deadline.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is { } declared && declared != update.SizeBytes)
                throw new InvalidDataException("The installer size does not match its manifest.");
            progress?.Report(new(UpdateDownloadStage.Downloading, 0, update.SizeBytes));
            await using (var input = await response.Content.ReadAsStreamAsync(deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536]; long total = 0; int count;
                var lastReport = Stopwatch.GetTimestamp();
                while ((count = await input.ReadAsync(buffer, deadline.Token).AsTask().WaitAsync(deadline.Token).ConfigureAwait(false)) > 0)
                {
                    total += count;
                    if (total > update.SizeBytes || total > MaximumInstallerBytes) throw new InvalidDataException("The installer exceeds its permitted size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), deadline.Token).ConfigureAwait(false);
                    if (total == update.SizeBytes || Stopwatch.GetElapsedTime(lastReport) >= TimeSpan.FromMilliseconds(100))
                    { progress?.Report(new(UpdateDownloadStage.Downloading, total, update.SizeBytes)); lastReport = Stopwatch.GetTimestamp(); }
                }
                if (total != update.SizeBytes) throw new InvalidDataException("The installer download is incomplete.");
            }
            progress?.Report(new(UpdateDownloadStage.Verifying, update.SizeBytes, update.SizeBytes));
            await RequireHashAsync(path, update.Sha256, deadline.Token).ConfigureAwait(false);
            if (publisher is not null && (await ReadPublisherKeyAsync(() => _environment.ReadPublisherKey(path), deadline.Token).ConfigureAwait(false) is not { } actual
                || !CryptographicOperations.FixedTimeEquals(publisher, actual)))
                throw new InvalidDataException("The installer signature does not match the trusted application publisher.");
            var prepared = new PreparedUpdate(update.Version, path, update.Sha256, update.Architecture, update.Trust, update.Provenance, update.SizeBytes);
            lock (_prepared) _prepared[path] = prepared;
            progress?.Report(new(UpdateDownloadStage.Ready, update.SizeBytes, update.SizeBytes));
            return prepared;
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            if (directory is not null) DeleteDownload(directory);
            throw new TimeoutException("The installer download or verification timed out. Try again or open the release page.", error);
        }
        catch
        {
            if (directory is not null) DeleteDownload(directory);
            throw;
        }
        finally { _downloadGate.Release(); }
    }

    public async Task OpenInstallerAsync(PreparedUpdate update, CancellationToken cancellationToken = default)
    {
        RequireSupportedPlatform();
        if (ParseVersion(update.Version) <= CurrentVersion || update.Architecture != ArchitectureName)
            throw new InvalidDataException("The prepared update is not newer or does not match this application architecture.");
        lock (_prepared)
            if (!_prepared.TryGetValue(update.InstallerPath, out var prepared) || prepared != update)
                throw new InvalidDataException("The prepared installer does not belong to this update operation.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_requestTimeout);
        try
        {
            byte[]? publisher;
            if (update.Trust == UpdateTrustPolicy.OfficialUnsignedEvaluation)
            {
                if (await CanVerifyPublisherAsync(deadline.Token).ConfigureAwait(false))
                    throw new InvalidOperationException("A signed installation cannot switch to unsigned evaluation updates.");
                await RequireEvaluationProvenanceAsync(new(update.Version,
                    InstallerUrl(update.Version, update.Architecture, UpdateChannel.Evaluation), update.Sha256, update.SizeBytes,
                    19045, update.Architecture, update.Trust, update.Provenance), deadline.Token).ConfigureAwait(false);
                publisher = null;
            }
            else publisher = await ReadPublisherKeyAsync(_environment.InstalledPublisherKey, deadline.Token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The application publisher cannot be verified.");
            await RequireHashAsync(update.InstallerPath, update.Sha256, deadline.Token).ConfigureAwait(false);
            if (publisher is not null && (await ReadPublisherKeyAsync(() => _environment.ReadPublisherKey(update.InstallerPath), deadline.Token).ConfigureAwait(false) is not { } actual
                || !CryptographicOperations.FixedTimeEquals(publisher, actual)))
                throw new InvalidDataException("The prepared installer failed signature verification.");
            deadline.Token.ThrowIfCancellationRequested();
            if (_environment.BeginHandoff is { } handoff) await handoff(update, publisher, deadline.Token).ConfigureAwait(false);
            else await BeginHandoffAsync(update, publisher, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Installer verification timed out. No installer was opened.", error); }
    }

    private async Task BeginHandoffAsync(PreparedUpdate update, byte[]? publisher, CancellationToken token)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("The current application path is unavailable.");
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        if (currentDirectory.Parent?.Name != "app" || currentDirectory.Parent.Parent is not { } installation
            || Path.GetFileName(current) != "Notchling.Windows.exe"
            || !string.Equals(Path.GetDirectoryName(current), currentDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("In-app updates require the installed Notchling application. Development builds can use the release installer.");
        var packagedScript = Path.Combine(AppContext.BaseDirectory, "Update", "update-handoff.ps1");
        if (!File.Exists(packagedScript)) throw new FileNotFoundException("The installed update helper is missing. Repair Notchling using its official installer.", packagedScript);
        var directory = Path.GetDirectoryName(update.InstallerPath)!;
        CancelPendingInstallation();
        var nonce = Guid.NewGuid().ToString("N");
        var script = Path.Combine(directory, "update-handoff-" + nonce + ".ps1");
        var requestPath = Path.Combine(directory, "request-" + nonce + ".json");
        var readyPath = Path.Combine(directory, "ready-" + nonce);
        var cancelPath = Path.Combine(directory, "cancel-" + nonce);
        File.Copy(packagedScript, script, overwrite: false);
        var process = Process.GetCurrentProcess();
        var request = new
        {
            SchemaVersion = 1, ParentId = process.Id, ParentStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks,
            CurrentExecutable = current, ExpectedExecutable = Path.Combine(installation.FullName, "app", update.Version, "Notchling.Windows.exe"),
            InstallDirectory = installation.FullName, update.Version, update.Architecture, update.InstallerPath,
            update.Sha256, update.SizeBytes, Trust = update.Trust.ToString(), update.Provenance,
            PublisherKeySha256 = publisher is null ? null : Convert.ToHexString(publisher), ReadyPath = readyPath, Nonce = nonce,
        };
        await File.WriteAllBytesAsync(requestPath, JsonSerializer.SerializeToUtf8Bytes(request), token).ConfigureAwait(false);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = directory };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-RequestPath", requestPath })
            start.ArgumentList.Add(argument);
        Interlocked.Exchange(ref _pendingHandoffCancelPath, cancelPath);
        using var worker = Process.Start(start) ?? throw new IOException("The Windows update helper could not be started.");
        try
        {
            while (!File.Exists(readyPath))
            {
                token.ThrowIfCancellationRequested();
                if (worker.HasExited) throw new IOException("The update helper stopped before Setup was ready. The current app remains usable.");
                await Task.Delay(100, token).ConfigureAwait(false);
            }
            if (await File.ReadAllTextAsync(readyPath, token).ConfigureAwait(false) != nonce)
                throw new InvalidDataException("The update helper readiness response did not match this operation.");
        }
        catch
        {
            // Cancellation before handoff must not become an installation when
            // the user happens to close the application later.
            try { File.WriteAllText(cancelPath, nonce); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private AvailableUpdate ValidateManifest(Manifest manifest)
    {
        Asset? asset;
        if (manifest.SchemaVersion == 1 && ArchitectureName == "x64" && manifest.Architecture == ArchitectureName)
            asset = new(manifest.Architecture, manifest.InstallerUrl, manifest.Sha256, manifest.SizeBytes);
        else if (manifest.SchemaVersion == 2 && manifest.Assets is { Length: > 0 and <= 3 }
            && manifest.Assets.All(item => item.Architecture is "x64" or "x86" or "arm64")
            && manifest.Assets.Select(item => item.Architecture).Distinct(StringComparer.Ordinal).Count() == manifest.Assets.Length)
            asset = manifest.Assets.SingleOrDefault(item => item.Architecture == ArchitectureName);
        else throw new InvalidDataException("Unsupported update manifest.");
        if (asset is null) throw new InvalidOperationException("This release has no installer for the application architecture.");
        var update = new AvailableUpdate(manifest.Version ?? "", asset.InstallerUrl ?? "", asset.Sha256 ?? "", asset.SizeBytes,
            manifest.MinimumWindowsBuild, asset.Architecture ?? "");
        ValidateUpdate(update, requireNewer: false);
        return update;
    }
    private void ValidateUpdate(AvailableUpdate update, bool requireNewer)
    {
        var version = ParseVersion(update.Version);
        if (update.Architecture != ArchitectureName
            || update.InstallerUrl != InstallerUrl(update.Version, update.Architecture, update.Trust == UpdateTrustPolicy.OfficialUnsignedEvaluation ? UpdateChannel.Evaluation : UpdateChannel.Stable)
            || update.Sha256 is not { Length: 64 } || !update.Sha256.All(Uri.IsHexDigit)
            || update.SizeBytes <= 0 || update.SizeBytes > MaximumInstallerBytes || update.MinimumWindowsBuild < 19045)
            throw new InvalidDataException("Invalid update asset or integrity information.");
        if (!Enum.IsDefined(update.Trust) || (update.Trust == UpdateTrustPolicy.OfficialUnsignedEvaluation
            && (update.Provenance is not { ReleaseId: > 0, AssetId: > 0 } || update.Provenance.Tag != "notchling-evaluation-" + update.Version
                || update.Provenance.SourceCommit is not { Length: 40 } || !update.Provenance.SourceCommit.All(Uri.IsHexDigit))))
            throw new InvalidDataException("Invalid evaluation provenance.");
        RequireSupportedPlatform();
        if (_environment.WindowsBuild < update.MinimumWindowsBuild) throw new InvalidOperationException("This update requires a newer Windows build.");
        if (requireNewer && version <= CurrentVersion) throw new InvalidDataException("Update installers must be newer than the running application.");
    }
    private void RequireSupportedPlatform()
    {
        if (!_environment.IsWindows || _environment.WindowsBuild < 19045 || ArchitectureName is not ("x64" or "x86" or "arm64"))
            throw new InvalidOperationException("Updates require Windows 10 22H2 or Windows 11 with a supported application architecture.");
    }
    private string ArchitectureName => _environment.ProcessArchitecture switch
    { Architecture.X64 => "x64", Architecture.X86 => "x86", Architecture.Arm64 => "arm64", _ => "unsupported" };
    private Version CurrentVersion => new(_environment.Version.Major, _environment.Version.Minor, Math.Max(0, _environment.Version.Build));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 16 };
    private static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Revision >= 0 || version.Build < 0
            || value != $"{version.Major}.{version.Minor}.{version.Build}") throw new InvalidDataException("The release version is invalid.");
        return version;
    }
    private static bool TryReleaseTag(string? value, out string version, out UpdateChannel channel, out string tag)
    {
        version = ""; tag = value ?? ""; channel = UpdateChannel.Stable;
        if (value?.StartsWith("notchling-evaluation-", StringComparison.Ordinal) == true)
        { version = value["notchling-evaluation-".Length..]; channel = UpdateChannel.Evaluation; }
        else if (value?.StartsWith('v') == true) version = value[1..];
        else return false;
        try { _ = ParseVersion(version); return true; } catch (InvalidDataException) { return false; }
    }
    private static string InstallerUrl(string version, string architecture, UpdateChannel channel)
        => Repository + "/releases/download/" + (channel == UpdateChannel.Stable ? "v" : "notchling-evaluation-") + version
            + "/Notchling-" + version + "-windows-" + architecture + (channel == UpdateChannel.Stable ? "-setup.exe" : "-evaluation-setup.exe");
    private async Task<HttpResponseMessage> SendAsync(string uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("Notchling-Updater/1.0");
        request.Headers.Accept.ParseAdd(uri.StartsWith("https://api.github.com/", StringComparison.Ordinal) ? "application/vnd.github+json" : "*/*");
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).WaitAsync(token).ConfigureAwait(false);
        try { response.EnsureSuccessStatusCode(); RequireHttpsResponse(response); return response; }
        catch { response.Dispose(); throw; }
    }
    private async Task<byte[]> ReadBoundedAsync(string uri, int maximum, CancellationToken token)
    {
        using var response = await SendAsync(uri, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > maximum) throw new InvalidDataException("The release response exceeds its size limit.");
        await using var input = await response.Content.ReadAsStreamAsync(token).WaitAsync(token).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await input.ReadAsync(buffer, token).AsTask().WaitAsync(token).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > maximum) throw new InvalidDataException("The release response exceeds its size limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    private static void RequireHttpsResponse(HttpResponseMessage response)
    {
        // GitHub release CDN redirects are HTTPS, with these exact first-party
        // hosts. An unrelated HTTPS endpoint does not become an accepted source.
        var uri = response.RequestMessage?.RequestUri;
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Host is not ("github.com" or "api.github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
            throw new InvalidDataException("An untrusted update redirect was refused.");
    }
    private static async Task RequireHashAsync(string path, string expected, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var actual = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit) || !CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected)))
            throw new InvalidDataException("Installer SHA-256 verification failed.");
    }
    private static void DeleteDownload(string directory)
    { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    internal sealed record WindowsUpdateEnvironment(Version Version, Architecture ProcessArchitecture, int WindowsBuild, bool IsWindows,
        Func<byte[]?> InstalledPublisherKey, Func<string, byte[]?> ReadPublisherKey,
        Func<PreparedUpdate, byte[]?, CancellationToken, Task>? BeginHandoff = null)
    {
        internal static WindowsUpdateEnvironment Installed => new(typeof(WindowsUpdateService).Assembly.GetName().Version ?? new(0, 0, 0),
            RuntimeInformation.ProcessArchitecture, Environment.OSVersion.Version.Build, OperatingSystem.IsWindows(), ReadInstalledPublisherKey, ReadTrustedPublisherKey);
    }
    private sealed record Asset(string? Architecture, string? InstallerUrl, string? Sha256, long SizeBytes);
    private sealed record Manifest(int SchemaVersion, string? Version, int MinimumWindowsBuild, string? Architecture,
        string? InstallerUrl, string? Sha256, long SizeBytes, Asset[]? Assets, string? SignerPublicKeySha256);
    private static byte[]? ReadInstalledPublisherKey()
    {
        if (Environment.ProcessPath is not { } executable) return null;
        var key = ReadTrustedPublisherKey(executable);
        var assembly = ReadTrustedPublisherKey(typeof(WindowsUpdateService).Assembly.Location);
        // A signed dotnet.exe host alone cannot authorize an unsigned DLL.
        return key is not null && assembly is not null && CryptographicOperations.FixedTimeEquals(key, assembly) ? key : null;
    }
    private static byte[]? ReadTrustedPublisherKey(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return null;
        return VerifyAndReadPublisherKey(path);
    }

    private static byte[]? VerifyAndReadPublisherKey(string path)
    {
        var file = new TrustFile { Size = (uint)Marshal.SizeOf<TrustFile>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<TrustFile>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), UiChoice = 2, RevocationChecks = 1, UnionChoice = 1, File = pointer, StateAction = 1, ProviderFlags = 0x80 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try
        {
            if (WinVerifyTrust(new IntPtr(-1), ref action, ref data) != 0) return null;
            var provider = WTHelperProvDataFromStateData(data.StateData);
            if (provider == 0) return null;
            var signer = WTHelperGetProvSignerFromChain(provider, 0, false, 0);
            if (signer == 0) return null;
            var providerCertificate = WTHelperGetProvCertFromChain(signer, 0);
            if (providerCertificate == 0) return null;
            // CRYPT_PROVIDER_CERT starts with DWORD cbStruct then aligned
            // PCCERT_CONTEXT pCert. Read the verified provider's leaf signer,
            // rather than independently guessing a certificate in the PE file.
            var contextPointer = Marshal.ReadIntPtr(providerCertificate, IntPtr.Size);
            if (contextPointer == 0) return null;
            var context = Marshal.PtrToStructure<CertificateContext>(contextPointer);
            if (context.Encoded == 0 || context.EncodedLength is 0 or > 131072) return null;
            var encoded = new byte[context.EncodedLength];
            Marshal.Copy(context.Encoded, encoded, 0, encoded.Length);
            using var certificate = X509CertificateLoader.LoadCertificate(encoded);
            return SHA256.HashData(certificate.GetPublicKey());
        }
        catch (CryptographicException) { return null; }
        finally
        {
            data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<TrustFile>(pointer); Marshal.FreeHGlobal(pointer);
        }
    }
    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(nint window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperProvDataFromStateData(nint stateData);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperGetProvSignerFromChain(nint provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern nint WTHelperGetProvCertFromChain(nint signer, uint certificate);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustFile { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public nint File; public nint KnownSubject; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData { public uint Size; public nint PolicyCallback; public nint SipClient; public uint UiChoice; public uint RevocationChecks; public uint UnionChoice; public nint File; public uint StateAction; public nint StateData; public nint Url; public uint ProviderFlags; public uint UiContext; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CertificateContext { public uint Encoding; public nint Encoded; public uint EncodedLength; public nint Information; public nint Store; }
}
