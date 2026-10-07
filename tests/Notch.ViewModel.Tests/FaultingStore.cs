using Notch.Core;

internal sealed class FaultingStore(string directory) : IDataStore
{
    private readonly LocalStore _inner = new(directory);
    public bool FailWrites { get; set; }
    public int Writes { get; private set; }
    public bool Disposed { get; private set; }
    public Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default) => _inner.ReadAsync<T>(name, cancellationToken);
    public Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        Writes++;
        return FailWrites ? Task.FromException(new IOException("Injected disk-full final save failure")) : _inner.WriteAsync(name, value, cancellationToken);
    }
    public void Dispose() { Disposed = true; _inner.Dispose(); }
}
