using Microsoft.UI.Dispatching;
using Notch.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Notch.Windows.Services;

/// <summary>Opt-in plain text history, held in memory only and cleared when disabled.</summary>
public sealed class ClipboardService : IDisposable
{
    private const int MaximumItems = 50;
    private const int MaximumTextLength = 100_000;
    private const int MaximumPendingCaptures = 50;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<ClipboardItem> _items = [];
    private readonly Queue<PendingCapture> _pending = new();
    private CancellationTokenSource _captureLifetime = new();
    private bool _capturing;
    private bool _enabled;
    private bool _subscribed;
    private bool _disposed;
    private long _generation;

    public ClipboardService(DispatcherQueue dispatcher) => _dispatcher = dispatcher;
    public event EventHandler<IReadOnlyList<ClipboardItem>>? Changed;
    public event EventHandler<string>? Error;
    public bool Enabled => _enabled;
    public IReadOnlyList<ClipboardItem> Items => _items.ToArray();

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OnDispatcher(() =>
        {
            if (_disposed) return;
            if (_enabled == enabled) return;
            InvalidateCaptures();
            if (enabled)
            {
                // Do not latch Enabled until Windows accepts registration: a locked or unavailable
                // clipboard must be recoverable by retrying the opt-in action.
                if (!_subscribed) { Clipboard.ContentChanged += OnContentChanged; _subscribed = true; }
                _enabled = true;
            }
            else
            {
                _enabled = false;
                try
                {
                    if (_subscribed) { Clipboard.ContentChanged -= OnContentChanged; _subscribed = false; }
                }
                finally { ClearCore(); }
            }
            // Do not inspect the current clipboard upon opt-in: capture new changes only.
        });
    }

    public Task CopyAsync(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(text);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OnDispatcher(() =>
        {
            try
            {
                if (_disposed) { completion.TrySetCanceled(); return; }
                var package = new DataPackage();
                package.SetText(text);
                Clipboard.SetContent(package);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        OnDispatcher(() => { if (_disposed) return; InvalidateCaptures(); ClearCore(); });
    }

    private void OnContentChanged(object? sender, object args)
    {
        if (_disposed || !_enabled) return;
        // Windows may deliver the last notification after the UI queue has shut down.
        // Throwing back through that native callback can terminate the application on exit.
        if (_dispatcher.HasThreadAccess) CaptureCurrentClipboard();
        else _dispatcher.TryEnqueue(CaptureCurrentClipboard);
    }

    private void CaptureCurrentClipboard()
    {
        if (!_enabled || _disposed) return;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains("ExcludeClipboardContentFromMonitorProcessing")) return;
            if (!content.Contains(StandardDataFormats.Text)) return;
            if (_pending.Count >= MaximumPendingCaptures)
            {
                _pending.Dequeue();
                Error?.Invoke(this, "Clipboard capture is busy. An older pending entry was skipped.");
            }
            // Snapshot the package at notification time. Queue readers so slower owners cannot reorder history.
            _pending.Enqueue(new(content, _generation, _captureLifetime.Token, DateTimeOffset.UtcNow));
            if (!_capturing)
            {
                _capturing = true;
                _ = CapturePendingAsync();
            }
        }
        catch (Exception error)
        {
            if (!_disposed) Error?.Invoke(this, $"Clipboard capture is unavailable: {error.Message}");
        }
    }

    private async Task CapturePendingAsync()
    {
        while (true)
        {
            PendingCapture? request = null;
            if (!await TryOnDispatcherAsync(() =>
            {
                if (_disposed || _pending.Count == 0) _capturing = false;
                else request = _pending.Dequeue();
            }).ConfigureAwait(false) || request is null) return;
            try
            {
                request.Token.ThrowIfCancellationRequested();
                var text = await request.Content.GetTextAsync().AsTask(request.Token)
                    .WaitAsync(TimeSpan.FromSeconds(3), request.Token).ConfigureAwait(false);
                await TryOnDispatcherAsync(() =>
                {
                    if (!_enabled || _disposed || request.Generation != _generation || string.IsNullOrWhiteSpace(text)) return;
                    // Never silently truncate code or sensitive content into a misleading entry.
                    if (text.Length > MaximumTextLength)
                    {
                        Error?.Invoke(this, "Clipboard text exceeds the 100,000-character capture limit and was skipped.");
                        return;
                    }
                    _items.RemoveAll(item => item.Text == text);
                    _items.Insert(0, new ClipboardItem(Guid.NewGuid(), text, request.CapturedAt));
                    if (_items.Count > MaximumItems) _items.RemoveRange(MaximumItems, _items.Count - MaximumItems);
                    Changed?.Invoke(this, Items);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                await TryOnDispatcherAsync(() =>
                {
                    if (!_disposed && _enabled && request.Generation == _generation)
                        Error?.Invoke(this, $"Clipboard text could not be captured: {error.Message}");
                }).ConfigureAwait(false);
            }
        }
    }

    private void InvalidateCaptures()
    {
        _generation++;
        _pending.Clear();
        _captureLifetime.Cancel();
        _captureLifetime.Dispose();
        _captureLifetime = new();
    }

    private Task<bool> TryOnDispatcherAsync(Action action)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Invoke()
        {
            try { action(); completion.TrySetResult(true); }
            catch (Exception error) { completion.TrySetException(error); }
        }
        if (_dispatcher.HasThreadAccess) Invoke();
        else if (!_dispatcher.TryEnqueue(Invoke)) completion.TrySetResult(false);
        return completion.Task;
    }

    private void ClearCore()
    {
        _items.Clear();
        Changed?.Invoke(this, Items);
    }

    private void OnDispatcher(Action action)
    {
        if (_dispatcher.HasThreadAccess) action();
        else if (!_dispatcher.TryEnqueue(() => action()))
            throw new InvalidOperationException("The clipboard UI dispatcher is shutting down.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        void Cleanup()
        {
            _enabled = false;
            try
            {
                if (_subscribed) Clipboard.ContentChanged -= OnContentChanged;
            }
            catch (Exception error) when (error is System.Runtime.InteropServices.COMException
                or System.Runtime.InteropServices.InvalidComObjectException or NotSupportedException) { }
            finally
            {
                _subscribed = false;
                InvalidateCaptures();
                _captureLifetime.Dispose();
                ClearCore();
            }
        }
        if (_dispatcher.HasThreadAccess) Cleanup();
        else if (!_dispatcher.TryEnqueue(Cleanup)) Cleanup();
    }

    private sealed record PendingCapture(DataPackageView Content, long Generation, CancellationToken Token, DateTimeOffset CapturedAt);
}
