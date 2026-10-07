using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Core.Providers;
using Notch.Windows.Services;
using System.Collections.ObjectModel;
using System.Net;
using System.Collections.Specialized;
using Notch.Core.Commerce;

namespace Notch.Windows.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly IMediaService _mediaService;
    private readonly ISystemService _systemService;
    private readonly ISecretVault _vault;
    private readonly IDataStore _store;
    private readonly string _dataDirectory;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly WeatherClient _weatherClient;
    private readonly SubscriptionService? _subscription;
    private readonly string _subscriptionUnavailable;
    private readonly HashSet<Guid> _queuedReminders = [];
    private string? _calendarSource;
    private CalendarRange? _calendarRange;
    private int _workspaceRevision;
    private bool _workspaceDirty;
    private string _saveState = "Saved locally";
    private SavedNote? _deletedNote;
    private bool _lastPremium;
    private bool _discardOnDispose;
    private long? _nonScratchpadBytes;
    public Func<bool>? CanPresentActivity { get; set; }
    public ObservableCollection<LiveActivity> NotificationHistory { get; } = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _lifetimeToken;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _shutdownGate = new();
    private readonly HashSet<Task> _pendingWrites = [];
    private readonly HashSet<Guid> _deliveredReminders = [];
    private readonly RequestSlot _weatherRequest = new();
    private readonly RequestSlot _revenueRequest = new();
    private readonly RequestSlot _analyticsRequest = new();
    private readonly RequestSlot _codingRequest = new();
    private readonly RequestSlot _calendarRequest = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _stopwatchTick = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? _saveDelay;
    private CancellationTokenSource? _volumeDelay;
    private int _tickCounter;
    private bool _loaded;
    private bool _initializing;
    private volatile bool _disposed;
    private bool _workspaceReadable = true;
    private bool _realAwake;
    private bool _preferencesChanged;
    private int _dataGeneration;
    private int _viewGeneration;
    private int _preferencesGeneration;
    private AppPreferences _preferences = new();
    private MediaSnapshot? _media;
    private SystemSnapshot? _system;
    private WeatherSnapshot? _weather;
    private RevenueSnapshot? _revenue;
    private CodingSnapshot? _coding;
    private AnalyticsSnapshot? _analytics;
    private string _status = "Local first. Ready when you are.";
    private string _error = "";
    private string _scratchpad = "";
    private IReadOnlyList<int> _ports = [];
    private IReadOnlyList<CalendarEvent> _calendarEvents = [];
    private readonly FocusSession _focus = new(new SystemClock());
    public OverlayStateMachine Overlay { get; } = new(new SystemClock());
    public nint WindowHandle { get; set; }
    public ClipboardService ClipboardService { get; }
    public AppPreferences Preferences { get => _preferences; private set => Set(ref _preferences, value); }
    public ModuleId SelectedModule => Overlay.SelectedModule;
    public MediaSnapshot? Media { get => _media; private set => Set(ref _media, value); }
    public SystemSnapshot? System { get => _system; private set => Set(ref _system, value); }
    public WeatherSnapshot? Weather { get => _weather; private set => Set(ref _weather, value); }
    public RevenueSnapshot? Revenue { get => _revenue; private set => Set(ref _revenue, value); }
    public CodingSnapshot? Coding { get => _coding; private set => Set(ref _coding, value); }
    public AnalyticsSnapshot? Analytics { get => _analytics; private set => Set(ref _analytics, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set => Set(ref _error, value); }
    public string Scratchpad
    {
        get => _scratchpad;
        set
        {
            if (!ReadyForWorkspaceInput()) return;
            value ??= "";
            WorkspaceLimits.RequireText(value, WorkspaceLimits.MaximumTextLength, "Scratchpad");
            WorkspaceLimits.RequireScalarBudget(_nonScratchpadBytes ??= WorkspaceLimits.Measure(LocalSnapshot() with { Scratchpad = "" }), value);
            if (Set(ref _scratchpad, value)) ScheduleSave();
        }
    }
    public string SaveState { get => _saveState; private set => Set(ref _saveState, value); }
    public bool HasUnsavedChanges => _workspaceDirty || _preferencesChanged;
    public bool CanUndoNoteDeletion => _deletedNote is not null;
#if DEBUG
    public bool IsDevelopmentBuild => true;
#else
    public bool IsDevelopmentBuild => false;
#endif
    public bool IsPremium => IsDevelopmentBuild || _subscription?.Current.IsPremium == true;
    public string PlanStatus => IsDevelopmentBuild ? "Development build — all tools enabled" : IsPremium ? "Premium — US$2/month" : "Free — basic media, Pomodoro and scratchpad";
    public string SubscriptionStatus => _subscription?.Current.Message ?? _subscriptionUnavailable;
    public bool BillingConfigured => _subscription is not null;
    public bool CanAccessModule(ModuleId module) => IsPremium || module is ModuleId.Home or ModuleId.Media or ModuleId.Focus or ModuleId.Scratchpad or ModuleId.Settings or ModuleId.Tools;
    private bool RequirePremium(ModuleId module)
    {
        if (CanAccessModule(module)) return true;
        ShowError("This tool is included in Premium. Your existing data remains available for export in Settings.");
        return false;
    }
    private bool RequirePremiumFeature()
    {
        if (IsPremium) return true;
        ShowError("This control is included in Premium. Basic playback, Pomodoro and scratchpad remain available."); return false;
    }
    public bool CalendarRangeLoaded(DateTime month) => _calendarRange?.ContainsMonth(month) == true;

    public IReadOnlyList<int> ListeningPorts { get => _ports; private set { if (!_ports.SequenceEqual(value)) Set(ref _ports, value); } }
    public IReadOnlyList<CalendarEvent> CalendarEvents { get => _calendarEvents; private set => Set(ref _calendarEvents, value); }
    public ObservableCollection<SavedNote> Notes { get; } = [];
    public ObservableCollection<ReminderItem> Reminders { get; } = [];
    public ObservableCollection<ShelfItem> Shelf { get; } = [];
    public ObservableCollection<SavedLink> Links { get; } = [];
    public ObservableCollection<ClipboardItem> Clipboard { get; } = [];
    public string FocusTime => FocusSession.Format(_focus.Pomodoro.Remaining);
    public string CountdownTime => FocusSession.Format(_focus.Countdown.Remaining);
    public string StopwatchTime => $"{(int)_focus.Stopwatch.Elapsed.TotalMinutes:00}:{_focus.Stopwatch.Elapsed.Seconds:00}.{_focus.Stopwatch.Elapsed.Milliseconds / 10:00}";
    public string HydrationTime => _focus.Hydration.IsRunning ? $"{Math.Ceiling(_focus.Hydration.Remaining.TotalMinutes):0}m left" : "Nudges paused";
    public double FocusProgress => _focus.Pomodoro.Progress;
    public bool FocusRunning => _focus.Pomodoro.IsRunning;
    public bool StopwatchRunning => _focus.Stopwatch.IsRunning;
    public IReadOnlyList<TimeSpan> StopwatchLaps => _focus.Stopwatch.Laps;
    public bool Awake { get; private set; }
    public bool IsDemo => Preferences.DemoMode;
    public bool WorkspaceReadable => _workspaceReadable;
    public bool IsReady => _loaded && !_disposed;
    private string WorkspaceRecoveryMessage => $"Your saved notebook could not be read. Its file is preserved and notebook saving is paused. Back up and repair or rename {Path.Combine(_dataDirectory, "workspace.json")}, then restart {ProductIdentity.DisplayName}.";

    public MainViewModel(DispatcherQueue dispatcher, string? dataDirectory = null, IDataStore? store = null, IMediaService? mediaService = null, ISystemService? systemService = null, ISecretVault? vault = null)
    {
        _dispatcher = dispatcher;
        _lifetimeToken = _lifetime.Token;
        _mediaService = mediaService ?? new WindowsMediaService();
        _systemService = systemService ?? new WindowsSystemService();
        _vault = vault ?? new WindowsSecretVault();
        var configuration = ProductConfiguration.Load();
        SubscriptionService.TryCreateFromConfiguration(_http, _vault, configuration.BillingUrl, configuration.EntitlementPublicKeyPem, out _subscription, out _subscriptionUnavailable);
        WeatherServiceConfiguration? weatherConfiguration = null;
        if (_subscription is not null && configuration.BillingUrl is { } billingUrl && new Uri(billingUrl).Scheme == Uri.UriSchemeHttps)
            weatherConfiguration = WeatherServiceConfiguration.CommercialProxy(new Uri(new Uri(billingUrl.TrimEnd('/') + "/"), "v1/weather").AbsoluteUri);
        _weatherClient = new(_http, weatherConfiguration, () => _subscription?.SessionToken);
        // Keep the established location across branding changes so existing notebooks and preferences remain available.
        _dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch");
        _store = store ?? new LocalStore(_dataDirectory);
        ClipboardService = new(dispatcher);
        ClipboardService.Changed += OnClipboardChanged;
        ClipboardService.Error += OnServiceError;
        _mediaService.Changed += OnMediaChanged;
        _mediaService.Error += OnServiceError;
        Overlay.ActivityPresented += (_, activity) =>
        {
            if (_queuedReminders.Remove(activity.Id)) _deliveredReminders.Add(activity.Id);
            NotificationHistory.Insert(0, activity);
            if (NotificationHistory.Count > 50) NotificationHistory.RemoveAt(50);
        };
        Overlay.Changed += (_, _) => { if (_disposed) return; _viewGeneration++; Notify(nameof(SelectedModule)); _ = RefreshForViewAsync(); UpdateStopwatchTick(); };
        _tick.Tick += OnTick;
        _stopwatchTick.Tick += (_, _) => { if (!_disposed) Notify(nameof(StopwatchTime)); };
        foreach (var collection in new INotifyCollectionChanged[] { Notes, Shelf, Links })
            collection.CollectionChanged += (_, _) => { _nonScratchpadBytes = null; ScheduleSave(); };
        Reminders.CollectionChanged += OnRemindersChanged;
    }
    public async Task InitializeAsync()
    {
        if (_loaded || _initializing || _disposed) return;
        _initializing = true;
        try
        {
            await ExecuteAsync(async () =>
            {
                var generation = _preferencesGeneration;
                var preferences = await _store.ReadAsync<AppPreferences>("preferences", _lifetimeToken);
                if (!_disposed && generation == _preferencesGeneration) Preferences = NormalizePreferences(preferences ?? new());
            });
            if (_disposed) return;
            await ExecuteAsync(async () =>
            {
                LocalData? local;
                try
                {
                    var existed = File.Exists(Path.Combine(_dataDirectory, "workspace.json"));
                    local = await _store.ReadAsync<LocalData>("workspace", _lifetimeToken);
                    if (_disposed) return;
                    if (local is null && existed) throw new InvalidDataException("The saved notebook must contain a workspace object.");
                    if (local is not null) local = NormalizeWorkspace(local);
                }
                catch (Exception error) when (Recoverable(error) && error is not OperationCanceledException)
                {
                    _workspaceReadable = false;
                    Notify(nameof(WorkspaceReadable));
                    throw new InvalidDataException(WorkspaceRecoveryMessage, error);
                }
                if (_disposed || local is null) return;
                foreach (var item in local.Notes) Notes.Add(item);
                foreach (var item in local.Reminders) Reminders.Add(item);
                foreach (var item in local.Shelf) Shelf.Add(item);
                foreach (var item in local.Links) Links.Add(item);
                _scratchpad = local.Scratchpad; Notify(nameof(Scratchpad));
            });
            if (_disposed) return;
            Overlay.Pinned = Preferences.Pinned;
            _focus.Pomodoro.Reset(TimeSpan.FromMinutes(Preferences.FocusMinutes));
            _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes));
            if (IsPremium) _focus.Hydration.Start();
            _loaded = true;
            _tick.Start();
            NotifyTimers();
            if (IsDemo) LoadDemo();
            Notify(nameof(IsReady));
            await ExecuteAsync(() => { ClipboardService.SetEnabled(IsPremium && Preferences.CaptureClipboard); return Task.CompletedTask; });
            if (_disposed) return;
            await ExecuteAsync(() => _mediaService.StartAsync(_lifetimeToken));
            if (_disposed) return;
            await ExecuteAsync(RefreshAsync);
            if (_disposed) return;
            // Cached entitlement already governs native startup. A remote refresh must
            // not delay ready local tools, media registration, or initial system readings.
            if (_subscription is not null) await ExecuteAsync(RefreshSubscriptionAsync);
            if (_disposed) return;
            if (!IsDemo && Preferences.CalendarPath is { } calendar) await ExecuteAsync(() => ImportCalendarAsync(calendar));
            if (_disposed) return;
            if (!IsDemo && Preferences.CodingPath is { } coding) await ExecuteAsync(() => ImportCodingAsync(coding));
        }
        finally { _initializing = false; }
    }
    private void OnMediaChanged(object? sender, MediaSnapshot? snapshot)
    {
        if (_disposed) return;
        var generation = _dataGeneration;
        _dispatcher.TryEnqueue(() => { if (CanPublish(generation)) Media = snapshot; });
    }
    private void OnClipboardChanged(object? sender, IReadOnlyList<ClipboardItem> items)
    {
        if (_disposed) return;
        Clipboard.Clear(); foreach (var item in items) Clipboard.Add(item);
        Notify(nameof(Clipboard));
    }
    private void OnServiceError(object? sender, string message)
    {
        if (!_disposed) _dispatcher.TryEnqueue(() => { if (!_disposed) ShowError(message); });
    }
    public void SelectModule(ModuleId module)
    {
        if (_disposed) return;
        if (!RequirePremium(module)) { Overlay.Expand(ModuleId.Settings); return; }
        Error = _workspaceReadable ? "" : WorkspaceRecoveryMessage;
        Overlay.Expand(module);
    }
    public void ShowError(string message)
    {
        if (!_disposed) Error = _workspaceReadable ? message : $"{WorkspaceRecoveryMessage}\n{message}";
    }
    public async Task ExecuteAsync(Func<Task> operation)
    {
        if (_disposed) return;
        try { await operation(); }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Recoverable(error))
        {
            if (_disposed) return;
            var message = error is HttpRequestException httpError && httpError.StatusCode is null ? "The service could not be reached. Check the connection and try again." : error.Message;
            Error = !_workspaceReadable && !message.Contains(WorkspaceRecoveryMessage, StringComparison.Ordinal) ? $"{WorkspaceRecoveryMessage}\n{message}" : message;
        }
    }
    public async Task RefreshAsync()
    {
        int generation;
        int viewGeneration;
        bool scanPorts;
        lock (_shutdownGate)
        {
            if (_disposed || !_loaded || IsDemo || !_refreshLock.Wait(0)) return;
            generation = _dataGeneration;
            viewGeneration = _viewGeneration;
            scanPorts = Overlay.Mode == OverlayMode.Expanded && SelectedModule is ModuleId.Home or ModuleId.Servers;
        }
        try
        {
            // Playback is independent of telemetry and listener enumeration. Publish
            // its current cache before either optional native reader can fail.
            Media = _mediaService.Current;
            var snapshot = await _systemService.ReadAsync(_lifetimeToken);
            if (!CanPublish(generation)) return;
            System = snapshot;
            if (scanPorts && CanPublishPorts(generation, viewGeneration))
            {
                // TCP enumeration is synchronous native work; never run it on the UI continuation.
                // Retain the refresh gate until it finishes so shutdown cannot dispose its service.
                try
                {
                    var ports = await Task.Run(_systemService.ListeningPorts, _lifetimeToken);
                    if (CanPublishPorts(generation, viewGeneration)) ListeningPorts = ports;
                }
                catch (Exception error) when (Recoverable(error) && !CanPublishPorts(generation, viewGeneration)) { }
            }
            if (!CanPublish(generation)) return;
            Media = _mediaService.Current;
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(generation)) { }
        finally { _refreshLock.Release(); }
    }
    private bool CanPublishPorts(int generation, int viewGeneration) => CanPublish(generation)
        && _viewGeneration == viewGeneration && Overlay.Mode == OverlayMode.Expanded
        && SelectedModule is ModuleId.Home or ModuleId.Servers;
    private Task RefreshForViewAsync() => ExecuteAsync(async () =>
    {
        if (_disposed || Overlay.Mode != OverlayMode.Expanded) return;
        if (SelectedModule is ModuleId.Home or ModuleId.System or ModuleId.Media or ModuleId.Servers or ModuleId.ScreenTime) await RefreshAsync();
        if (_disposed || Overlay.Mode != OverlayMode.Expanded) return;
        if (SelectedModule == ModuleId.Weather && Weather is null) await RefreshWeatherAsync();
    });
    public Task SetPreferencesAsync(AppPreferences preferences) => ApplyPreferencesAsync(preferences);
    private async Task ApplyPreferencesAsync(AppPreferences preferences, RequestSlot? origin = null, int originGeneration = 0)
    {
        if (_disposed) return;
        var old = Preferences;
        var normalized = NormalizePreferences(preferences);
        _preferencesGeneration++;
        _preferencesChanged = true;
        Preferences = normalized;
        if (old.WeatherCity != Preferences.WeatherCity && (origin != _weatherRequest || originGeneration != _weatherRequest.Generation)) { Invalidate(_weatherRequest); if (!IsDemo) Weather = null; }
        if (old.AnalyticsEndpoint != Preferences.AnalyticsEndpoint || old.AnalyticsSite != Preferences.AnalyticsSite) { Invalidate(_analyticsRequest); if (!IsDemo) Analytics = null; }
        if (old.AdSenseAccount != Preferences.AdSenseAccount) { Invalidate(_revenueRequest); if (!IsDemo) Revenue = null; }
        if (old.CodingPath != Preferences.CodingPath && (origin != _codingRequest || originGeneration != _codingRequest.Generation)) { Invalidate(_codingRequest); if (!IsDemo) Coding = null; }
        if (old.CalendarPath != Preferences.CalendarPath && (origin != _calendarRequest || originGeneration != _calendarRequest.Generation))
        {
            Invalidate(_calendarRequest); _calendarSource = null; _calendarRange = null;
            if (!IsDemo) CalendarEvents = [];
        }
        Overlay.Pinned = Preferences.Pinned;
        await ExecuteAsync(() => { ClipboardService.SetEnabled(IsPremium && Preferences.CaptureClipboard); return Task.CompletedTask; });
        if (_disposed) return;
        if (old.FocusMinutes != Preferences.FocusMinutes && !FocusRunning) _focus.Pomodoro.Reset(TimeSpan.FromMinutes(Preferences.FocusMinutes));
        if (old.HydrationMinutes != Preferences.HydrationMinutes)
        {
            var wasRunning = _focus.Hydration.IsRunning;
            _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes));
            if (IsPremium && wasRunning) _focus.Hydration.Start();
        }
        if (old.DemoMode != Preferences.DemoMode)
        {
            _dataGeneration++;
            InvalidateRequests();
            _calendarSource = null; _calendarRange = null;
            _volumeDelay?.Cancel(); _volumeDelay?.Dispose(); _volumeDelay = null;
            if (IsDemo && _realAwake)
                await ExecuteAsync(() => { _systemService.SetAwake(false); _realAwake = false; return Task.CompletedTask; });
            if (_disposed) return;
            Awake = false; Notify(nameof(Awake));
            Weather = null; Revenue = null; Analytics = null; Coding = null; CalendarEvents = [];
            Media = null; System = null; ListeningPorts = [];
            if (IsDemo) LoadDemo();
            else
            {
                await ExecuteAsync(RefreshAsync);
                if (_disposed) return;
                if (!IsDemo && Preferences.CalendarPath is { } calendar) await ExecuteAsync(() => ImportCalendarAsync(calendar));
                if (_disposed) return;
                if (!IsDemo && Preferences.CodingPath is { } coding) await ExecuteAsync(() => ImportCodingAsync(coding));
            }
        }
        if (_disposed) return;
        Notify(nameof(IsDemo)); NotifyTimers();
        var savedPreferencesGeneration = _preferencesGeneration;
        var savedPreferences = Preferences;
        await PersistAsync("preferences", savedPreferences, _lifetimeToken);
        if (savedPreferencesGeneration == _preferencesGeneration) _preferencesChanged = false;
        Notify(nameof(HasUnsavedChanges));
    }
    public Task PlayPauseAsync()
    {
        if (!ReadyForInput()) return Task.CompletedTask;
        if (IsDemo) { if (Media is { } media) Media = media with { IsPlaying = !media.IsPlaying }; return Task.CompletedTask; }
        return _mediaService.PlayPauseAsync();
    }
    public Task PreviousAsync() => !ReadyForInput() || IsDemo ? Task.CompletedTask : _mediaService.PreviousAsync();
    public Task NextAsync() => !ReadyForInput() || IsDemo ? Task.CompletedTask : _mediaService.NextAsync();
    public Task SeekMediaAsync(double proportion) => !ReadyForInput() || !RequirePremiumFeature() || IsDemo || Media is null ? Task.CompletedTask : _mediaService.SeekAsync(Media.Duration * Math.Clamp(proportion, 0, 1));
    public async Task SetVolumeAsync(double value)
    {
        if (!ReadyForInput() || !RequirePremiumFeature()) return;
        if (IsDemo) { if (System is { } system) System = system with { Volume = Math.Clamp(value, 0, 1) }; return; }
        _volumeDelay?.Cancel(); _volumeDelay?.Dispose();
        _volumeDelay = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        var token = _volumeDelay.Token;
        var generation = _dataGeneration;
        try
        {
            await Task.Delay(120, token);
            Task write;
            lock (_shutdownGate)
            {
                if (!CanPublish(generation) || token.IsCancellationRequested) return;
                write = _systemService.SetVolumeAsync(Math.Clamp(value, 0, 1));
                _pendingWrites.Add(write);
            }
            try { await write; }
            finally { lock (_shutdownGate) _pendingWrites.Remove(write); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (Recoverable(error) && (!CanPublish(generation) || token.IsCancellationRequested)) { }
    }
    public void ToggleFocus() { if (!ReadyForInput()) return; if (FocusRunning) _focus.Pomodoro.Pause(); else _focus.Pomodoro.Start(); NotifyTimers(); }
    public void ResetFocus() { if (!ReadyForInput()) return; _focus.Pomodoro.Reset(TimeSpan.FromMinutes(Preferences.FocusMinutes)); NotifyTimers(); }
    public void StartCountdown(int minutes) { if (!ReadyForInput() || !RequirePremiumFeature()) return; _focus.Countdown.Reset(TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180))); _focus.Countdown.Start(); NotifyTimers(); }
    public void ToggleStopwatch() { if (!ReadyForInput() || !RequirePremiumFeature()) return; if (StopwatchRunning) _focus.Stopwatch.Pause(); else _focus.Stopwatch.Start(); UpdateStopwatchTick(); NotifyTimers(); }
    public void ResetStopwatch() { if (!ReadyForInput() || !RequirePremiumFeature()) return; var hadLaps = _focus.Stopwatch.Laps.Count > 0; _focus.Stopwatch.Reset(); UpdateStopwatchTick(); NotifyTimers(); if (hadLaps) Notify(nameof(StopwatchLaps)); }
    public void LapStopwatch() { if (!ReadyForInput() || !RequirePremiumFeature() || !StopwatchRunning) return; _focus.Stopwatch.Lap(); Notify(nameof(StopwatchLaps)); Status = $"Lap {_focus.Stopwatch.Laps.Count}: {StopwatchTime}"; }
    public void DrankWater() { if (!ReadyForInput() || !RequirePremiumFeature()) return; _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes)); _focus.Hydration.Start(); NotifyTimers(); Status = "Hydration reminder reset."; }
    private void UpdateStopwatchTick()
    {
        if (_disposed) return;
        if (StopwatchRunning && SelectedModule == ModuleId.Focus && Overlay.Mode == OverlayMode.Expanded) _stopwatchTick.Start();
        else _stopwatchTick.Stop();
    }
    private void OnTick(object? sender, object args)
    {
        if (_disposed) return;
        ReconcileEntitlement();
        Overlay.SetInteractionSuppressed(!CanPresentActivities());
        Overlay.Tick();
        if (_focus.Pomodoro.Tick()) Activity(ActivityKind.Focus, "Focus", "Session complete", "Take a breath. You earned it.");
        if (IsPremium && _focus.Countdown.Tick()) Activity(ActivityKind.Focus, "Countdown", "Timer complete", null);
        if (IsPremium && _focus.Hydration.Tick()) Activity(ActivityKind.Information, "Hydration", "Time for a little water", "Open Focus to reset your reminder.");
        NotifyTimers();
        var now = DateTimeOffset.Now;
        if (IsPremium)
            foreach (var reminder in Reminders.Where(item => !item.Completed && item.DueAt <= now).ToArray())
                if (!_deliveredReminders.Contains(reminder.Id) && _queuedReminders.Add(reminder.Id))
                {
                    var accepted = Overlay.ShowActivity(new(reminder.Id, ActivityKind.Meeting, "Reminder", reminder.Title, null, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(8), Destination: ModuleId.Calendar));
                    if (!accepted) _queuedReminders.Remove(reminder.Id);
                }
        if (_tickCounter % 300 == 0 && _subscription is not null) _ = ExecuteAsync(RefreshSubscriptionAsync);
        if (++_tickCounter % 5 == 0 && Overlay.Mode == OverlayMode.Expanded && SelectedModule is ModuleId.Home or ModuleId.System or ModuleId.Media or ModuleId.Servers or ModuleId.ScreenTime)
            _ = ExecuteAsync(RefreshAsync);
    }
    private void NotifyTimers()
    {
        foreach (var name in new[] { nameof(FocusTime), nameof(CountdownTime), nameof(StopwatchTime), nameof(HydrationTime), nameof(FocusProgress), nameof(FocusRunning), nameof(StopwatchRunning) }) Notify(name);
    }
    public void Activity(ActivityKind kind, string source, string title, string? detail)
    {
        if (!_disposed)
        {
            Overlay.SetInteractionSuppressed(!CanPresentActivities());
            var destination = kind is ActivityKind.Focus || source == "Hydration" ? ModuleId.Focus : kind == ActivityKind.Meeting ? ModuleId.Calendar : (ModuleId?)null;
            Overlay.ShowActivity(new(Guid.NewGuid(), kind, source, title, detail, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(8), Destination: destination));
        }
    }
    public async Task RefreshWeatherAsync(string? city = null)
    {
        if (!ReadyForInput() || !RequirePremium(ModuleId.Weather) || IsDemo) return;
        var request = BeginRequest(_weatherRequest);
        try
        {
            var snapshot = await _weatherClient.ReadAsync(city ?? Preferences.WeatherCity, request.Token);
            if (!CanPublish(_weatherRequest, request)) return;
            Weather = snapshot;
            if (city is not null) await ApplyPreferencesAsync(Preferences with { WeatherCity = city }, _weatherRequest, request.Generation);
            if (!CanPublish(_weatherRequest, request)) return;
            Status = "Weather updated.";
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(_weatherRequest, request)) { }
    }
    public async Task RefreshRevenueAsync(RevenueProvider provider, int days = 30)
    {
        if (!ReadyForInput() || !RequirePremium(ModuleId.Revenue)) return;
        if (IsDemo) { if (Revenue is { } revenue) Revenue = revenue with { Provider = provider }; return; }
        var request = BeginRequest(_revenueRequest);
        try
        {
            var snapshot = await new RevenueClient(_http, _vault).ReadAsync(provider, days, Preferences.AdSenseAccount, request.Token);
            if (!CanPublish(_revenueRequest, request)) return;
            Revenue = snapshot;
            Status = snapshot.Complete ? "Revenue updated." : "Partial revenue result — pagination limit reached.";
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(_revenueRequest, request)) { }
    }
    public async Task RefreshAnalyticsAsync()
    {
        if (!ReadyForInput() || !RequirePremium(ModuleId.Analytics) || IsDemo) return;
        if (string.IsNullOrWhiteSpace(Preferences.AnalyticsEndpoint)) throw new InvalidOperationException("Configure an HTTPS analytics endpoint in Settings.");
        var request = BeginRequest(_analyticsRequest);
        try
        {
            var snapshot = await new AnalyticsClient(_http, _vault).ReadAsync(Preferences.AnalyticsEndpoint, Preferences.AnalyticsSite, request.Token);
            if (!CanPublish(_analyticsRequest, request)) return;
            Analytics = snapshot;
            Status = "Analytics updated.";
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(_analyticsRequest, request)) { }
    }
    public async Task ImportCodingAsync(string path)
    {
        if (!ReadyForInput() || !RequirePremium(ModuleId.Coding) || IsDemo) return;
        var request = BeginRequest(_codingRequest);
        try
        {
            var snapshot = await CodingImporter.ReadAsync(path, request.Token);
            if (!CanPublish(_codingRequest, request)) return;
            Coding = snapshot;
            if (Preferences.CodingPath != path) await ApplyPreferencesAsync(Preferences with { CodingPath = path }, _codingRequest, request.Generation);
            if (!CanPublish(_codingRequest, request)) return;
            Status = "Imported local token usage. Message content is not read into the UI.";
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(_codingRequest, request)) { }
    }
    public async Task ImportCalendarAsync(string path, DateTime? month = null)
    {
        if (!ReadyForInput() || !RequirePremium(ModuleId.Calendar) || IsDemo) return;
        var request = BeginRequest(_calendarRequest);
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 5 * 1024 * 1024) throw new InvalidDataException("Calendar file exceeds the 5 MB limit.");
            var content = await File.ReadAllTextAsync(path, request.Token);
            if (!CanPublish(_calendarRequest, request)) return;
            var range = CalendarRange.ForMonth(month ?? DateTime.Today);
            var snapshot = await Task.Run(() => IcsCalendar.Parse(content, range.From, range.Until, range.Zone), request.Token);
            if (!CanPublish(_calendarRequest, request)) return;
            _calendarSource = content; _calendarRange = range;
            CalendarEvents = snapshot;
            if (Preferences.CalendarPath != path) await ApplyPreferencesAsync(Preferences with { CalendarPath = path }, _calendarRequest, request.Generation);
            if (!CanPublish(_calendarRequest, request)) return;
            Status = $"Imported {snapshot.Count} calendar events.";
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(_calendarRequest, request)) { }
    }
    public void SaveCredential(string provider, string value)
    {
        if (!ReadyForInput()) return;
        if (!new[] { "stripe", "polar", "dodo", "adsense", "analytics" }.Contains(provider)) throw new ArgumentException("Unknown provider.");
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Enter a credential before saving.");
        _vault.Save(provider, value.Trim()); InvalidateCredential(provider); Status = "Credential stored in Windows Credential Locker.";
    }
    public void DeleteCredential(string provider) { if (!ReadyForInput()) return; _vault.Delete(provider); InvalidateCredential(provider); Status = "Credential removed."; }
    public bool HasCredential(string provider) => !_disposed && !string.IsNullOrEmpty(_vault.Read(provider));
    private void InvalidateCredential(string provider)
    {
        if (provider == "analytics") { Invalidate(_analyticsRequest); if (!IsDemo) Analytics = null; }
        else { Invalidate(_revenueRequest); if (!IsDemo) Revenue = null; }
    }
    public async Task EnsureCalendarMonthAsync(DateTime month)
    {
        if (IsDemo || CalendarRangeLoaded(month) || _calendarSource is null) return;
        var request = BeginRequest(_calendarRequest);
        var range = CalendarRange.ForMonth(month);
        var content = _calendarSource;
        var snapshot = await Task.Run(() => IcsCalendar.Parse(content, range.From, range.Until, range.Zone), request.Token);
        if (!CanPublish(_calendarRequest, request)) return;
        _calendarRange = range; CalendarEvents = snapshot;
    }
    public void OnSuspending() => _focus.OnSuspending();
    public void OnResumed(TimeSpan duration) { _focus.OnResumed(duration); NotifyTimers(); _ = ExecuteAsync(RetryNativeServicesAsync); }
    public async Task RetryNativeServicesAsync()
    {
        if (!ReadyForInput()) return;
        await ExecuteAsync(() => _mediaService.StartAsync(_lifetimeToken));
        if (_disposed) return;
        TryOptionalService(() => ClipboardService.SetEnabled(IsPremium && Preferences.CaptureClipboard));
        await ExecuteAsync(RefreshAsync);
    }
    public async Task RefreshSubscriptionAsync()
    {
        if (_disposed || _subscription is null) return;
        if (string.IsNullOrWhiteSpace(_subscription.SessionToken)) { ReconcileEntitlement(force: true); return; }
        try { await _subscription.RefreshAsync(_lifetimeToken); }
        finally { ReconcileEntitlement(force: true); }
    }
    private void ReconcileEntitlement(bool force = false)
    {
        if (_disposed) return;
        var premium = IsPremium;
        var changed = premium != _lastPremium;
        if (!force && !changed) return;
        _lastPremium = premium;
        TryOptionalService(() => Notify(nameof(IsPremium)));
        TryOptionalService(() => Notify(nameof(PlanStatus)));
        TryOptionalService(() => Notify(nameof(SubscriptionStatus)));
        if (!changed) return;
        if (premium && _loaded)
        {
            TryOptionalService(() => ClipboardService.SetEnabled(Preferences.CaptureClipboard));
            if (!_focus.Hydration.IsRunning) _focus.Hydration.Start();
        }
        if (!premium)
        {
            TryOptionalService(() => ClipboardService.SetEnabled(false));
            _focus.Hydration.Pause(); _focus.Countdown.Pause(); _focus.Stopwatch.Pause(); UpdateStopwatchTick();
            if (_realAwake) TryOptionalService(() => { _systemService.SetAwake(false); _realAwake = false; Awake = false; Notify(nameof(Awake)); });
            Overlay.ClearActivities(); _queuedReminders.Clear();
        }
        if (!CanAccessModule(SelectedModule)) Overlay.Expand(ModuleId.Settings);
    }
    public async Task CheckForUpdatesAsync()
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = DecompressionMethods.All });
        var service = new WindowsUpdateService(http);
        var update = await service.CheckAsync(_lifetimeToken);
        if (update is null) { Status = "You have the current stable version."; return; }
        Status = "Downloading verified update " + update.Version + "…";
        var prepared = await service.DownloadAsync(update, _lifetimeToken);
        if (!await SaveBeforeExitAsync()) return;
        await service.OpenInstallerAsync(prepared, _lifetimeToken);
        Status = $"Update installer opened. Save and quit {ProductIdentity.DisplayName} to let installation proceed.";
    }
    public Task RequestLoginAsync(string email) => _subscription?.RequestLoginAsync(email) ?? Task.FromException(new InvalidOperationException(_subscriptionUnavailable));
    public async Task VerifyLoginAsync(string email, string code) { if (_subscription is null) throw new InvalidOperationException(_subscriptionUnavailable); await _subscription.VerifyLoginAsync(email, code); await RefreshSubscriptionAsync(); }
    public Task<Uri> CheckoutAsync() => _subscription?.CreateCheckoutAsync() ?? Task.FromException<Uri>(new InvalidOperationException(_subscriptionUnavailable));
    public Task<Uri> CustomerPortalAsync() => _subscription?.CreatePortalAsync() ?? Task.FromException<Uri>(new InvalidOperationException(_subscriptionUnavailable));
    public void SignOut() { _subscription?.SignOut(); ReconcileEntitlement(force: true); }
    public async Task SignOutAsync() { var signOut = _subscription?.SignOutAsync(); ReconcileEntitlement(force: true); try { if (signOut is not null) await signOut; } finally { ReconcileEntitlement(force: true); } }
    public void AddNote(string title, string text)
    {
        if (!ReadyForWorkspaceInput() || !RequirePremium(ModuleId.Notes)) return;
        if (Notes.Count >= 100) throw new InvalidOperationException("The local notebook is limited to 100 notes.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Write a note first.");
        WorkspaceLimits.RequireText(title, WorkspaceLimits.MaximumTitleLength, "Note title");
        WorkspaceLimits.RequireText(text, WorkspaceLimits.MaximumTextLength, "Note");
        var note = new SavedNote(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? "Untitled note" : title.Trim(), text, DateTimeOffset.Now);
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Notes = [note, .. Notes] });
        Notes.Insert(0, note);
    }
    public void UpdateNote(Guid id, string title, string text)
    {
        if (!ReadyForWorkspaceInput()) return;
        var item = Notes.FirstOrDefault(note => note.Id == id); if (item is null) return;
        WorkspaceLimits.RequireText(title, WorkspaceLimits.MaximumTitleLength, "Note title");
        WorkspaceLimits.RequireText(text, WorkspaceLimits.MaximumTextLength, "Note");
        var replacement = item with { Title = string.IsNullOrWhiteSpace(title) ? "Untitled note" : title.Trim(), Text = text, UpdatedAt = DateTimeOffset.Now };
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Notes = Notes.Select(note => note.Id == id ? replacement : note).ToArray() });
        Notes[Notes.IndexOf(item)] = replacement;
    }
    public void RemoveNote(Guid id) { if (!ReadyForWorkspaceInput()) return; var item = Notes.FirstOrDefault(note => note.Id == id); if (item is not null) { _deletedNote = item; Notes.Remove(item); Notify(nameof(CanUndoNoteDeletion)); Status = "Note deleted. Undo is available until the next deletion or restart."; } }
    public void UndoNoteDeletion() { if (!ReadyForWorkspaceInput() || _deletedNote is not { } note) return; if (Notes.Count >= 100) throw new InvalidOperationException("Remove a note before restoring this deletion; the notebook is limited to 100 notes."); WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Notes = [note, .. Notes] }); Notes.Insert(0, note); _deletedNote = null; Notify(nameof(CanUndoNoteDeletion)); }
    public void AddReminder(string title, DateTimeOffset due)
    {
        if (!ReadyForWorkspaceInput()) return;
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Write a reminder first.");
        if (Reminders.Count >= 100) throw new InvalidOperationException("The local reminder list is limited to 100 items.");
        if (!RequirePremium(ModuleId.Calendar)) return;
        WorkspaceLimits.RequireText(title, 120, "Reminder title");
        var reminder = new ReminderItem(Guid.NewGuid(), title.Trim(), due, false);
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Reminders = [.. Reminders, reminder] });
        Reminders.Add(reminder);
    }
    public void CompleteReminder(Guid id) { if (!ReadyForWorkspaceInput()) return; var item = Reminders.FirstOrDefault(reminder => reminder.Id == id); if (item is not null) Reminders[Reminders.IndexOf(item)] = item with { Completed = !item.Completed }; }
    public void RemoveReminder(Guid id) { if (!ReadyForWorkspaceInput()) return; var item = Reminders.FirstOrDefault(reminder => reminder.Id == id); if (item is not null) Reminders.Remove(item); }
    public void AddShelf(string path)
    {
        if (!ReadyForWorkspaceInput()) return;
        if (!RequirePremium(ModuleId.Shelf)) return;
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The dropped file is no longer available.");
        if (Shelf.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        if (Shelf.Count >= 100) throw new InvalidOperationException("The file shelf is limited to 100 entries.");
        var shelfItem = new ShelfItem(Guid.NewGuid(), path, DateTimeOffset.Now);
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Shelf = [shelfItem, .. Shelf] });
        Shelf.Insert(0, shelfItem);
    }
    public void RemoveShelf(Guid id) { if (!ReadyForWorkspaceInput()) return; var item = Shelf.FirstOrDefault(file => file.Id == id); if (item is not null) Shelf.Remove(item); }
    public void AddLink(string title, string url)
    {
        if (!ReadyForWorkspaceInput()) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Use an http or https link without embedded credentials.");
        if (Links.Count >= 100) throw new InvalidOperationException("The link shelf is limited to 100 entries.");
        if (!RequirePremium(ModuleId.Links)) return;
        WorkspaceLimits.RequireText(title, 200, "Link title"); WorkspaceLimits.RequireText(url, 4096, "Link URL");
        var link = new SavedLink(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(), uri.AbsoluteUri);
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Links = [link, .. Links] });
        Links.Insert(0, link);
    }
    public void RemoveLink(Guid id) { if (!ReadyForWorkspaceInput()) return; var item = Links.FirstOrDefault(link => link.Id == id); if (item is not null) Links.Remove(item); }
    public void SetAwake(bool awake)
    {
        if (!ReadyForInput() || (awake && !RequirePremiumFeature())) return;
        if (!IsDemo) { _systemService.SetAwake(awake); _realAwake = awake; }
        Awake = awake; Notify(nameof(Awake));
    }
    private void OnRemindersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        _nonScratchpadBytes = null;
        if (_disposed) return;
        if (args.Action == NotifyCollectionChangedAction.Reset) { _deliveredReminders.Clear(); _queuedReminders.Clear(); Overlay.ClearActivities(); }
        if (args.OldItems is { } oldItems)
            foreach (ReminderItem item in oldItems) { _deliveredReminders.Remove(item.Id); _queuedReminders.Remove(item.Id); Overlay.CancelActivity(item.Id); }
        ScheduleSave();
    }
    private void ScheduleSave()
    {
        if (!_loaded || _disposed) return;
        _workspaceDirty = true; _workspaceRevision++; Notify(nameof(HasUnsavedChanges)); SaveState = "Unsaved changes";
        if (!_workspaceReadable) return;
        _saveDelay?.Cancel(); _saveDelay?.Dispose(); _saveDelay = new();
        var token = _saveDelay.Token;
        _ = ExecuteAsync(async () => { await Task.Delay(400, token); await SaveLocalAsync(token); });
    }
    private LocalData LocalSnapshot() => new(Notes.ToArray(), Reminders.ToArray(), Shelf.ToArray(), Links.ToArray(), Scratchpad);
    private static LocalData NormalizeWorkspace(LocalData data)
    {
        ValidateWorkspace(data);
        return new(
            (data.Notes ?? []).Select(item => item with { Title = item.Title ?? "Untitled note", Text = item.Text ?? "" }).ToArray(),
            (data.Reminders ?? []).Select(item => item with { Title = item.Title ?? "Reminder" }).ToArray(),
            (data.Shelf ?? []).Select(item => item with { Path = item.Path ?? "" }).ToArray(),
            (data.Links ?? []).Select(item => item with { Title = item.Title ?? "Saved link", Url = item.Url ?? "" }).ToArray(),
            data.Scratchpad ?? "");
    }
    private static void ValidateWorkspace(LocalData data)
    {
        if ((data.Notes ?? []).Any(item => item is null) || (data.Reminders ?? []).Any(item => item is null) || (data.Shelf ?? []).Any(item => item is null) || (data.Links ?? []).Any(item => item is null))
            throw new InvalidDataException("The notebook contains an invalid empty record. The original file is preserved.");
        if (data.Notes?.Length > 100 || data.Reminders?.Length > 100 || data.Shelf?.Length > 100 || data.Links?.Length > 100
            || data.Scratchpad?.Length > 500000 || (data.Notes ?? []).Any(note => note?.Text?.Length > 500000))
            throw new InvalidDataException("The saved notebook exceeds a supported item or text limit. It must be repaired before saving.");
        if (InvalidIdentifiers(data.Notes, item => item.Id) || InvalidIdentifiers(data.Reminders, item => item.Id)
            || InvalidIdentifiers(data.Shelf, item => item.Id) || InvalidIdentifiers(data.Links, item => item.Id))
            throw new InvalidDataException("The saved notebook contains missing or duplicated item identifiers. It must be repaired before saving.");
    }
    private static bool InvalidIdentifiers<T>(T[]? items, Func<T, Guid> identifier) where T : class
    {
        var seen = new HashSet<Guid>();
        foreach (var item in items ?? [])
            if (item is not null && (identifier(item) == Guid.Empty || !seen.Add(identifier(item)))) return true;
        return false;
    }
    private async Task SaveLocalAsync(CancellationToken cancellationToken = default)
    {
        if (!_workspaceReadable) return;
        var revision = _workspaceRevision;
        var snapshot = LocalSnapshot();
        WorkspaceLimits.RequireStorageBudget(snapshot);
        SaveState = "Saving…";
        try { await PersistAsync("workspace", snapshot, cancellationToken); }
        catch { SaveState = "Save failed — changes remain in memory"; throw; }
        if (revision == _workspaceRevision) { _workspaceDirty = false; SaveState = "Saved locally"; Notify(nameof(HasUnsavedChanges)); }
    }
    public async Task<bool> SaveBeforeExitAsync()
    {
        _saveDelay?.Cancel();
        try
        {
            Task[] writes; lock (_shutdownGate) writes = _pendingWrites.ToArray();
            try { await Task.WhenAll(writes); } catch (Exception error) when (Recoverable(error)) { /* Retry durable saves below; an earlier native or cancelled write cannot decide whether the current notebook is durable. */ }
            if (_preferencesChanged) { var generation = _preferencesGeneration; var snapshot = Preferences; await PersistAsync("preferences", snapshot, CancellationToken.None); if (generation == _preferencesGeneration) _preferencesChanged = false; }
            if (_loaded && !_workspaceReadable && _workspaceDirty) throw new InvalidDataException("Recover or export the unsaved notebook before quitting.");
            if (_loaded && _workspaceReadable) await SaveLocalAsync(CancellationToken.None);
            Notify(nameof(HasUnsavedChanges));
            if (HasUnsavedChanges) throw new InvalidOperationException("New changes arrived while saving. Retry after editing finishes.");
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { SaveState = "Save failed — export or retry before quitting"; ShowError("Your changes could not be saved: " + error.Message); return false; }
    }
    public async Task ExportWorkspaceAsync(string path)
    {
        var bytes = WorkspaceLimits.SerializeExport(LocalSnapshot());
        await File.WriteAllBytesAsync(path, bytes, _lifetimeToken);
        Status = "Notebook exported. Keep the file private; it contains your local notes and shortcuts.";
    }
    public async Task RestoreWorkspaceAsync(string path)
    {
        var file = new FileInfo(path);
        if (file.Length > LocalStore.MaximumBytes) throw new InvalidDataException("Notebook import exceeds 10 MB.");
        var data = global::System.Text.Json.JsonSerializer.Deserialize<LocalData>(await File.ReadAllTextAsync(path, _lifetimeToken), new global::System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("The export contains no notebook.");
        data = NormalizeWorkspace(data); WorkspaceLimits.RequireStorageBudget(data);
        if (!_workspaceReadable) await RecoverWorkspaceAsync();
        await ExportWorkspaceAsync(Path.Combine(_dataDirectory, "workspace-before-import-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
        _deletedNote = null; Notify(nameof(CanUndoNoteDeletion));
        Notes.Clear(); foreach (var item in data.Notes ?? []) Notes.Add(item);
        Reminders.Clear(); foreach (var item in data.Reminders ?? []) Reminders.Add(item);
        Shelf.Clear(); foreach (var item in data.Shelf ?? []) Shelf.Add(item);
        Links.Clear(); foreach (var item in data.Links ?? []) Links.Add(item);
        _scratchpad = data.Scratchpad ?? ""; Notify(nameof(Scratchpad)); ScheduleSave();
        await SaveLocalAsync(_lifetimeToken); Status = "Notebook restored. A backup of the previous workspace was kept.";
    }
    public Task RecoverWorkspaceAsync()
    {
        if (_workspaceReadable) return Task.CompletedTask;
        var source = Path.Combine(_dataDirectory, "workspace.json");
        if (File.Exists(source)) File.Move(source, Path.Combine(_dataDirectory, "workspace-unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json"));
        _workspaceReadable = true; Notify(nameof(WorkspaceReadable)); Error = ""; Status = "Unreadable file preserved as a backup. You can now restore an export or start a new notebook.";
        return Task.CompletedTask;
    }
    private async Task PersistAsync<T>(string name, T value, CancellationToken cancellationToken)
    {
        Task write;
        lock (_shutdownGate)
        {
            if (_disposed) return;
            write = _store.WriteAsync(name, value, cancellationToken);
            _pendingWrites.Add(write);
        }
        try { await write; }
        finally { lock (_shutdownGate) _pendingWrites.Remove(write); }
    }
    private static bool Recoverable(Exception error) => error is not OutOfMemoryException and not StackOverflowException;
    private void TryOptionalService(Action operation)
    {
        try { operation(); }
        catch (Exception error) when (Recoverable(error)) { ReportOptionalFailure(error); }
    }
    private void ReportOptionalFailure(Exception error)
    {
        StartupDiagnostics.Write("MainViewModel.OptionalService", error);
        try { ShowError(error.Message); }
        catch (Exception displayError) when (Recoverable(displayError))
        {
            // The error text is retained even when its native view cannot receive it.
            // Never turn a failed fallback display into a dispatcher exception.
            StartupDiagnostics.Write("MainViewModel.OptionalErrorDisplay", displayError);
        }
    }
    private bool CanPresentActivities()
    {
        try { return CanPresentActivity?.Invoke() != false; }
        catch (Exception error) when (Recoverable(error)) { ReportOptionalFailure(error); return false; }
    }
    private bool ReadyForInput()
    {
        if (_disposed) return false;
        if (_loaded) return true;
        ShowError($"{ProductIdentity.DisplayName} is loading your saved settings and notebook. Please wait a moment.");
        return false;
    }
    private bool ReadyForWorkspaceInput()
    {
        if (!ReadyForInput()) return false;
        if (!_workspaceReadable) throw new InvalidOperationException("Preserve the unreadable notebook using Settings recovery before editing. Your draft remains available for export.");
        return true;
    }
    private static string Limited(string? value, int maximum, string fallback = "")
    {
        value ??= fallback;
        if (value.Length == 0) value = fallback;
        return value[..Math.Min(maximum, value.Length)];
    }
    private static AppPreferences NormalizePreferences(AppPreferences value)
    {
        var normalized = AppPreferences.Normalize(value) with
        {
            AnalyticsSite = Limited(value.AnalyticsSite?.Trim(), 200, "My workspace"),
            AnalyticsEndpoint = string.IsNullOrWhiteSpace(value.AnalyticsEndpoint) ? null : value.AnalyticsEndpoint.Trim(),
            AdSenseAccount = string.IsNullOrWhiteSpace(value.AdSenseAccount) ? null : value.AdSenseAccount.Trim(),
            CalendarPath = string.IsNullOrWhiteSpace(value.CalendarPath) ? null : value.CalendarPath,
            CodingPath = string.IsNullOrWhiteSpace(value.CodingPath) ? null : value.CodingPath,
        };
        if (normalized.AnalyticsEndpoint is { } endpoint && (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)))
            throw new ArgumentException("Use an absolute HTTPS analytics endpoint without credentials or a fragment in its URL.");
        return normalized;
    }
    private RequestContext BeginRequest(RequestSlot slot)
    {
        Invalidate(slot);
        slot.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
        return new(slot.Generation, _dataGeneration, slot.Cancellation.Token);
    }
    private static void Invalidate(RequestSlot slot)
    {
        slot.Generation++;
        slot.Cancellation?.Cancel(); slot.Cancellation?.Dispose(); slot.Cancellation = null;
    }
    private void InvalidateRequests()
    {
        foreach (var slot in new[] { _weatherRequest, _revenueRequest, _analyticsRequest, _codingRequest, _calendarRequest }) Invalidate(slot);
    }
    private bool CanPublish(int generation) => !_disposed && !IsDemo && generation == _dataGeneration;
    private bool CanPublish(RequestSlot slot, RequestContext request) => CanPublish(request.DataGeneration) && request.Generation == slot.Generation && !request.Token.IsCancellationRequested;
    private sealed class RequestSlot
    {
        public int Generation;
        public CancellationTokenSource? Cancellation;
    }
    private readonly record struct RequestContext(int Generation, int DataGeneration, CancellationToken Token);
    private void LoadDemo()
    {
        if (_disposed || !IsDemo) return;
        Media = new("Golden Hour Drive", "Neon Harbor · Coastline", null, false, TimeSpan.FromSeconds(74), TimeSpan.FromSeconds(214), "Sample media — no audio", false);
        System = new(12, 48, 78, .64, "Sample output", TimeSpan.FromHours(3.3)); ListeningPorts = [3000, 5173, 8080];
        Revenue = new(RevenueProvider.Stripe, 7896.30m, "USD", Enumerable.Range(0, 8).Select(i => new RevenuePayment($"sample-{i}", i % 2 == 0 ? "Pro plan · sample" : "Team plan · sample", i % 2 == 0 ? 49 : 149, "USD", DateTimeOffset.Now.AddMinutes(-i * 4))).ToArray(), Enumerable.Range(0, 30).Select(i => (decimal)(20 + (i * 31 % 190))).ToArray(), DateTimeOffset.Now);
        Analytics = new("Sample workspace", 142, 9812, 1915, Enumerable.Range(0, 30).Select(i => 2 + (i * 7 % 18)).ToArray(), [new("/", 48), new("/pricing", 27), new("/features", 19), new("/changelog", 14)], DateTimeOffset.Now);
        Coding = new("Sample local activity", 2300000, 1600000, 186, Enumerable.Range(0, 140).Select(i => new CodingDay(DateOnly.FromDateTime(DateTime.Now.AddDays(-i)), i * 139 % 9000, i * 61 % 3000, i % 4)).ToArray(), "Explicit demo mode");
        Weather = new("San Francisco · sample", 18, 17, 64, 14, 2, Enumerable.Range(0, 7).Select(i => new WeatherHour(DateTimeOffset.Now.AddHours(i), 18 + i % 3, i % 3)).ToArray(), Enumerable.Range(0, 7).Select(i => new WeatherDay(DateOnly.FromDateTime(DateTime.Today.AddDays(i)), 20 + i % 4, 12 + i % 3, i % 4)).ToArray(), DateTimeOffset.Now);
        CalendarEvents = [new("Product review · sample", DateTimeOffset.Now.AddHours(1), DateTimeOffset.Now.AddHours(2), null)];
        Status = "DEMO MODE — all displayed metrics are sample data.";
    }
    public async ValueTask DiscardAndDisposeAsync() { _discardOnDispose = true; await DisposeAsync(); }
    public async ValueTask DisposeAsync()
    {
        if (!_disposed && !_discardOnDispose && !await SaveBeforeExitAsync()) throw new IOException("The notebook could not be saved; disposal was cancelled to preserve your edits.");
        Task[] pendingWrites;
        lock (_shutdownGate)
        {
            if (_disposed) return;
            _disposed = true;
            pendingWrites = _pendingWrites.ToArray();
        }
        Notify(nameof(IsReady));
        _tick.Stop(); _stopwatchTick.Stop(); _saveDelay?.Cancel(); _volumeDelay?.Cancel();
        _lifetime.Cancel();
        InvalidateRequests();
        // No new refresh can acquire the gate after _disposed is set under _shutdownGate.
        // Acquire it once more so the current reader releases it before services are disposed.
        await _refreshLock.WaitAsync();
        try { await Task.WhenAll(pendingWrites); } catch (Exception error) when (Recoverable(error)) { }
        ClipboardService.Changed -= OnClipboardChanged; ClipboardService.Error -= OnServiceError;
        try { ClipboardService.Dispose(); } catch (Exception error) when (Recoverable(error)) { }
        _mediaService.Changed -= OnMediaChanged; _mediaService.Error -= OnServiceError;
        try { await _mediaService.DisposeAsync(); } catch (Exception error) when (Recoverable(error)) { }
        try { _systemService.Dispose(); } catch (Exception error) when (Recoverable(error)) { }
        _http.Dispose(); _store.Dispose();
        _saveDelay?.Dispose(); _volumeDelay?.Dispose(); _lifetime.Dispose(); _refreshLock.Dispose();
    }
    public sealed record LocalData(SavedNote[] Notes, ReminderItem[] Reminders, ShelfItem[] Shelf, SavedLink[] Links, string Scratchpad);
}
