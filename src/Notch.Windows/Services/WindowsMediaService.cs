using Notch.Core;
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
    private MediaSnapshot? _current;
    private string? _artworkKey;
    private string? _artworkPath;
    private bool _disposed;

    public MediaSnapshot? Current { get { lock (_gate) return _current; } }
    public event EventHandler<MediaSnapshot?>? Changed;
    public event EventHandler<string>? Error;

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
                    manager.CurrentSessionChanged += OnCurrentSessionChanged;
                    manager.SessionsChanged += OnSessionsChanged;
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
    private void OnPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => ScheduleRefresh();
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => ScheduleRefresh();
    private void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed) return;
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
            await RefreshAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!_disposed)
            {
                Publish(null);
                Error?.Invoke(this, $"Media information is unavailable: {error.Message}");
            }
        }
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        await _refreshGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var session = _manager?.GetCurrentSession();
            lock (_gate)
            {
                if (_disposed) return;
                if (_session != session)
                {
                    UnsubscribeSession();
                    _session = session;
                    _artworkKey = null;
                    _artworkPath = null;
                    if (session is not null)
                    {
                        session.MediaPropertiesChanged += OnPropertiesChanged;
                        session.PlaybackInfoChanged += OnPlaybackChanged;
                        session.TimelinePropertiesChanged += OnTimelineChanged;
                    }
                }
            }
            if (session is null)
            {
                Publish(null);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync().AsTask(token)
                .WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var duration = timeline.EndTime > timeline.StartTime ? timeline.EndTime - timeline.StartTime : TimeSpan.Zero;
            var position = timeline.Position - timeline.StartTime;
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var playbackRate = playback.PlaybackRate ?? 1;
            if (!double.IsFinite(playbackRate)) playbackRate = 1;
            playbackRate = Math.Clamp(playbackRate, -16, 16);
            var positionUpdatedAt = DateTimeOffset.UtcNow;
            if (playing && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
            {
                var elapsed = positionUpdatedAt - timeline.LastUpdatedTime;
                if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromDays(1))
                    position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds + elapsed.TotalSeconds * playbackRate,
                        0, duration.TotalSeconds));
            }
            position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, duration.Ticks));
            var artworkKey = $"{session.SourceAppUserModelId}\n{properties.Title}\n{properties.Artist}\n{properties.AlbumTitle}";
            if (artworkKey != _artworkKey)
            {
                _artworkPath = await CacheArtworkAsync(properties.Thumbnail, token).ConfigureAwait(false);
                _artworkKey = artworkKey;
            }
            token.ThrowIfCancellationRequested();
            Publish(new MediaSnapshot(properties.Title, properties.Artist, _artworkPath, playing,
                position, duration, session.SourceAppUserModelId,
                playback.Controls.IsPlaybackPositionEnabled && duration > TimeSpan.Zero,
                playback.Controls.IsPlayEnabled, playback.Controls.IsPauseEnabled,
                playback.Controls.IsPreviousEnabled, playback.Controls.IsNextEnabled,
                positionUpdatedAt, playbackRate));
        }
        finally { _refreshGate.Release(); }
    }

    private void Publish(MediaSnapshot? snapshot)
    {
        lock (_gate)
        {
            if (_disposed || Equals(_current, snapshot)) return;
            _current = snapshot;
        }
        Changed?.Invoke(this, snapshot);
    }

    private async Task<string?> CacheArtworkAsync(IRandomAccessStreamReference? thumbnail, CancellationToken token)
    {
        if (thumbnail is null) return null;
        try
        {
            using var source = await thumbnail.OpenReadAsync().AsTask(token)
                .WaitAsync(TimeSpan.FromSeconds(3), token).ConfigureAwait(false);
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
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    copied += count;
                    if (copied > MaximumArtworkBytes) throw new IOException("Media artwork exceeds the cache limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                return path;
            }
            catch
            {
                try { File.Delete(path); } catch { }
                throw;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException or System.Runtime.InteropServices.COMException)
        {
            return null; // A missing artwork preview must not hide a valid playback session.
        }
    }

    private void TrimArtworkCache()
    {
        var files = new DirectoryInfo(_artworkDirectory).EnumerateFiles()
            .Where(file => file.Extension is ".png" or ".jpg" or ".webp" or ".gif" or ".bmp")
            .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
        long bytes = 0;
        for (var index = 0; index < files.Length; index++)
        {
            bytes += files[index].Length;
            // Leave room for the next bounded four-megabyte thumbnail.
            if (index >= MaximumArtworkFiles - 1 || bytes > 20 * 1024 * 1024)
            {
                try { files[index].Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private void UnsubscribeSession()
    {
        if (_session is null) return;
        _session.MediaPropertiesChanged -= OnPropertiesChanged;
        _session.PlaybackInfoChanged -= OnPlaybackChanged;
        _session.TimelinePropertiesChanged -= OnTimelineChanged;
        _session = null;
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
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager.SessionsChanged -= OnSessionsChanged;
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
