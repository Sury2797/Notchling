using Microsoft.UI.Dispatching;
using Notch.Windows.Services;
using Notch.Windows.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Playback;
using Windows.Storage;

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
Console.WriteLine($"PASS: {passed} native orchestration regression cases (explicit API doubles; native Windows runtime unverified).");
if (Directory.Exists(soundCache)) Directory.Delete(soundCache, recursive: true);
