using Notch.Windows.Services;

static class ShelfCaptureCases
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task ExpectAsync<T>(Func<Task> operation) where T : Exception
    {
        try { await operation(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name} from the shelf capture.");
    }

    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "Notch.Shelf.Tests", Guid.NewGuid().ToString("N"));
        var passed = 0;
        Directory.CreateDirectory(root);
        try
        {
            {
                var source = Path.Combine(root, "Original.txt"); var bytes = "Original file must stay untouched"u8.ToArray();
                await File.WriteAllBytesAsync(source, bytes);
                var service = new ShelfCaptureService(Path.Combine(root, "copy"));
                var capture = await service.SaveFileAsync(source);
                Check(service.Owns(capture) && capture != source && (await File.ReadAllBytesAsync(capture)).SequenceEqual(bytes)
                    && (await File.ReadAllBytesAsync(source)).SequenceEqual(bytes), "Saving a shelf copy changed or failed to preserve its original bytes.");
                var second = await service.SaveFileAsync(source);
                Check(second != capture && File.Exists(capture) && File.Exists(second), "Repeated shelf captures overwrote an earlier file.");
                Check(!service.Owns(source) && !service.Owns(Path.Combine(service.DirectoryPath + "-other", "fake.txt")), "Capture ownership accepted a neighbouring folder.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "virtual"));
                await using var input = new MemoryStream("virtual file bytes"u8.ToArray());
                var path = await service.SaveStreamAsync(input, "../../outside\\CON:<bad>?*.txt");
                Check(service.Owns(path) && Path.GetFileName(path).EndsWith("CONbad.txt") && !Path.GetFileName(path).Any(character => "<>:\"/\\|?*".Contains(character)),
                    "A virtual filename escaped the shelf folder or retained invalid Windows characters.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "images"));
                var bytes = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
                await using var input = new OneByteStream(bytes);
                var path = await service.SaveImageAsync(input);
                Check(Path.GetExtension(path) == ".png" && (await File.ReadAllBytesAsync(path)).SequenceEqual(bytes), "A non-seekable, short-read image stream was corrupted or misidentified.");
                await using var jpeg = new MemoryStream(new byte[] { 255, 216, 255, 224, 1 });
                Check(Path.GetExtension(await service.SaveImageAsync(jpeg)) == ".jpg", "JPEG captures received the wrong extension.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "invalid"));
                await using var input = new MemoryStream("<html>not an image</html>"u8.ToArray());
                await ExpectAsync<InvalidDataException>(() => service.SaveImageAsync(input));
                Check(!Directory.EnumerateFiles(service.DirectoryPath).Any(), "An unsupported image left a file presented as a saved capture.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "limit"), fileLimit: 32, totalLimit: 64);
                await using var input = new MemoryStream(new byte[33]);
                await ExpectAsync<IOException>(() => service.SaveStreamAsync(input, "too-large.bin"));
                Check(!Directory.EnumerateFiles(service.DirectoryPath).Any(), "An over-limit stream left an incomplete or visible copy.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "total"), fileLimit: 32, totalLimit: 40);
                await using var first = new MemoryStream(new byte[30]); await service.SaveStreamAsync(first, "first.part");
                await using var second = new MemoryStream(new byte[11]); await ExpectAsync<IOException>(() => service.SaveStreamAsync(second, "second.bin"));
                Check(Directory.EnumerateFiles(service.DirectoryPath).Count() == 1 && Directory.EnumerateFiles(service.DirectoryPath).All(path => new FileInfo(path).Length == 30),
                    "A full shelf copy folder accepted another file or damaged its existing copy.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "cancel"));
                await using var input = new BlockAfterPrefixStream(); using var cancellation = new CancellationTokenSource();
                var save = service.SaveStreamAsync(input, "cancelled.bin", cancellation.Token);
                await input.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
                await ExpectAsync<OperationCanceledException>(() => save);
                Check(!Directory.EnumerateFiles(service.DirectoryPath).Any(), "Cancellation retained an incomplete shelf copy.");
                await using var retry = new MemoryStream("retry"u8.ToArray());
                Check(File.Exists(await service.SaveStreamAsync(retry, "retry.bin")), "Cancelling a capture prevented its next retry.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "concurrent"), fileLimit: 32, totalLimit: 40);
                await using var first = new MemoryStream(new byte[30]); await using var second = new MemoryStream(new byte[30]);
                var outcomes = await Task.WhenAll(TrySave(first, "one.bin"), TrySave(second, "two.bin"));
                async Task<bool> TrySave(Stream stream, string name) { try { await service.SaveStreamAsync(stream, name); return true; } catch (IOException) { return false; } }
                Check(outcomes.Count(result => result) == 1 && Directory.EnumerateFiles(service.DirectoryPath).Count() == 1,
                    "Concurrent captures bypassed the total storage budget.");
                passed++;
            }
            {
                var service = new ShelfCaptureService(Path.Combine(root, "empty"));
                await using var empty = new MemoryStream();
                Check(new FileInfo(await service.SaveStreamAsync(empty, "empty.txt")).Length == 0, "A legitimate empty file could not be saved to the shelf.");
                for (var index = 1; index < 100; index++) await File.WriteAllBytesAsync(Path.Combine(service.DirectoryPath, $"existing-{index}.txt"), []);
                await using var last = new MemoryStream("next"u8.ToArray());
                await ExpectAsync<IOException>(() => service.SaveStreamAsync(last, "over-count.txt"));
                Check(Directory.EnumerateFiles(service.DirectoryPath).Count() == 100, "The owned capture count limit admitted a 101st file.");
                passed++;
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        return passed;
    }

    private sealed class OneByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
            => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], token);
    }

    private sealed class BlockAfterPrefixStream : MemoryStream
    {
        private bool _prefixRead;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (!_prefixRead) { buffer.Span[..16].Clear(); _prefixRead = true; return 16; }
            Blocked.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return 0;
        }
    }
}
