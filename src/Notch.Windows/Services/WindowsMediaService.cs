using Notch.Core;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Notch.Windows.Services;

/// <summary>Reads the active Windows media session; no synthetic player or polling loop.</summary>
public sealed class WindowsMediaService : IMediaService
{
    private const long MaximumArtworkBytes = 4 * 1024 * 1024;
    private const int MaximumArtworkFiles = 12;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _artworkDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch", "Cache", "Media");
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _activeRefresh;
    private MediaSnapshot? _current;
    private string? _artworkKey;
    private string? _artworkPath;
    private long _refreshVersion;
    private long _sessionRevision;
    private long _propertiesVersion;
    private long _artworkVersion = -1;
    private string? _sourceIdentity;
    private string? _sourceDisplayName;
    private string? _sourceIconPath;
    private DateTimeOffset _sourceResolvedAt;
    private GlobalSystemMediaTransportControlsSession? _knownMetadataSession;
    private long _knownPropertiesVersion = -1;
    private string _knownTitle = "";
    private string _knownArtist = "";
    private string _knownAlbum = "";
    private bool _disposed;

    public MediaSnapshot? Current { get { lock (_gate) return _current; } }
    public event EventHandler<MediaSnapshot?>? Changed;
    public event EventHandler<string>? Error;

    public WindowsMediaService() { }
    internal WindowsMediaService(string artworkDirectory) => _artworkDirectory = artworkDirectory;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            if (_manager is null)
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                    .AsTask(lifetime.Token).WaitAsync(TimeSpan.FromSeconds(5), lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (_disposed) return;
                    _manager = manager;
                    TrySessionEvent(() => manager.CurrentSessionChanged += OnCurrentSessionChanged);
                    TrySessionEvent(() => manager.SessionsChanged += OnSessionsChanged);
                }
            }
            // Repeated Start is a real recoverable refresh, including after a transient media error.
            await RefreshAsync(lifetime.Token).ConfigureAwait(false);
        }
        finally { _startGate.Release(); }
    }

    public async Task PlayPauseAsync()
    {
        var session = RequireSession();
        var playback = session.GetPlaybackInfo();
        bool succeeded;
        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            if (!playback.Controls.IsPauseEnabled) throw Unsupported("Pause");
            succeeded = await session.TryPauseAsync().AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token);
        }
        else
        {
            if (!playback.Controls.IsPlayEnabled) throw Unsupported("Play");
            succeeded = await session.TryPlayAsync().AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token);
        }
        EnsureAccepted(succeeded);
        ScheduleRefresh();
    }

    public async Task PreviousAsync()
    {
        var session = RequireSession();
        if (!session.GetPlaybackInfo().Controls.IsPreviousEnabled) throw Unsupported("Previous track");
        EnsureAccepted(await session.TrySkipPreviousAsync().AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token));
        ScheduleRefresh();
    }

    public async Task NextAsync()
    {
        var session = RequireSession();
        if (!session.GetPlaybackInfo().Controls.IsNextEnabled) throw Unsupported("Next track");
        EnsureAccepted(await session.TrySkipNextAsync().AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token));
        ScheduleRefresh();
    }

    public async Task SeekAsync(TimeSpan position)
    {
        var session = RequireSession();
        if (!session.GetPlaybackInfo().Controls.IsPlaybackPositionEnabled) throw Unsupported("Seeking");
        var timeline = session.GetTimelineProperties();
        var duration = timeline.EndTime - timeline.StartTime;
        if (duration <= TimeSpan.Zero) throw Unsupported("Seeking");
        var clamped = Math.Clamp(position.Ticks, 0, duration.Ticks);
        EnsureAccepted(await session.TryChangePlaybackPositionAsync(timeline.StartTime.Ticks + clamped)
            .AsTask(_lifetime.Token).WaitAsync(TimeSpan.FromSeconds(3), _lifetime.Token));
        ScheduleRefresh();
    }

    private GlobalSystemMediaTransportControlsSession RequireSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
            return _session ?? throw new InvalidOperationException("No Windows media session is active. Start playback in a supported player.");
    }

    private static InvalidOperationException Unsupported(string operation) =>
        new($"{operation} is not supported by the active media application.");
    private static void EnsureAccepted(bool accepted)
    {
        if (!accepted) throw new InvalidOperationException("The media application did not accept this control request.");
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => ScheduleRefresh();
    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => ScheduleRefresh();
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        Interlocked.Increment(ref _propertiesVersion);
        ScheduleRefresh();
    }
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => ScheduleRefresh();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => ScheduleRefresh(invalidateActive: false);

    private void ScheduleRefresh(bool invalidateActive = true)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
            if (invalidateActive)
            {
                _refreshVersion++;
                _activeRefresh?.Cancel();
            }
            _debounce?.Cancel();
            _debounce?.Dispose();
            _debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _debounce.Token;
        }
        _ = DebouncedRefreshAsync(token);
    }

    private async Task DebouncedRefreshAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(60, token).ConfigureAwait(false);
            await RefreshAsync(token, nativeEvent: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_disposed)
            {
                Error?.Invoke(this, $"Media information is unavailable: {error.Message}");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken, bool nativeEvent = false)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var refresh = nativeEvent ? CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        lock (_gate) _activeRefresh = refresh;
        var token = refresh.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            var session = FindSession();
            long refreshVersion;
            long sessionRevision;
            lock (_gate)
            {
                if (_disposed) return;
                refreshVersion = _refreshVersion;
                if (_session != session)
                {
                    UnsubscribeSession();
                    _session = session;
                    _sessionRevision++;
                    _artworkKey = null;
                    _artworkPath = null;
                    _artworkVersion = -1;
                    _knownMetadataSession = null;
                    _knownPropertiesVersion = -1;
                    if (session is not null)
                    {
                        TrySessionEvent(() => session.MediaPropertiesChanged += OnPropertiesChanged);
                        TrySessionEvent(() => session.PlaybackInfoChanged += OnPlaybackChanged);
                        TrySessionEvent(() => session.TimelinePropertiesChanged += OnTimelineChanged);
                    }
                }
                sessionRevision = _sessionRevision;
            }
            if (session is null)
            {
                PublishCurrent(null, refreshVersion, null);
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties? properties = null;
            var propertiesVersion = Interlocked.Read(ref _propertiesVersion);
            try
            {
                properties = await session.TryGetMediaPropertiesAsync().AsTask(token)
                    .WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            }
            catch (Exception error) when (IsUnavailableSession(error) || error is TimeoutException or ArgumentException) { }
            token.ThrowIfCancellationRequested();
            if (!IsCurrentRefresh(session, refreshVersion)) return;
            var playback = session.GetPlaybackInfo();
            // Live streams and some otherwise valid players expose no timeline. Their transport
            // controls must remain usable even when the optional seek metadata is unavailable.
            GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline = null;
            try { timeline = session.GetTimelineProperties(); }
            catch (Exception error) when (IsUnavailableSession(error)) { }
            var duration = timeline is not null && timeline.EndTime > timeline.StartTime
                ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero;
            var position = timeline is null ? TimeSpan.Zero : timeline.Position - timeline.StartTime;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var playbackRate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(playbackRate)) playbackRate = 1;
            playbackRate = Math.Clamp(playbackRate, -16, 16);
            var positionUpdatedAt = DateTimeOffset.UtcNow;
            if (playing && timeline is not null && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
            {
                var elapsed = positionUpdatedAt - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
                    position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds + elapsed.TotalSeconds * playbackRate,
                        0, duration.TotalSeconds));
            }
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
            var source = session.SourceAppUserModelId;
            var keepKnownMetadata = properties is null && _knownMetadataSession == session && _knownPropertiesVersion == propertiesVersion;
            var title = keepKnownMetadata ? _knownTitle : MediaPresentation.CleanMetadata(properties?.Title);
            var artist = keepKnownMetadata ? _knownArtist : MediaPresentation.CleanMetadata(properties?.Artist, 256);
            var album = keepKnownMetadata ? _knownAlbum : MediaPresentation.CleanMetadata(properties?.AlbumTitle, 256);
            if (properties is not null)
            {
                _knownMetadataSession = session; _knownPropertiesVersion = propertiesVersion;
                _knownTitle = title; _knownArtist = artist; _knownAlbum = album;
            }
            var artworkKey = $"{source}\n{title}\n{artist}\n{album}";
            var artworkChanged = artworkKey != _artworkKey || propertiesVersion != _artworkVersion;
            var sourceChanged = source != _sourceIdentity;
            var refreshSource = sourceChanged || (_sourceIconPath is null && DateTimeOffset.UtcNow - _sourceResolvedAt > TimeSpan.FromMinutes(1));
            var state = playback.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackState.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackState.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackState.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackState.Loading,
                _ => MediaPlaybackState.Unknown
            };
            var snapshot = new MediaSnapshot(title, artist, artworkKey != _artworkKey ? null : _artworkPath, playing,
                position, duration, source,
                playback.Controls.IsPlaybackPositionEnabled && duration > TimeSpan.Zero,
                playback.Controls.IsPlayEnabled, playback.Controls.IsPauseEnabled,
                playback.Controls.IsPreviousEnabled, playback.Controls.IsNextEnabled,
                positionUpdatedAt, playbackRate, album,
                sourceChanged ? null : _sourceDisplayName, sourceChanged ? null : _sourceIconPath, state, sessionRevision);
            // Metadata and transport become available before optional thumbnail/logo I/O.
            // A slow player preview must not postpone its title or playback controls.
            if (!PublishCurrent(session, refreshVersion, snapshot)) return;
            if (artworkChanged)
            {
                var artworkPath = await CacheArtworkAsync(properties?.Thumbnail, token).ConfigureAwait(false);
                if (!IsCurrentRefresh(session, refreshVersion)) return;
                if (artworkPath is null && properties?.Thumbnail is not null && artworkKey == _artworkKey)
                    artworkPath = _artworkPath;
                _artworkPath = artworkPath;
                _artworkKey = artworkKey;
                _artworkVersion = propertiesVersion;
            }
            token.ThrowIfCancellationRequested();
            if (refreshSource)
            {
                var (displayName, iconPath) = await ReadSourceApplicationAsync(source, token).ConfigureAwait(false);
                if (!IsCurrentRefresh(session, refreshVersion)) return;
                _sourceIdentity = source;
                _sourceDisplayName = displayName ?? (sourceChanged ? null : _sourceDisplayName);
                _sourceIconPath = iconPath;
                _sourceResolvedAt = DateTimeOffset.UtcNow;
            }
            PublishCurrent(session, refreshVersion, snapshot with
            {
                ArtworkPath = _artworkPath, SourceDisplayName = _sourceDisplayName, SourceIconPath = _sourceIconPath
            });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_lifetime.IsCancellationRequested)
        { /* A newer native event superseded this refresh; its scheduled read owns publication. */ }
        finally
        {
            lock (_gate) { if (ReferenceEquals(_activeRefresh, refresh)) _activeRefresh = null; }
            _refreshGate.Release();
        }
    }

    private bool IsCurrentRefresh(GlobalSystemMediaTransportControlsSession session, long version)
    {
        lock (_gate)
            return !_disposed && _session == session && _refreshVersion == version && FindSession() == session;
    }

    private bool PublishCurrent(GlobalSystemMediaTransportControlsSession? session, long version, MediaSnapshot? snapshot)
    {
        lock (_gate)
        {
            if (_disposed || _session != session || _refreshVersion != version || FindSession() != session) return false;
            if (Equals(_current, snapshot)) return true;
            _current = snapshot;
        }
        Changed?.Invoke(this, snapshot);
        return true;
    }

    private async Task<(string? Name, string? Icon)> ReadSourceApplicationAsync(string source, CancellationToken token)
    {
        // The OS resolves this exact application ID. Never turn metadata into an executable/file
        // path or a remote favicon request. Unregistered desktop IDs simply use the neutral fallback.
        if (string.IsNullOrWhiteSpace(source) || source.Length > 512 || source.Any(char.IsControl)) return (null, null);
        string? displayName = null;
        try
        {
            var info = AppInfo.GetFromAppUserModelId(source);
            if (info is null) return (null, null);
            displayName = MediaPresentation.CleanMetadata(info.DisplayInfo.DisplayName, 80);
            var icon = await CacheArtworkAsync(info.DisplayInfo.GetLogo(new Size(256, 256)), token).ConfigureAwait(false);
            return (displayName.Length > 0 ? displayName : null, icon);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (IsUnavailableSession(error) || error is ArgumentException or InvalidOperationException or IOException or TimeoutException)
        { return (displayName, null); }
    }

    private async Task<string?> CacheArtworkAsync(IRandomAccessStreamReference? thumbnail, CancellationToken token)
    {
        if (thumbnail is null) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var readToken = deadline.Token;
        try
        {
            using var source = await thumbnail.OpenReadAsync().AsTask(readToken)
                .WaitAsync(TimeSpan.FromSeconds(3), readToken).ConfigureAwait(false);
            if (source.Size == 0 || source.Size > MaximumArtworkBytes) return null;
            var extension = source.ContentType switch
            {
                "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp",
                "image/gif" => ".gif", "image/bmp" => ".bmp", _ => null
            };
            if (extension is null) return null;
            Directory.CreateDirectory(_artworkDirectory);
            TrimArtworkCache();
            var path = Path.Combine(_artworkDirectory, Guid.NewGuid().ToString("N") + extension);
            try
            {
                using var input = source.AsStreamForRead();
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, true);
                var buffer = new byte[81920];
                long copied = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, readToken).AsTask().WaitAsync(readToken).ConfigureAwait(false)) > 0)
                {
                    copied += count;
                    if (copied > MaximumArtworkBytes) throw new IOException("Media artwork exceeds the cache limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), readToken).AsTask().WaitAsync(readToken).ConfigureAwait(false);
                }
                if (copied == 0) throw new IOException("The media application supplied empty artwork.");
                return path;
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException
            or ArgumentException or ObjectDisposedException or System.Security.SecurityException || IsUnavailableSession(error))
        {
            return null; // A missing artwork preview must not hide a valid playback session.
        }
    }

    private void TrimArtworkCache()
    {
        var files = new DirectoryInfo(_artworkDirectory).EnumerateFiles()
            .Where(file => file.Extension is ".png" or ".jpg" or ".webp" or ".gif" or ".bmp")
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        var protectedFiles = files.Where(file => file.FullName == _artworkPath || file.FullName == _sourceIconPath).ToArray();
        var retained = protectedFiles.Length;
        long bytes = protectedFiles.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (file.FullName == _artworkPath || file.FullName == _sourceIconPath) continue;
            // Leave room for the next bounded four-megabyte thumbnail.
            if (retained >= MaximumArtworkFiles - 1 || bytes + file.Length > 20 * 1024 * 1024)
            {
                try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else { retained++; bytes += file.Length; }
        }
    }

    private void UnsubscribeSession()
    {
        var session = _session;
        if (session is null) return;
        _session = null;
        // A player can close while its COM event registration is being revoked.
        TrySessionEvent(() => session.MediaPropertiesChanged -= OnPropertiesChanged);
        TrySessionEvent(() => session.PlaybackInfoChanged -= OnPlaybackChanged);
        TrySessionEvent(() => session.TimelinePropertiesChanged -= OnTimelineChanged);
    }

    private GlobalSystemMediaTransportControlsSession? FindSession()
    {
        GlobalSystemMediaTransportControlsSession? current = null;
        try { current = _manager?.GetCurrentSession(); }
        catch (Exception error) when (IsUnavailableSession(error)) { }
        if (current is not null)
        {
            try
            {
                if (current.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed) return current;
            }
            catch (Exception error) when (IsUnavailableSession(error)) { }
        }
        if (_manager is null) return null;
        // Windows can have a playing session without designating a current one, particularly
        // immediately after a player starts. Prefer playback and then a paused session.
        GlobalSystemMediaTransportControlsSession? paused = null;
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
        try { sessions = _manager.GetSessions(); }
        catch (Exception error) when (IsUnavailableSession(error)) { return null; }
        foreach (var candidate in sessions)
        {
            try
            {
                var status = candidate.GetPlaybackInfo().PlaybackStatus;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) return candidate;
                if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) paused ??= candidate;
            }
            catch (Exception error) when (IsUnavailableSession(error)) { }
        }
        return paused;
    }

    private static bool IsUnavailableSession(Exception error) => error is COMException
        or InvalidComObjectException or NotSupportedException or UnauthorizedAccessException;

    private static void TrySessionEvent(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (IsUnavailableSession(error)) { }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _debounce?.Cancel();
            if (_manager is not null)
            {
                TrySessionEvent(() => _manager.CurrentSessionChanged -= OnCurrentSessionChanged);
                TrySessionEvent(() => _manager.SessionsChanged -= OnSessionsChanged);
            }
            UnsubscribeSession();
        }
        // Wait for in-flight thumbnail I/O before releasing synchronization objects.
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        _refreshGate.Release();
        await _startGate.WaitAsync().ConfigureAwait(false);
        _startGate.Release();
        _debounce?.Dispose();
        _lifetime.Dispose();
        // Canceled refresh continuations can still observe the gate; leave its tiny managed object valid.
    }
}
