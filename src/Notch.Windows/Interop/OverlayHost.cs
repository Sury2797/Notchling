using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Notch.Core;
using Notch.Windows.Services;

namespace Notch.Windows.Interop;

public sealed record PowerTransition(bool Suspended, DateTimeOffset At, TimeSpan SuspendedFor);

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
    private readonly DispatcherTimer _displayRetry = new();
    private readonly DispatcherTimer _fullscreenCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private Monitor? _placementMonitor;
    private bool _animationRunning;
    private long _animationStarted;
    private TimeSpan _animationDuration = TimeSpan.FromMilliseconds(180);
    private bool _animateNextLayout;
    private double _animationProgress;
    private double? _placementScale;
    private OverlayLayout? _animationStart;
    private OverlayLayout? _targetLayout;
    private OverlayLayout? _currentLayout;
    private Action? _geometryChanged;
    private int _retryCount;
    private bool _hidden;
    private bool _shown;
    private bool _nativeVisible;
    private bool _fullscreenSuppressed;
    private bool _suppressInFullscreen = true;
    private nint _fullscreenOverride;
    private NativeMethods.Rect _monitorBounds;
    private string? _monitorDeviceId;
    private double _horizontalOffset;
    private double _topOffset;
    private long? _suspendedAt;
    private double _width = 256;
    private double _panelHeight = 40;
    private double _cornerRadius = 20;
    private bool _expanded;
    private bool _pinned;
    private bool _showToolbar = true;
    private double _toolbarWidth = 720;
    private int _monitorIndex;
    private bool _hasLayoutRequest;
    private (int X, int Y, int Width, int Height)? _nativeLayout;
    private RegionGeometry? _nativeRegion;
    private readonly StringBuilder _foregroundClassName = new(128);

    public event EventHandler? ToggleRequested;
    public event EventHandler? ShowRequested;
    public event EventHandler? DisplayChanged;
    public event EventHandler<string>? Error;
    public event EventHandler<PowerTransition>? PowerStateChanged;

    public nint Handle => _handle;
    public bool HotkeyRegistered { get; }
    public double LogicalWidth { get; private set; } = 256;
    public double LogicalPanelHeight { get; private set; } = 40;
    public bool ToolbarVisible { get; private set; }
    public string? ActiveMonitorDeviceId { get; private set; }
    public bool IsHidden => _hidden;
    public bool IsShown => _shown;
    public bool IsVisible => _shown && !_hidden && !_fullscreenSuppressed;
    public bool IsFullscreenSuppressed => _fullscreenSuppressed;
    public bool IsPointerInsideWindow
    {
        get
        {
            if (_disposed || !IsVisible || _currentLayout is not { } layout || _placementScale is not { } scale
                || !NativeMethods.GetCursorPos(out var point)) return false;
            return OverlayGeometry.ContainsInteractionPoint((point.X - (double)layout.X) / scale,
                (point.Y - (double)layout.Y) / scale, layout.Width / scale,
                layout.LogicalBodyHeight, _expanded && layout.ToolbarVisible,
                _toolbarWidth, _cornerRadius, _targetLayout?.LogicalBodyHeight, layout.Height / scale);
        }
    }

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
        _displayRetry.Tick += OnDisplayRetry;
        _fullscreenCheck.Tick += (_, _) => UpdateVisibility();
    }

    /// <param name="width">Actual expanded window width in device-independent pixels, including its content margins.</param>
    /// <param name="panelHeight">Expanded body height in device-independent pixels, excluding the detached toolbar.</param>
    /// <param name="monitorIndex">Primary monitor is index zero; remaining monitors sort by their desktop coordinates.</param>
    public void ResizeAndPlace(double width, double panelHeight, bool expanded, bool pinned, int monitorIndex,
        bool showToolbar = true, string? monitorDeviceId = null, double horizontalOffset = 0,
        double topOffset = 0, bool suppressInFullscreen = true, bool animate = false,
        Action? geometryChanged = null, double toolbarWidth = 720, double? cornerRadius = null,
        bool animationsEnabled = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(width) || !double.IsFinite(panelHeight) || width <= 0 || panelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Overlay dimensions must be finite and positive.");
        if (!double.IsFinite(toolbarWidth) || toolbarWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(toolbarWidth));
        var nextCornerRadius = cornerRadius ?? (expanded ? 26 : 20);
        if (!double.IsFinite(nextCornerRadius) || nextCornerRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(cornerRadius));

        var deviceId = string.IsNullOrWhiteSpace(monitorDeviceId) ? null : monitorDeviceId;
        var nextHorizontalOffset = double.IsFinite(horizontalOffset) ? horizontalOffset : 0;
        var nextTopOffset = double.IsFinite(topOffset) ? Math.Max(0, topOffset) : 0;
        var monitorChanged = !_hasLayoutRequest || _monitorIndex != monitorIndex
            || !StringComparer.OrdinalIgnoreCase.Equals(_monitorDeviceId, deviceId);
        var geometryUnchanged = _hasLayoutRequest && !monitorChanged && _width == width
            && _panelHeight == panelHeight && _expanded == expanded && _showToolbar == showToolbar
            && _toolbarWidth == toolbarWidth
            && _cornerRadius == nextCornerRadius
            && _horizontalOffset == nextHorizontalOffset && _topOffset == nextTopOffset;
        _pinned = pinned;
        _suppressInFullscreen = suppressInFullscreen;
        _geometryChanged = geometryChanged;
        if (geometryUnchanged)
        {
            if (!animationsEnabled && _animationRunning)
            {
                StopResizeAnimation();
                TryPlace(refreshVisibility: false);
            }
            // Preferences such as accent, billing or a text edit do not change HWND geometry.
            // Preserve a permitted resize animation and avoid repeated monitor/GDI work.
            UpdateVisibility();
            NotifyGeometryChanged();
            return;
        }

        _width = width;
        _panelHeight = panelHeight;
        _cornerRadius = nextCornerRadius;
        _expanded = expanded;
        _monitorIndex = monitorIndex;
        _showToolbar = showToolbar;
        _toolbarWidth = toolbarWidth;

        _monitorDeviceId = deviceId;
        _horizontalOffset = nextHorizontalOffset;
        _topOffset = nextTopOffset;
        _hasLayoutRequest = true;
        StopResizeAnimation();
        if (monitorChanged) { _placementMonitor = null; _placementScale = null; }
        _targetLayout = null;
        _animateNextLayout = animate && animationsEnabled && IsVisible && !monitorChanged;
        TryPlace();
        // XAML takes its final layout once. Rendering frames only update the native
        // boundary; no frame callback rebuilds controls or reassigns XAML row sizes.
        NotifyGeometryChanged();
        if (_animationRunning) CompositionTarget.Rendering += OnResizeAnimation;
    }

    private bool TryPlace(bool refreshVisibility = true)
    {
        try
        {
            PlaceCore();
            _displayRetry.Stop();
            _retryCount = 0;
            if (refreshVisibility) UpdateVisibility();
            return true;
        }
        catch (Win32Exception exception)
        {
            StopResizeAnimation();
            // Keep the previous usable geometry while the display driver changes topology.
            Debug.WriteLine($"Overlay display update: {exception.Message}");
            StartupDiagnostics.Write("OverlayHost.Display", exception);
            if (_retryCount < 3)
            {
                _displayRetry.Interval = TimeSpan.FromMilliseconds(200 * (1 << _retryCount++));
                _displayRetry.Start();
            }
            else
                ReportError($"The display layout is temporarily unavailable. Reconnect the display or reopen {ProductIdentity.DisplayName} to retry.");
            return false;
        }
    }

    private void PlaceCore()
    {
        if (_placementMonitor is null)
        {
            var monitors = EnumerateMonitors();
            if (monitors.Count == 0)
                throw new Win32Exception("No usable monitor is currently available.");
            // A saved device identity survives coordinate reordering. If disconnected, prefer primary.
            _placementMonitor = _monitorDeviceId is not null
                ? monitors.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.DeviceName, _monitorDeviceId)) ?? monitors[0]
                : monitors[Math.Clamp(_monitorIndex, 0, monitors.Count - 1)];
        }
        var monitor = _placementMonitor;
        var scale = _placementScale ??= NativeMethods.GetMonitorDpi(monitor.Handle, _handle) / 96.0;
        var workArea = monitor.WorkArea;
        if (workArea.Width <= 0 || workArea.Bottom <= workArea.Top)
            throw new Win32Exception("The selected monitor has no usable work area.");
        if (_targetLayout is null)
        {
            _targetLayout = OverlayGeometry.Calculate(_width, _panelHeight, _expanded,
                _expanded && _showToolbar, workArea.Left, workArea.Top, workArea.Width,
                workArea.Bottom - workArea.Top, scale, _horizontalOffset, _topOffset);
            var target = _targetLayout;
            LogicalWidth = target.LogicalWidth;
            LogicalPanelHeight = target.LogicalBodyHeight;
            ToolbarVisible = target.ToolbarVisible;
            if (_animateNextLayout && _currentLayout is { } previous && previous != target)
            {
                _animationStart = previous;
                _animationProgress = 0;
                _animationDuration = OverlayGeometry.TransitionDuration(previous, target, scale);
                _animationStarted = Stopwatch.GetTimestamp();
                _animationRunning = _animationDuration > TimeSpan.Zero;
            }
            _animateNextLayout = false;
        }
        var layout = _animationRunning && _animationStart is { } start
            ? OverlayGeometry.Interpolate(start, _targetLayout, _animationProgress) : _targetLayout;
        var nativeLayout = (layout.X, layout.Y, layout.Width, layout.Height);
        if (_nativeLayout != nativeLayout)
        {
            if (!NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, layout.X, layout.Y,
                    layout.Width, layout.Height, NativeMethods.SwpNoActivate))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not position the overlay.");
            _nativeLayout = nativeLayout;
        }

        ApplyRegion(layout.Width / scale, layout.LogicalBodyHeight, layout.Height,
            _expanded && layout.ToolbarVisible, scale, _targetLayout.LogicalBodyHeight, _targetLayout.LogicalWidth);
        _currentLayout = layout;
        ActiveMonitorDeviceId = monitor.DeviceName;
        _monitorBounds = monitor.Bounds;
    }

    private void OnResizeAnimation(object? sender, object args)
    {
        if (_disposed || !_animationRunning) { StopResizeAnimation(); return; }
        _animationProgress = Math.Clamp(Stopwatch.GetElapsedTime(_animationStarted).TotalMilliseconds
            / _animationDuration.TotalMilliseconds, 0, 1);
        if (_animationProgress >= 1) StopResizeAnimation();
        TryPlace(refreshVisibility: false);
    }

    private void StopResizeAnimation()
    {
        if (_animationRunning) CompositionTarget.Rendering -= OnResizeAnimation;
        _animationRunning = false;
        _animateNextLayout = false;
    }

    private void NotifyGeometryChanged()
    {
        try { _geometryChanged?.Invoke(); }
        catch (Exception error)
        {
            StopResizeAnimation();
            ReportError($"The {ProductIdentity.DisplayName} transition could not be refreshed: {error.Message}");
        }
    }

    public void Hide()
    {
        if (!_disposed)
        {
            _hidden = true;
            StopResizeAnimation();
            _fullscreenCheck.Stop();
            NativeMethods.ShowWindow(_handle, NativeMethods.SwHide);
            _nativeVisible = false;
            if (_targetLayout is not null && _currentLayout != _targetLayout)
                TryPlace(refreshVisibility: false);
        }
    }

    public void Show(bool userRequested = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shown = true;
        if (userRequested)
        {
            _hidden = false;
            _fullscreenOverride = NativeMethods.GetForegroundWindow();
            if (_retryCount >= 3 || _targetLayout is not null && _currentLayout != _targetLayout)
            { _retryCount = 0; TryPlace(); }
        }
        if (!_hidden) _fullscreenCheck.Start();
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (_disposed) return;
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != _fullscreenOverride && foreground != _handle) _fullscreenOverride = 0;
        _fullscreenSuppressed = _suppressInFullscreen && foreground != _fullscreenOverride && IsForegroundFullscreen(foreground);
        if (IsVisible == _nativeVisible) return;
        if (!IsVisible)
        {
            // Hidden WinUI surfaces need not receive further Rendering callbacks.
            // Release the static subscription and finish geometry while hidden so
            // fullscreen suppression cannot retain a half-open transition forever.
            StopResizeAnimation();
            NativeMethods.ShowWindow(_handle, NativeMethods.SwHide);
            _nativeVisible = false;
            if (_targetLayout is not null && _currentLayout != _targetLayout)
                TryPlace(refreshVisibility: false);
            return;
        }
        NativeMethods.ShowWindow(_handle, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _nativeVisible = true;
    }

    private bool IsForegroundFullscreen(nint foreground)
    {
        if (foreground == 0 || foreground == _handle || NativeMethods.IsIconic(foreground)
            || NativeMethods.IsZoomed(foreground) || _monitorBounds.Width <= 0)
            return false;
        _foregroundClassName.Clear();
        NativeMethods.GetClassName(foreground, _foregroundClassName, _foregroundClassName.Capacity);
        if (_foregroundClassName.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        // A normal maximized window can cover full bounds with an auto-hidden taskbar.
        // Captioned applications are not presentation/fullscreen surfaces.
        if ((NativeMethods.GetWindowLong(foreground, NativeMethods.GwlStyle).ToInt64() & NativeMethods.WsCaption) == NativeMethods.WsCaption)
            return false;
        return NativeMethods.GetWindowRect(foreground, out var bounds)
            && bounds.Left <= _monitorBounds.Left && bounds.Top <= _monitorBounds.Top
            && bounds.Right >= _monitorBounds.Right && bounds.Bottom >= _monitorBounds.Bottom;
    }

    private void ReportError(string message)
    {
        try { Error?.Invoke(this, message); }
        catch (Exception error) { Debug.WriteLine($"Overlay error notification: {error.Message}"); }
    }

    private static int Pixels(double logicalPixels, double scale) =>
        checked((int)Math.Round(logicalPixels * scale, MidpointRounding.AwayFromZero));

    private void ApplyRegion(double width, double bodyHeight, int physicalWindowHeight, bool showToolbar, double scale,
        double toolbarBodyHeight, double targetWidth)
    {
        var right = Pixels(width, scale);
        var bottom = Pixels(bodyHeight, scale);
        var radius = Pixels(Math.Clamp(_cornerRadius, 0, Math.Min(width, bodyHeight) / 2), scale);
        // Keep the dock region at the same final coordinates as its stable XAML
        // layout; an animated body must not create empty capsules travelling below it.
        var toolbarWidth = Math.Min(_toolbarWidth, targetWidth);
        var toolbarLeft = (width - toolbarWidth) / 2;
        var toolbarTop = Pixels(toolbarBodyHeight + 10, scale);
        var toolbarBottom = Pixels(toolbarBodyHeight + 58, scale);
        var diameter = Pixels(48, scale);
        // A dock below the current HWND cannot be visible. Avoid allocating and
        // combining its two GDI regions until an opening reaches that boundary.
        var hasToolbar = showToolbar && physicalWindowHeight > toolbarTop;
        var geometry = new RegionGeometry(right, bottom, radius, hasToolbar,
            Pixels(toolbarLeft, scale), Pixels(toolbarLeft + toolbarWidth - 112, scale),
            Pixels(toolbarLeft + Math.Max(0, toolbarWidth - 96), scale), Pixels(toolbarLeft + toolbarWidth, scale),
            toolbarTop, toolbarBottom, diameter);
        if (_nativeRegion == geometry) return;
        var region = NativeMethods.CreateRoundRectRgn(0, 0, right, bottom, radius * 2, radius * 2);
        if (region == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the overlay region.");
        try
        {
            UnionRegion(region, NativeMethods.CreateRectRgn(0, 0, right, Math.Max(1, bottom - radius)));
            if (hasToolbar)
            {
                if (toolbarWidth > 112)
                {
                    UnionRegion(region, NativeMethods.CreateRoundRectRgn(geometry.ToolbarLeft, toolbarTop,
                        geometry.NavigationRight, toolbarBottom, diameter, diameter));
                }
                UnionRegion(region, NativeMethods.CreateRoundRectRgn(
                    geometry.ControlsLeft, toolbarTop, geometry.ToolbarRight, toolbarBottom, diameter, diameter));
            }

            if (NativeMethods.SetWindowRgn(_handle, region, true) == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not apply the overlay region.");
            region = 0; // Successful SetWindowRgn transfers ownership to the system.
            _nativeRegion = geometry;
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
            var info = new NativeMethods.MonitorInfoEx { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(), DeviceName = string.Empty };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
                monitors.Add(new Monitor(handle, info.Monitor, info.WorkArea, NativeMethods.GetMonitorDeviceId(info.DeviceName), (info.Flags & NativeMethods.MonitorInfoPrimary) != 0));
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
                QueueUiAction(() =>
                {
                    Show();
                    ShowRequested?.Invoke(this, EventArgs.Empty);
                }, "Opening " + ProductIdentity.DisplayName);
                return 0;
            }
            if (message == NativeMethods.WmHotkey && wParam == HotkeyId)
            {
                QueueUiAction(() =>
                {
                    ToggleRequested?.Invoke(this, EventArgs.Empty);
                }, "Opening " + ProductIdentity.DisplayName + " from the keyboard");
                return 0;
            }
            if (message is NativeMethods.WmDpiChanged or NativeMethods.WmDisplayChange or NativeMethods.WmSettingChange)
                QueueDisplayChange();
            if (message == NativeMethods.WmPowerBroadcast)
                QueuePowerTransition((uint)wParam);
            if (message == NativeMethods.WmNcDestroy)
                Dispose();
        }
        catch (Exception exception)
        {
            // Exceptions must never cross the unmanaged subclass callback boundary.
            Debug.WriteLine($"Overlay window hook: {exception.Message}");
            StartupDiagnostics.Write("OverlayHost.WindowProcedure", exception);
        }
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

    private void QueueUiAction(Action action, string context) => _window.DispatcherQueue.TryEnqueue(() =>
    {
        if (_disposed) return;
        try { action(); }
        catch (Exception error) { ReportError($"{context} failed: {error.Message}"); }
    });

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
                StopResizeAnimation();
                _placementMonitor = null;
                _placementScale = null;
                _targetLayout = null;
                _nativeLayout = null;
                _nativeRegion = null;
                _retryCount = 0;
                if (TryPlace()) PublishDisplayChanged();
            }))
            _displayChangePending = false;
    }

    private void OnDisplayRetry(object? sender, object args)
    {
        _displayRetry.Stop();
        _placementMonitor = null;
        _placementScale = null;
        _targetLayout = null;
        if (!_disposed && TryPlace()) PublishDisplayChanged();
    }

    private void PublishDisplayChanged()
    {
        try { DisplayChanged?.Invoke(this, EventArgs.Empty); }
        catch (Exception error) { ReportError($"The display layout could not be refreshed: {error.Message}"); }
    }

    private void QueuePowerTransition(uint state)
    {
        PowerTransition? transition = null;
        if (state == NativeMethods.PowerSuspend && _suspendedAt is null)
        {
            _suspendedAt = Stopwatch.GetTimestamp();
            transition = new(true, DateTimeOffset.UtcNow, TimeSpan.Zero);
        }
        else if (state is NativeMethods.PowerResumeAutomatic or NativeMethods.PowerResumeSuspend && _suspendedAt is { } started)
        {
            _suspendedAt = null;
            transition = new(false, DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(started));
        }
        if (transition is null) return;
        void Publish()
        {
            if (_disposed) return;
            try { PowerStateChanged?.Invoke(this, transition); }
            catch (Exception error) { ReportError($"Power-state recovery failed: {error.Message}"); }
            if (!transition.Suspended) QueueDisplayChange();
        }
        // WM_POWERBROADCAST already runs on the window thread. Pause the timers before
        // returning to Windows, rather than leaving a suspend callback queued until resume.
        if (_window.DispatcherQueue.HasThreadAccess) Publish();
        else _window.DispatcherQueue.TryEnqueue(Publish);
    }

    private void OnWindowClosed(object? sender, WindowEventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _displayRetry.Stop();
        _fullscreenCheck.Stop();
        StopResizeAnimation();
        _displayRetry.Tick -= OnDisplayRetry;
        if (HotkeyRegistered)
            NativeMethods.UnregisterHotKey(_handle, HotkeyId);
        NativeMethods.RemoveWindowSubclass(_handle, _callback, _subclassId);
        _window.Closed -= OnWindowClosed;
        GC.KeepAlive(_callback);
    }

    private sealed record Monitor(nint Handle, NativeMethods.Rect Bounds, NativeMethods.Rect WorkArea, string DeviceName, bool Primary);
    private readonly record struct RegionGeometry(int Right, int Bottom, int Radius, bool HasToolbar,
        int ToolbarLeft, int NavigationRight, int ControlsLeft, int ToolbarRight,
        int ToolbarTop, int ToolbarBottom, int ToolbarDiameter);
}
