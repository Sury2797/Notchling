using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Notch.Windows.Interop;

/// <summary>Native notification-area entry with Explorer-restart recovery and deterministic cleanup.</summary>
public sealed class TrayService : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x32;
    private const uint TrayId = 1;
    private const uint NimAdd = 0;
    private const uint NimDelete = 2;
    private const uint NimSetVersion = 4;
    private const uint NinSelect = 0x0400;
    private const uint NinKeySelect = 0x0401;
    private static long s_nextSubclassId = 1000;
    private readonly nint _handle;
    private readonly Action _open;
    private readonly Action _settings;
    private readonly Action _quit;
    private readonly NativeMethods.SubclassProc _callback;
    private readonly nuint _subclassId;
    private readonly uint _taskbarCreated;
    private readonly nint _icon;
    private bool _iconAdded;
    private bool _version4;
    private bool _disposed;

    public bool IsAvailable => _iconAdded;

    public TrayService(nint hwnd, Action open, Action settings, Action quit)
    {
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            throw new ArgumentException("A live native window handle is required.", nameof(hwnd));
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(quit);
        _handle = hwnd;
        _open = open;
        _settings = settings;
        _quit = quit;
        _callback = WindowProcedure;
        _subclassId = (nuint)Interlocked.Increment(ref s_nextSubclassId);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _icon = NativeMethods.SendMessage(hwnd, NativeMethods.WmGetIcon, 2, 0);
        if (_icon == 0)
            _icon = NativeMethods.SendMessage(hwnd, NativeMethods.WmGetIcon, 0, 0);
        if (_icon == 0)
            _icon = NativeMethods.LoadIcon(0, new nint(32512)); // Shared IDI_APPLICATION fallback; never destroy it.
        if (!NativeMethods.SetWindowSubclass(hwnd, _callback, _subclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the notification-area hook.");
        AddIcon();
    }

    private void AddIcon()
    {
        if (_disposed)
            return;
        var iconData = CreateIconData();
        _iconAdded = ShellNotifyIcon(NimAdd, ref iconData);
        if (_iconAdded)
        {
            iconData.TimeoutOrVersion = 4;
            _version4 = ShellNotifyIcon(NimSetVersion, ref iconData);
        }
    }

    private NotifyIconData CreateIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(),
        Window = _handle,
        Id = TrayId,
        Flags = 0x0001 | 0x0002 | 0x0004 | 0x0080, // NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP
        Callback = CallbackMessage,
        Icon = _icon,
        Tip = "Notch — Open controls",
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == _taskbarCreated && _taskbarCreated != 0)
            {
                _iconAdded = false;
                _version4 = false;
                AddIcon();
            }
            else if (message == CallbackMessage && !_disposed)
            {
                var notification = _version4 ? (uint)((long)lParam & 0xFFFF) : unchecked((uint)lParam);
                if (notification is NinSelect or NinKeySelect || (!_version4 && notification == NativeMethods.WmLeftButtonUp))
                    _open();
                else if (notification == NativeMethods.WmContextMenu || (!_version4 && notification == NativeMethods.WmRightButtonUp))
                    OpenContextMenu();
                return 0;
            }
            else if (message == NativeMethods.WmNcDestroy)
            {
                Dispose();
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Notification-area hook: {exception.Message}");
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void OpenContextMenu()
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
            return;
        uint selection;
        try
        {
            NativeMethods.AppendMenu(menu, 0, 1, "Open Notch");
            NativeMethods.AppendMenu(menu, 0, 2, "Settings");
            NativeMethods.AppendMenu(menu, 0x0800, 0, null);
            NativeMethods.AppendMenu(menu, 0, 3, "Quit");
            NativeMethods.GetCursorPos(out var cursor);
            NativeMethods.SetForegroundWindow(_handle);
            selection = NativeMethods.TrackPopupMenuEx(menu, 0x0002 | 0x0100 | 0x0080,
                cursor.X, cursor.Y, _handle, 0);
            NativeMethods.PostMessage(_handle, NativeMethods.WmNull, 0, 0);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
        switch (selection)
        {
            case 1: _open(); break;
            case 2: _settings(); break;
            case 3: _quit(); break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_iconAdded)
        {
            var iconData = CreateIconData();
            ShellNotifyIcon(NimDelete, ref iconData);
            _iconAdded = false;
        }
        NativeMethods.RemoveWindowSubclass(_handle, _callback, _subclassId);
        GC.KeepAlive(_callback);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id;
        internal uint Flags;
        internal uint Callback;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid ItemGuid;
        internal nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData iconData);
}
