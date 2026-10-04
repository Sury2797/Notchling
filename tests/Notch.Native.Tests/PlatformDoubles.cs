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
        public static event EventHandler<object>? ContentChanged;
        public static DataPackageView GetContent() => _content;
        public static void SetContent(DataPackage package) => Publish(new(Task.FromResult(package.Text)));
        public static void Publish(DataPackageView content) { _content = content; ContentChanged?.Invoke(null, new()); }
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
