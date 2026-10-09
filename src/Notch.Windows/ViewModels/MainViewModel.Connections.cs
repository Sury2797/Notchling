using Notch.Core;
using Notch.Core.Providers;

namespace Notch.Windows.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, ToolConnectionStatus> _connectionChecks = [];
    private readonly object _connectionGate = new();
    private bool? _stripeCredentialPresent, _analyticsCredentialPresent;
    private bool _weatherProxyConfigured;
    private bool _isCheckingConnections;
    private int _connectionRevision;
    private Task? _connectionCheckTask;
    public bool IsCheckingConnections { get => _isCheckingConnections; private set => Set(ref _isCheckingConnections, value); }
    public bool CanRefreshWeather => _weatherProxyConfigured && !string.IsNullOrWhiteSpace(_subscription?.SessionToken);
    public string WeatherConnectionGuidance => !_weatherProxyConfigured
        ? "The licensed weather service has not been configured. Tool access is free during public testing; provider setup is still required."
        : !CanRefreshWeather ? "Sign in to the configured service account in Settings to connect weather. No Notchling subscription is required during public testing."
        : "Weather is ready to refresh from the configured provider.";

    public IReadOnlyList<ToolConnectionStatus> ConnectionStatuses
    {
        get
        {
            Dictionary<string, ToolConnectionStatus> observedRows;
            lock (_connectionGate) observedRows = new(_connectionChecks);
            ToolConnectionStatus Row(string id, string name, ConnectionState state, string detail)
                => observedRows.TryGetValue(id, out var observed) ? observed : new(id, name, state, detail);
            ToolConnectionStatus[] rows =
            [
                Row("media", "Windows media", ConnectionState.NotChecked, "Check the Windows media service. A supported player provides track metadata and available controls."),
                Row("audio", "Audio output", ConnectionState.NotChecked, "Check the current Windows output device and volume control."),
                new("clipboard", "Clipboard", ClipboardService.Enabled ? ConnectionState.Ready : ConnectionState.NeedsSetup,
                    ClipboardService.Enabled ? "Text capture is enabled for this session; Clear and Disable discard pending entries." : "Text history is opt-in. Enable Capture clipboard text to test it; clipboard contents are never uploaded."),
                Row("stripe", "Stripe reporting", _stripeCredentialPresent == true ? ConnectionState.NotChecked : ConnectionState.NeedsSetup,
                    _stripeCredentialPresent == true ? "Credential saved; refresh to verify read access. This connection does not purchase Notchling." : "Save your own read-only Stripe key, then check connections. Other revenue providers have no adapter yet."),
                Row("analytics", "Website analytics", !string.IsNullOrWhiteSpace(Preferences.AnalyticsEndpoint) && _analyticsCredentialPresent == true ? ConnectionState.NotChecked : ConnectionState.NeedsSetup,
                    "Configure your HTTPS analytics endpoint and bearer token. The endpoint must return the documented analytics JSON contract."),
                Row("calendar", "Calendar import", string.IsNullOrWhiteSpace(Preferences.CalendarPath) ? ConnectionState.NeedsSetup : ConnectionState.NotChecked,
                    string.IsNullOrWhiteSpace(Preferences.CalendarPath) ? "Import a local ICS file in Calendar. This is a file import, rather than a live Google or Outlook account sync." : "Calendar source selected; check that it remains readable and valid."),
                Row("coding", "Coding activity", string.IsNullOrWhiteSpace(Preferences.CodingPath) ? ConnectionState.NeedsSetup : ConnectionState.NotChecked,
                    string.IsNullOrWhiteSpace(Preferences.CodingPath) ? "Import supported Claude or Codex JSONL activity in Coding. No agent account or message contents are uploaded." : "Activity source selected; check that the file remains readable."),
                Row("weather", "Weather service", CanRefreshWeather ? ConnectionState.NotChecked : ConnectionState.NeedsSetup, WeatherConnectionGuidance),
            ];
            return IsDemo ? rows.Select(row => row with { State = ConnectionState.NotChecked, Detail = "Sample-data preview is active. Exit preview before checking real connections.", CheckedAt = null }).ToArray() : rows;
        }
    }

    private void ObserveConnection(string id, string name, ConnectionState state, string detail, DateTimeOffset? checkedAt = null)
    {
        if (_disposed || IsDemo) return;
        var value = new ToolConnectionStatus(id, name, state, detail, checkedAt ?? DateTimeOffset.UtcNow);
        lock (_connectionGate) _connectionChecks[id] = value;
        Notify(nameof(ConnectionStatuses));
    }
    private void InvalidateConnection(string id)
    {
        Interlocked.Increment(ref _connectionRevision);
        lock (_connectionGate) _connectionChecks.Remove(id);
        if (!_disposed) Notify(nameof(ConnectionStatuses));
    }
    private void ResetConnectionChecks()
    {
        Interlocked.Increment(ref _connectionRevision);
        lock (_connectionGate) _connectionChecks.Clear();
        if (!_disposed) Notify(nameof(ConnectionStatuses));
    }
    private void ObserveMediaConnection()
    {
        var media = _mediaService.Current;
        ObserveConnection("media", "Windows media", media is null ? ConnectionState.Ready : ConnectionState.Connected,
            media is null ? "Windows media is ready. No supported player currently exposes a media session."
                : "Connected to " + MediaPresentation.SourceLabel(media.Source, media.SourceDisplayName) + ". Titles and controls come from the current Windows media session.");
    }
    private async Task StartMediaConnectionAsync()
    {
        try { await _mediaService.StartAsync(_lifetimeToken); ObserveMediaConnection(); }
        catch (Exception error) when (Recoverable(error))
        { ObserveConnection("media", "Windows media", ConnectionState.Failed, ConnectionFailure(error)); throw; }
    }
    private void ObserveAudioConnection(SystemSnapshot snapshot)
        => ObserveConnection("audio", "Audio output", snapshot.AudioAvailable ? ConnectionState.Ready : ConnectionState.Unavailable,
            snapshot.AudioAvailable ? "Windows output is available: " + snapshot.OutputDevice + "." : "No usable Windows output endpoint. Connect or select a device, then check again.");

    public Task RefreshConnectionsAsync()
    {
        if (!ReadyForInput() || IsCheckingConnections) return Task.CompletedTask;
        if (IsDemo) { Status = "Exit sample-data preview before checking real connections."; return Task.CompletedTask; }
        _connectionCheckTask = CheckConnectionsCoreAsync();
        return _connectionCheckTask;
    }
    private async Task CheckConnectionsCoreAsync()
    {
        IsCheckingConnections = true;
        var revision = Volatile.Read(ref _connectionRevision);
        var generation = _dataGeneration;
        bool Current() => CanPublish(generation) && revision == Volatile.Read(ref _connectionRevision);
        async Task Check(string id, string name, Func<Task> read, Func<string> detail, Func<ConnectionState>? successState = null)
        {
            if (!Current()) return;
            ObserveConnection(id, name, ConnectionState.Checking, "Checking the real connection…");
            try
            {
                await read();
                if (Current()) ObserveConnection(id, name, successState?.Invoke() ?? ConnectionState.Connected, detail());
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested || !Current()) { }
            catch (Exception error) when (Recoverable(error))
            {
                if (Current()) ObserveConnection(id, name, ConnectionState.Failed, ConnectionFailure(error));
            }
        }
        try
        {
            // Vault access is explicit and off the UI thread. Only presence flags
            // leave this task; secret values never enter status text or diagnostics.
            try
            {
                var presence = await Task.Run(() => (!string.IsNullOrWhiteSpace(_vault.Read("stripe")), !string.IsNullOrWhiteSpace(_vault.Read("analytics"))), _lifetimeToken)
                    .WaitAsync(TimeSpan.FromSeconds(5), _lifetimeToken);
                if (!Current()) return;
                _stripeCredentialPresent = presence.Item1; _analyticsCredentialPresent = presence.Item2;
                lock (_connectionGate)
                {
                    if (!presence.Item1) _connectionChecks.Remove("stripe");
                    if (!presence.Item2 || string.IsNullOrWhiteSpace(Preferences.AnalyticsEndpoint)) _connectionChecks.Remove("analytics");
                }
                Notify(nameof(ConnectionStatuses));
            }
            catch (OperationCanceledException) when (_lifetimeToken.IsCancellationRequested) { return; }
            catch (Exception error) when (Recoverable(error))
            {
                if (!Current()) return;
                _stripeCredentialPresent = null; _analyticsCredentialPresent = null;
                ObserveConnection("stripe", "Stripe reporting", ConnectionState.Failed, "Windows could not inspect the saved credential. Retry or save it again.");
                ObserveConnection("analytics", "Website analytics", ConnectionState.Failed, "Windows could not inspect the saved credential. Retry or save it again.");
            }
            if (!Current()) return;
            var tasks = new List<Task>
            {
                Check("media", "Windows media", StartMediaConnectionAsync, () => _mediaService.Current is null
                    ? "Windows media is available; no supported player has an active session." : "Current Windows media session read successfully.",
                    () => _mediaService.Current is null ? ConnectionState.Ready : ConnectionState.Connected),
                Check("audio", "Audio output", async () =>
                {
                    await _refreshLock.WaitAsync(_lifetimeToken); _refreshLock.Release();
                    await RefreshAsync();
                    if (System is null) throw new InvalidOperationException("No native system reading is available.");
                }, () => System?.AudioAvailable == true ? "Current Windows output read successfully: " + System.OutputDevice + "."
                    : "No usable Windows output endpoint. Connect or select a device, then check again.",
                    () => System?.AudioAvailable == true ? ConnectionState.Ready : ConnectionState.Unavailable)
            };
            if (_stripeCredentialPresent == true)
                tasks.Add(Check("stripe", "Stripe reporting", () => RefreshRevenueAsync(RevenueProvider.Stripe), () => Revenue?.Complete == false
                    ? "Stripe read succeeded; this report is partial because the pagination limit was reached." : "Stripe read-only payment report read successfully."));
            if (_analyticsCredentialPresent == true && !string.IsNullOrWhiteSpace(Preferences.AnalyticsEndpoint))
                tasks.Add(Check("analytics", "Website analytics", RefreshAnalyticsAsync, () => "The configured endpoint returned a valid analytics snapshot."));
            if (!string.IsNullOrWhiteSpace(Preferences.CalendarPath))
                tasks.Add(Check("calendar", "Calendar import", () => ImportCalendarAsync(Preferences.CalendarPath), () => "Local ICS source read successfully; " + CalendarEvents.Count + " events in the loaded date range."));
            if (!string.IsNullOrWhiteSpace(Preferences.CodingPath))
                tasks.Add(Check("coding", "Coding activity", () => ImportCodingAsync(Preferences.CodingPath), () => "Supported local activity file read successfully."));
            if (CanRefreshWeather)
                tasks.Add(Check("weather", "Weather service", () => RefreshWeatherAsync(), () => "The configured licensed provider returned a valid forecast."));
            await Task.WhenAll(tasks);
            if (Current()) Status = "Connection checks finished. Each source shows its own result; providers without setup were not contacted.";
        }
        finally
        {
            // A setup edit invalidates the run. Do not leave unrelated rows saying
            // Checking after its obsolete responses have been discarded.
            lock (_connectionGate)
                foreach (var id in _connectionChecks.Where(pair => pair.Value.State == ConnectionState.Checking).Select(pair => pair.Key).ToArray())
                    _connectionChecks.Remove(id);
            if (!_disposed) { IsCheckingConnections = false; Notify(nameof(ConnectionStatuses)); }
        }
    }
    private static string ConnectionFailure(Exception error) => error switch
    {
        HttpRequestException { StatusCode: not null } => error.Message,
        HttpRequestException => "The service could not be reached. Check network/proxy settings and retry.",
        TimeoutException => "The connection did not finish in time. Retry when the provider or Windows service is available.",
        InvalidDataException => "The source returned invalid or unsupported data. Check its documented format.",
        IOException or UnauthorizedAccessException => "The selected source could not be read. Re-select the file or verify its permissions.",
        _ => "The connection could not be checked. Verify its setup and retry.",
    };
}
