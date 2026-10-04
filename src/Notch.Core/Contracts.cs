namespace Notch.Core;

public interface IMediaService : IAsyncDisposable
{
    MediaSnapshot? Current { get; }
    event EventHandler<MediaSnapshot?>? Changed;
    event EventHandler<string>? Error;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task PlayPauseAsync();
    Task PreviousAsync();
    Task NextAsync();
    Task SeekAsync(TimeSpan position);
}
public interface ISystemService : IDisposable
{
    Task<SystemSnapshot> ReadAsync(CancellationToken cancellationToken = default);
    Task SetVolumeAsync(double volume);
    IReadOnlyList<int> ListeningPorts();
    void SetAwake(bool awake);
}
public interface ISecretVault
{
    void Save(string name, string value);
    string? Read(string name);
    void Delete(string name);
}
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    // Legacy/custom clocks can retain their existing implementation. Production duration clocks
    // and test clocks should override this with an independently advancing elapsed-time source.
    TimeSpan Elapsed => TimeSpan.FromTicks(UtcNow.UtcTicks);
}

public sealed class SystemClock : IClock
{
    private readonly long _origin = System.Diagnostics.Stopwatch.GetTimestamp();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public TimeSpan Elapsed => System.Diagnostics.Stopwatch.GetElapsedTime(_origin);
}
