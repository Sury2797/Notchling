using Notch.Core;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public bool HasThreadAccess => true;
        public bool TryEnqueue(Action action) { action(); return true; }
    }
}

namespace Microsoft.UI.Xaml
{
    public sealed class DispatcherTimer
    {
        public static List<DispatcherTimer> Instances { get; } = [];
        public DispatcherTimer() => Instances.Add(this);
        public TimeSpan Interval { get; set; }
        public bool IsEnabled { get; private set; }
        public event EventHandler<object>? Tick;
        public void Start() => IsEnabled = true;
        public void Stop() => IsEnabled = false;
        public void Fire() { if (IsEnabled) Tick?.Invoke(this, EventArgs.Empty); }
    }
}

namespace Notch.Windows.Services
{
    public sealed class WindowsMediaService : IMediaService
    {
        public static WindowsMediaService Latest { get; private set; } = null!;
        public static Exception? StartFailure { get; set; }
        public WindowsMediaService() => Latest = this;
        public MediaSnapshot? Current { get; set; }
        public int Starts { get; private set; }
        public int PlaybackCommands { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? DisposalFailure { get; set; }
        public event EventHandler<MediaSnapshot?>? Changed;
        public event EventHandler<string>? Error;
        public void ReportError(string error) => Error?.Invoke(this, error);
        public void Publish(MediaSnapshot? media) { Current = media; Changed?.Invoke(this, media); }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            Starts++;
            return StartFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
        }
        public Task PlayPauseAsync() { PlaybackCommands++; return Task.CompletedTask; }
        public Task PreviousAsync() { PlaybackCommands++; return Task.CompletedTask; }
        public Task NextAsync() { PlaybackCommands++; return Task.CompletedTask; }
        public Task SeekAsync(TimeSpan position) { PlaybackCommands++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() { Disposed = true; return DisposalFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask; }
    }
    public sealed class WindowsSystemService : ISystemService
    {
        public static WindowsSystemService Latest { get; private set; } = null!;
        public static Exception? ReadFailure { get; set; }
        public WindowsSystemService() => Latest = this;
        public static SystemSnapshot RealSnapshot => new(91, 82, null, .4, "Actual native device", TimeSpan.FromHours(2));
        public TaskCompletionSource<SystemSnapshot>? ReadGate { get; set; }
        public TaskCompletionSource? ReadStarted { get; set; }
        public TaskCompletionSource? VolumeGate { get; set; }
        public TaskCompletionSource? PortsGate { get; set; }
        public TaskCompletionSource PortsStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SynchronizationContext? PortsContext { get; private set; }
        public IReadOnlyList<int> Ports { get; set; } = [49152];
        public Exception? PortsFailure { get; set; }
        public int PortReads { get; private set; }
        public TaskCompletionSource VolumeStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<double> VolumeRequests { get; } = [];
        public int Reads { get; private set; }
        public List<bool> AwakeRequests { get; } = [];
        public bool Disposed { get; private set; }
        public Exception? DisposalFailure { get; set; }
        public Task<SystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(WindowsSystemService));
            Reads++;
            ReadStarted?.TrySetResult();
            if (ReadGate is { } gate) return gate.Task;
            return ReadFailure is { } failure ? Task.FromException<SystemSnapshot>(failure) : Task.FromResult(RealSnapshot);
        }
        public Task SetVolumeAsync(double volume)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            VolumeRequests.Add(volume);
            VolumeStarted.TrySetResult();
            return VolumeGate?.Task ?? Task.CompletedTask;
        }
        public IReadOnlyList<int> ListeningPorts()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            PortReads++;
            PortsContext = SynchronizationContext.Current;
            PortsStarted.TrySetResult();
            PortsGate?.Task.GetAwaiter().GetResult();
            ObjectDisposedException.ThrowIf(Disposed, this);
            if (PortsFailure is { } failure) throw failure;
            return Ports.ToArray();
        }
        public void SetAwake(bool awake) { ObjectDisposedException.ThrowIf(Disposed, this); AwakeRequests.Add(awake); }
        public void Dispose() { Disposed = true; if (DisposalFailure is { } failure) throw failure; }
    }
    public sealed class WindowsSecretVault : ISecretVault
    {
        private readonly Dictionary<string, string> _values = [];
        public void Save(string name, string value) => _values[name] = value;
        public string? Read(string name) => _values.GetValueOrDefault(name);
        public void Delete(string name) => _values.Remove(name);
    }
    public sealed class ClipboardService(Microsoft.UI.Dispatching.DispatcherQueue dispatcher) : IDisposable
    {
        public bool Enabled { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? DisposalFailure { get; set; }
        public Exception? EnableFailure { get; set; }
        public event EventHandler<IReadOnlyList<ClipboardItem>>? Changed;
        public event EventHandler<string>? Error;
        public void SetEnabled(bool enabled) { if (EnableFailure is { } failure) throw failure; Enabled = enabled; }
        public void Publish(IReadOnlyList<ClipboardItem> items) => dispatcher.TryEnqueue(() => Changed?.Invoke(this, items));
        public Task CopyAsync(string text) => Task.CompletedTask;
        public void Clear() => Publish([]);
        public void ReportError(string error) => Error?.Invoke(this, error);
        public void Dispose() { Disposed = true; Clear(); if (DisposalFailure is { } failure) throw failure; }
    }
}
