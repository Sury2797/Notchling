using Microsoft.UI.Dispatching;
using Notch.Windows.Services;
using Notch.Core;
using Windows.ApplicationModel;
using Windows.Storage.Streams;
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
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Title = "  Track\r\n🎧  title\0 "; session.Properties.Artist = "  Alice\t & Bob  ";
    session.Properties.AlbumTitle = " Album\nOne ";
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current is { Title: "Track 🎧 title", Artist: "Alice & Bob", AlbumTitle: "Album One" }, "Media title/artist/album metadata did not preserve real Unicode while removing control characters.");
    Check(MediaPresentation.CleanMetadata(new string('x', 900)).Length == 512
        && MediaPresentation.CleanMetadata("😀", 1).Length == 0, "Metadata bounds split a Unicode character or left unlimited player-controlled text."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Title = " "; session.Properties.Artist = ""; session.Properties.AlbumTitle = "";
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current is { Title: "", Artist: "", ArtworkPath: null, AlbumTitle: "" }
        && MediaPresentation.Title(media.Current) == "Untitled media", "Missing metadata invented an artist/title or hid the genuine player.");
    Check(MediaPresentation.SourceLabel("fakechrome.app") == "Connected player"
        && MediaPresentation.SourceLabel("Chrome") == "Google Chrome"
        && MediaPresentation.SourceLabel("test_123!App") == "Connected player", "Player source fallback guessed an unrelated app from an ID substring."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.PropertiesReader = () => Task.FromException<GlobalSystemMediaTransportControlsSessionMediaProperties>(new COMException("Metadata unavailable temporarily."));
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current is { Title: "", CanPause: true }, "Optional media properties failure hid usable native transport controls.");
    await media.PlayPauseAsync(); Check(session.PauseCalls == 1, "Metadata failure prevented a real player pause."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
    session.Playback.PlaybackRate = 2;
    session.Timeline.Position = TimeSpan.FromSeconds(30);
    session.Timeline.LastUpdatedTime = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(20);
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current?.Position == TimeSpan.FromSeconds(30), "Paused media incorrectly projected elapsed playback time.");
    session.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
    await media.StartAsync();
    Check(media.Current?.Position.TotalSeconds is > 69 and < 72, "Playing timeline did not respect the actual 2x playback rate.");
    Check(MediaPresentation.Clock(TimeSpan.FromHours(25) + TimeSpan.FromMinutes(3)) == "25:03:00", "Long media duration wrapped its total hours."); passed++;
}
{
    var first = new GlobalSystemMediaTransportControlsSession();
    var properties = new TaskCompletionSource<GlobalSystemMediaTransportControlsSessionMediaProperties>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    first.PropertiesReader = () => { requested.TrySetResult(); return properties.Task; };
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = first };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService(); var old = media.StartAsync();
    await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var observed = new List<string>(); media.Changed += (_, snapshot) => { lock (observed) observed.Add(snapshot?.Title ?? ""); };
    var replacement = new GlobalSystemMediaTransportControlsSession(); replacement.Properties.Title = "Newest session";
    manager.Current = replacement; manager.NotifyCurrent();
    await old; properties.SetResult(first.Properties);
    await WaitFor(() => Task.FromResult(media.Current?.Title == "Newest session"));
    lock (observed) Check(!observed.Contains("Real player title"), "A delayed old-session metadata read was published after a native player switch."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Thumbnail = new Artwork(() => Task.FromResult(new RandomAccessStream(new MemoryStream(new byte[] { 1, 2, 3 }), "image/png")));
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = session };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    var cache = Path.Combine(soundCache, "media");
    await using var media = new WindowsMediaService(cache); await media.StartAsync();
    var firstPath = media.Current?.ArtworkPath;
    session.Properties.Thumbnail = new Artwork(() => Task.FromResult(new RandomAccessStream(new MemoryStream(new byte[] { 4, 5, 6 }), "image/png")));
    session.NotifyProperties();
    await WaitFor(() => Task.FromResult(media.Current?.ArtworkPath is { } path && path != firstPath));
    Check(firstPath is not null && File.Exists(firstPath)
        && File.ReadAllBytes(media.Current!.ArtworkPath!).SequenceEqual(new byte[] { 4, 5, 6 }), "Changed native artwork was missed when track text remained unchanged."); passed++;
}
{
    var first = new GlobalSystemMediaTransportControlsSession();
    var artwork = new TaskCompletionSource<RandomAccessStream>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    first.Properties.Thumbnail = new Artwork(() => { requested.TrySetResult(); return artwork.Task; });
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = first };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "switch"));
    var old = media.StartAsync(); await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(media.Current?.Title == "Real player title", "A slow thumbnail blocked immediate track metadata and transport publication.");
    var replacement = new GlobalSystemMediaTransportControlsSession(); replacement.Properties.Title = "Replacement without art";
    manager.Current = replacement; manager.NotifyCurrent();
    await old;
    await WaitFor(() => Task.FromResult(media.Current?.Title == "Replacement without art"));
    using var lateStream = new RandomAccessStream(new MemoryStream(new byte[] { 1 }), "image/png");
    artwork.SetResult(lateStream);
    await media.StartAsync();
    Check(media.Current is { Title: "Replacement without art", ArtworkPath: null }, "Late previous-session artwork replaced the new active player."); passed++;
}
{
    var info = new AppInfo(); info.DisplayInfo.DisplayName = "  Registered\nPlayer  ";
    info.DisplayInfo.Logo = new Artwork(() => Task.FromResult(new RandomAccessStream(new MemoryStream(new byte[] { 7, 8 }), "image/png")));
    var lookups = new List<string>(); AppInfo.Resolver = id => { lookups.Add(id); return info; };
    var session = new GlobalSystemMediaTransportControlsSession { SourceAppUserModelId = "Registered.player" };
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "source")); await media.StartAsync();
    Check(media.Current is { ArtworkPath: null, SourceDisplayName: "Registered Player", SourceIconPath: not null }
        && File.Exists(media.Current.SourceIconPath) && lookups.SequenceEqual(new[] { "Registered.player" }), "Native application icon fallback did not preserve its exact source identity or masqueraded as album art.");
    await media.StartAsync(); Check(lookups.Count == 1, "Unchanged player logo lookup repeated on every timeline refresh.");
    AppInfo.Resolver = _ => null; passed++;
}
{
    var lookups = 0; AppInfo.Resolver = _ => { lookups++; throw new ArgumentException("An unpackaged app ID has no AppInfo."); };
    var session = new GlobalSystemMediaTransportControlsSession { SourceAppUserModelId = "Unregistered.player" };
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(); await media.StartAsync(); await media.StartAsync();
    Check(media.Current is { Title: "Real player title", SourceIconPath: null, CanPause: true } && lookups == 1,
        "Unavailable OS app information broke media controls or caused repeated logo work.");
    AppInfo.Resolver = _ => null; passed++;
}
{
    var closed = new GlobalSystemMediaTransportControlsSession(); closed.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
    var playing = new GlobalSystemMediaTransportControlsSession(); playing.Properties.Title = "Surviving active player";
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = closed }; manager.Sessions.Add(playing);
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService(); await media.StartAsync();
    Check(media.Current?.Title == "Surviving active player", "A closed designated session hid a genuinely active fallback player.");
    manager.Current = playing; playing.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped;
    await media.StartAsync();
    Check(media.Current?.PlaybackState == MediaPlaybackState.Stopped && !media.Current.IsPlaying, "Stopped playback was incorrectly classified as a paused track.");
    manager.Current = null; manager.Sessions.Clear(); manager.NotifyCurrent();
    await WaitFor(() => Task.FromResult(media.Current is null)); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Thumbnail = new Artwork(() => Task.FromResult(new RandomAccessStream(new MemoryStream(new byte[] { 1, 2 }), "image/png")));
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "retain-art")); await media.StartAsync();
    var path = media.Current!.ArtworkPath; var sawBlank = false;
    media.Changed += (_, snapshot) => { if (snapshot?.ArtworkPath is null) sawBlank = true; };
    session.Properties.Thumbnail = new FailingArtwork(new ArgumentException("Transient same-track thumbnail error."));
    session.NotifyProperties(); await media.StartAsync();
    Check(!sawBlank && path is not null && media.Current?.ArtworkPath == path, "Refreshing unchanged-track artwork flickered blank or discarded a valid cache after an optional preview failure."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "timeline-art")); await media.StartAsync();
    var artwork = new TaskCompletionSource<RandomAccessStream>(TaskCreationOptions.RunContinuationsAsynchronously);
    var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    session.Properties.Thumbnail = new Artwork(() => { requested.TrySetResult(); return artwork.Task; });
    session.NotifyProperties(); await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
    session.Timeline.Position = TimeSpan.FromSeconds(45); session.NotifyTimeline();
    session.Timeline.Position = TimeSpan.FromSeconds(46); session.NotifyTimeline();
    artwork.SetResult(new RandomAccessStream(new MemoryStream(new byte[] { 9, 10 }), "image/png"));
    await WaitFor(() => Task.FromResult(media.Current?.ArtworkPath is not null));
    Check(File.ReadAllBytes(media.Current!.ArtworkPath!).SequenceEqual(new byte[] { 9, 10 }), "Frequent same-track timeline updates starved an in-flight thumbnail refresh."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession();
    session.Properties.Thumbnail = new Artwork(() => Task.FromResult(new RandomAccessStream(new StalledArtworkStream(), "image/png")));
    GlobalSystemMediaTransportControlsSessionManager.Available = new() { Current = session };
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "stalled"));
    var started = System.Diagnostics.Stopwatch.GetTimestamp(); await media.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(System.Diagnostics.Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(4.5)
        && media.Current is { Title: "Real player title", ArtworkPath: null, CanPause: true }, "A thumbnail byte-stream that ignored cancellation retained the media refresh gate indefinitely.");
    session.Properties.Thumbnail = null; await media.StartAsync();
    await media.PlayPauseAsync(); Check(session.PauseCalls == 1, "Optional stalled artwork poisoned a later media refresh/control."); passed++;
}
{
    var session = new GlobalSystemMediaTransportControlsSession(); session.Properties.AlbumTitle = "Known album";
    session.Properties.Thumbnail = new Artwork(() => Task.FromResult(new RandomAccessStream(new MemoryStream(new byte[] { 1, 2 }), "image/png")));
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = session };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService(Path.Combine(soundCache, "known-metadata")); await media.StartAsync();
    var artwork = media.Current!.ArtworkPath;
    session.PropertiesReader = () => Task.FromException<GlobalSystemMediaTransportControlsSessionMediaProperties>(new COMException("A temporary same-session metadata failure."));
    session.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
    await media.StartAsync();
    Check(media.Current is { Title: "Real player title", Artist: "Real player artist", AlbumTitle: "Known album", IsPlaying: false }
        && media.Current.ArtworkPath == artwork, "An unchanged-session metadata failure erased known track details instead of updating its transport.");
    session.PropertiesReader = () => Task.FromException<GlobalSystemMediaTransportControlsSessionMediaProperties>(new TimeoutException("Temporary metadata timeout."));
    await media.StartAsync();
    Check(media.Current?.Title == "Real player title", "An unchanged-session metadata timeout erased its known title.");
    session.NotifyProperties(); await media.StartAsync();
    Check(media.Current is { Title: "", Artist: "", AlbumTitle: "", ArtworkPath: null }, "An actual track-property change reused old metadata after the new track failed to provide any.");
    manager.Current = new(); manager.Current.PropertiesReader = session.PropertiesReader;
    await media.StartAsync();
    Check(media.Current is { Title: "", Artist: "", AlbumTitle: "", ArtworkPath: null }, "A replacement session inherited metadata from its previous application."); passed++;
}
{
    var first = new GlobalSystemMediaTransportControlsSession { SourceAppUserModelId = "Chrome" };
    var manager = new GlobalSystemMediaTransportControlsSessionManager { Current = first };
    GlobalSystemMediaTransportControlsSessionManager.Available = manager;
    await using var media = new WindowsMediaService(); await media.StartAsync();
    var original = media.Current!;
    Check(original.SessionRevision > 0, "A native media session did not publish its session identity.");
    var selection = MediaSourceIdentity.SelectForTrack(original, MediaSourceBrand.YouTube);
    Check(selection is not null, "A generic browser did not allow a track-specific provider choice.");
    first.Timeline.Position = TimeSpan.FromSeconds(30);
    first.Playback.PlaybackStatus = GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
    await media.StartAsync();
    Check(media.Current!.SessionRevision == original.SessionRevision && MediaSourceIdentity.Matches(media.Current, selection),
        "Same-session position/playback refresh invalidated the selected media provider.");
    var replacement = new GlobalSystemMediaTransportControlsSession { SourceAppUserModelId = first.SourceAppUserModelId };
    replacement.Properties.Title = first.Properties.Title;
    replacement.Properties.Artist = first.Properties.Artist;
    replacement.Properties.AlbumTitle = first.Properties.AlbumTitle;
    manager.Current = replacement; await media.StartAsync();
    Check(media.Current!.SessionRevision > original.SessionRevision
        && MediaSourceIdentity.Resolve(media.Current, selection) is { Brand: MediaSourceBrand.Chrome, IsUserSelected: false },
        "An identical-title replacement browser session inherited the previous session's YouTube choice.");
    var replacementRevision = media.Current.SessionRevision;
    manager.Current = null; await media.StartAsync();
    Check(media.Current is null, "Clearing the active browser did not remove its media identity.");
    manager.Current = first; await media.StartAsync();
    Check(media.Current!.SessionRevision > replacementRevision && !MediaSourceIdentity.Matches(media.Current, selection),
        "A browser session that returned after disappearing revived an expired provider choice."); passed++;
}
passed += await UpdateServiceCases.RunAsync();
passed += await ShelfCaptureCases.RunAsync();
Console.WriteLine($"PASS: {passed} native orchestration regression cases (explicit API doubles; native Windows runtime unverified).");
if (Directory.Exists(soundCache)) Directory.Delete(soundCache, recursive: true);
