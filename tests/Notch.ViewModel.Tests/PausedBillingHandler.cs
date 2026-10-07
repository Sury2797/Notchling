using System.Net;

internal sealed class PausedBillingHandler : HttpMessageHandler
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Cancelled { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Started.TrySetResult();
        try { await Release.Task.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { Cancelled = true; throw; }
        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
    }
}
