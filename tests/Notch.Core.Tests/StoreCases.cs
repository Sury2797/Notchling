using System.Text.Json;
using Notch.Core;

namespace Notch.Core.Tests;

internal static class StoreCases
{
    private sealed class CyclicValue
    {
        public CyclicValue Self => this;
    }

    private static async Task InStore(Func<LocalStore, string, Task> run)
    {
        var directory = Path.Combine(Path.GetTempPath(), "notch-core-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new LocalStore(directory);
            await run(store, directory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    public static void Register(TestSuite suite)
    {
        suite.AddAsync("Local storage roundtrips Unicode notes and overwrites atomically", () => InStore(async (store, directory) =>
        {
            var note = new SavedNote(Guid.NewGuid(), "Today 🌱", "नमस्ते\nKeep it calm.", DateTimeOffset.UtcNow);
            await store.WriteAsync("notes", new[] { note });
            Check.Equal(note, (await store.ReadAsync<SavedNote[]>("notes"))![0]);
            await store.WriteAsync("notes", new[] { note with { Text = "Updated" } });
            Check.Equal("Updated", (await store.ReadAsync<SavedNote[]>("notes"))![0].Text);
            Check.Equal(1, Directory.GetFiles(directory).Length);
            Check.False(Directory.GetFiles(directory).Any(path => path.EndsWith(".tmp", StringComparison.Ordinal)));
            Check.True(await store.ReadAsync<AppPreferences>("missing") is null);
        }));
        suite.AddAsync("Canceled writes preserve prior data and leave no temporary files", () => InStore(async (store, directory) =>
        {
            await store.WriteAsync("value", 7);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Check.ThrowsAsync<OperationCanceledException>(() => store.WriteAsync("value", 8, canceled.Token));
            Check.Equal(7, await store.ReadAsync<int>("value"));
            Check.Equal(1, Directory.GetFiles(directory).Length);
        }));
        suite.AddAsync("Concurrent writes and reads never expose partial JSON", () => InStore(async (store, directory) =>
        {
            await store.WriteAsync("value", new string('A', 2000));
            var writes = Task.WhenAll(Enumerable.Range(1, 32).Select(index => store.WriteAsync("value", new string('A', index * 1000))));
            while (!writes.IsCompleted)
            {
                var read = await store.ReadAsync<string>("value");
                Check.True(read is not null && read.All(character => character == 'A'));
            }
            await writes;
            Check.True((await store.ReadAsync<string>("value"))!.Length >= 1000);
            Check.Equal(1, Directory.GetFiles(directory).Length);
        }));
        suite.AddAsync("Local storage rejects path traversal and absolute paths", () => InStore(async (store, _) =>
        {
            foreach (var name in new[] { "../outside", "..", ".", "/tmp/outside", "folder/name", "folder\\name", " " })
            {
                await Check.ThrowsAsync<ArgumentException>(() => store.WriteAsync(name, 1));
                await Check.ThrowsAsync<ArgumentException>(() => store.ReadAsync<int>(name));
            }
        }));
        suite.AddAsync("Corrupted or oversized local files produce explicit recoverable errors", () => InStore(async (store, directory) =>
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "broken.json"), "{unfinished");
            await Check.ThrowsAsync<JsonException>(() => store.ReadAsync<AppPreferences>("broken"));
            await using (var file = File.Create(Path.Combine(directory, "large.json"))) file.SetLength(10 * 1024 * 1024 + 1);
            await Check.ThrowsAsync<InvalidDataException>(() => store.ReadAsync<string>("large"));
        }));
        suite.AddAsync("Oversized note writes preserve the last readable data and remove temporary files", () => InStore(async (store, directory) =>
        {
            var previous = new SavedNote(Guid.NewGuid(), "Keep this", "The valid saved note.", DateTimeOffset.UtcNow);
            await store.WriteAsync("notes", new[] { previous });
            var previousBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "notes.json"));
            var text = new string('A', 500_000);
            var oversized = Enumerable.Range(0, 21)
                .Select(index => new SavedNote(Guid.NewGuid(), $"Note {index}", text, previous.UpdatedAt)).ToArray();

            await Check.ThrowsAsync<InvalidDataException>(() => store.WriteAsync("notes", oversized));

            var retainedBytes = await File.ReadAllBytesAsync(Path.Combine(directory, "notes.json"));
            Check.True(previousBytes.SequenceEqual(retainedBytes));
            Check.Equal(previous, (await store.ReadAsync<SavedNote[]>("notes"))![0]);
            Check.Equal(1, Directory.GetFiles(directory).Length);
            await store.WriteAsync("notes", new[] { previous with { Text = "Saved after rejection" } });
            Check.Equal("Saved after rejection", (await store.ReadAsync<SavedNote[]>("notes"))![0].Text);
        }));
        suite.AddAsync("Storage limits count serialized UTF-8 bytes and accept readable near-limit notes", () => InStore(async (store, directory) =>
        {
            var updatedAt = DateTimeOffset.UtcNow;
            var nearLimit = Enumerable.Range(0, 20)
                .Select(index => new SavedNote(Guid.NewGuid(), $"Note {index}", new string('A', 500_000), updatedAt)).ToArray();
            await store.WriteAsync("notes", nearLimit);
            Check.True(new FileInfo(Path.Combine(directory, "notes.json")).Length <= 10 * 1024 * 1024);
            Check.Equal(nearLimit.Length, (await store.ReadAsync<SavedNote[]>("notes"))!.Length);

            var escapedText = new string('\u2603', 500_000);
            var encodedOversized = Enumerable.Range(0, 4)
                .Select(index => new SavedNote(Guid.NewGuid(), $"Unicode {index}", escapedText, updatedAt)).ToArray();
            await Check.ThrowsAsync<InvalidDataException>(() => store.WriteAsync("notes", encodedOversized));
            Check.Equal(nearLimit[0], (await store.ReadAsync<SavedNote[]>("notes"))![0]);
            Check.Equal(1, Directory.GetFiles(directory).Length);
        }));
        suite.AddAsync("A failed serialization preserves data and releases the store for subsequent writes", () => InStore(async (store, directory) =>
        {
            await store.WriteAsync("value", 7);
            await Check.ThrowsAsync<JsonException>(() => store.WriteAsync("value", new CyclicValue()));

            Check.Equal(7, await store.ReadAsync<int>("value"));
            Check.Equal(1, Directory.GetFiles(directory).Length);
            await store.WriteAsync("value", 9).WaitAsync(TimeSpan.FromSeconds(5));
            Check.Equal(9, await store.ReadAsync<int>("value"));
            Check.Equal(1, Directory.GetFiles(directory).Length);
        }));
    }
}
