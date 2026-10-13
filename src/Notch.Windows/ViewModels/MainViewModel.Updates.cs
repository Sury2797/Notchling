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
    public event EventHandler? UpdateInstallerReady;

    public string ShellStatus => HasUnsavedChanges ? _workspaceDirty ? SaveState : "Unsaved settings"
        : _feedbackModule == SelectedModule && _feedbackTime.GetElapsedTime(_feedbackAt) < TimeSpan.FromSeconds(8)
            && Status != "Live data — controls follow your active Windows player." ? Status : "";
    public bool IsCheckingUpdates { get => _isCheckingUpdates; private set => Set(ref _isCheckingUpdates, value); }
    public bool IsInstallingUpdate { get => _isInstallingUpdate; private set => Set(ref _isInstallingUpdate, value); }
    public bool HasAvailableUpdate => _availableUpdate is not null;
    public bool CanInstallAvailableUpdate => _availableUpdate is { CanInstallVerified: true, VerifiedUpdate: not null } && !IsCheckingUpdates;
    public string? AvailableUpdateVersion => _availableUpdate?.Version;
    public string InstalledUpdateVersion => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "Unknown";
    public string UpdateReleaseNotes => _availableUpdate?.ReleaseNotes ?? "";
    public string UpdateTrustDescription => _availableUpdate?.TrustDescription ?? "";
    public AvailableUpdate? AvailableUpdateDetails => _availableUpdate?.VerifiedUpdate;
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
    public Task InstallAvailableUpdateAsync(bool allowUnsignedEvaluation = false, AvailableUpdate? acceptedUpdate = null)
        => StartUpdateOperation(install: true, allowUnsignedEvaluation, acceptedUpdate);
    private Task StartUpdateOperation(bool install, bool allowUnsignedEvaluation = false, AvailableUpdate? acceptedUpdate = null)
    {
        if (!ReadyForInput() || IsCheckingUpdates) return Task.CompletedTask;
        if (IsDemo) { UpdateStatus = "Exit sample-data preview to check real releases."; return Task.CompletedTask; }
        if (install && !CanInstallAvailableUpdate) return Task.CompletedTask;
        if (install && _availableUpdate!.RequiresEvaluationConsent
            && (!allowUnsignedEvaluation || acceptedUpdate != _availableUpdate.VerifiedUpdate))
        {
            UpdateStatus = "Review and accept the official unsigned public-test update before downloading.";
            return Task.CompletedTask;
        }
        return _updateOperationTask = RunUpdateOperationAsync(install, background: false);
    }
    public void CancelPendingUpdateInstallation()
    {
        _updateService.CancelPendingInstallation();
        if (!_disposed) UpdateStatus = "Restart cancelled. The update was not installed; your app remains open.";
    }
    private async Task RestoreUpdateInstallationResultAsync()
    {
        try
        {
            var result = await _updateService.ReadLastInstallationResultAsync(_lifetimeToken);
            if (_disposed || result is null || IsCheckingUpdates) return;
            UpdateStatus = $"Last update: {result.Message} Running version: {InstalledUpdateVersion}.";
            NotificationHistory.Insert(0, new(Guid.NewGuid(), ActivityKind.Information, "Update result",
                $"Notchling update · {result.State}", result.Message, result.CheckedAtUtc,
                TimeSpan.FromSeconds(8), Destination: ModuleId.Settings));
            if (NotificationHistory.Count > 50) NotificationHistory.RemoveAt(50);
        }
        catch (Exception error) when (Recoverable(error)) { /* A previous local result must not delay or fail startup. */ }
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
                Notify(nameof(UpdateReleaseNotes)); Notify(nameof(UpdateTrustDescription)); Notify(nameof(AvailableUpdateDetails));
                UpdateStatus = release is null ? "You’re up to date. No newer compatible release was found."
                    : release.CanInstallVerified ? $"Notchling {release.Version} is available. Update and restart when ready."
                        + (release.RequiresEvaluationConsent ? " Official unsigned public-test update." : "")
                    : $"Notchling {release.Version} is available. In-app installation unavailable: its release integrity could not be verified. The release page offers manual downloads.";
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
            if (!_disposed)
            {
                UpdateStatus = "Update is ready. Notchling will close safely, install and restart.";
                UpdateInstallerReady?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            if (install) _updateService.CancelPendingInstallation();
            if (!_disposed) UpdateStatus = "Update check cancelled. Your app and data are unchanged.";
        }
        catch (Exception error) when (Recoverable(error))
        {
            if (install) _updateService.CancelPendingInstallation();
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
