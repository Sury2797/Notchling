using Microsoft.UI.Dispatching;
using System.Globalization;
using Notch.Core;
using Notch.Windows.ViewModels;

internal sealed class ConnectionMediaDouble : IMediaService
{
    public MediaSnapshot? Current { get; private set; }
    public Exception? Failure { get; set; }
    public TaskCompletionSource? Gate { get; set; }
    public TaskCompletionSource Started { get; private set; } = Signal();
    public int Starts { get; private set; }
    public bool CancellationObserved { get; private set; }
    public bool Disposed { get; private set; }
    public event EventHandler<MediaSnapshot?>? Changed;
    public event EventHandler<string>? Error;
    public void PrepareBlockedStart() { Gate = Signal(); Started = Signal(); }
    public void Publish(MediaSnapshot? snapshot) { Current = snapshot; Changed?.Invoke(this, snapshot); }
    public void ReportError(string detail) => Error?.Invoke(this, detail);
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        Starts++; Started.TrySetResult();
        if (Gate is { } gate)
        {
            try { await gate.Task.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { CancellationObserved = true; throw; }
        }
        if (Failure is { } error) throw error;
    }
    public Task PlayPauseAsync() => Task.CompletedTask;
    public Task PreviousAsync() => Task.CompletedTask;
    public Task NextAsync() => Task.CompletedTask;
    public Task SeekAsync(TimeSpan position) => Task.CompletedTask;
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ConnectionSystemDouble : ISystemService
{
    public SystemSnapshot Snapshot { get; set; } = new(5, 25, null, .4, "Actual test output", TimeSpan.FromMinutes(20));
    public Exception? Failure { get; set; }
    public int Reads { get; private set; }
    public bool Disposed { get; private set; }
    public Task<SystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Reads++;
        return Failure is { } failure ? Task.FromException<SystemSnapshot>(failure) : Task.FromResult(Snapshot);
    }
    public Task SetVolumeAsync(double volume) => Task.CompletedTask;
    public IReadOnlyList<int> ListeningPorts() => [];
    public void SetAwake(bool awake) { }
    public void Dispose() => Disposed = true;
}

internal sealed class ConnectionVaultDouble : ISecretVault
{
    private readonly Dictionary<string, string> _secrets = [];
    public Exception? ReadFailure { get; set; }
    public int Reads { get; private set; }
    public void Save(string name, string value) => _secrets[name] = value;
    public string? Read(string name) { Reads++; if (ReadFailure is { } failure) throw failure; return _secrets.GetValueOrDefault(name); }
    public void Delete(string name) => _secrets.Remove(name);
}

internal static class ConnectionStatusCases
{
    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static ToolConnectionStatus Row(MainViewModel vm, string id) => vm.ConnectionStatuses.Single(row => row.Id == id);
    private static MainViewModel Create(string data, ConnectionMediaDouble media, ConnectionSystemDouble system, ConnectionVaultDouble? vault = null)
        => new(new DispatcherQueue(), dataDirectory: data, mediaService: media, systemService: system, vault: vault ?? new ConnectionVaultDouble());

    public static async Task RegisterAsync(Func<string, Func<Task>, Task> run, string data)
    {
        await run("Connection health distinguishes unconfigured providers from free tool access", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            Assert(vm.IsPublicTesting && !vm.IsPremium && Enum.GetValues<ModuleId>().All(vm.CanAccessModule), "Public testing relocked tools or claimed a purchase.");
            foreach (var id in new[] { "stripe", "analytics", "calendar", "coding", "weather", "clipboard" })
                Assert(Row(vm, id).State == ConnectionState.NeedsSetup, $"Unconfigured {id} was reported connected.");
            Assert(!vm.CanRefreshWeather && vm.WeatherConnectionGuidance.Contains("not been configured", StringComparison.Ordinal), "Weather implied an unavailable deployment was usable.");
            await vm.RefreshConnectionsAsync();
            Assert(!vm.IsCheckingConnections && Row(vm, "media").State == ConnectionState.Ready && Row(vm, "audio").State == ConnectionState.Ready,
                "An available native service with no player was reported as a connected source or failed device.");
            foreach (var id in new[] { "stripe", "analytics", "calendar", "coding", "weather" })
                Assert(Row(vm, id).State == ConnectionState.NeedsSetup && Row(vm, id).CheckedAt is null, $"Unconfigured {id} was checked or connected without its setup.");
            Assert(vm.Revenue is null && vm.Analytics is null && vm.Weather is null, "An unconfigured provider produced a fabricated snapshot.");
        });
        await run("Saved reporting credentials are not verified connections and deletion invalidates readiness", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            await vm.SetPreferencesAsync(vm.Preferences with { AnalyticsEndpoint = "https://analytics.example.invalid/data" });
            vm.SaveCredential("stripe", "synthetic-read-only-key");
            vm.SaveCredential("analytics", "synthetic-analytics-token");
            foreach (var id in new[] { "stripe", "analytics" })
                Assert(Row(vm, id).State == ConnectionState.NotChecked && Row(vm, id).CheckedAt is null,
                    $"Saving {id} was treated as a missing setup or verified read access.");
            Assert(vm.Revenue is null && vm.Analytics is null && !vm.IsPremium, "Saving provider credentials fabricated data or a subscription.");
            vm.DeleteCredential("stripe"); vm.DeleteCredential("analytics");
            Assert(Row(vm, "stripe").State == ConnectionState.NeedsSetup && Row(vm, "analytics").State == ConnectionState.NeedsSetup,
                "Deleted credentials retained connection readiness.");
            Assert(Row(vm, "stripe").CheckedAt is null && Row(vm, "analytics").CheckedAt is null, "Deleted credentials retained an old successful check timestamp.");
        });
        await run("Sample-data preview cannot claim or recheck live connections", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
            var starts = media.Starts; var reads = system.Reads;
            await vm.RefreshConnectionsAsync();
            Assert(vm.IsDemo && vm.ConnectionStatuses.All(row => row.State == ConnectionState.NotChecked && row.CheckedAt is null), "Preview data claimed live readiness or a check timestamp.");
            Assert(media.Starts == starts && system.Reads == reads && !vm.IsCheckingConnections, "Preview connection checks contacted native sources.");
            await vm.ExitDemoAsync();
            Assert(!vm.IsDemo && vm.Media is null && vm.Revenue is null && vm.Analytics is null && vm.Weather is null,
                "Preview exit retained sampled provider data as a real connection.");
        });
        await run("Missing Windows audio output remains unavailable rather than a failed connection", async () =>
        {
            var media = new ConnectionMediaDouble();
            var system = new ConnectionSystemDouble { Snapshot = new(5, 25, null, 0, "No audio output", TimeSpan.FromMinutes(20), AudioAvailable: false) };
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            Assert(Row(vm, "audio").State == ConnectionState.Unavailable, "Startup misclassified a disconnected output device.");
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "audio").State == ConnectionState.Unavailable && Row(vm, "audio").Detail.Contains("device", StringComparison.OrdinalIgnoreCase),
                "An absent output endpoint became a generic failed request or a false success.");
            system.Snapshot = new(5, 25, null, .5, "Reconnected headphones", TimeSpan.FromMinutes(20));
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "audio").State == ConnectionState.Ready && vm.System?.OutputDevice == "Reconnected headphones", "A reconnected Windows output did not recover.");
        });
        await run("Native connection failures replace prior success and a subsequent check recovers", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            media.Publish(new("Real track", "Real artist", null, false, TimeSpan.Zero, TimeSpan.FromMinutes(4), "FixturePlayer", true));
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            await vm.RefreshConnectionsAsync();
            var lastSystem = vm.System;
            media.Failure = new IOException("private native failure details"); system.Failure = new IOException("private device details");
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "media").State == ConnectionState.Failed && Row(vm, "audio").State == ConnectionState.Failed,
                "A failed native check retained its previous successful connection status.");
            Assert(ReferenceEquals(lastSystem, vm.System) && !Row(vm, "audio").Detail.Contains("private device", StringComparison.Ordinal),
                "A native failure destroyed the last useful reading or exposed private error detail.");
            media.Failure = null; system.Failure = null;
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "media").State == ConnectionState.Connected && Row(vm, "audio").State == ConnectionState.Ready,
                "Native connections could not recover after transient failures.");
        });
        await run("Ordinary telemetry refresh failure replaces audio readiness without deleting cached readings", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            var last = vm.System;
            Assert(Row(vm, "audio").State == ConnectionState.Ready, "The initial native output was not ready.");
            system.Failure = new IOException("Transient native telemetry unavailable");
            await vm.ExecuteAsync(vm.RefreshAsync);
            Assert(Row(vm, "audio").State == ConnectionState.Failed && ReferenceEquals(last, vm.System),
                "A normal refresh failure retained audio success or discarded its useful cached reading.");
            system.Failure = null;
            await vm.ExecuteAsync(vm.RefreshAsync);
            Assert(Row(vm, "audio").State == ConnectionState.Ready && vm.IsReady, "An ordinary telemetry retry did not recover connection readiness.");
        });
        await run("Native media startup retry reports failure and recovery without needing a track-change event", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            media.Publish(new("Actual paused track", "Actual artist", null, false, TimeSpan.Zero, TimeSpan.FromMinutes(4), "FixturePlayer", true));
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync();
            var last = vm.Media;
            Assert(Row(vm, "media").State == ConnectionState.Connected, "The existing media session was not observed at startup.");
            media.Failure = new IOException("Transient media startup failure");
            await vm.RetryNativeServicesAsync();
            Assert(Row(vm, "media").State == ConnectionState.Failed && ReferenceEquals(last, vm.Media),
                "A native startup retry kept old connected status or destroyed a useful paused-track snapshot.");
            media.Failure = null;
            await vm.RetryNativeServicesAsync();
            Assert(Row(vm, "media").State == ConnectionState.Connected && ReferenceEquals(last, vm.Media),
                "A successful startup retry required a new media event to recover its connection status.");
        });
        await run("Unsupported reporting providers do not damage the unrelated Stripe connection status", async () =>
        {
            var vault = new ConnectionVaultDouble();
            await using var vm = Create(data, new ConnectionMediaDouble(), new ConnectionSystemDouble(), vault);
            await vm.InitializeAsync();
            var stripe = Row(vm, "stripe"); var reads = vault.Reads;
            foreach (var provider in new[] { RevenueProvider.Polar, RevenueProvider.Dodo, RevenueProvider.AdSense })
            {
                await vm.ExecuteAsync(() => vm.RefreshRevenueAsync(provider));
                Assert(Row(vm, "stripe") == stripe && vm.Revenue is null && vault.Reads == reads,
                    $"An unsupported {provider} request changed Stripe's status, read its credential or fabricated revenue.");
            }
        });
        await run("Local source failures replace connected status while preserving the last valid imported data", async () =>
        {
            var calendarPath = Path.Combine(data, "connected.ics"); var codingPath = Path.Combine(data, "connected.jsonl");
            var date = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(calendarPath, "BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\nDTSTART:" + date.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "\nSUMMARY:Actual calendar event\nEND:VEVENT\nEND:VCALENDAR");
            await File.WriteAllTextAsync(codingPath, "{\"type\":\"assistant\",\"timestamp\":\"" + date.ToString("O", CultureInfo.InvariantCulture) + "\",\"sessionId\":\"session\",\"message\":{\"id\":\"message\",\"usage\":{\"input_tokens\":17,\"output_tokens\":9}}}");
            await using var vm = Create(data, new ConnectionMediaDouble(), new ConnectionSystemDouble());
            await vm.InitializeAsync();
            await vm.ImportCalendarAsync(calendarPath); await vm.ImportCodingAsync(codingPath);
            Assert(Row(vm, "calendar").State == ConnectionState.Connected && Row(vm, "coding").State == ConnectionState.Connected, "Valid local files did not verify their connections.");
            var calendar = vm.CalendarEvents; var coding = vm.Coding;
            await File.WriteAllTextAsync(calendarPath, "TITLE:Not a calendar"); await File.WriteAllTextAsync(codingPath, "{unfinished");
            await vm.ExecuteAsync(() => vm.ImportCalendarAsync(calendarPath));
            await vm.ExecuteAsync(() => vm.ImportCodingAsync(codingPath));
            Assert(Row(vm, "calendar").State == ConnectionState.Failed && Row(vm, "coding").State == ConnectionState.Failed,
                "A malformed source retained its old connected status.");
            Assert(ReferenceEquals(calendar, vm.CalendarEvents) && ReferenceEquals(coding, vm.Coding), "A failed refresh destroyed the last valid imported source data.");
            await vm.SetPreferencesAsync(vm.Preferences with { CalendarPath = Path.Combine(data, "different.ics"), CodingPath = Path.Combine(data, "different.jsonl") });
            Assert(Row(vm, "calendar").State == ConnectionState.NotChecked && Row(vm, "coding").State == ConnectionState.NotChecked,
                "Changing an import source retained its old failure or successful verification.");
            Assert(Row(vm, "calendar").CheckedAt is null && Row(vm, "coding").CheckedAt is null, "A new source retained an old check timestamp.");
        });
        await run("Credential locker read failure cannot claim old reporting connectivity", async () =>
        {
            var vault = new ConnectionVaultDouble();
            await using var vm = Create(data, new ConnectionMediaDouble(), new ConnectionSystemDouble(), vault);
            await vm.InitializeAsync();
            vault.ReadFailure = new IOException("private locker detail");
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "stripe").State == ConnectionState.Failed && Row(vm, "analytics").State == ConnectionState.Failed,
                "A failed credential presence read was shown as configured or connected.");
            Assert(vm.ConnectionStatuses.All(row => !row.Detail.Contains("private locker", StringComparison.Ordinal)), "Credential locker details leaked into connection status.");
            vault.ReadFailure = null;
            await vm.RefreshConnectionsAsync();
            Assert(Row(vm, "stripe").State == ConnectionState.NeedsSetup && Row(vm, "analytics").State == ConnectionState.NeedsSetup,
                "An empty locker could not recover its setup state after an earlier read failure.");
        });
        await run("Preview entry invalidates a pending real connection check", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            await using var vm = Create(data, media, system);
            await vm.InitializeAsync(); media.PrepareBlockedStart();
            var check = vm.RefreshConnectionsAsync();
            try
            {
                await media.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
                media.Publish(new("Late actual title", "Actual artist", null, false, TimeSpan.Zero, TimeSpan.FromMinutes(3), "ActualPlayer", true));
                media.Gate!.TrySetResult(); await check;
                Assert(vm.IsDemo && vm.Media?.Title == "Sample track" && vm.ConnectionStatuses.All(row => row.State == ConnectionState.NotChecked && row.CheckedAt is null),
                    "A late native check replaced preview media or claimed live connections.");
            }
            finally { media.Gate!.TrySetResult(); await check; }
        });
        await run("Disposal cancels pending connection checks and rejects late native snapshots", async () =>
        {
            var media = new ConnectionMediaDouble(); var system = new ConnectionSystemDouble();
            var vm = Create(data, media, system);
            await vm.InitializeAsync(); media.PrepareBlockedStart();
            var check = vm.RefreshConnectionsAsync();
            try
            {
                await media.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var notifications = 0;
                vm.PropertyChanged += (_, args) => { if (!vm.IsReady && args.PropertyName != nameof(vm.IsReady)) notifications++; };
                await vm.DisposeAsync(); await check;
                media.Publish(new("Too late", "Actual artist", null, false, TimeSpan.Zero, TimeSpan.FromMinutes(3), "ActualPlayer", true));
                Assert(media.CancellationObserved && media.Disposed && system.Disposed && notifications == 0 && vm.Media is null,
                    "Exit kept a connection request alive or accepted native data after service disposal.");
            }
            finally { media.Gate!.TrySetResult(); await check; await vm.DisposeAsync(); }
        });
    }
}
