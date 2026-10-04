using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Windows.Services;
using Notch.Windows.ViewModels;

var data = Path.Combine(Path.GetTempPath(), "Notch.ViewModel.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(data);
var passed = 0;
var failures = new List<string>();
var filters = args;
async Task Case(string name, Func<Task> run)
{
    if (filters.Length > 0 && !filters.Any(filter => name.Contains(filter, StringComparison.OrdinalIgnoreCase))) return;
    if (Directory.Exists(data)) Directory.Delete(data, true);
    Directory.CreateDirectory(data);
    DispatcherTimer.Instances.Clear();
    WindowsMediaService.StartFailure = null;
    WindowsSystemService.ReadFailure = null;
    try { await run().WaitAsync(TimeSpan.FromSeconds(15)); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception error) { failures.Add(name + ": " + error); Console.WriteLine("FAIL " + name + ": " + error.Message); }
}
static void Assert(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
#if DEBUG
static bool Loaded(MainViewModel vm) => (bool)typeof(MainViewModel).GetField("_loaded", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
static DispatcherTimer Tick() => DispatcherTimer.Instances.Single(timer => timer.Interval == TimeSpan.FromSeconds(1));
MainViewModel ViewModel() => new(new DispatcherQueue(), dataDirectory: data);
void SelectWithoutAutomaticRefresh(MainViewModel vm, ModuleId module)
{
    // Hold the existing refresh gate only during the selection event, then drive its real public refresh.
    var gate = (SemaphoreSlim)typeof(MainViewModel).GetField("_refreshLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    Assert(gate.Wait(0), "Fixture attempted navigation while an unrelated refresh was active.");
    try { vm.SelectModule(module); }
    finally { gate.Release(); }
}
Task BeginRefreshOnContext(MainViewModel vm, SynchronizationContext context)
{
    var previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(context);
    try { return vm.RefreshAsync(); }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
}
#else
MainViewModel ViewModel() => new(new DispatcherQueue(), dataDirectory: data);
#endif

#if !DEBUG
await Case("Release starts Free and refuses extended tools and direct commands", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    Assert(!vm.IsDevelopmentBuild && !vm.IsPremium, "Unconfigured Release granted development or Premium access.");
    vm.SelectModule(ModuleId.Notes);
    Assert(vm.SelectedModule == ModuleId.Settings && vm.Error.Contains("Premium"), "Premium navigation did not explain the required plan.");
    vm.AddNote("Blocked", "Must not create a paid note");
    vm.SetAwake(true); vm.StartCountdown(5); vm.ToggleStopwatch();
    await vm.SetVolumeAsync(.8);
    Assert(vm.Notes.Count == 0 && !vm.Awake && !vm.StopwatchRunning && WindowsSystemService.Latest.VolumeRequests.Count == 0,
        "Calling commands directly bypassed the Free plan.");
    vm.Scratchpad = "Free scratchpad";
    await vm.SetPreferencesAsync(vm.Preferences with { HydrationMinutes = 5 });
    Assert(vm.HydrationTime == "Nudges paused", "Changing a Free setting enabled Premium hydration nudges.");
    vm.ToggleFocus();
    Assert(vm.Scratchpad == "Free scratchpad" && vm.FocusRunning, "Free essentials were unavailable.");
    var export = Path.Combine(data, "free-export.json");
    await vm.ExportWorkspaceAsync(export);
    Assert(File.Exists(export), "Free data recovery/export was blocked.");
    await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
    Assert(!vm.IsPremium, "Demo mode granted paid access.");
});
Console.WriteLine($"{passed + failures.Count} Release plan scenarios executed, {passed} passed, {failures.Count} failed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
try { Directory.Delete(data, true); } catch (IOException) { }
return passed > 0 && failures.Count == 0 ? 0 : 1;
#else
await Case("Unchanged entitlement refresh preserves a paused hydration timer", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    Tick().Fire();
    var focus = (FocusSession)typeof(MainViewModel).GetField("_focus", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    focus.Hydration.Pause();
    vm.SignOut();
    await vm.SetPreferencesAsync(vm.Preferences with { HydrationMinutes = 5 });
    Assert(!focus.Hydration.IsRunning, "Unchanged entitlement or interval setting restarted a paused timer.");
});
await Case("Direct unsaved collection edits require corrupt notebook recovery before exit", async () =>
{
    var path = Path.Combine(data, "workspace.json");
    const string original = "{unreadable original}";
    await File.WriteAllTextAsync(path, original);
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    vm.Notes.Add(new SavedNote(Guid.NewGuid(), "Retained draft", "Recover this edit", DateTimeOffset.UtcNow));
    Assert(vm.HasUnsavedChanges && !await vm.SaveBeforeExitAsync(), "A direct collection edit was lost during corrupt-file exit.");
    Assert(await File.ReadAllTextAsync(path) == original, "An unsaved edit replaced the unreadable original.");
    await vm.RecoverWorkspaceAsync();
    Assert(await vm.SaveBeforeExitAsync(), "Recovery could not save the retained edit.");
    Assert(Directory.GetFiles(data, "workspace-unreadable-*.json").Length == 1, "Recovery did not preserve the unreadable source.");
});
await Case("A failed final write preserves live edits and allows a successful retry", async () =>
{
    var store = new FaultingStore(data);
    var vm = new MainViewModel(new DispatcherQueue(), dataDirectory: data, store: store);
    await vm.InitializeAsync();
    vm.Scratchpad = "Last accepted edit";
    store.FailWrites = true;
    Assert(!await vm.SaveBeforeExitAsync(), "Final disk failure was reported as a successful save.");
    Assert(vm.HasUnsavedChanges && vm.Scratchpad == "Last accepted edit" && vm.SaveState.Contains("failed"), "Unsaved edits disappeared after final save failure.");
    var cancelledDisposal = false;
    try { await vm.DisposeAsync(); } catch (IOException) { cancelledDisposal = true; }
    Assert(cancelledDisposal && vm.IsReady && !WindowsSystemService.Latest.Disposed, "Failed exit released the live application and its edits.");
    store.FailWrites = false;
    Assert(await vm.SaveBeforeExitAsync() && !vm.HasUnsavedChanges, "Retry could not persist retained edits.");
    await vm.DisposeAsync();
    var local = JsonSerializer.Deserialize<MainViewModel.LocalData>(await File.ReadAllTextAsync(Path.Combine(data, "workspace.json")));
    Assert(local?.Scratchpad == "Last accepted edit", "Retry saved incorrect content.");
});
await Case("Over-limit edits are rejected before mutation without silently truncating content", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    vm.AddNote("Original", "Keep original");
    var original = vm.Notes.Single();
    var oversized = new string('x', 500001);
    var rejected = false;
    try { vm.UpdateNote(original.Id, "Changed", oversized); } catch (ArgumentException) { rejected = true; }
    Assert(rejected && vm.Notes.Single() == original, "Oversized note replaced or truncated the original.");
    vm.Scratchpad = "Keep scratchpad";
    rejected = false;
    try { vm.Scratchpad = oversized; } catch (ArgumentException) { rejected = true; }
    Assert(rejected && vm.Scratchpad == "Keep scratchpad", "Oversized scratchpad replaced or truncated accepted text.");
});
await Case("Simultaneous reminders queue and enter history only when presented", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    for (var index = 0; index < 4; index++) vm.AddReminder($"Reminder {index}", DateTimeOffset.Now.AddMinutes(-1));
    Tick().Fire();
    Assert(vm.NotificationHistory.Count == 1 && vm.Overlay.Activity is not null, "All due reminders were marked presented immediately.");
    var presented = new HashSet<Guid>();
    for (var index = 0; index < 4; index++)
    {
        Assert(vm.Overlay.Activity is { }, "A simultaneous reminder was lost.");
        presented.Add(vm.Overlay.Activity!.Id);
        vm.Overlay.DismissActivity();
    }
    Assert(presented.Count == 4 && vm.NotificationHistory.Count == 4, "Every due reminder was not presented exactly once.");
    Tick().Fire();
    Assert(vm.Overlay.Activity is null, "Acknowledged reminders replayed without reschedule.");
});

await Case("Corrupt workspace bytes survive initialization, edits and disposal", async () =>
{
    var path = Path.Combine(data, "workspace.json");
    const string original = "{broken notebook: do not replace}";
    await File.WriteAllTextAsync(path, original);
    await using (var vm = ViewModel())
    {
        await vm.InitializeAsync();
        Assert(!vm.WorkspaceReadable && Loaded(vm) && Tick().IsEnabled, "Core initialization must finish with notebook recovery enabled.");
        Assert(vm.Error.Contains("preserved") && vm.Error.Contains(path), "Recovery error must explain preservation and the actual path.");
        var noteRejected = false;
        try { vm.AddNote("Temporary", "Must not overwrite saved corrupt notebook"); } catch (InvalidOperationException) { noteRejected = true; }
        Assert(noteRejected && vm.Notes.Count == 0, "Unreadable notebook silently accepted a note it could not save.");
        var editRejected = false;
        try { vm.Scratchpad = "Temporary scratchpad"; } catch (InvalidOperationException) { editRejected = true; }
        Assert(editRejected && vm.Scratchpad == "", "Unreadable notebook accepted a draft it could not save.");
        await Task.Delay(450);
        Assert(await File.ReadAllTextAsync(path) == original, "Debounced edits overwrote the corrupt file.");
    }
    Assert(await File.ReadAllTextAsync(path) == original, "Disposal overwrote the corrupt notebook.");
});
await Case("Corrupt preferences fall back without disabling timers or local tools", async () =>
{
    await File.WriteAllTextAsync(Path.Combine(data, "preferences.json"), "{unfinished");
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    Assert(Loaded(vm) && Tick().IsEnabled, "Preferences corruption prevented a usable initialized app.");
    Assert(vm.Preferences.FocusMinutes == 25 && vm.System is not null, "Preference defaults or system refresh did not recover.");
    vm.StartCountdown(3);
    Assert(vm.CountdownTime is "03:00" or "02:59", "Local timer unavailable after preference recovery.");
});
await Case("Optional media, system, calendar and coding failures preserve initialization", async () =>
{
    var preferences = new AppPreferences { CalendarPath = Path.Combine(data, "missing.ics"), CodingPath = Path.Combine(data, "missing.jsonl") };
    await File.WriteAllTextAsync(Path.Combine(data, "preferences.json"), JsonSerializer.Serialize(preferences));
    WindowsMediaService.StartFailure = new InvalidOperationException("Injected unsupported media system");
    WindowsSystemService.ReadFailure = new IOException("Injected unavailable system reading");
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    Assert(Loaded(vm) && Tick().IsEnabled, "An optional integration failure prevented startup.");
    Assert(WindowsMediaService.Latest.Starts == 1 && WindowsSystemService.Latest.Reads == 1, "Startup skipped supported subsequent attempts.");
    Assert(vm.Error.Contains("missing.jsonl"), "Coding attempt should run even after calendar and media failure.");
    vm.AddNote("Works", "Local productivity survives integration failures");
    Assert(vm.Notes.Count == 1, "Local notes unavailable after optional failures.");
});
await Case("Clipboard collection changes also publish viewmodel property notifications", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    var notified = 0;
    vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.Clipboard)) notified++; };
    var item = new ClipboardItem(Guid.NewGuid(), "Copied fixture", DateTimeOffset.UtcNow);
    vm.ClipboardService.Publish([item]);
    Assert(vm.Clipboard.Count == 1 && vm.Clipboard[0] == item && notified == 1, "Clipboard change did not update and notify.");
    vm.ClipboardService.Clear();
    Assert(vm.Clipboard.Count == 0 && notified == 2, "Clipboard clear did not update and notify.");
});
await Case("Overdue reminders deliver once after delayed ticks and replay when rescheduled or reopened", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    vm.AddReminder("Overdue by two hours", DateTimeOffset.Now.AddHours(-2));
    var reminder = vm.Reminders[0];
    Tick().Fire();
    Assert(vm.Overlay.Activity?.Title == reminder.Title, "A reminder missed during a long suspension was dropped.");
    vm.Overlay.DismissActivity();
    Tick().Fire();
    Assert(vm.Overlay.Activity is null, "Same due reminder repeated on every tick.");
    vm.Reminders[0] = reminder with { DueAt = DateTimeOffset.Now.AddHours(1) };
    Tick().Fire();
    Assert(vm.Overlay.Activity is null, "Rescheduled future reminder delivered early.");
    vm.Reminders[0] = reminder with { DueAt = DateTimeOffset.Now.AddMinutes(-10) };
    Tick().Fire();
    Assert(vm.Overlay.Activity?.Title == reminder.Title, "Rescheduled due reminder was not delivered again.");
    vm.Overlay.DismissActivity();
    vm.CompleteReminder(reminder.Id);
    Tick().Fire();
    Assert(vm.Overlay.Activity is null, "Completed reminder was delivered.");
    vm.CompleteReminder(reminder.Id);
    Tick().Fire();
    Assert(vm.Overlay.Activity?.Title == reminder.Title, "Reopened due reminder was not delivered again.");
});
await Case("Entering demo releases real power request and demo controls never issue native requests", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    vm.SetAwake(true);
    Assert(system.AwakeRequests.SequenceEqual(new[] { true }), "Real keep-awake did not issue its power request.");
    await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
    Assert(system.AwakeRequests.SequenceEqual(new[] { true, false }) && !vm.Awake, "Entering demo failed to release native keep-awake.");
    vm.SetAwake(true);
    vm.SetAwake(false);
    Assert(system.AwakeRequests.Count == 2 && !vm.Awake, "Demo awake controls issued native power requests.");
});
await Case("Late real refresh cannot replace demo system or media samples", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    var gate = new TaskCompletionSource<SystemSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    system.ReadGate = gate;
    var refresh = vm.RefreshAsync();
    Assert(!refresh.IsCompleted, "Fixture did not hold an in-flight refresh.");
    await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
    var sampleSystem = vm.System;
    var sampleMedia = vm.Media;
    gate.SetResult(WindowsSystemService.RealSnapshot);
    await refresh;
    Assert(ReferenceEquals(sampleSystem, vm.System) && ReferenceEquals(sampleMedia, vm.Media), "Stale refresh replaced demo samples.");
    Assert(vm.System?.OutputDevice == "Sample output" && vm.ListeningPorts.SequenceEqual(new[] { 3000, 5173, 8080 }), "Stale native values leaked into demo mode.");
});
await Case("Disposal waits for active refresh before disposing adapters", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    var media = WindowsMediaService.Latest;
    var gate = new TaskCompletionSource<SystemSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    system.ReadGate = gate;
    var refresh = vm.RefreshAsync();
    var disposal = vm.DisposeAsync().AsTask();
    Assert(!disposal.IsCompleted && !system.Disposed && !media.Disposed, "Services were disposed while their refresh was active.");
    gate.SetResult(WindowsSystemService.RealSnapshot);
    await refresh;
    await disposal;
    Assert(!Tick().IsEnabled, "Successful disposal left the UI timer running.");
    Assert(system.Disposed && media.Disposed && vm.ClipboardService.Disposed, "Disposal failed to release adapters after refresh completed.");
    var reads = system.Reads;
    await vm.RefreshAsync();
    Assert(system.Reads == reads, "Refresh reached an adapter after disposal.");
});
async Task PreservedWorkspace(string name, string content)
{
    await Case(name, async () =>
    {
        var path = Path.Combine(data, "workspace.json");
        await File.WriteAllTextAsync(path, content);
        var original = await File.ReadAllBytesAsync(path);
        await using (var vm = ViewModel())
        {
            await vm.InitializeAsync();
            Assert(!vm.WorkspaceReadable && Loaded(vm) && Tick().IsEnabled, "Invalid workspace did not enter recovery while completing startup.");
            Assert(vm.Notes.Count == 0 && vm.Scratchpad == "", "Invalid workspace was partially published.");
            var noteRejected = false;
            try { vm.AddNote("Temporary", "Recovery must preserve the original file"); } catch (InvalidOperationException) { noteRejected = true; }
            Assert(noteRejected && vm.Notes.Count == 0, "Invalid notebook silently accepted an unsavable note.");
            var editRejected = false;
            try { vm.Scratchpad = "Temporary edit"; } catch (InvalidOperationException) { editRejected = true; }
            Assert(editRejected && vm.Scratchpad == "", "Invalid notebook accepted an unsafe autosave draft.");
        }
        var retained = await File.ReadAllBytesAsync(path);
        Assert(original.SequenceEqual(retained), "Initialization or disposal replaced original invalid workspace bytes.");
    });
}
string WorkspaceJson(SavedNote[] notes) => JsonSerializer.Serialize(new MainViewModel.LocalData(notes, [], [], [], ""));
await PreservedWorkspace("JSON-null workspace root is preserved instead of replaced", "null");
await PreservedWorkspace("Workspace with over 100 notes is preserved without truncation", WorkspaceJson(Enumerable.Range(0, 101)
    .Select(index => new SavedNote(Guid.NewGuid(), $"Note {index}", "Short note", DateTimeOffset.UtcNow)).ToArray()));
await PreservedWorkspace("Workspace with over-limit note text is preserved without truncation", WorkspaceJson([
    new SavedNote(Guid.NewGuid(), "Long note", new string('N', 500_001), DateTimeOffset.UtcNow)]));
var duplicateId = Guid.NewGuid();
await PreservedWorkspace("Workspace with duplicate note IDs is preserved instead of loaded ambiguously", WorkspaceJson([
    new SavedNote(duplicateId, "First", "One", DateTimeOffset.UtcNow), new SavedNote(duplicateId, "Second", "Two", DateTimeOffset.UtcNow)]));
await PreservedWorkspace("Workspace with missing item IDs is preserved instead of repaired destructively", JsonSerializer.Serialize(new
{
    Notes = new[] { new { Title = "Missing identifier", Text = "Keep original", UpdatedAt = DateTimeOffset.UtcNow } },
    Reminders = Array.Empty<ReminderItem>(), Shelf = Array.Empty<ShelfItem>(), Links = Array.Empty<SavedLink>(), Scratchpad = ""
}));
await Case("Null workspace collections and strings normalize while valid identifiers load", async () =>
{
    var now = DateTimeOffset.UtcNow;
    var noteId = Guid.NewGuid();
    var reminderId = Guid.NewGuid();
    var shelfId = Guid.NewGuid();
    var linkId = Guid.NewGuid();
    var workspace = new MainViewModel.LocalData([new(noteId, null!, null!, now)], [new(reminderId, null!, now.AddHours(1), false)],
        [new(shelfId, null!, now)], [new(linkId, null!, null!)], null!);
    await File.WriteAllTextAsync(Path.Combine(data, "workspace.json"), JsonSerializer.Serialize(workspace));
    await using (var vm = ViewModel())
    {
        await vm.InitializeAsync();
        Assert(vm.WorkspaceReadable && Loaded(vm) && Tick().IsEnabled, "Valid IDs with null strings failed startup.");
        Assert(vm.Notes.Single().Id == noteId && vm.Notes[0].Title == "Untitled note" && vm.Notes[0].Text == "", "Null note strings did not normalize.");
        Assert(vm.Reminders.Single().Id == reminderId && vm.Reminders[0].Title == "Reminder", "Null reminder title did not normalize.");
        Assert(vm.Shelf.Single().Path == "" && vm.Links.Single().Title == "Saved link" && vm.Links[0].Url == "" && vm.Scratchpad == "", "Other null strings did not normalize.");
    }
    await File.WriteAllTextAsync(Path.Combine(data, "workspace.json"), "{\"Notes\":null,\"Reminders\":null,\"Shelf\":null,\"Links\":null,\"Scratchpad\":null}");
    await using var empty = ViewModel();
    await empty.InitializeAsync();
    Assert(empty.WorkspaceReadable && empty.Notes.Count == 0 && empty.Reminders.Count == 0 && empty.Shelf.Count == 0 && empty.Links.Count == 0 && empty.Scratchpad == "", "Null collections did not normalize to empty collections.");
});
await Case("Invalid analytics URLs reject before mutating preferences or persisted bytes", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    await vm.SetPreferencesAsync(vm.Preferences with { AnalyticsEndpoint = "https://example.invalid/metrics?site=fixture", AnalyticsSite = "Valid fixture" });
    var original = vm.Preferences;
    var path = Path.Combine(data, "preferences.json");
    var originalBytes = await File.ReadAllBytesAsync(path);
    foreach (var endpoint in new[] { "http://example.invalid/metrics", "https://fixture:secret@example.invalid/metrics", "https://example.invalid/metrics#fragment" })
    {
        var rejected = false;
        try { await vm.SetPreferencesAsync(original with { AnalyticsEndpoint = endpoint, DemoMode = true }); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected && vm.Preferences == original && !vm.IsDemo, "Invalid endpoint changed preferences before rejection.");
        var retainedBytes = await File.ReadAllBytesAsync(path);
        Assert(originalBytes.SequenceEqual(retainedBytes), "Invalid endpoint changed persisted preference bytes.");
    }
});
await Case("Public mutations and native calls become inert after disposal", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    vm.AddNote("Existing", "Keep this note");
    vm.AddReminder("Existing reminder", DateTimeOffset.Now.AddHours(1));
    vm.AddLink("Existing link", "https://example.invalid/");
    var shelfFile = Path.Combine(data, "shelf.txt");
    await File.WriteAllTextAsync(shelfFile, "fixture");
    vm.AddShelf(shelfFile);
    vm.Scratchpad = "Keep scratchpad";
    var note = vm.Notes.Single();
    var reminder = vm.Reminders.Single();
    var link = vm.Links.Single();
    var shelf = vm.Shelf.Single();
    var preferences = vm.Preferences;
    var system = WindowsSystemService.Latest;
    await vm.DisposeAsync();
    var notifications = 0;
    vm.PropertyChanged += (_, _) => notifications++;
    vm.AddNote("", ""); vm.UpdateNote(note.Id, "Changed", "Changed"); vm.RemoveNote(note.Id);
    vm.AddReminder("", DateTimeOffset.Now); vm.CompleteReminder(reminder.Id); vm.RemoveReminder(reminder.Id);
    vm.AddLink("", "invalid URL"); vm.RemoveLink(link.Id);
    vm.AddShelf("/missing/path"); vm.RemoveShelf(shelf.Id);
    vm.Scratchpad = "Changed"; vm.SetAwake(true); vm.ShowError("Changed"); vm.SelectModule(ModuleId.Notes);
    vm.ToggleFocus(); vm.ResetFocus(); vm.StartCountdown(1); vm.ToggleStopwatch(); vm.ResetStopwatch(); vm.LapStopwatch(); vm.DrankWater();
    vm.SaveCredential("invalid", ""); vm.DeleteCredential("invalid");
    await vm.SetPreferencesAsync(preferences with { DemoMode = true }); await vm.SetVolumeAsync(.9);
    await vm.PlayPauseAsync(); await vm.PreviousAsync(); await vm.NextAsync(); await vm.SeekMediaAsync(.5);
    Assert(vm.Notes.Single() == note && vm.Reminders.Single() == reminder && vm.Links.Single() == link && vm.Shelf.Single() == shelf && vm.Scratchpad == "Keep scratchpad", "Disposed mutator changed local data.");
    Assert(vm.Preferences == preferences && notifications == 0 && !vm.FocusRunning && !vm.StopwatchRunning, "Disposed mutator published state or timer changes.");
    Assert(system.AwakeRequests.Count == 0 && system.VolumeRequests.Count == 0 && !vm.HasCredential("invalid"), "Disposed operation reached a native adapter.");
});
await Case("Disposal waits for an outstanding native volume write", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    system.VolumeGate = gate;
    var volume = vm.SetVolumeAsync(.75);
    await system.VolumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var disposal = vm.DisposeAsync().AsTask();
    Assert(!disposal.IsCompleted && !system.Disposed && !volume.IsCompleted, "Disposal released the native adapter before its volume write finished.");
    gate.SetResult();
    await volume;
    await disposal;
    Assert(system.Disposed && system.VolumeRequests.SequenceEqual(new[] { .75 }), "Volume completion did not precede native adapter disposal.");
});
await Case("Disposal cancels a debounced volume change before it reaches native audio", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    var volume = vm.SetVolumeAsync(.6);
    await vm.DisposeAsync();
    await volume;
    Assert(system.VolumeRequests.Count == 0, "Canceled debounced volume reached the disposed native service.");
});
await Case("Accepted preferences survive disposal while their first write is awaiting the store lock", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    var store = (LocalStore)typeof(MainViewModel).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
    var writeLock = (SemaphoreSlim)typeof(LocalStore).GetField("_accessLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
    await writeLock.WaitAsync();
    var accepted = vm.Preferences with { WeatherCity = "Accepted before shutdown", FocusMinutes = 42 };
    var preferencesWrite = vm.SetPreferencesAsync(accepted);
    Assert(!preferencesWrite.IsCompleted && vm.Preferences == accepted, "Fixture did not hold an accepted pending preference write.");
    var disposal = vm.DisposeAsync().AsTask();
    Assert(!disposal.IsCompleted, "Disposal did not wait for the store lock.");
    writeLock.Release();
    try { await preferencesWrite; } catch (OperationCanceledException) { }
    await disposal;
    var persisted = JsonSerializer.Deserialize<AppPreferences>(await File.ReadAllTextAsync(Path.Combine(data, "preferences.json")));
    Assert(persisted == accepted, "Canceled preference persistence lost the last accepted settings during shutdown.");
});
await Case("Turning demo off survives disposal during the awaited slow native refresh", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
    var system = WindowsSystemService.Latest;
    var gate = new TaskCompletionSource<SystemSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
    system.ReadGate = gate;
    var accepted = vm.Preferences with { DemoMode = false, WeatherCity = "Persist during refresh" };
    var apply = vm.SetPreferencesAsync(accepted);
    Assert(!apply.IsCompleted && vm.Preferences == accepted, "Fixture did not hold Demo-off before its persistence stage.");
    var disposal = vm.DisposeAsync().AsTask();
    Assert(!disposal.IsCompleted && !system.Disposed, "Disposal did not wait for the in-flight preference refresh.");
    gate.SetResult(WindowsSystemService.RealSnapshot);
    await apply;
    await disposal;
    var persisted = JsonSerializer.Deserialize<AppPreferences>(await File.ReadAllTextAsync(Path.Combine(data, "preferences.json")));
    Assert(persisted == accepted && !persisted!.DemoMode, "Demo-off acceptance was lost when shutdown bypassed the normal write stage.");
});
Task BeginPausedInitialization(MainViewModel vm, PausedContext context)
{
    var previous = SynchronizationContext.Current;
    SynchronizationContext.SetSynchronizationContext(context);
    try { return vm.InitializeAsync(); }
    finally { SynchronizationContext.SetSynchronizationContext(previous); }
}
await Case("Commands during delayed workspace loading cannot lose edits or exceed the saved note limit", async () =>
{
    var notes = Enumerable.Range(0, 100).Select(index => new SavedNote(Guid.NewGuid(), $"Saved {index}", new string('S', 30_000), DateTimeOffset.UtcNow)).ToArray();
    var workspace = new MainViewModel.LocalData(notes, [], [], [], "Saved scratchpad");
    await File.WriteAllTextAsync(Path.Combine(data, "workspace.json"), JsonSerializer.Serialize(workspace));
    await using var vm = ViewModel();
    var context = new PausedContext();
    var initialize = BeginPausedInitialization(vm, context);
    await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!initialize.IsCompleted && !vm.IsReady && vm.Notes.Count == 0, "Fixture did not pause the actual workspace read before publication.");
    vm.AddNote("Early new note", "Would otherwise push the saved notebook past 100");
    vm.Scratchpad = "Early scratchpad edit";
    vm.ToggleFocus(); vm.StartCountdown(1); vm.ToggleStopwatch(); vm.SetAwake(true);
    Assert(vm.Notes.Count == 0 && vm.Scratchpad == "" && !vm.FocusRunning && !vm.StopwatchRunning && WindowsSystemService.Latest.AwakeRequests.Count == 0, "An unready mutation was accepted and could be overwritten by loading.");
    await context.PumpUntilAsync(initialize);
    Assert(vm.IsReady && vm.Notes.Count == 100 && vm.Notes[0] == notes[0] && vm.Scratchpad == "Saved scratchpad", "Saved workspace lost data or exceeded its item limit after early commands.");
});
await Case("Native Awake is gated while delayed saved-demo preferences are loading", async () =>
{
    var preferences = JsonSerializer.Serialize(new { DemoMode = true, Padding = new string('P', 4_000_000) });
    await File.WriteAllTextAsync(Path.Combine(data, "preferences.json"), preferences);
    await using var vm = ViewModel();
    var context = new PausedContext();
    var initialize = BeginPausedInitialization(vm, context);
    await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!initialize.IsCompleted && !vm.IsReady && !vm.IsDemo, "Fixture did not pause saved preference publication.");
    vm.SetAwake(true); vm.ToggleFocus(); vm.ToggleStopwatch();
    Assert(WindowsSystemService.Latest.AwakeRequests.Count == 0 && !vm.Awake, "An early native Awake request escaped before saved demo mode loaded.");
    await context.PumpUntilAsync(initialize);
    Assert(vm.IsReady && vm.IsDemo && !vm.Awake && WindowsSystemService.Latest.AwakeRequests.Count == 0, "Saved demo initialization retained an early real power request.");
});
await Case("Accepted early preferences fence delayed saved preferences from replacing them", async () =>
{
    await File.WriteAllTextAsync(Path.Combine(data, "preferences.json"), JsonSerializer.Serialize(new { DemoMode = true, WeatherCity = "Old saved city", Padding = new string('P', 4_000_000) }));
    await using var vm = ViewModel();
    var context = new PausedContext();
    var initialize = BeginPausedInitialization(vm, context);
    await context.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!initialize.IsCompleted && !vm.IsReady, "Fixture did not hold saved preference publication.");
    var accepted = vm.Preferences with { DemoMode = false, WeatherCity = "Accepted early city", FocusMinutes = 41 };
    await vm.SetPreferencesAsync(accepted);
    await context.PumpUntilAsync(initialize);
    Assert(vm.IsReady && vm.Preferences == accepted && !vm.IsDemo, "A delayed startup preference read overwrote accepted settings.");
    var persisted = JsonSerializer.Deserialize<AppPreferences>(await File.ReadAllTextAsync(Path.Combine(data, "preferences.json")));
    Assert(persisted == accepted, "Accepted early preferences failed to persist.");
});
await Case("TCP enumeration is limited to visible Home and Servers and unchanged ports do not notify", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    var system = WindowsSystemService.Latest;
    Assert(system.PortReads == 0, "Collapsed initialization scanned TCP listeners.");
    foreach (var module in new[] { ModuleId.Media, ModuleId.System, ModuleId.ScreenTime, ModuleId.Focus })
    {
        SelectWithoutAutomaticRefresh(vm, module);
        await vm.RefreshAsync();
    }
    Assert(system.PortReads == 0, "Unrelated module triggered TCP listener enumeration.");
    var notifications = 0;
    vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.ListeningPorts)) notifications++; };
    SelectWithoutAutomaticRefresh(vm, ModuleId.Home);
    await vm.RefreshAsync();
    await vm.RefreshAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Servers);
    await vm.RefreshAsync();
    Assert(system.PortReads == 3 && notifications == 1, "Visible port views were not scanned or unchanged arrays caused redundant notifications.");
    system.Ports = [3000, 49152];
    await vm.RefreshAsync();
    Assert(notifications == 2 && vm.ListeningPorts.SequenceEqual(system.Ports), "Changed TCP listeners did not publish.");
    vm.Overlay.Collapse();
    await vm.RefreshAsync();
    Assert(system.PortReads == 4, "Collapsed panel still scanned listeners.");
});
await Case("Blocking TCP enumeration runs off the UI context and publishes back through it", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Home);
    var system = WindowsSystemService.Latest;
    system.PortsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var context = new PausedContext();
    var publishedOnContext = false;
    vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.ListeningPorts)) publishedOnContext = SynchronizationContext.Current == context; };
    var refresh = BeginRefreshOnContext(vm, context);
    try
    {
        await system.PortsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert(!refresh.IsCompleted && system.PortsContext is null, "Listener scan blocked or executed on the UI synchronization context.");
    }
    finally { system.PortsGate.SetResult(); }
    await context.PumpUntilAsync(refresh);
    Assert(publishedOnContext && vm.ListeningPorts.SequenceEqual(system.Ports), "Background scan did not publish on its captured UI continuation.");
});
await Case("Navigation during telemetry reading skips obsolete listener work", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Home);
    var system = WindowsSystemService.Latest;
    system.ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var refresh = vm.RefreshAsync();
    vm.SelectModule(ModuleId.Media);
    system.ReadGate.SetResult(WindowsSystemService.RealSnapshot);
    await refresh;
    Assert(system.PortReads == 0 && vm.ListeningPorts.Count == 0, "Obsolete Home request scanned or published ports after navigation.");
});
await Case("Home scan cannot publish after navigating away and returning to Home", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Home);
    var system = WindowsSystemService.Latest;
    system.PortsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var refresh = vm.RefreshAsync();
    await system.PortsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    vm.SelectModule(ModuleId.Media);
    vm.SelectModule(ModuleId.Home);
    system.PortsGate.SetResult();
    await refresh;
    Assert(vm.ListeningPorts.Count == 0, "An earlier visit's scan published into the new Home visit.");
    system.PortsGate = null;
    await vm.RefreshAsync();
    Assert(vm.ListeningPorts.SequenceEqual(system.Ports), "Current Home refresh failed to publish after the obsolete scan drained.");
});
await Case("Late listener failure is suppressed after its port view closes", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Servers);
    var system = WindowsSystemService.Latest;
    system.PortsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    system.PortsFailure = new IOException("Late old-view scan failure");
    var refresh = vm.RefreshAsync();
    await system.PortsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    vm.Overlay.Collapse();
    system.PortsGate.SetResult();
    await refresh;
    Assert(vm.ListeningPorts.Count == 0 && vm.Error == "", "Obsolete listener failure leaked into the current view.");
});
await Case("Entering demo prevents in-flight TCP scan from replacing sample listeners", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Home);
    var system = WindowsSystemService.Latest;
    system.PortsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var refresh = vm.RefreshAsync();
    await system.PortsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
    system.PortsGate.SetResult();
    await refresh;
    Assert(vm.ListeningPorts.SequenceEqual(new[] { 3000, 5173, 8080 }), "Old native scan replaced demo listeners.");
});
await Case("Disposal waits for synchronous native TCP scan and prevents reads after release", async () =>
{
    var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Servers);
    var system = WindowsSystemService.Latest;
    system.PortsGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var refresh = vm.RefreshAsync();
    await system.PortsStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var disposal = vm.DisposeAsync().AsTask();
    Assert(!disposal.IsCompleted && !system.Disposed, "Shutdown disposed the adapter while its TCP scan was still running.");
    system.PortsGate.SetResult();
    await refresh;
    await disposal;
    Assert(system.Disposed, "Shutdown released resources incorrectly after its active TCP scan.");
    var scans = system.PortReads;
    var reads = system.Reads;
    var lastPorts = vm.ListeningPorts.ToArray();
    await vm.RefreshAsync();
    Assert(system.PortReads == scans && system.Reads == reads && vm.ListeningPorts.SequenceEqual(lastPorts),
        "A post-disposal refresh read a released adapter or published changed state.");
});
await Case("Stopwatch fast ticks notify time only while real lap changes still notify", async () =>
{
    await using var vm = ViewModel();
    await vm.InitializeAsync();
    SelectWithoutAutomaticRefresh(vm, ModuleId.Focus);
    vm.ToggleStopwatch();
    var fastTick = DispatcherTimer.Instances.Single(timer => timer.Interval == TimeSpan.FromMilliseconds(100));
    Assert(fastTick.IsEnabled, "Visible running stopwatch did not start its fast timer.");
    var times = 0;
    var laps = 0;
    vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(vm.StopwatchTime)) times++; if (args.PropertyName == nameof(vm.StopwatchLaps)) laps++; };
    for (var index = 0; index < 10; index++) fastTick.Fire();
    Assert(times == 10 && laps == 0, "Fast stopwatch ticks emitted unchanged lap notifications or missed time updates.");
    vm.LapStopwatch();
    Assert(laps == 1 && vm.StopwatchLaps.Count == 1, "Actual lap did not notify.");
    vm.ToggleStopwatch();
    vm.LapStopwatch();
    Assert(laps == 1 && !fastTick.IsEnabled, "Paused stopwatch emitted a fake lap or retained fast ticks.");
    vm.ResetStopwatch();
    Assert(laps == 2 && vm.StopwatchLaps.Count == 0, "Reset failed to notify cleared laps.");
    vm.ResetStopwatch();
    Assert(laps == 2, "Reset with unchanged empty laps issued a redundant lap notification.");
    vm.ToggleStopwatch();
    vm.SelectModule(ModuleId.Media);
    Assert(!fastTick.IsEnabled, "Navigating away kept the 10 Hz stopwatch UI timer running.");
});
Console.WriteLine($"{passed + failures.Count} viewmodel behavioral scenarios executed, {passed} passed, {failures.Count} failed.");
foreach (var failure in failures) Console.Error.WriteLine(failure);
try { Directory.Delete(data, true); } catch (IOException) { }
return passed > 0 && failures.Count == 0 ? 0 : 1;
#endif
