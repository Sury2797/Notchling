namespace Notch.Core.Providers;

/// <summary>Keeps a single deadline across a provider operation, including pagination and its body reads.</summary>
internal sealed class ProviderOperation : IDisposable
{
    private readonly CancellationTokenSource _deadline;
    public ProviderOperation(HttpClient client, CancellationToken cancellationToken)
    {
        _deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var maximum = TimeSpan.FromSeconds(30);
        var timeout = client.Timeout;
        _deadline.CancelAfter(timeout == Timeout.InfiniteTimeSpan || timeout > maximum ? maximum : timeout);
    }
    public CancellationToken Token => _deadline.Token;
    public void Dispose() => _deadline.Dispose();
}
