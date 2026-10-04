using System.Text;

namespace Notch.Windows.Services;

/// <summary>Best-effort, local-only evidence for otherwise opaque native startup failures.</summary>
internal static class StartupDiagnostics
{
    private const int MaximumLogBytes = 256 * 1024;
    private const int MaximumEntryCharacters = 32 * 1024;
    private static readonly object Sync = new();
    private static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static void Write(string source, Exception? error, string? message = null)
    {
        try
        {
            var details = error is null ? string.Empty :
                $"Type: {error.GetType().FullName}{Environment.NewLine}HResult: 0x{error.HResult:X8}{Environment.NewLine}{error}";
            if (!string.IsNullOrWhiteSpace(message))
                details = $"Native detail: {message}{Environment.NewLine}{details}";
            if (details.Length == 0) details = "No managed exception was supplied.";
            if (details.Length > MaximumEntryCharacters)
                details = details[..MaximumEntryCharacters] + Environment.NewLine + "[Exception details truncated]";
            var entry = $"[{DateTimeOffset.UtcNow:O}] {source}{Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}";
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchling", "Diagnostics");
            var path = Path.Combine(directory, "startup.log");
            lock (Sync)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(path) && new FileInfo(path).Length + Encoding.GetByteCount(entry) > MaximumLogBytes)
                    File.WriteAllText(path, string.Empty, Encoding);
                File.AppendAllText(path, entry, Encoding);
            }
        }
        catch
        {
            // Diagnostics must never replace the original failure or make startup depend on disk access.
        }
    }
}
