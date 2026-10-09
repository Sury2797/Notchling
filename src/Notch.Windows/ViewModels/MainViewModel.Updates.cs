using System.Net;
using Notch.Core;
using Notch.Windows.Services;

namespace Notch.Windows.ViewModels;

public sealed partial class MainViewModel
{
    private readonly TimeProvider _feedbackTime;
    private long _feedbackAt;
    private ModuleId? _feedbackModule;
    private bool _feedbackWasVisible;
    private readonly HttpClient _updateHttp = new(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All });
    private readonly IWindowsUpdateService _updateService;
    private CancellationTokenSource? _updateCancellation;
    private Task? _updateOperationTask;
    private long? _lastUpdateAttempt;
    private string? _announcedUpdateVersion;
    private ReleaseUpdate? _availableUpdate;
    private bool _isCheckingUpdates;
    private bool _isInstallingUpdate;

    public string ShellStatus => HasUnsavedChanges ? _workspaceDirty ? SaveState : "Unsaved settings"
        : _feedbackModule == SelectedModule && _feedbackTime.GetElapsedTime(_feedbackAt) < TimeSpan.FromSeconds(8)
            && Status != "Live data — controls follow your active Windows player." ? Status : "";
    public bool IsCheckingUpdates { get => _isCheckingUpdates; private set => Set(ref _isCheckingUpdates, value); }
    public bool IsInstallingUpdate { get => _isInstallingUpdate; private set => Set(ref _isInstallingUpdate, value); }
    public bool HasAvailableUpdate => _availableUpdate is not null;
    public bool CanInstallAvailableUpdate => _availableUpdate is { CanInstallVerified: true, VerifiedUpdate: not null } && !IsCheckingUpdates;
    public string? AvailableUpdateVersion => _availableUpdate?.Version;
    public string UpdateReleasePage => _availableUpdate?.ReleasePageUrl ?? "https://github.com/Sury2797/Notchling/releases";

    private void RefreshTransientFeedback()
    {
        if (_feedbackWasVisible && _feedbackTime.GetElapsedTime(_feedbackAt) >= TimeSpan.FromSeconds(8))
        { _feedbackWasVisible = false; Notify(nameof(ShellStatus)); }
    }
    private void TryScheduleUpdateCheck()
    {
        if (!_loaded || _disposed || IsDemo || !Preferences.CheckForUpdatesAutomatically || IsCheckingUpdates) return;
        if (_lastUpdateAttempt is { } previous && _feedbackTime.GetElapsedTime(previous) < TimeSpan.FromHours(24)) return;
        _updateOperationTask = RunUpdateOperationAsync(install: false, background: true);
    }
    public Task CheckForUpdatesAsync() => StartUpdateOperation(install: false);
    public Task InstallAvailableUpdateAsync() => StartUpdateOperation(install: true);
    private Task StartUpdateOperation(bool install)
    {
        if (!ReadyForInput() || IsCheckingUpdates) return Task.CompletedTask;
        if (IsDemo) { UpdateStatus = "Exit sample-data preview to check real releases."; return Task.CompletedTask; }
        if (install && !CanInstallAvailableUpdate) return Task.CompletedTask;
        return _updateOperationTask = RunUpdateOperationAsync(install, background: false);
    }
    public void CancelUpdateCheck()
    {
        if (!_disposed) _updateCancellation?.Cancel();
    }
    private async Task RunUpdateOperationAsync(bool install, bool background)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        _updateCancellation = cancellation;
        _lastUpdateAttempt = _feedbackTime.GetTimestamp();
        IsCheckingUpdates = true;
        IsInstallingUpdate = install;
        Notify(nameof(CanInstallAvailableUpdate));
        UpdateStatus = install ? "Preparing the verified update…" : "Checking official releases…";
        try
        {
            if (!install)
            {
                var release = await _updateService.DiscoverAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (_disposed) return;
                _availableUpdate = release;
                Notify(nameof(HasAvailableUpdate)); Notify(nameof(AvailableUpdateVersion)); Notify(nameof(UpdateReleasePage));
                UpdateStatus = release is null ? "No newer compatible release was found. Open Release page to view downloads."
                    : release.CanInstallVerified ? $"Notchling {release.Version} is available. Choose Install verified update when you are ready."
                    : $"Notchling {release.Version} is available. This evaluation uses manual updates; open Release page to download its installer.";
                if (release is not null && _announcedUpdateVersion != release.Version)
                {
                    _announcedUpdateVersion = release.Version;
                    // Updates belong in history and the compact indicator. A network
                    // response must never take over the user's current tool or editor.
                    NotificationHistory.Insert(0, new(Guid.NewGuid(), ActivityKind.Information, "Notchling update",
                        $"Notchling {release.Version} is available", "Open Settings to review the release.", DateTimeOffset.UtcNow,
                        TimeSpan.FromSeconds(8), Destination: ModuleId.Settings));
                    if (NotificationHistory.Count > 50) NotificationHistory.RemoveAt(50);
                }
                return;
            }
            var update = _availableUpdate!.VerifiedUpdate!;
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                _dispatcher.TryEnqueue(() =>
                {
                    if (_disposed || cancellation.IsCancellationRequested || !ReferenceEquals(_updateCancellation, cancellation)) return;
                    UpdateStatus = value.Stage switch
                    {
                        UpdateDownloadStage.Downloading => $"Downloading {update.Version} · {value.Fraction:P0}",
                        UpdateDownloadStage.Verifying => "Verifying installer integrity and publisher…",
                        _ => "Verified update downloaded. Preparing Setup…",
                    };
                });
            });
            var prepared = await _updateService.DownloadAsync(update, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            if (!await SaveBeforeExitAsync()) { UpdateStatus = "Update downloaded. Save your changes successfully before installing."; return; }
            cancellation.Token.ThrowIfCancellationRequested();
            if (_disposed) return;
            await _updateService.OpenInstallerAsync(prepared, cancellation.Token);
            if (!_disposed) UpdateStatus = "Setup opened. Save and quit Notchling to continue installation.";
        }
        catch (OperationCanceledException) { if (!_disposed) UpdateStatus = "Update check cancelled. Your app and data are unchanged."; }
        catch (Exception error) when (Recoverable(error))
        {
            if (_disposed) return;
            UpdateStatus = install ? "The update could not be verified or prepared. No installer was opened; try again or open Release page."
                : "Couldn’t check online. Try again or open Release page for manual downloads.";
            if (install && !background) ShowError("The update was not installed. " + error.Message);
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation)) _updateCancellation = null;
            if (!_disposed) { IsInstallingUpdate = false; IsCheckingUpdates = false; Notify(nameof(CanInstallAvailableUpdate)); }
            else { _isInstallingUpdate = false; _isCheckingUpdates = false; }
        }
    }
}
