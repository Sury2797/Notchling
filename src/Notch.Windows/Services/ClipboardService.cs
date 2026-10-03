using Microsoft.UI.Dispatching;
using Notch.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Notch.Windows.Services;

/// <summary>Opt-in plain text history, held in memory only and cleared when disabled.</summary>
public sealed class ClipboardService : IDisposable
{
    private const int MaximumItems = 50;
    private const int MaximumTextLength = 100_000;
    private readonly DispatcherQueue _dispatcher;
    private readonly List<ClipboardItem> _items = [];
    private bool _enabled;
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
            if (_enabled == enabled) return;
            _enabled = enabled;
            _generation++;
            if (enabled) Clipboard.ContentChanged += OnContentChanged;
            else
            {
                Clipboard.ContentChanged -= OnContentChanged;
                ClearCore();
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
        OnDispatcher(() => { _generation++; ClearCore(); });
    }

    private async void OnContentChanged(object? sender, object args)
    {
        if (!_enabled || _disposed) return;
        var generation = ++_generation;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains("ExcludeClipboardContentFromMonitorProcessing")) return;
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = await content.GetTextAsync();
            OnDispatcher(() =>
            {
                if (!_enabled || _disposed || generation != _generation || string.IsNullOrWhiteSpace(text)) return;
                // Avoid silently truncating sensitive/code content into a misleading clipboard entry.
                if (text.Length > MaximumTextLength) return;
                _items.RemoveAll(item => item.Text == text);
                _items.Insert(0, new ClipboardItem(Guid.NewGuid(), text, DateTimeOffset.UtcNow));
                if (_items.Count > MaximumItems) _items.RemoveRange(MaximumItems, _items.Count - MaximumItems);
                Changed?.Invoke(this, Items);
            });
        }
        catch (Exception error)
        {
            if (!_disposed) _dispatcher.TryEnqueue(() => Error?.Invoke(this, $"Clipboard text is unavailable: {error.Message}"));
        }
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
        OnDispatcher(() =>
        {
            Clipboard.ContentChanged -= OnContentChanged;
            _enabled = false;
            _generation++;
            ClearCore();
        });
    }
}
