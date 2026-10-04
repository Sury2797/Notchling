using System.Runtime.InteropServices;

namespace Notch.Windows.Interop;

/// <summary>Resolves shell folder redirection through the Windows known-folder API.</summary>
public static class NativeFolders
{
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string DownloadsPath
    {
        get
        {
            var folder = Downloads;
            // KF_FLAG_DONT_VERIFY avoids probing a redirected network location on the UI thread.
            var result = SHGetKnownFolderPath(ref folder, 0x4000, 0, out var path);
            try
            {
                if (result < 0) Marshal.ThrowExceptionForHR(result);
                return Marshal.PtrToStringUni(path) ?? throw new InvalidOperationException("Windows did not provide a Downloads folder.");
            }
            finally { if (path != 0) Marshal.FreeCoTaskMem(path); }
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, nint token, out nint path);
}
