using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Windows.Services;
using Notch.Windows.ViewModels;

// The ViewModel tests exercise consent, lifetime and local-data behavior. The
// native service suite separately validates real release metadata and installers.
internal sealed class FakeUpdateService : IWindowsUpdateService
{
    public static ReleaseUpdate Evaluation => new("999.0.0",
        "https://github.com/Sury2797/Notchling/releases/tag/notchling-evaluation-999.0.0",
        "x64", UpdateChannel.Evaluation, false, null);
    public static ReleaseUpdate Signed => new("999.0.0",
        "https://github.com/Sury2797/Notchling/releases/tag/v999.0.0", "x64", UpdateChannel.Stable, true,
        new("999.0.0", "https://github.com/Sury2797/Notchling/releases/download/v999.0.0/Notchling-999.0.0-windows-x64-setup.exe",
            new string('a', 64), 64, 19045));

    public ReleaseUpdate? Result { get; set; } = Evaluation;
    public Func<CancellationToken, Task<ReleaseUpdate?>>? Discovery { get; set; }
    public int Discoveries { get; private set; }
    public int Downloads { get; private set; }
    public int Opens { get; private set; }
    public bool CancellationObserved { get; private set; }
    public Task<bool> CanVerifyPublisherAsync(CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result?.CanInstallVerified == true); }
    public async Task<ReleaseUpdate?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Discoveries++;
        try { return Discovery is { } discovery ? await discovery(cancellationToken) : Result; }
        catch (OperationCanceledException) { CancellationObserved = true; throw; }
    }
    public async Task<AvailableUpdate?> CheckAsync(CancellationToken cancellationToken = default)
        => (await DiscoverAsync(cancellationToken))?.VerifiedUpdate;
    public Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, CancellationToken cancellationToken = default)
        => DownloadAsync(update, null, cancellationToken);
    public Task<PreparedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); Downloads++;
        return Task.FromResult(new PreparedUpdate(update.Version, "synthetic-verified-installer.exe", update.Sha256, update.Architecture));
    }
    public Task OpenInstallerAsync(PreparedUpdate update, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); Opens++; return Task.CompletedTask; }
}

internal sealed class UpdateTestTimeProvider : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan duration) => _timestamp += duration.Ticks;
}

internal static class UpdateNotificationCases
{
    private static void Assert(bool result, string message)
    { if (!result) throw new InvalidOperationException(message); }
    private static DispatcherTimer Tick() => DispatcherTimer.Instances.Single(timer => timer.Interval == TimeSpan.FromSeconds(1));
    private static MainViewModel Create(string data, FakeUpdateService updates, UpdateTestTimeProvider? time = null,
        IDataStore? store = null, ConnectionMediaDouble? media = null)
        => new(new DispatcherQueue(), dataDirectory: data, store: store,
            mediaService: media ?? new ConnectionMediaDouble(), systemService: new ConnectionSystemDouble(),
            vault: new ConnectionVaultDouble(), updateService: updates, timeProvider: time);
    private static async Task<ReleaseUpdate?> AwaitCancellation(CancellationToken token)
    { await Task.Delay(Timeout.InfiniteTimeSpan, token); return null; }

    public static async Task RegisterAsync(Func<string, Func<Task>, Task> run, string data)
    {
        await run("Update checks allow one operation, cancellation and an ordinary retry", async () =>
        {
            var updates = new FakeUpdateService { Discovery = AwaitCancellation };
            await using var vm = Create(data, updates);
            await vm.InitializeAsync();
            var pending = vm.CheckForUpdatesAsync();
            Assert(vm.IsCheckingUpdates && updates.Discoveries == 1 && !pending.IsCompleted,
                "A pending release request did not expose its cancellable busy state.");
            await vm.CheckForUpdatesAsync();
            await vm.InstallAvailableUpdateAsync();
            Assert(updates.Discoveries == 1 && updates.Downloads == 0 && updates.Opens == 0,
                "Repeated clicks started another discovery or an unsolicited installation.");
            vm.CancelUpdateCheck();
            await pending;
            Assert(updates.CancellationObserved && !vm.IsCheckingUpdates && !vm.IsInstallingUpdate
                && vm.UpdateStatus.Contains("cancelled", StringComparison.OrdinalIgnoreCase) && vm.Error == "",
                "Cancelling a read-only check left a busy control or an app error.");
            updates.Discovery = null;
            await vm.CheckForUpdatesAsync();
            Assert(updates.Discoveries == 2 && vm.HasAvailableUpdate && !vm.CanInstallAvailableUpdate
                && vm.AvailableUpdateVersion == "999.0.0" && vm.IsReady,
                "A cancelled evaluation check could not retry or offered automatic installation.");
        });

        await run("Daily update checks require opt-in, skip preview and preserve the current editor", async () =>
        {
            var time = new UpdateTestTimeProvider(); var updates = new FakeUpdateService();
            await using var vm = Create(data, updates, time);
            await vm.InitializeAsync();
            time.Advance(TimeSpan.FromHours(26)); Tick().Fire();
            Assert(!vm.Preferences.CheckForUpdatesAutomatically && updates.Discoveries == 0,
                "A default installation contacted release metadata without opt-in.");
            await vm.SetPreferencesAsync(vm.Preferences with { CheckForUpdatesAutomatically = true });
            vm.SelectModule(ModuleId.Notes); vm.Scratchpad = "Retain this unfinished thought";
            Tick().Fire();
            Assert(updates.Discoveries == 1 && vm.HasAvailableUpdate && vm.SelectedModule == ModuleId.Notes
                && vm.Overlay.Mode == OverlayMode.Expanded && vm.Scratchpad == "Retain this unfinished thought",
                "A background release result took over the user's panel or changed the editor.");
            Assert(vm.NotificationHistory.Count(item => item.Source == "Notchling update") == 1
                && vm.NotificationHistory.Single(item => item.Source == "Notchling update").Destination == ModuleId.Settings,
                "A release was not recorded once with a useful Settings destination.");
            time.Advance(TimeSpan.FromHours(23)); Tick().Fire();
            Assert(updates.Discoveries == 1, "A daily check repeated before its interval elapsed.");
            time.Advance(TimeSpan.FromHours(1)); Tick().Fire();
            Assert(updates.Discoveries == 2 && vm.NotificationHistory.Count(item => item.Source == "Notchling update") == 1,
                "A daily retry failed or repeatedly notified the same available version.");
            await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
            time.Advance(TimeSpan.FromHours(24)); Tick().Fire();
            await vm.CheckForUpdatesAsync();
            Assert(updates.Discoveries == 2 && vm.UpdateStatus.Contains("preview", StringComparison.OrdinalIgnoreCase),
                "Sample-data preview contacted real releases or implied a real check completed.");
            await vm.ExitDemoAsync(); Tick().Fire();
            Assert(updates.Discoveries == 3, "A live session could not resume opted-in checks after preview.");
            updates.Result = FakeUpdateService.Evaluation with { Version = "999.1.0" };
            time.Advance(TimeSpan.FromHours(24)); Tick().Fire();
            Assert(updates.Discoveries == 4 && vm.NotificationHistory.Count(item => item.Source == "Notchling update") == 2,
                "A different release version did not receive its own notification.");
            await vm.SetPreferencesAsync(vm.Preferences with { CheckForUpdatesAutomatically = false });
            time.Advance(TimeSpan.FromHours(25)); Tick().Fire();
            Assert(updates.Discoveries == 4 && updates.Downloads == 0 && updates.Opens == 0,
                "Disabling automatic checks failed or discovery silently executed an installer.");
        });

        await run("Shutdown cancels release discovery without publishing to released views", async () =>
        {
            var updates = new FakeUpdateService { Discovery = AwaitCancellation };
            var vm = Create(data, updates);
            await vm.InitializeAsync();
            var pending = vm.CheckForUpdatesAsync();
            var lateUpdateNotifications = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(vm.UpdateStatus) or nameof(vm.IsCheckingUpdates)
                    or nameof(vm.IsInstallingUpdate) or nameof(vm.HasAvailableUpdate) or nameof(vm.AvailableUpdateVersion)
                    or nameof(vm.CanInstallAvailableUpdate)) lateUpdateNotifications++;
            };
            await vm.DisposeAsync(); await pending;
            Assert(updates.CancellationObserved && lateUpdateNotifications == 0 && vm.NotificationHistory.Count == 0
                && !vm.IsReady && !vm.IsCheckingUpdates && !vm.IsInstallingUpdate,
                "Shutdown retained a release request or published its cancellation to released views.");
            await vm.CheckForUpdatesAsync(); vm.CancelUpdateCheck(); await vm.InstallAvailableUpdateAsync();
            Assert(updates.Discoveries == 1 && updates.Downloads == 0 && updates.Opens == 0,
                "A command contacted the updater after disposal.");
        });

        await run("Verified discovery waits for explicit install and a successful notebook save", async () =>
        {
            var updates = new FakeUpdateService { Result = FakeUpdateService.Signed };
            var store = new FaultingStore(data);
            await using var vm = Create(data, updates, store: store);
            await vm.InitializeAsync();
            vm.AddNote("Unfinished note", "Keep this while updating");
            await vm.CheckForUpdatesAsync();
            Assert(vm.CanInstallAvailableUpdate && updates.Downloads == 0 && updates.Opens == 0
                && vm.Notes.Single().Text == "Keep this while updating",
                "Discovering a verified release began installation or changed local work.");
            store.FailWrites = true;
            try
            {
                await vm.InstallAvailableUpdateAsync();
                Assert(updates.Downloads == 1 && updates.Opens == 0 && vm.HasUnsavedChanges && vm.IsReady
                    && vm.UpdateStatus.Contains("Save your changes", StringComparison.Ordinal)
                    && vm.Notes.Single().Text == "Keep this while updating",
                    "Setup opened after a failed notebook save or the unsaved note was lost.");
            }
            finally { store.FailWrites = false; }
            await vm.InstallAvailableUpdateAsync();
            Assert(updates.Downloads == 2 && updates.Opens == 1 && !vm.HasUnsavedChanges && vm.IsReady
                && vm.UpdateStatus.Contains("Setup opened", StringComparison.Ordinal)
                && !vm.IsCheckingUpdates && !vm.IsInstallingUpdate,
                "An explicit install could not recover after the notebook became durable.");
            using var savedStore = new LocalStore(data);
            var saved = await savedStore.ReadAsync<MainViewModel.LocalData>("workspace");
            Assert(saved?.Notes.Single().Text == "Keep this while updating", "The successful install handoff did not preserve the saved notebook.");
        });

        await run("Release metadata failure stays in Settings and permits a harmless retry", async () =>
        {
            var updates = new FakeUpdateService
            { Discovery = _ => Task.FromException<ReleaseUpdate?>(new HttpRequestException("Synthetic metadata unavailable")) };
            await using var vm = Create(data, updates);
            await vm.InitializeAsync();
            vm.SelectModule(ModuleId.Settings);
            vm.Scratchpad = "Offline work still matters";
            await vm.SaveBeforeExitAsync();
            var before = Directory.GetFiles(data).ToDictionary(path => path, File.ReadAllBytes);
            await vm.CheckForUpdatesAsync();
            Assert(vm.Error == "" && vm.IsReady && !vm.IsCheckingUpdates && vm.SelectedModule == ModuleId.Settings
                && vm.UpdateStatus.Contains("Try again", StringComparison.Ordinal) && !vm.HasAvailableUpdate
                && vm.Scratchpad == "Offline work still matters" && updates.Downloads == 0 && updates.Opens == 0,
                "A metadata outage disrupted the application, claimed an update or changed local work.");
            Assert(Directory.GetFiles(data).Length == before.Count
                && before.All(file => File.ReadAllBytes(file.Key).SequenceEqual(file.Value)),
                "A failed release lookup wrote local notebook or preferences files.");
            updates.Discovery = null; updates.Result = null;
            await vm.CheckForUpdatesAsync();
            Assert(updates.Discoveries == 2 && vm.Error == "" && !vm.HasAvailableUpdate
                && vm.UpdateStatus.Contains("No newer compatible release", StringComparison.Ordinal),
                "A metadata retry could not recover or treated no available release as a failure.");
        });

        await run("Transient footer feedback belongs to its tool while unsaved work remains visible", async () =>
        {
            var time = new UpdateTestTimeProvider(); var store = new FaultingStore(data);
            await using var vm = Create(data, new FakeUpdateService(), time, store);
            await vm.InitializeAsync();
            vm.SelectModule(ModuleId.Focus); vm.DrankWater();
            Assert(vm.ShellStatus == "Hydration reminder reset.", "The tool did not show its immediate action feedback.");
            vm.SelectModule(ModuleId.Settings);
            Assert(vm.ShellStatus == "", "Hydration feedback followed the user into unrelated Settings.");
            vm.SelectModule(ModuleId.Focus);
            Assert(vm.ShellStatus == "Hydration reminder reset.", "Returning to the owning tool lost still-current feedback.");
            var expiryNotifications = 0;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.ShellStatus)) expiryNotifications++; };
            time.Advance(TimeSpan.FromSeconds(8)); Tick().Fire();
            Assert(vm.ShellStatus == "" && expiryNotifications == 1, "Transient feedback did not expire cleanly at its deadline.");
            Tick().Fire(); Tick().Fire();
            Assert(expiryNotifications == 1, "Expired feedback generated a notification on every idle tick.");
            vm.Scratchpad = "Needs a durable save"; store.FailWrites = true;
            try
            {
                Assert(!await vm.SaveBeforeExitAsync(), "The disk failure fixture did not retain unsaved work.");
                time.Advance(TimeSpan.FromHours(1)); Tick().Fire(); vm.SelectModule(ModuleId.Settings);
                Assert(vm.HasUnsavedChanges && vm.ShellStatus.Contains("Save failed", StringComparison.Ordinal),
                    "A transient-feedback timeout or navigation hid the unsaved notebook warning.");
            }
            finally { store.FailWrites = false; }
            Assert(await vm.SaveBeforeExitAsync() && vm.ShellStatus == "", "A successful save left a stale footer warning.");
        });

        await run("Browser provider choice preserves native metadata and expires for a different session or track", async () =>
        {
            var media = new ConnectionMediaDouble();
            var track = new MediaSnapshot("Exact Shorts title", "Actual creator", "native-thumbnail.png", true,
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20), "chrome.exe", true,
                AlbumTitle: "Native album", SourceDisplayName: "Google Chrome", SessionRevision: 41);
            media.Publish(track);
            await using var vm = Create(data, new FakeUpdateService(), media: media);
            await vm.InitializeAsync();
            vm.SelectMediaSource(MediaSourceBrand.YouTube);
            Assert(vm.Media == track && MediaSourceIdentity.Resolve(vm.Media, vm.MediaSourceSelection) is
                { Brand: MediaSourceBrand.YouTube, IsUserSelected: true },
                "Selecting a browser provider changed title/artist/artwork metadata or failed to label the selection.");
            media.Publish(track with { Position = TimeSpan.FromSeconds(8), ArtworkPath = "new-native-thumbnail.png", IsPlaying = false });
            Assert(vm.MediaSourceSelection?.Brand == MediaSourceBrand.YouTube,
                "An ordinary timeline, transport or artwork refresh erased the current track's provider choice.");
            media.Publish(track with { SessionRevision = 42 });
            Assert(vm.MediaSourceSelection is null && MediaSourceIdentity.Resolve(vm.Media).Brand == MediaSourceBrand.Chrome,
                "An identically titled replacement session inherited the previous website identity.");
            vm.SelectMediaSource(MediaSourceBrand.YouTube); media.Publish(track with { SessionRevision = 42, Title = "A different video" });
            Assert(vm.MediaSourceSelection is null, "A different track retained an old manually selected provider.");
            vm.SelectMediaSource(MediaSourceBrand.YouTube); vm.SelectMediaSource(MediaSourceBrand.Automatic);
            Assert(vm.MediaSourceSelection is null, "Returning to Automatic did not restore the Windows-supplied identity.");
            media.Publish(track with { Source = "Spotify.exe", SourceDisplayName = "Spotify", SessionRevision = 43 });
            vm.SelectMediaSource(MediaSourceBrand.YouTube);
            Assert(vm.MediaSourceSelection is null && MediaSourceIdentity.Resolve(vm.Media).Brand == MediaSourceBrand.Spotify,
                "A browser-only provider override replaced a known native player's identity.");
            media.Publish(null);
            Assert(vm.Media is null && vm.MediaSourceSelection is null, "Removing the active player left a source choice behind.");
        });
    }
}
