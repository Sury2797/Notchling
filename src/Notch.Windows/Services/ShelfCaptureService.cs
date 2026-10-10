using System.Buffers;

namespace Notch.Windows.Services;

/// <summary>Owns copies explicitly saved to the local shelf; never downloads or changes a source file.</summary>
public sealed class ShelfCaptureService
{
    public const long MaximumFileBytes = 50L * 1024 * 1024;
    public const long MaximumTotalBytes = 250L * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly long _fileLimit, _totalLimit;
    public string DirectoryPath { get; }

    public ShelfCaptureService(string workspaceDirectory, long fileLimit = MaximumFileBytes, long totalLimit = MaximumTotalBytes)
    {
        if (fileLimit <= 0 || totalLimit < fileLimit) throw new ArgumentOutOfRangeException(nameof(fileLimit));
        DirectoryPath = Path.GetFullPath(Path.Combine(workspaceDirectory, "shelf-captures"));
        _fileLimit = fileLimit; _totalLimit = totalLimit;
    }

    public bool Owns(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.GetDirectoryName(Path.GetFullPath(path))?.Equals(DirectoryPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) == true; }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
    }

    public async Task<string> SaveFileAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await SaveAsync(input, Path.GetFileName(sourcePath), false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SaveImageAsync(Stream input, CancellationToken cancellationToken = default)
        => await SaveAsync(input, "Image", true, cancellationToken).ConfigureAwait(false);

    public async Task<string> SaveStreamAsync(Stream input, string suggestedName, CancellationToken cancellationToken = default)
        => await SaveAsync(input, suggestedName, false, cancellationToken).ConfigureAwait(false);

    private async Task<string> SaveAsync(Stream input, string suggestedName, bool requireImage, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporary = null;
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(DirectoryPath);
            if ((File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The shelf copy folder must be a local folder, not a redirected link.");
            var existingBytes = 0L;
            var files = 0;
            foreach (var path in Directory.EnumerateFiles(DirectoryPath))
            {
                existingBytes = checked(existingBytes + new FileInfo(path).Length);
                files++;
            }
            if (files >= 100) throw new IOException("The saved-copy folder contains 100 files. Reveal saved copies to remove files you no longer need.");

            // A stream need not be seekable and may return just one byte at a time.
            var prefixBytes = 0;
            while (prefixBytes < 16)
            {
                var read = await input.ReadAsync(buffer.AsMemory(prefixBytes, 16 - prefixBytes), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                prefixBytes += read;
            }
            var extension = requireImage ? ImageExtension(buffer.AsSpan(0, prefixBytes)) : null;
            if (requireImage && extension is null)
                throw new InvalidDataException("This image format could not be saved. Copy the image in your browser and use Paste image; PNG, JPEG, GIF, BMP, TIFF and WebP are supported.");
            var name = SafeName(suggestedName);
            if (requireImage) name = "Image" + extension;
            var destination = Path.Combine(DirectoryPath, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}-{name}");
            temporary = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".part");
            long written = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var read = prefixBytes;
                while (true)
                {
                    if (read == 0) break;
                    written = checked(written + read);
                    if (written > _fileLimit) throw new IOException($"This file exceeds the shelf copy limit of {_fileLimit / 1024 / 1024} MB. Add it as a reference instead.");
                    if (written > _totalLimit - existingBytes) throw new IOException("The saved-copy folder is full. Reveal saved copies to free space before saving another image or file.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                if (written == 0 && requireImage) throw new InvalidDataException("The dropped image is empty.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination); // Only a complete, durable copy becomes a shelf item.
            temporary = null;
            return destination;
        }
        finally
        {
            if (temporary is not null) { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            ArrayPool<byte>.Shared.Return(buffer);
            _gate.Release();
        }
    }

    private static string SafeName(string name)
    {
        // Windows filename rules also apply when the capture service is tested on Linux.
        name = name.Replace('\\', '/').Split('/')[^1];
        name = new string(name.Where(character => character >= ' ' && !"<>:\"/\\|?*".Contains(character)).Take(100).ToArray()).Trim(' ', '.');
        if (name.Length == 0) name = "File";
        return name;
    }

    private static string? ImageExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.StartsWith(new byte[] { 255, 216, 255 })) return ".jpg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return ".gif";
        if (bytes.StartsWith("BM"u8)) return ".bmp";
        if (bytes.StartsWith(new byte[] { 73, 73, 42, 0 }) || bytes.StartsWith(new byte[] { 77, 77, 0, 42 })) return ".tiff";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return ".webp";
        return null;
    }
}
