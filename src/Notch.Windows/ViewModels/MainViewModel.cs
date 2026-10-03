using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Core.Providers;
using Notch.Windows.Services;
using System.Collections.ObjectModel;
using System.Net;
using System.Collections.Specialized;

namespace Notch.Windows.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly IMediaService _mediaService = new WindowsMediaService();
    private readonly ISystemService _systemService = new WindowsSystemService();
    private readonly ISecretVault _vault = new WindowsSecretVault();
    private readonly LocalStore _store;
    private readonly string _dataDirectory;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly WeatherClient _weatherClient;
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
    public string Scratchpad { get => _scratchpad; set { if (!ReadyForInput()) return; value ??= ""; if (Set(ref _scratchpad, value.Length > 500000 ? value[..500000] : value)) ScheduleSave(); } }
    public IReadOnlyList<int> ListeningPorts { get => _ports; private set => Set(ref _ports, value); }
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
    private string WorkspaceRecoveryMessage => $"Your saved notebook could not be read. Its file is preserved and notebook saving is paused. Back up and repair or rename {Path.Combine(_dataDirectory, "workspace.json")}, then restart Notch.";

    public MainViewModel(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _lifetimeToken = _lifetime.Token;
        _weatherClient = new(_http);
        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch");
        _store = new(_dataDirectory);
        ClipboardService = new(dispatcher);
        ClipboardService.Changed += OnClipboardChanged;
        _mediaService.Changed += OnMediaChanged;
        Overlay.Changed += (_, _) => { if (_disposed) return; Notify(nameof(SelectedModule)); _ = RefreshForViewAsync(); UpdateStopwatchTick(); };
        _tick.Tick += OnTick;
        _stopwatchTick.Tick += (_, _) => { if (_disposed) return; Notify(nameof(StopwatchTime)); Notify(nameof(StopwatchLaps)); };
        foreach (var collection in new INotifyCollectionChanged[] { Notes, Shelf, Links })
            collection.CollectionChanged += (_, _) => ScheduleSave();
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
                    if (local is not null) ValidateWorkspace(local);
                }
                catch (Exception error) when (Recoverable(error) && error is not OperationCanceledException)
                {
                    _workspaceReadable = false;
                    Notify(nameof(WorkspaceReadable));
                    throw new InvalidDataException(WorkspaceRecoveryMessage, error);
                }
                if (_disposed || local is null) return;
                foreach (var item in (local.Notes ?? []).Where(item => item is not null))
                    Notes.Add(item with { Title = item.Title ?? "Untitled note", Text = item.Text ?? "" });
                foreach (var item in (local.Reminders ?? []).Where(item => item is not null))
                    Reminders.Add(item with { Title = item.Title ?? "Reminder" });
                foreach (var item in (local.Shelf ?? []).Where(item => item is not null))
                    Shelf.Add(item with { Path = item.Path ?? "" });
                foreach (var item in (local.Links ?? []).Where(item => item is not null))
                    Links.Add(item with { Title = item.Title ?? "Saved link", Url = item.Url ?? "" });
                _scratchpad = local.Scratchpad ?? ""; Notify(nameof(Scratchpad));
            });
            if (_disposed) return;
            Overlay.Pinned = Preferences.Pinned;
            _focus.Pomodoro.Reset(TimeSpan.FromMinutes(Preferences.FocusMinutes));
            _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes));
            _focus.Hydration.Start();
            _loaded = true;
            _tick.Start();
            NotifyTimers();
            if (IsDemo) LoadDemo();
            Notify(nameof(IsReady));
            await ExecuteAsync(() => { ClipboardService.SetEnabled(Preferences.CaptureClipboard); return Task.CompletedTask; });
            if (_disposed) return;
            await ExecuteAsync(() => _mediaService.StartAsync(_lifetimeToken));
            if (_disposed) return;
            await ExecuteAsync(RefreshAsync);
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
    public void SelectModule(ModuleId module) { if (_disposed) return; Error = _workspaceReadable ? "" : WorkspaceRecoveryMessage; Overlay.Expand(module); }
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
            var message = error is HttpRequestException ? "The service could not be reached. Check the connection and try again." : error.Message;
            Error = !_workspaceReadable && !message.Contains(WorkspaceRecoveryMessage, StringComparison.Ordinal) ? $"{WorkspaceRecoveryMessage}\n{message}" : message;
        }
    }
    public async Task RefreshAsync()
    {
        int generation;
        lock (_shutdownGate)
        {
            if (_disposed || !_loaded || IsDemo || !_refreshLock.Wait(0)) return;
            generation = _dataGeneration;
        }
        try
        {
            var snapshot = await _systemService.ReadAsync(_lifetimeToken);
            if (!CanPublish(generation)) return;
            System = snapshot;
            ListeningPorts = _systemService.ListeningPorts();
            Media = _mediaService.Current;
        }
        catch (Exception error) when (Recoverable(error) && !CanPublish(generation)) { }
        finally { _refreshLock.Release(); }
    }
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
        if (old.CalendarPath != Preferences.CalendarPath && (origin != _calendarRequest || originGeneration != _calendarRequest.Generation)) { Invalidate(_calendarRequest); if (!IsDemo) CalendarEvents = []; }
        Overlay.Pinned = Preferences.Pinned;
        await ExecuteAsync(() => { ClipboardService.SetEnabled(Preferences.CaptureClipboard); return Task.CompletedTask; });
        if (_disposed) return;
        if (old.FocusMinutes != Preferences.FocusMinutes && !FocusRunning) _focus.Pomodoro.Reset(TimeSpan.FromMinutes(Preferences.FocusMinutes));
        if (old.HydrationMinutes != Preferences.HydrationMinutes) { _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes)); _focus.Hydration.Start(); }
        if (old.DemoMode != Preferences.DemoMode)
        {
            _dataGeneration++;
            InvalidateRequests();
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
        await PersistAsync("preferences", Preferences, _lifetimeToken);
    }
    public Task PlayPauseAsync()
    {
        if (!ReadyForInput()) return Task.CompletedTask;
        if (IsDemo) { if (Media is { } media) Media = media with { IsPlaying = !media.IsPlaying }; return Task.CompletedTask; }
        return _mediaService.PlayPauseAsync();
    }
    public Task PreviousAsync() => !ReadyForInput() || IsDemo ? Task.CompletedTask : _mediaService.PreviousAsync();
    public Task NextAsync() => !ReadyForInput() || IsDemo ? Task.CompletedTask : _mediaService.NextAsync();
    public Task SeekMediaAsync(double proportion) => !ReadyForInput() || IsDemo || Media is null ? Task.CompletedTask : _mediaService.SeekAsync(Media.Duration * Math.Clamp(proportion, 0, 1));
    public async Task SetVolumeAsync(double value)
    {
        if (!ReadyForInput()) return;
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
    public void StartCountdown(int minutes) { if (!ReadyForInput()) return; _focus.Countdown.Reset(TimeSpan.FromMinutes(Math.Clamp(minutes, 1, 180))); _focus.Countdown.Start(); NotifyTimers(); }
    public void ToggleStopwatch() { if (!ReadyForInput()) return; if (StopwatchRunning) _focus.Stopwatch.Pause(); else _focus.Stopwatch.Start(); UpdateStopwatchTick(); NotifyTimers(); }
    public void ResetStopwatch() { if (!ReadyForInput()) return; _focus.Stopwatch.Reset(); UpdateStopwatchTick(); NotifyTimers(); }
    public void LapStopwatch() { if (!ReadyForInput()) return; _focus.Stopwatch.Lap(); Notify(nameof(StopwatchLaps)); Status = $"Lap {_focus.Stopwatch.Laps.Count}: {StopwatchTime}"; }
    public void DrankWater() { if (!ReadyForInput()) return; _focus.Hydration.Reset(TimeSpan.FromMinutes(Preferences.HydrationMinutes)); _focus.Hydration.Start(); NotifyTimers(); Status = "Hydration reminder reset."; }
    private void UpdateStopwatchTick()
    {
        if (_disposed) return;
        if (StopwatchRunning && SelectedModule == ModuleId.Focus && Overlay.Mode == OverlayMode.Expanded) _stopwatchTick.Start();
        else _stopwatchTick.Stop();
    }
    private void OnTick(object? sender, object args)
    {
        if (_disposed) return;
        Overlay.Tick();
        if (_focus.Pomodoro.Tick()) Activity(ActivityKind.Focus, "Focus", "Session complete", "Take a breath. You earned it.");
        if (_focus.Countdown.Tick()) Activity(ActivityKind.Focus, "Countdown", "Timer complete", null);
        if (_focus.Hydration.Tick()) Activity(ActivityKind.Information, "Hydration", "Time for a little water", "Open Focus to reset your reminder.");
        NotifyTimers();
        var now = DateTimeOffset.Now;
        foreach (var reminder in Reminders.Where(item => !item.Completed && item.DueAt <= now).ToArray())
            if (_deliveredReminders.Add(reminder.Id)) Activity(ActivityKind.Meeting, "Reminder", reminder.Title, null);
        if (++_tickCounter % 5 == 0 && Overlay.Mode == OverlayMode.Expanded && SelectedModule is ModuleId.Home or ModuleId.System or ModuleId.Media or ModuleId.Servers or ModuleId.ScreenTime)
            _ = ExecuteAsync(RefreshAsync);
    }
    private void NotifyTimers()
    {
        foreach (var name in new[] { nameof(FocusTime), nameof(CountdownTime), nameof(StopwatchTime), nameof(HydrationTime), nameof(FocusProgress), nameof(FocusRunning), nameof(StopwatchRunning) }) Notify(name);
    }
    public void Activity(ActivityKind kind, string source, string title, string? detail)
    {
        if (!_disposed) Overlay.ShowActivity(new(Guid.NewGuid(), kind, source, title, detail, DateTimeOffset.UtcNow, TimeSpan.FromSeconds(8)));
    }
    public async Task RefreshWeatherAsync(string? city = null)
    {
        if (!ReadyForInput() || IsDemo) return;
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
        if (!ReadyForInput()) return;
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
        if (!ReadyForInput() || IsDemo) return;
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
        if (!ReadyForInput() || IsDemo) return;
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
    public async Task ImportCalendarAsync(string path)
    {
        if (!ReadyForInput() || IsDemo) return;
        var request = BeginRequest(_calendarRequest);
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 5 * 1024 * 1024) throw new InvalidDataException("Calendar file exceeds the 5 MB limit.");
            var content = await File.ReadAllTextAsync(path, request.Token);
            if (!CanPublish(_calendarRequest, request)) return;
            var lowerBound = DateTimeOffset.Now.AddMonths(-1); var upperBound = DateTimeOffset.Now.AddMonths(3);
            var snapshot = await Task.Run(() => IcsCalendar.Parse(content, lowerBound, upperBound), request.Token);
            if (!CanPublish(_calendarRequest, request)) return;
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
    public void AddNote(string title, string text)
    {
        if (!ReadyForInput()) return;
        if (Notes.Count >= 100) throw new InvalidOperationException("The local notebook is limited to 100 notes.");
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Write a note first.");
        Notes.Insert(0, new(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? "Untitled note" : title.Trim()[..Math.Min(80, title.Trim().Length)], text[..Math.Min(500000, text.Length)], DateTimeOffset.Now));
    }
    public void UpdateNote(Guid id, string title, string text)
    {
        if (!ReadyForInput()) return;
        var item = Notes.FirstOrDefault(note => note.Id == id); if (item is null) return;
        Notes[Notes.IndexOf(item)] = item with { Title = string.IsNullOrWhiteSpace(title) ? "Untitled note" : title[..Math.Min(80, title.Length)], Text = text[..Math.Min(500000, text.Length)], UpdatedAt = DateTimeOffset.Now };
    }
    public void RemoveNote(Guid id) { if (!ReadyForInput()) return; var item = Notes.FirstOrDefault(note => note.Id == id); if (item is not null) Notes.Remove(item); }
    public void AddReminder(string title, DateTimeOffset due)
    {
        if (!ReadyForInput()) return;
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Write a reminder first.");
        if (Reminders.Count >= 100) throw new InvalidOperationException("The local reminder list is limited to 100 items.");
        Reminders.Add(new(Guid.NewGuid(), title.Trim()[..Math.Min(120, title.Trim().Length)], due, false));
    }
    public void CompleteReminder(Guid id) { if (!ReadyForInput()) return; var item = Reminders.FirstOrDefault(reminder => reminder.Id == id); if (item is not null) Reminders[Reminders.IndexOf(item)] = item with { Completed = !item.Completed }; }
    public void RemoveReminder(Guid id) { if (!ReadyForInput()) return; var item = Reminders.FirstOrDefault(reminder => reminder.Id == id); if (item is not null) Reminders.Remove(item); }
    public void AddShelf(string path)
    {
        if (!ReadyForInput()) return;
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("The dropped file is no longer available.");
        if (Shelf.Any(item => item.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        if (Shelf.Count >= 100) throw new InvalidOperationException("The file shelf is limited to 100 entries.");
        Shelf.Insert(0, new(Guid.NewGuid(), path, DateTimeOffset.Now));
    }
    public void RemoveShelf(Guid id) { if (!ReadyForInput()) return; var item = Shelf.FirstOrDefault(file => file.Id == id); if (item is not null) Shelf.Remove(item); }
    public void AddLink(string title, string url)
    {
        if (!ReadyForInput()) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Use an http or https link without embedded credentials.");
        if (Links.Count >= 100) throw new InvalidOperationException("The link shelf is limited to 100 entries.");
        Links.Insert(0, new(Guid.NewGuid(), string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(), uri.AbsoluteUri));
    }
    public void RemoveLink(Guid id) { if (!ReadyForInput()) return; var item = Links.FirstOrDefault(link => link.Id == id); if (item is not null) Links.Remove(item); }
    public void SetAwake(bool awake)
    {
        if (!ReadyForInput()) return;
        if (!IsDemo) { _systemService.SetAwake(awake); _realAwake = awake; }
        Awake = awake; Notify(nameof(Awake));
    }
    private void OnRemindersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed) return;
        if (args.Action == NotifyCollectionChangedAction.Reset) _deliveredReminders.Clear();
        if (args.OldItems is { } oldItems)
            foreach (ReminderItem item in oldItems) _deliveredReminders.Remove(item.Id);
        ScheduleSave();
    }
    private void ScheduleSave()
    {
        if (!_loaded || _disposed || !_workspaceReadable) return;
        _saveDelay?.Cancel(); _saveDelay?.Dispose(); _saveDelay = new();
        var token = _saveDelay.Token;
        _ = ExecuteAsync(async () => { await Task.Delay(400, token); await SaveLocalAsync(token); });
    }
    private LocalData LocalSnapshot() => new(Notes.ToArray(), Reminders.ToArray(), Shelf.ToArray(), Links.ToArray(), Scratchpad);
    private static void ValidateWorkspace(LocalData data)
    {
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
    private Task SaveLocalAsync(CancellationToken cancellationToken = default) => !_workspaceReadable ? Task.CompletedTask : PersistAsync("workspace", LocalSnapshot(), cancellationToken);
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
    private bool ReadyForInput()
    {
        if (_disposed) return false;
        if (_loaded) return true;
        ShowError("Notch is loading your saved settings and notebook. Please wait a moment.");
        return false;
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
    public async ValueTask DisposeAsync()
    {
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
        try { if (_preferencesChanged) await _store.WriteAsync("preferences", Preferences); }
        catch (Exception error) when (Recoverable(error)) { }
        try { if (_loaded && _workspaceReadable) await _store.WriteAsync("workspace", LocalSnapshot()); }
        catch (Exception error) when (Recoverable(error)) { }
        ClipboardService.Changed -= OnClipboardChanged; ClipboardService.Dispose();
        _mediaService.Changed -= OnMediaChanged;
        try { await _mediaService.DisposeAsync(); } catch (Exception error) when (Recoverable(error)) { }
        _systemService.Dispose(); _http.Dispose(); _store.Dispose();
        _saveDelay?.Dispose(); _volumeDelay?.Dispose(); _lifetime.Dispose(); _refreshLock.Dispose();
    }
    public sealed record LocalData(SavedNote[] Notes, ReminderItem[] Reminders, ShelfItem[] Shelf, SavedLink[] Links, string Scratchpad);
}
