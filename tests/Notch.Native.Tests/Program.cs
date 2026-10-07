using Microsoft.UI.Dispatching;
using Notch.Windows.Services;
using Notch.Windows.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.Media.Control;
using Windows.Storage;
using System.Runtime.InteropServices;

// Production C# implementations, platform APIs doubled; see README for verification limits.
using var dispatcher = new DispatcherQueue();
var soundCache = Path.Combine(Path.GetTempPath(), "Notch.Native.Tests", Guid.NewGuid().ToString("N"));
var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
async Task WaitFor(Func<Task<bool>> predicate)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!await predicate()) await Task.Delay(10, timeout.Token);
}
async Task<ClipboardService> ClipboardAsync() => await dispatcher.InvokeAsync(() =>
{
    var service = new ClipboardService(dispatcher); service.SetEnabled(true); return service;
});
async Task FinishAsync(ClipboardService service) => await dispatcher.InvokeAsync(service.Dispose);
Task<string> Text(string value) => Task.FromResult(value);

{
    var service = await ClipboardAsync();
    var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    await dispatcher.InvokeAsync(() => { Clipboard.Publish(new(slow.Task)); Clipboard.Publish(new(Text("B"))); Clipboard.Publish(new(Text("C"))); });
    slow.SetResult("A");
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 3));
    Check(await dispatcher.InvokeAsync(() => service.Items.Select(item => item.Text).SequenceEqual(new[] { "C", "B", "A" })), "Clipboard captures lost or reordered delayed entries.");
    await FinishAsync(service); passed++;
}
{
    var service = await dispatcher.InvokeAsync(() => new ClipboardService(dispatcher));
    Clipboard.RegistrationFailure = new COMException("Clipboard service is temporarily unavailable.");
    var failed = false;
    try { await dispatcher.InvokeAsync(() => service.SetEnabled(true)); }
    catch (COMException) { failed = true; }
    Check(failed && !service.Enabled, "Clipboard registration failure left capture enabled and blocked retries.");
    Clipboard.RegistrationFailure = null;
    await dispatcher.InvokeAsync(() => { service.SetEnabled(true); Clipboard.Publish(new(Text("retry-captured"))); });
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 1));
    await FinishAsync(service); passed++;
}
{
    var service = await ClipboardAsync();
    await dispatcher.InvokeAsync(() => Clipboard.Publish(new(Text("private-entry"))));
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 1));
    Clipboard.RevocationFailure = new COMException("The clipboard component disconnected.");
    await FinishAsync(service);
    Check(!service.Enabled && service.Items.Count == 0, "Clipboard disposal failed to clear private history after event revocation failure.");
    Clipboard.RevocationFailure = null;
    // The native component may retain its callback after failed revocation; it must now be inert.
    await dispatcher.InvokeAsync(() => Clipboard.Publish(new(Text("after-exit"))));
    Check(service.Items.Count == 0, "A retained clipboard notification revived disposed capture."); passed++;
}
{
    var service = await ClipboardAsync();
    var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    await dispatcher.InvokeAsync(() => { Clipboard.Publish(new(slow.Task)); service.Clear(); Clipboard.Publish(new(Text("after-clear"))); });
    slow.SetResult("sensitive-before-clear");
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 1));
    Check(await dispatcher.InvokeAsync(() => service.Items[0].Text == "after-clear"), "Clear allowed old clipboard material to return.");
    await FinishAsync(service); passed++;
}
{
    var service = await ClipboardAsync();
    var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    await dispatcher.InvokeAsync(() => { Clipboard.Publish(new(slow.Task)); service.SetEnabled(false); service.SetEnabled(true); Clipboard.Publish(new(Text("new-session"))); });
    slow.SetResult("disabled-session");
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 1));
    Check(await dispatcher.InvokeAsync(() => service.Items[0].Text == "new-session"), "Disable/re-enable allowed an invalidated entry to return.");
    await FinishAsync(service); passed++;
}
{
    var service = await ClipboardAsync();
    var slow = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    var warnings = 0;
    await dispatcher.InvokeAsync(() =>
    {
        service.Error += (_, _) => warnings++;
        Clipboard.Publish(new(slow.Task));
        for (var index = 0; index < 60; index++) Clipboard.Publish(new(Text("entry-" + index)));
    });
    slow.SetResult("first");
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 50 && service.Items[0].Text == "entry-59"));
    Check(await dispatcher.InvokeAsync(() => warnings == 10 && service.Items[^1].Text == "entry-10"), "Clipboard pending work was not bounded or overflow was silent.");
    await FinishAsync(service); passed++;
}
{
    var service = await ClipboardAsync();
    var warning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await dispatcher.InvokeAsync(() =>
    {
        service.Error += (_, _) => warning.TrySetResult();
        Clipboard.Publish(new(Text("excluded"), true));
        Clipboard.Publish(new(Text(new string('x', 100_001))));
        Clipboard.Publish(new(Text("supported")));
    });
    await warning.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await WaitFor(() => dispatcher.InvokeAsync(() => service.Items.Count == 1));
    Check(await dispatcher.InvokeAsync(() => service.Items[0].Text == "supported"), "Excluded or oversize clipboard content was captured.");
    await FinishAsync(service); passed++;
}
{
    using var sound = new AmbientSound(soundCache);
    var loading = new TaskCompletionSource<StorageFile>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    StorageFile.Loader = path => { requested.TrySetResult(path); return loading.Task; };
    var play = sound.PlayAsync(false);
    var path = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    sound.Stop(); loading.SetResult(new(path)); await play;
    Check(MediaPlayer.Latest!.PlayCalls == 0, "Ambient playback started after Stop."); passed++;
}
{
    using var sound = new AmbientSound(soundCache);
    var first = new TaskCompletionSource<StorageFile>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    StorageFile.Loader = path => path.EndsWith("white.wav") ? Request(path) : Task.FromResult(new StorageFile(path));
    Task<StorageFile> Request(string path) { requested.TrySetResult(path); return first.Task; }
    var old = sound.PlayAsync(false);
    var oldPath = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await sound.PlayAsync(true); first.SetResult(new(oldPath)); await old;
    Check(MediaPlayer.Latest!.PlayCalls == 1 && MediaPlayer.Latest.Source!.File.Path.EndsWith("brown.wav"), "Older ambient request replaced the newest sound."); passed++;
}
{
    var sound = new AmbientSound(soundCache);
    var loading = new TaskCompletionSource<StorageFile>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    StorageFile.Loader = path => { requested.TrySetResult(path); return loading.Task; };
    var play = sound.PlayAsync(false);
    var path = await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    sound.Dispose(); loading.SetResult(new(path)); await play;
    Check(MediaPlayer.Latest!.PlayCalls == 0, "Ambient playback started after Dispose."); passed++;
}
{
    MediaPlayer.ComponentAvailable = false;
    using var sound = new AmbientSound(soundCache);
    Check(!sound.IsAvailable && sound.UnavailableReason is not null, "Unavailable media component had no graceful fallback.");
    sound.Stop(); sound.Volume = .7;
    MediaPlayer.ComponentAvailable = true; passed++;
}
{
    using var sound = new AmbientSound(soundCache);
    var warnings = 0; sound.Error += (_, _) => warnings++;
    MediaPlayer.Latest!.Fail();
    Check(!sound.IsAvailable && warnings == 1, "Asynchronous media failure was not exposed."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession { TimelineFailure = new COMException("Live stream exposes no timeline.") };
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService();
    await media.StartAsync();
    Check(media.Current is { Title: "Real player title", CanSeek: false, CanPause: true }, "Unsupported timeline hid working live media controls.");
    await media.PlayPauseAsync();
    Check(session.PauseCalls == 1, "A live player without timeline could not be paused."); passed++;
}
{
    var manager = new GlobalSystemMediaTransportControlsSessionManager();
    manager.Sessions.Add(new() { PlaybackFailure = new COMException("Player disconnected.") });
    var paused = new GlobalSystemMediaTransportControlsSession(); paused.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
    paused.Properties.Title = "Paused player"; manager.Sessions.Add(paused);
    var playing = new GlobalSystemMediaTransportControlsSession(); playing.Properties.Title = "Playing player"; manager.Sessions.Add(playing);
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService();
    await media.StartAsync();
    Check(media.Current?.Title == "Playing player", "An active player was missed when Windows had no designated current session."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped;
    var manager = new GlobalSystemMediaTransportControlsSessionManager(); manager.Sessions.Add(session);
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService();
    await media.StartAsync();
    Check(media.Current is null, "A stopped fallback player was incorrectly presented as active.");
    var failed = false; try { await media.PlayPauseAsync(); } catch (InvalidOperationException) { failed = true; }
    Check(failed && session.PlayCalls == 0, "The service accepted playback control without an active session."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Timeline.StartTime = TimeSpan.FromSeconds(10); session.Timeline.EndTime = TimeSpan.FromSeconds(40);
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    await media.SeekAsync(TimeSpan.FromMinutes(5));
    Check(session.SeekTicks == TimeSpan.FromSeconds(40).Ticks, "Seeking past the track did not clamp to its absolute timeline end.");
    await media.SeekAsync(TimeSpan.FromSeconds(-5));
    Check(session.SeekTicks == TimeSpan.FromSeconds(10).Ticks, "Negative seeking did not clamp to its absolute timeline start.");
    session.Playback.Controls.IsPauseEnabled = false;
    var failed = false; try { await media.PlayPauseAsync(); } catch (InvalidOperationException) { failed = true; }
    Check(failed && session.PauseCalls == 0, "An unsupported pause operation was sent to the player."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Thumbnail = new Windows.Storage.Streams.FailingArtwork(new ArgumentException("The player provided an invalid thumbnail."));
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current is { Title: "Real player title", ArtworkPath: null, CanPause: true }, "Invalid optional artwork hid the active media session."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = session };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    var media = new WindowsMediaService(); await media.StartAsync();
    session.EventFailure = new COMException("The old player disconnected before event revocation.");
    manager.Current = new() { EventFailure = new COMException("Events unavailable on new player.") };
    manager.Current.Properties.Title = "Replacement player";
    await media.StartAsync();
    Check(media.Current?.Title == "Replacement player", "Revoking a disconnected player's events prevented player switching.");
    manager.EventFailure = new COMException("Manager disconnected at shutdown.");
    await media.DisposeAsync(); passed++;
}
{
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = new() };
    await using var media = new WindowsMediaService();
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var canceled = false; try { await media.StartAsync(cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
    Check(canceled, "Canceled media startup did not honor its caller's cancellation.");
    await media.StartAsync();
    Check(media.Current is not null, "Cancellation poisoned a subsequent media startup retry."); passed++;
}
Console.WriteLine($"PASS: {passed} native orchestration regression cases (explicit API doubles; native Windows runtime unverified).");
if (Directory.Exists(soundCache)) Directory.Delete(soundCache, recursive: true);
