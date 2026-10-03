using System.Text.Json;

namespace Notch.Core;

public sealed class LocalStore : IDisposable
{
    public const long MaximumBytes = 10 * 1024 * 1024;
    private readonly string _directory;
    private readonly SemaphoreSlim _accessLock = new(1, 1);
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public LocalStore(string directory) { _directory = Path.GetFullPath(directory); Directory.CreateDirectory(_directory); }
    private string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name is "." or "..")
            throw new ArgumentException("Use a local data file name, not a path.", nameof(name));
        return Path.Combine(_directory, name + ".json");
    }
    public async Task<T?> ReadAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var path = Resolve(name);
        // Keep each read handle closed before replacing the file. Windows can
        // reject a new open while the previous file is pending deletion.
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return default;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumBytes) throw new InvalidDataException("Local data exceeds the 10 MB limit.");
            return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken).ConfigureAwait(false);
        }
        finally { _accessLock.Release(); }
    }
    public async Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        var path = Resolve(name);
        await _accessLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken).ConfigureAwait(false);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("The notebook exceeds 10 MB. Shorten or remove a note before saving. Your previous saved data is preserved.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _accessLock.Release(); }
        }
    }
    public void Dispose() => _accessLock.Dispose();
}
