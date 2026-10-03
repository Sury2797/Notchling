using System.Text.Json;

namespace Notch.Core;

public sealed class LocalStore : IDisposable
{
    public const long MaximumBytes = 10 * 1024 * 1024;
    private readonly string _directory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
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
        if (!File.Exists(path)) return default;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Local data exceeds the 10 MB limit.");
        return await JsonSerializer.DeserializeAsync<T>(stream, _options, cancellationToken);
    }
    public async Task WriteAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        var path = Resolve(name);
        await _writeLock.WaitAsync(cancellationToken);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, _options, cancellationToken);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("The notebook exceeds 10 MB. Shorten or remove a note before saving. Your previous saved data is preserved.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _writeLock.Release(); }
        }
    }
    public void Dispose() => _writeLock.Dispose();
}
