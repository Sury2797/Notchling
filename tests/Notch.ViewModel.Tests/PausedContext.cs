using System.Collections.Concurrent;

internal sealed class PausedContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    public TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override void Post(SendOrPostCallback callback, object? state)
    {
        _queue.Enqueue((callback, state));
        Posted.TrySetResult();
    }

    public async Task PumpUntilAsync(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Paused initialization failed to drain.");
            if (_queue.TryDequeue(out var continuation))
            {
                var previous = Current;
                SetSynchronizationContext(this);
                try { continuation.Callback(continuation.State); }
                finally { SetSynchronizationContext(previous); }
            }
            else await Task.Delay(1);
        }
        await task;
    }
}
