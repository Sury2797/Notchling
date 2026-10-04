using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Notch.Windows.Interop;

/// <summary>Owns the native large and small window icons used by the shell and notification area.</summary>
internal sealed class WindowIcon : IDisposable
{
    private const uint WmSetIcon = 0x0080;
    private readonly nint _window;
    private readonly string _path;
    private IconHandle _large;
    private IconHandle _small;
    private uint _dpi;
    private bool _disposed;

    internal nint SmallIcon => _small.DangerousGetHandle();

    internal WindowIcon(nint window, string path)
    {
        _window = window;
        _path = path;
        _dpi = CurrentDpi();
        _large = Load(path, GetSystemMetricsForDpi(11, _dpi), GetSystemMetricsForDpi(12, _dpi));
        try { _small = Load(path, GetSystemMetricsForDpi(49, _dpi), GetSystemMetricsForDpi(50, _dpi)); }
        catch { _large.Dispose(); throw; }
        NativeMethods.SendMessage(window, WmSetIcon, 1, _large.DangerousGetHandle());
        NativeMethods.SendMessage(window, WmSetIcon, 0, _small.DangerousGetHandle());
    }

    internal void RefreshForDpi(Action<nint> updateTray)
    {
        if (_disposed || !NativeMethods.IsWindow(_window)) return;
        ArgumentNullException.ThrowIfNull(updateTray);
        var dpi = CurrentDpi();
        if (dpi == _dpi) return;
        var large = Load(_path, GetSystemMetricsForDpi(11, dpi), GetSystemMetricsForDpi(12, dpi));
        IconHandle small;
        try { small = Load(_path, GetSystemMetricsForDpi(49, dpi), GetSystemMetricsForDpi(50, dpi)); }
        catch { large.Dispose(); throw; }
        var previousLarge = _large;
        var previousSmall = _small;
        _large = large;
        _small = small;
        _dpi = dpi;
        NativeMethods.SendMessage(_window, WmSetIcon, 1, large.DangerousGetHandle());
        NativeMethods.SendMessage(_window, WmSetIcon, 0, small.DangerousGetHandle());
        // Explorer restart recovery must borrow the new handle before the old owner releases it.
        updateTray(small.DangerousGetHandle());
        previousSmall.Dispose();
        previousLarge.Dispose();
    }

    private uint CurrentDpi()
    {
        var dpi = NativeMethods.GetDpiForWindow(_window);
        return dpi == 0 ? 96 : dpi;
    }

    private static IconHandle Load(string path, int width, int height)
    {
        var handle = new IconHandle(LoadImage(0, path, 1, Math.Max(1, width), Math.Max(1, height), 0x0010));
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "The Notchling app icon could not be loaded.");
        }
        return handle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Remove the notification-area entry before disposing this owner; TrayService borrows the small icon.
        if (NativeMethods.IsWindow(_window))
        {
            NativeMethods.SendMessage(_window, WmSetIcon, 0, 0);
            NativeMethods.SendMessage(_window, WmSetIcon, 1, 0);
        }
        _small.Dispose();
        _large.Dispose();
    }

    private sealed class IconHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal IconHandle(nint value) : base(true) { SetHandle(value); }
        protected override bool ReleaseHandle() => DestroyIcon(handle);
    }

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);
}
