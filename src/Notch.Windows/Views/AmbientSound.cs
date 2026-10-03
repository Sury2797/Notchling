using System.Text;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Notch.Windows.Views;

internal sealed class AmbientSound : IDisposable
{
    private readonly MediaPlayer _player = new() { IsLoopingEnabled = true, Volume = .15 };
    private bool _disposed;
    public double Volume { get => _player.Volume; set => _player.Volume = Math.Clamp(value, 0, 1); }
    public async Task PlayAsync(bool brown)
    {
        if (_disposed) return;
        var directory = Path.Combine(Path.GetTempPath(), "Notch", "sounds"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, brown ? "brown.wav" : "white.wav");
        if (!File.Exists(path)) await Task.Run(() => Generate(path, brown));
        if (_disposed) return;
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (_disposed) return;
        _player.Source = MediaSource.CreateFromStorageFile(file); _player.Play();
    }
    public void Stop() => _player.Pause();
    private static void Generate(string path, bool brown)
    {
        const int rate = 44100, seconds = 8;
        var samples = new short[rate * seconds]; var random = new Random(416); var accumulated = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var noise = random.NextDouble() * 2 - 1;
            accumulated = Math.Clamp((accumulated + noise * .04) * .999, -1, 1);
            var fade = Math.Min(1.0, Math.Min(i, samples.Length - 1 - i) / 2205.0);
            samples[i] = (short)((brown ? accumulated : noise * .3) * fade * 16000);
        }
        using var file = File.Create(path); using var writer = new BinaryWriter(file, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2); foreach (var sample in samples) writer.Write(sample);
    }
    public void Dispose() { _disposed = true; _player.Pause(); _player.Source = null; _player.Dispose(); }
}
