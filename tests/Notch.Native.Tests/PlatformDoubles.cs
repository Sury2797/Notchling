using System.Collections.Concurrent;

// Deliberate API doubles: this project is not a native Windows runtime test.
namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        public DispatcherQueue()
        {
            _thread = new(() => { foreach (var action in _queue.GetConsumingEnumerable()) action(); }) { IsBackground = true };
            _thread.Start();
        }
        public bool HasThreadAccess => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;
        public bool TryEnqueue(Action action)
        {
            try { _queue.Add(action); return true; }
            catch (InvalidOperationException) { return false; }
        }
        public Task<T> InvokeAsync<T>(Func<T> action)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!TryEnqueue(() => { try { result.SetResult(action()); } catch (Exception error) { result.SetException(error); } }))
                result.SetException(new InvalidOperationException("Dispatcher is closed."));
            return result.Task;
        }
        public Task InvokeAsync(Action action) => InvokeAsync(() => { action(); return true; });
        public void Dispose() { _queue.CompleteAdding(); _thread.Join(); _queue.Dispose(); }
    }
}
namespace Windows.ApplicationModel.DataTransfer
{
    public static class StandardDataFormats { public const string Text = "Text"; }
    public sealed class DataPackageView(Task<string> text, bool excluded = false)
    {
        public bool Contains(string format) => format == StandardDataFormats.Text || (excluded && format == "ExcludeClipboardContentFromMonitorProcessing");
        public Task<string> GetTextAsync() => text;
    }
    public sealed class DataPackage
    {
        internal string Text { get; private set; } = string.Empty;
        public void SetText(string text) => Text = text;
    }
    public static class Clipboard
    {
        private static DataPackageView _content = new(Task.FromResult(string.Empty));
        private static EventHandler<object>? _contentChanged;
        public static Exception? RegistrationFailure { get; set; }
        public static Exception? RevocationFailure { get; set; }
        public static event EventHandler<object>? ContentChanged
        {
            add { if (RegistrationFailure is not null) throw RegistrationFailure; _contentChanged += value; }
            remove { if (RevocationFailure is not null) throw RevocationFailure; _contentChanged -= value; }
        }
        public static DataPackageView GetContent() => _content;
        public static void SetContent(DataPackage package) => Publish(new(Task.FromResult(package.Text)));
        public static void Publish(DataPackageView content) { _content = content; _contentChanged?.Invoke(null, new()); }
    }
}
namespace Windows.Media.Control
{
    public sealed class CurrentSessionChangedEventArgs;
    public sealed class SessionsChangedEventArgs;
    public sealed class MediaPropertiesChangedEventArgs;
    public sealed class PlaybackInfoChangedEventArgs;
    public sealed class TimelinePropertiesChangedEventArgs;
    public enum GlobalSystemMediaTransportControlsSessionPlaybackStatus { Closed, Opened, Changing, Stopped, Playing, Paused }
    public sealed class GlobalSystemMediaTransportControlsSessionPlaybackControls
    {
        public bool IsPlaybackPositionEnabled { get; set; } = true;
        public bool IsPlayEnabled { get; set; } = true;
        public bool IsPauseEnabled { get; set; } = true;
        public bool IsPreviousEnabled { get; set; } = true;
        public bool IsNextEnabled { get; set; } = true;
    }
    public sealed class GlobalSystemMediaTransportControlsSessionPlaybackInfo
    {
        public GlobalSystemMediaTransportControlsSessionPlaybackStatus PlaybackStatus { get; set; } = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        public GlobalSystemMediaTransportControlsSessionPlaybackControls Controls { get; } = new();
        public double? PlaybackRate { get; set; }
    }
    public sealed class GlobalSystemMediaTransportControlsSessionTimelineProperties
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; } = TimeSpan.FromMinutes(3);
        public TimeSpan Position { get; set; } = TimeSpan.FromSeconds(20);
        public DateTimeOffset LastUpdatedTime { get; set; } = DateTimeOffset.MinValue;
    }
    public sealed class GlobalSystemMediaTransportControlsSessionMediaProperties
    {
        public string Title { get; set; } = "Real player title";
        public string Artist { get; set; } = "Real player artist";
        public string AlbumTitle { get; set; } = string.Empty;
        public Windows.Storage.Streams.IRandomAccessStreamReference? Thumbnail { get; set; }
    }
    public sealed class GlobalSystemMediaTransportControlsSession
    {
        private Action<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>? _propertiesChanged;
        private Action<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>? _playbackChanged;
        private Action<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>? _timelineChanged;
        public GlobalSystemMediaTransportControlsSessionMediaProperties Properties { get; } = new();
        public GlobalSystemMediaTransportControlsSessionPlaybackInfo Playback { get; } = new();
        public GlobalSystemMediaTransportControlsSessionTimelineProperties Timeline { get; } = new();
        public Exception? PlaybackFailure { get; set; }
        public Exception? TimelineFailure { get; set; }
        public Exception? EventFailure { get; set; }
        public string SourceAppUserModelId { get; set; } = "Test.player";
        public int PlayCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public long? SeekTicks { get; private set; }
        public bool AcceptControl { get; set; } = true;
        public Task<GlobalSystemMediaTransportControlsSessionMediaProperties> TryGetMediaPropertiesAsync() => Task.FromResult(Properties);
        public GlobalSystemMediaTransportControlsSessionPlaybackInfo GetPlaybackInfo() => PlaybackFailure is null ? Playback : throw PlaybackFailure;
        public GlobalSystemMediaTransportControlsSessionTimelineProperties GetTimelineProperties() => TimelineFailure is null ? Timeline : throw TimelineFailure;
        public Task<bool> TryPauseAsync() { PauseCalls++; return Task.FromResult(AcceptControl); }
        public Task<bool> TryPlayAsync() { PlayCalls++; return Task.FromResult(AcceptControl); }
        public Task<bool> TrySkipPreviousAsync() => Task.FromResult(AcceptControl);
        public Task<bool> TrySkipNextAsync() => Task.FromResult(AcceptControl);
        public Task<bool> TryChangePlaybackPositionAsync(long ticks) { SeekTicks = ticks; return Task.FromResult(AcceptControl); }
        public event Action<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> MediaPropertiesChanged
        {
            add { if (EventFailure is not null) throw EventFailure; _propertiesChanged += value; }
            remove { if (EventFailure is not null) throw EventFailure; _propertiesChanged -= value; }
        }
        public event Action<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> PlaybackInfoChanged
        {
            add { if (EventFailure is not null) throw EventFailure; _playbackChanged += value; }
            remove { if (EventFailure is not null) throw EventFailure; _playbackChanged -= value; }
        }
        public event Action<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> TimelinePropertiesChanged
        {
            add { if (EventFailure is not null) throw EventFailure; _timelineChanged += value; }
            remove { if (EventFailure is not null) throw EventFailure; _timelineChanged -= value; }
        }
        public void NotifyPlayback() => _playbackChanged?.Invoke(this, new());
    }
    public sealed class GlobalSystemMediaTransportControlsSessionManager
    {
        private Action<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs>? _currentChanged;
        private Action<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs>? _sessionsChanged;
        public static GlobalSystemMediaTransportControlsSessionManager Available { get; set; } = new();
        public GlobalSystemMediaTransportControlsSession? Current { get; set; }
        public List<GlobalSystemMediaTransportControlsSession> Sessions { get; } = [];
        public Exception? EventFailure { get; set; }
        public static Task<GlobalSystemMediaTransportControlsSessionManager> RequestAsync() => Task.FromResult(Available);
        public GlobalSystemMediaTransportControlsSession? GetCurrentSession() => Current;
        public IReadOnlyList<GlobalSystemMediaTransportControlsSession> GetSessions() => Sessions;
        public event Action<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> CurrentSessionChanged
        {
            add { if (EventFailure is not null) throw EventFailure; _currentChanged += value; }
            remove { if (EventFailure is not null) throw EventFailure; _currentChanged -= value; }
        }
        public event Action<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> SessionsChanged
        {
            add { if (EventFailure is not null) throw EventFailure; _sessionsChanged += value; }
            remove { if (EventFailure is not null) throw EventFailure; _sessionsChanged -= value; }
        }
        public void NotifyCurrent() => _currentChanged?.Invoke(this, new());
    }
}
namespace Windows.Storage.Streams
{
    public interface IRandomAccessStreamReference
    {
        Task<RandomAccessStream> OpenReadAsync();
    }
    public sealed class RandomAccessStream(Stream stream, string contentType) : IDisposable
    {
        public ulong Size => (ulong)stream.Length;
        public string ContentType { get; } = contentType;
        public Stream AsStreamForRead() => stream;
        public void Dispose() => stream.Dispose();
    }
    public sealed class FailingArtwork(Exception error) : IRandomAccessStreamReference
    {
        public Task<RandomAccessStream> OpenReadAsync() => Task.FromException<RandomAccessStream>(error);
    }
}
namespace Windows.Storage
{
    public sealed class StorageFile(string path)
    {
        public string Path { get; } = path;
        public static Func<string, Task<StorageFile>> Loader { get; set; } = path => Task.FromResult(new StorageFile(path));
        public static Task<StorageFile> GetFileFromPathAsync(string path) => Loader(path);
    }
}
namespace Windows.Media.Core
{
    public sealed class MediaSource(Windows.Storage.StorageFile file)
    {
        public Windows.Storage.StorageFile File { get; } = file;
        public static MediaSource CreateFromStorageFile(Windows.Storage.StorageFile file) => new(file);
    }
}
namespace Windows.Media.Playback
{
    public sealed class MediaPlayerFailedEventArgs;
    public sealed class MediaPlayer : IDisposable
    {
        public static bool ComponentAvailable { get; set; } = true;
        public static MediaPlayer? Latest { get; private set; }
        public MediaPlayer()
        {
            if (!ComponentAvailable) throw new InvalidOperationException("Optional component unavailable.");
            Latest = this;
        }
        public bool IsLoopingEnabled { get; set; }
        public double Volume { get; set; }
        public Windows.Media.Core.MediaSource? Source { get; set; }
        public int PlayCalls { get; private set; }
        public bool IsPlaying { get; private set; }
        public event Action<MediaPlayer, MediaPlayerFailedEventArgs>? MediaFailed;
        public void Play() { PlayCalls++; IsPlaying = true; }
        public void Pause() => IsPlaying = false;
        public void Fail() => MediaFailed?.Invoke(this, new());
        public void Dispose() => IsPlaying = false;
    }
}
namespace System
{
    public static class FakeWinRtTaskExtensions
    {
        public static Task<T> AsTask<T>(this Task<T> task, CancellationToken token) => task.WaitAsync(token);
    }
}
