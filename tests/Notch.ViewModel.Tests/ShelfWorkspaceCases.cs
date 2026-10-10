using System.Text.Json;
using Microsoft.UI.Dispatching;
using Notch.Windows.ViewModels;

internal static class ShelfWorkspaceCases
{
    private static void Assert(bool result, string message) { if (!result) throw new InvalidOperationException(message); }
    private static async Task ExpectAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} from shelf workspace operation.");
    }
    private static MainViewModel Create(string data) => new(new DispatcherQueue(), dataDirectory: data);

    public static async Task RegisterAsync(Func<string, Func<Task>, Task> run, string data)
    {
        await run("Shelf batch preserves originals, deduplicates and immediately survives restart", async () =>
        {
            var first = Path.Combine(data, "First.txt"); var second = Path.Combine(data, "Second.txt");
            await File.WriteAllTextAsync(first, "First original"); await File.WriteAllTextAsync(second, "Second original");
            await using (var vm = Create(data))
            {
                await vm.InitializeAsync();
                Assert(vm.AddShelfBatch([first, first, second]) == 2 && vm.Shelf.Select(item => item.Path).SequenceEqual([first, second]),
                    "A shelf drop duplicated or reordered files.");
                await vm.SaveShelfAsync();
                var saved = JsonSerializer.Deserialize<MainViewModel.LocalData>(await File.ReadAllTextAsync(Path.Combine(data, "workspace.json")))!;
                Assert(saved.Shelf.Select(item => item.Path).SequenceEqual([first, second]) && !vm.HasUnsavedChanges,
                    "Successful shelf admission did not immediately persist the complete batch.");
                Assert(await File.ReadAllTextAsync(first) == "First original" && await File.ReadAllTextAsync(second) == "Second original",
                    "Adding file references modified the originals.");
            }
            await using var reopened = Create(data); await reopened.InitializeAsync();
            Assert(reopened.Shelf.Select(item => item.Path).SequenceEqual([first, second]), "A saved file drop disappeared after restart.");
        });

        await run("Shelf batch rejects missing or excess files before adding a partial list", async () =>
        {
            await using var vm = Create(data); await vm.InitializeAsync();
            var first = Path.Combine(data, "First.txt"); await File.WriteAllTextAsync(first, "Keep");
            await ExpectAsync<FileNotFoundException>(() => { vm.AddShelfBatch([first, Path.Combine(data, "Missing.txt")]); return Task.CompletedTask; });
            Assert(vm.Shelf.Count == 0, "A failed batch retained only its earlier valid files.");
            var paths = Enumerable.Range(0, 100).Select(index => Path.Combine(data, $"Shelf-{index}.txt")).ToArray();
            foreach (var path in paths) await File.WriteAllTextAsync(path, "");
            vm.AddShelfBatch(paths);
            await ExpectAsync<InvalidOperationException>(() => { vm.AddShelfBatch([first, paths[0]]); return Task.CompletedTask; });
            Assert(vm.Shelf.Count == 100 && vm.Shelf.All(item => item.Path != first), "A full shelf admitted part of an over-capacity drop.");
            Assert(vm.AddShelfBatch([paths[0], paths[0]]) == 0 && vm.Shelf.Count == 100, "Duplicated references incorrectly consumed shelf capacity.");
            await vm.SaveShelfAsync();
        });

        await run("Shelf save failure preserves in-memory recovery and retries without losing the file", async () =>
        {
            await using var vm = Create(data); await vm.InitializeAsync();
            var capture = Path.Combine(data, "Capture.png"); await File.WriteAllBytesAsync(capture, [1, 2, 3]);
            vm.AddShelfBatch([capture]);
            var workspace = Path.Combine(data, "workspace.json");
            if (File.Exists(workspace)) File.Delete(workspace);
            Directory.CreateDirectory(workspace);
            await ExpectAsync<IOException>(vm.SaveShelfAsync);
            Assert(vm.Shelf.Single().Path == capture && File.Exists(capture) && vm.HasUnsavedChanges && vm.SaveState.Contains("failed"),
                "A shelf save failure dropped its recovery state or removed the captured file.");
            Directory.Delete(workspace); await vm.SaveShelfAsync();
            Assert(!vm.HasUnsavedChanges && JsonSerializer.Deserialize<MainViewModel.LocalData>(await File.ReadAllTextAsync(workspace))!.Shelf.Single().Path == capture,
                "A failed shelf save could not be retried safely.");
        });

        await run("Shelf admission rejects sample preview and disposed workspace centrally", async () =>
        {
            var path = Path.Combine(data, "Live.txt"); await File.WriteAllTextAsync(path, "Real file");
            var vm = Create(data); await vm.InitializeAsync();
            await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
            await ExpectAsync<InvalidOperationException>(() => { vm.AddShelfBatch([path]); return Task.CompletedTask; });
            await ExpectAsync<InvalidOperationException>(vm.SaveShelfAsync);
            Assert(vm.Shelf.Count == 0, "An image drop edited a sample workspace through the public batch API.");
            await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = false });
            await vm.DisposeAsync();
            await ExpectAsync<InvalidOperationException>(() => { vm.AddShelfBatch([path]); return Task.CompletedTask; });
            await ExpectAsync<InvalidOperationException>(vm.SaveShelfAsync);
            Assert(vm.Shelf.Count == 0 && File.Exists(path), "A late capture changed a disposed workspace or its original file.");
        });
    }
}
