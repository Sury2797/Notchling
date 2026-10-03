namespace Notch.Core;

public interface IMediaService : IAsyncDisposable
{
    MediaSnapshot? Current { get; }
    event EventHandler<MediaSnapshot?>? Changed;
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
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
