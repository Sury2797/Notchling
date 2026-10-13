using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Core.AiUsage;

namespace Notch.Windows.ViewModels;

public sealed partial class MainViewModel
{
    private IAiUsageService? _aiUsageService;
    private readonly DispatcherTimer _aiUsageTimer = new() { Interval = TimeSpan.FromSeconds(45) };
    private CancellationTokenSource? _aiUsageCancellation;
    private Task? _aiUsageReadTask;
    private int _aiUsageRevision;
    private bool _aiUsagePanelVisible;
    private bool _isReadingAiUsage;
    private AiUsageSnapshot? _aiUsage;
    private string _aiUsageStatus = "Open this tool to read supported local usage records. No account credentials are needed.";

    public AiUsageSnapshot? AiUsage { get => _aiUsage; private set => Set(ref _aiUsage, value); }
    public bool IsReadingAiUsage { get => _isReadingAiUsage; private set => Set(ref _isReadingAiUsage, value); }
    public string AiUsageStatus { get => _aiUsageStatus; private set => Set(ref _aiUsageStatus, value); }
    public Func<bool>? CanReadAiUsage { get; set; }

    private bool AiUsageHostVisible()
    {
        try { return CanReadAiUsage?.Invoke() != false; }
        catch (Exception error) when (Recoverable(error)) { return false; }
    }

    private void InitializeAiUsage(IAiUsageService? service)
    {
        _aiUsageService = service ?? new LocalAiUsageService();
        _aiUsageTimer.Tick += OnAiUsageTick;
    }

    /// <summary>Only the visible, expanded tool may schedule local disk reads.</summary>
    public void SetAiUsagePanelVisible(bool visible)
    {
        var active = visible && IsReady && !IsDemo && AiUsageHostVisible() && SelectedModule == ModuleId.AiUsage
            && Overlay.Mode == OverlayMode.Expanded;
        if (active == _aiUsagePanelVisible) return;
        _aiUsagePanelVisible = active;
        Interlocked.Increment(ref _aiUsageRevision);
        if (!active)
        {
            _aiUsageTimer.Stop();
            _aiUsageCancellation?.Cancel();
            if (!_disposed) IsReadingAiUsage = false;
            return;
        }
        _aiUsageTimer.Start();
        _ = RefreshAiUsageAsync(forceRefresh: false);
    }

    private void OnAiUsageTick(object? sender, object args)
    {
        if (!AiUsageHostVisible()) { SetAiUsagePanelVisible(false); return; }
        if (_aiUsagePanelVisible) _ = RefreshAiUsageAsync(forceRefresh: false);
    }

    public Task RefreshAiUsageAsync(bool forceRefresh = true)
    {
        if (!ReadyForInput() || IsDemo || IsReadingAiUsage || !_aiUsagePanelVisible || !AiUsageHostVisible()
            || SelectedModule != ModuleId.AiUsage || Overlay.Mode != OverlayMode.Expanded || _aiUsageService is null)
            return Task.CompletedTask;
        return _aiUsageReadTask = ReadAiUsageCoreAsync(forceRefresh);
    }

    private async Task ReadAiUsageCoreAsync(bool forceRefresh)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        cancellation.CancelAfter(TimeSpan.FromSeconds(15));
        _aiUsageCancellation = cancellation;
        var revision = Volatile.Read(ref _aiUsageRevision);
        var dataGeneration = _dataGeneration;
        var viewGeneration = _viewGeneration;
        bool Current() => CanPublish(dataGeneration) && _aiUsagePanelVisible && AiUsageHostVisible()
            && revision == Volatile.Read(ref _aiUsageRevision) && viewGeneration == _viewGeneration
            && ReferenceEquals(_aiUsageCancellation, cancellation)
            && SelectedModule == ModuleId.AiUsage && Overlay.Mode == OverlayMode.Expanded;
        IsReadingAiUsage = true;
        AiUsageStatus = "Reading actual local usage records…";
        try
        {
            // A filesystem adapter must never perform its initial enumeration on
            // the UI thread, even if it completes a cache hit synchronously.
            var result = await Task.Run(() => _aiUsageService!.ReadAsync(forceRefresh, cancellation.Token), cancellation.Token)
                .WaitAsync(TimeSpan.FromSeconds(15), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            AiUsage = result;
            AiUsageStatus = "Checked " + result.CheckedAt.ToLocalTime().ToString("t")
                + ". Refreshes every 45 seconds while this panel is open.";
        }
        catch (OperationCanceledException)
        {
            if (Current()) AiUsageStatus = "The local check timed out. Previous readings remain labelled by their observation time; try Refresh again.";
        }
        catch (Exception error) when (Recoverable(error))
        {
            if (Current()) AiUsageStatus = "Local usage could not be read. Check that your supported CLI has readable usage records, then refresh. Existing readings keep their original timestamps.";
        }
        finally
        {
            if (ReferenceEquals(_aiUsageCancellation, cancellation))
            {
                _aiUsageCancellation = null;
                if (!_disposed) IsReadingAiUsage = false;
                else _isReadingAiUsage = false;
            }
        }
    }

    private async Task StopAiUsageAsync()
    {
        _aiUsagePanelVisible = false;
        Interlocked.Increment(ref _aiUsageRevision);
        _aiUsageTimer.Stop();
        _aiUsageTimer.Tick -= OnAiUsageTick;
        _aiUsageCancellation?.Cancel();
        if (_aiUsageReadTask is { } read)
            try { await read; } catch (Exception error) when (Recoverable(error)) { }
    }
}
