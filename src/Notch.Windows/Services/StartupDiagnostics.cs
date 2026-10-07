using System.Text;
using System.Runtime.InteropServices;

namespace Notch.Windows.Services;

/// <summary>Best-effort, local-only evidence for otherwise opaque native startup failures.</summary>
internal static class StartupDiagnostics
{
    private const int MaximumLogBytes = 256 * 1024;
    private const int MaximumEntryCharacters = 32 * 1024;
    private static readonly object Sync = new();
    private static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static int _fatalReported;

    public static string LogPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Notchling", "Diagnostics", "startup.log");

    public static void BeginSession() => Write("App.Starting", null,
        $"Version: {typeof(StartupDiagnostics).Assembly.GetName().Version}; Windows: {Environment.OSVersion.Version}; " +
        $"Process: {RuntimeInformation.ProcessArchitecture}; Runtime: {RuntimeInformation.FrameworkDescription}");

    /// <summary>A Win32 dialog remains usable when WinUI itself failed to initialize.</summary>
    public static void ReportFatal(string source, Exception error, string? message = null)
    {
        Write(source, error, message);
        if (Interlocked.Exchange(ref _fatalReported, 1) != 0) return;
        try
        {
            var cause = error.GetBaseException();
            var detail = cause.Message;
            if (detail.Length > 800) detail = detail[..800] + "…";
            MessageBox(0, "Notchling could not start.\n\n" + detail +
                $"\n\nError code: 0x{cause.HResult:X8}\nLocal diagnostic file:\n{LogPath}" +
                "\n\nTry reinstalling the latest Notchling setup. If the problem continues, include this diagnostic file when reporting it.",
                "Notchling — startup error", 0x0010 | 0x00010000);
        }
        catch
        {
            // Preserve the original error even if the native desktop is also unavailable.
        }
    }

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
            var path = LogPath;
            var directory = Path.GetDirectoryName(path)!;
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

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(nint owner, string message, string caption, uint type);
}
