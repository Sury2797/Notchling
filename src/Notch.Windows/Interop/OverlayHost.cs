using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace Notch.Windows.Interop;

/// <summary>Positions the WinUI surface at the top of a monitor and clips out its floating-toolbar gaps.</summary>
public sealed class OverlayHost : IDisposable
{
    private const uint ShowFromExistingInstanceMessage = 0x8001; // WM_APP + 1
    private const int HotkeyId = 0x4E01;
    private static long s_nextSubclassId = 100;
    private readonly Window _window;
    private readonly nint _handle;
    private readonly NativeMethods.SubclassProc _callback;
    private readonly nuint _subclassId;
    private bool _disposed;
    private bool _displayChangePending;
    private double _width = 256;
    private double _panelHeight = 40;
    private bool _expanded;
    private bool _pinned;
    private bool _showToolbar = true;
    private int _monitorIndex;

    public event EventHandler? ToggleRequested;
    public event EventHandler? ShowRequested;
    public event EventHandler? DisplayChanged;

    public nint Handle => _handle;
    public bool HotkeyRegistered { get; }
    public double LogicalWidth { get; private set; } = 256;
    public double LogicalPanelHeight { get; private set; } = 40;

    public OverlayHost(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (_handle == 0)
            throw new InvalidOperationException("The WinUI window must have a native handle before attaching the overlay.");

        _callback = WindowProcedure; // Keep the native callback rooted for the complete HWND lifetime.
        _subclassId = (nuint)Interlocked.Increment(ref s_nextSubclassId);
        if (!NativeMethods.SetWindowSubclass(_handle, _callback, _subclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not attach the overlay window hook.");
        _window.Closed += OnWindowClosed;

        var style = NativeMethods.GetWindowLong(_handle, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption | NativeMethods.WsThickFrame | NativeMethods.WsMinimizeBox |
                   NativeMethods.WsMaximizeBox | NativeMethods.WsSysMenu);
        style |= NativeMethods.WsPopup;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GwlStyle, unchecked((nint)style));

        var extendedStyle = NativeMethods.GetWindowLong(_handle, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle = (extendedStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        NativeMethods.SetWindowLong(_handle, NativeMethods.GwlExStyle, unchecked((nint)extendedStyle));
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpFrameChanged);

        // Windows 11's whole-window corners would round the monitor-attached top edge.
        var noSystemCorners = 1; // DWMWCP_DONOTROUND; older Windows safely returns an unsupported-attribute HRESULT.
        _ = NativeMethods.DwmSetWindowAttribute(_handle, 33, ref noSystemCorners, sizeof(int));
        HotkeyRegistered = NativeMethods.RegisterHotKey(_handle, HotkeyId, 0x0002 | 0x0004 | 0x4000, 0x20);
    }

    /// <param name="width">Actual expanded window width in device-independent pixels, including its content margins.</param>
    /// <param name="panelHeight">Expanded body height in device-independent pixels, excluding the detached toolbar.</param>
    /// <param name="monitorIndex">Primary monitor is index zero; remaining monitors sort by their desktop coordinates.</param>
    public void ResizeAndPlace(double width, double panelHeight, bool expanded, bool pinned, int monitorIndex, bool showToolbar = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(width) || !double.IsFinite(panelHeight) || width <= 0 || panelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay dimensions must be finite and positive.");

        _width = width;
        _panelHeight = panelHeight;
        _expanded = expanded;
        _pinned = pinned;
        _monitorIndex = monitorIndex;
        _showToolbar = showToolbar;

        var monitors = EnumerateMonitors();
        if (monitors.Count == 0)
            return;
        var monitor = monitors[Math.Clamp(monitorIndex, 0, monitors.Count - 1)];
        var scale = NativeMethods.GetMonitorDpi(monitor.Handle, _handle) / 96.0;
        var availableWidth = Math.Max(1, monitor.Bounds.Width / scale - 16);
        var availableBodyHeight = Math.Max(1, (monitor.Bounds.Bottom - monitor.Bounds.Top) / scale
            - (expanded && showToolbar ? 80 : 10));
        // On narrow/high-DPI monitors the minimum must fit inside the actual available desktop.
        var logicalWidth = expanded ? Math.Clamp(width, Math.Min(256, availableWidth), availableWidth)
            : Math.Min(256, availableWidth);
        var logicalBodyHeight = expanded
            ? Math.Clamp(panelHeight, Math.Min(40, availableBodyHeight), availableBodyHeight)
            : Math.Min(40, availableBodyHeight);
        var physicalWidth = Pixels(logicalWidth, scale);
        var physicalHeight = Pixels(logicalBodyHeight + (expanded && showToolbar ? 70 : 0), scale);
        var x = monitor.Bounds.Left + (monitor.Bounds.Width - physicalWidth) / 2;

        if (!NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, x, monitor.Bounds.Top,
                physicalWidth, physicalHeight, NativeMethods.SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the overlay.");

        ApplyRegion(logicalWidth, logicalBodyHeight, expanded, showToolbar, scale);
        LogicalWidth = logicalWidth;
        LogicalPanelHeight = logicalBodyHeight;
    }

    public void Hide()
    {
        if (!_disposed)
            NativeMethods.ShowWindow(_handle, NativeMethods.SwHide);
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NativeMethods.ShowWindow(_handle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private static int Pixels(double logicalPixels, double scale) =>
        checked((int)Math.Round(logicalPixels * scale, MidpointRounding.AwayFromZero));

    private void ApplyRegion(double width, double bodyHeight, bool expanded, bool showToolbar, double scale)
    {
        var right = Pixels(width, scale);
        var bottom = Pixels(bodyHeight, scale);
        var radius = Pixels(Math.Min(expanded ? 26 : 20, bodyHeight / 2), scale);
        var region = NativeMethods.CreateRoundRectRgn(0, 0, right, bottom, radius * 2, radius * 2);
        if (region == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the overlay region.");
        try
        {
            UnionRegion(region, NativeMethods.CreateRectRgn(0, 0, right, Math.Max(1, bottom - radius)));
            if (expanded && showToolbar)
            {
                var toolbarWidth = Math.Min(720, width);
                var left = (width - toolbarWidth) / 2;
                var top = bodyHeight + 10;
                var toolbarBottom = Pixels(top + 48, scale);
                var toolbarTop = Pixels(top, scale);
                var diameter = Pixels(48, scale);
                if (toolbarWidth > 112)
                {
                    UnionRegion(region, NativeMethods.CreateRoundRectRgn(Pixels(left, scale), toolbarTop,
                        Pixels(left + toolbarWidth - 112, scale), toolbarBottom, diameter, diameter));
                }
                UnionRegion(region, NativeMethods.CreateRoundRectRgn(
                    Pixels(left + Math.Max(0, toolbarWidth - 96), scale), toolbarTop,
                    Pixels(left + toolbarWidth, scale), toolbarBottom, diameter, diameter));
            }

            if (NativeMethods.SetWindowRgn(_handle, region, true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not apply the overlay region.");
            region = 0; // Successful SetWindowRgn transfers ownership to the system.
        }
        finally
        {
            if (region != 0)
                NativeMethods.DeleteObject(region);
        }
    }

    private static void UnionRegion(nint target, nint addition)
    {
        if (addition == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a toolbar region.");
        try
        {
            if (NativeMethods.CombineRgn(target, target, addition, NativeMethods.RgnOr) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not combine overlay regions.");
        }
        finally
        {
            NativeMethods.DeleteObject(addition);
        }
    }

    private static List<Monitor> EnumerateMonitors()
    {
        var monitors = new List<Monitor>();
        NativeMethods.MonitorEnumProc callback = (nint handle, nint _, ref NativeMethods.Rect bounds, nint __) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
                monitors.Add(new Monitor(handle, info.Monitor, (info.Flags & NativeMethods.MonitorInfoPrimary) != 0));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(0, 0, callback, 0);
        GC.KeepAlive(callback);
        return monitors.OrderByDescending(monitor => monitor.Primary)
            .ThenBy(monitor => monitor.Bounds.Left).ThenBy(monitor => monitor.Bounds.Top).ToList();
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == ShowFromExistingInstanceMessage)
            {
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (_disposed)
                        return;
                    Show();
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                });
                return 0;
            }
            if (message == NativeMethods.WmHotkey && wParam == HotkeyId)
            {
                _window.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_disposed)
                        ToggleRequested?.Invoke(this, EventArgs.Empty);
                });
                return 0;
            }
            if (message is NativeMethods.WmDpiChanged or NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange)
                QueueDisplayChange();
            if (message == NativeMethods.WmNcDestroy)
                Dispose();
        }
        catch (Exception exception)
        {
            // Exceptions must never cross the unmanaged subclass callback boundary.
            Debug.WriteLine($"Overlay window hook: {exception.Message}");
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void QueueDisplayChange()
    {
        if (_disposed || _displayChangePending)
            return;
        _displayChangePending = true;
        if (!_window.DispatcherQueue.TryEnqueue(() =>
            {
                _displayChangePending = false;
                if (_disposed)
                    return;
                try
                {
                ResizeAndPlace(_width, _panelHeight, _expanded, _pinned, _monitorIndex, _showToolbar);
                }
                catch (Win32Exception exception)
                {
                    // Display reconfiguration can briefly invalidate monitor geometry.
                    Debug.WriteLine($"Overlay display update: {exception.Message}");
                }
                DisplayChanged?.Invoke(this, EventArgs.Empty);
            }))
            _displayChangePending = false;
    }

    private void OnWindowClosed(object? sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (HotkeyRegistered)
            NativeMethods.UnregisterHotKey(_handle, HotkeyId);
        NativeMethods.RemoveWindowSubclass(_handle, _callback, _subclassId);
        _window.Closed -= OnWindowClosed;
        GC.KeepAlive(_callback);
    }

    private readonly record struct Monitor(nint Handle, NativeMethods.Rect Bounds, bool Primary);
}
