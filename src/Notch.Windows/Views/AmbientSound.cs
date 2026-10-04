using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Notch.Windows.Views;

/// <summary>Optional local audio with cancellation of every pending playback request.</summary>
internal sealed class AmbientSound : IDisposable
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly string _cacheDirectory;
    private MediaPlayer? _player;
    private CancellationTokenSource? _playRequest;
    private long _generation;
    private double _volume = .15;
    private bool _disposed;

    public event EventHandler<string>? Error;
    public bool IsAvailable => !_disposed && _player is not null && UnavailableReason is null;
    public string? UnavailableReason { get; private set; }

    public AmbientSound(string? cacheDirectory = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notch", "Cache", "Sounds");
        try
        {
            _player = new MediaPlayer { IsLoopingEnabled = true, Volume = _volume };
            _player.MediaFailed += OnMediaFailed;
        }
        catch (Exception error)
        {
            // Missing optional Windows media components must not prevent other tools loading.
            UnavailableReason = $"Ambient sound is unavailable on this Windows installation: {error.Message}";
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            if (!double.IsFinite(value)) return;
            lock (_gate)
            {
                _volume = Math.Clamp(value, 0, 1);
                if (IsAvailable) _player!.Volume = _volume;
            }
        }
    }

    public async Task PlayAsync(bool brown)
    {
        CancellationToken token;
        long generation;
        lock (_gate)
        {
            if (_disposed) return;
            if (!IsAvailable) throw new InvalidOperationException(UnavailableReason ?? "Ambient sound is unavailable.");
            InvalidatePlayback();
            _playRequest = new();
            token = _playRequest.Token;
            generation = _generation;
        }
        try
        {
            var directory = _cacheDirectory;
            var path = Path.Combine(directory, brown ? "brown.wav" : "white.wav");
            await _fileGate.WaitAsync(token);
            try
            {
                Directory.CreateDirectory(directory);
                if (!IsCompleteSound(path)) await Task.Run(() => GenerateAtomically(path, brown, token), token);
            }
            finally { _fileGate.Release(); }
            token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path).AsTask(token);
            lock (_gate)
            {
                if (_disposed || token.IsCancellationRequested || generation != _generation) return;
                _player!.Source = MediaSource.CreateFromStorageFile(file);
                _player.Play();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed) return;
            InvalidatePlayback();
            if (_player is not null) _player.Pause();
        }
    }

    private void InvalidatePlayback()
    {
        _generation++;
        _playRequest?.Cancel();
        _playRequest?.Dispose();
        _playRequest = null;
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed) return;
            InvalidatePlayback();
            UnavailableReason = "Ambient audio could not start. Check the Windows media components and output device.";
        }
        Error?.Invoke(this, UnavailableReason);
    }

    private static bool IsCompleteSound(string path) =>
        File.Exists(path) && new FileInfo(path).Length == 44 + 44_100 * 8 * sizeof(short);

    private static void GenerateAtomically(string path, bool brown, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Generate(temporary, brown, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Generate(string path, bool brown, CancellationToken token)
    {
        const int rate = 44100, seconds = 8;
        var samples = new short[rate * seconds];
        var random = new Random(416);
        var accumulated = 0.0;
        for (var index = 0; index < samples.Length; index++)
        {
            if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
            var noise = random.NextDouble() * 2 - 1;
            accumulated = Math.Clamp((accumulated + noise * .04) * .999, -1, 1);
            var fade = Math.Min(1.0, Math.Min(index, samples.Length - 1 - index) / 2205.0);
            samples[index] = (short)((brown ? accumulated : noise * .3) * fade * 16000);
        }
        using var file = File.Create(path);
        using var writer = new BinaryWriter(file, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
        foreach (var sample in samples) writer.Write(sample);
    }

    public void Dispose()
    {
        MediaPlayer? player;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            InvalidatePlayback();
            player = _player;
            _player = null;
            if (player is not null) player.MediaFailed -= OnMediaFailed;
        }
        if (player is not null)
        {
            try { player.Pause(); player.Source = null; }
            finally { player.Dispose(); }
        }
        // A canceled generator may still release this managed gate; leave it alive.
    }
}
