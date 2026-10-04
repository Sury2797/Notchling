using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Notch.Windows.Services;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Notch.Windows.Views;

/// <summary>
/// Mutable palette brushes for controls created in code. Windows contrast/color
/// changes update existing controls as well as subsequent renders. Use on the UI thread.
/// </summary>
internal static class NativeTheme
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SurfaceColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#050505", "#141414", "#191919", "#1B1B1B", "#202020", "#242424", "#252525", "#102218", "#292422", "Black"
    };
    private static readonly AccessibilitySettings? Accessibility = TryCreate(() => new AccessibilitySettings());
    private static readonly UISettings? Settings = TryCreate(() => new UISettings());
    private static DispatcherQueue? _dispatcher;
    private static bool _subscribed;

    public static event EventHandler? Changed;
    public static bool IsHighContrast
    {
        get
        {
            if (Accessibility is not null)
            {
                try { return Accessibility.HighContrast; }
                catch (Exception error) when (Unsupported(error)) { }
            }
            var state = new HighContrastState { Size = (uint)Marshal.SizeOf<HighContrastState>() };
            return SystemParametersInfo(0x0042, state.Size, ref state, 0) && (state.Flags & 1) != 0;
        }
    }
    public static double TextScaleFactor
    {
        get
        {
            if (Settings is not null)
            {
                try
                {
                    var factor = Settings.TextScaleFactor;
                    if (double.IsFinite(factor) && factor > 0) return factor;
                }
                catch (Exception error) when (Unsupported(error)) { }
            }
            try
            {
                using var preferences = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
                if (preferences?.GetValue("TextScaleFactor") is int percentage)
                    return Math.Clamp(percentage, 100, 225) / 100d;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException) { }
            return 1;
        }
    }
    public static SolidColorBrush Foreground => Brush("#FFFFFF");
    public static SolidColorBrush Muted => Brush("#919191");
    public static SolidColorBrush Background => Brush("#050505");
    public static SolidColorBrush Card => Brush("#141414");
    public static SolidColorBrush Border => Brush("#282828");
    public static SolidColorBrush SelectedBackground => Foreground;
    public static SolidColorBrush SelectedForeground => Background;

    public static void Initialize(DispatcherQueue dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        if (_subscribed) return;
        if (Accessibility is not null)
            TrySubscribe("HighContrastChanged", () => Accessibility.HighContrastChanged += (_, _) => RequestUpdate());
        if (Settings is not null)
        {
            TrySubscribe("ColorValuesChanged", () => Settings.ColorValuesChanged += (_, _) => RequestUpdate());
            TrySubscribe("TextScaleFactorChanged", () => Settings.TextScaleFactorChanged += (_, _) => RequestUpdate());
        }
        _subscribed = true;
        Update();
    }

    // The host also calls this for WM_SETTINGCHANGE, covering desktops without WinRT notifications.
    public static void Refresh() => RequestUpdate();

    private static T? TryCreate<T>(Func<T> create) where T : class
    {
        try { return create(); }
        catch (Exception error) when (Unsupported(error))
        {
            StartupDiagnostics.Write("Optional Windows theme API unavailable: " + typeof(T).Name, error);
            return null;
        }
    }

    private static void TrySubscribe(string notification, Action subscribe)
    {
        try { subscribe(); }
        catch (Exception error) when (Unsupported(error))
        {
            StartupDiagnostics.Write("Optional Windows theme notification unavailable: " + notification, error);
        }
    }

    private static bool Unsupported(Exception error) => error is COMException or PlatformNotSupportedException;

    public static SolidColorBrush Brush(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        if (!Brushes.TryGetValue(color, out var brush))
            Brushes[color] = brush = new SolidColorBrush(Resolve(color));
        return brush;
    }

    private static void RequestUpdate()
    {
        if (_dispatcher is not { } dispatcher) return;
        if (dispatcher.HasThreadAccess) Update();
        else dispatcher.TryEnqueue(Update);
    }

    private static void Update()
    {
        foreach (var (color, brush) in Brushes) brush.Color = Resolve(color);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Color Resolve(string value)
    {
        var original = Parse(value);
        if (!IsHighContrast || original.A == 0) return original;
        // Preserve an inverse text/background pair for selected tabs and dates.
        // Saturated chart/status colors use the system link color, while dark
        // surfaces and neutral outlines use the user's window and text colors.
        if (SurfaceColors.Contains(value)) return SystemColor(UIElementType.Window, 5);
        var difference = Math.Max(original.R, Math.Max(original.G, original.B)) - Math.Min(original.R, Math.Min(original.G, original.B));
        if (difference > 24) return SystemColor(UIElementType.Hotlight, 26);
        return SystemColor(UIElementType.WindowText, 8);
    }

    private static Color SystemColor(UIElementType element, int win32Color)
    {
        if (Settings is not null)
        {
            try { return Settings.UIElementColor(element); }
            catch (Exception error) when (Unsupported(error)) { }
        }
        var color = GetSysColor(win32Color);
        return Microsoft.UI.ColorHelper.FromArgb(255, (byte)color, (byte)(color >> 8), (byte)(color >> 16));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastState
    {
        internal uint Size;
        internal uint Flags;
        internal nint DefaultScheme;
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, ref HighContrastState state, uint flags);
    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int index);

    private static Color Parse(string value)
    {
        if (value.Equals("Transparent", StringComparison.OrdinalIgnoreCase)) return Microsoft.UI.Colors.Transparent;
        if (value.Equals("White", StringComparison.OrdinalIgnoreCase)) return Microsoft.UI.Colors.White;
        if (value.Equals("Black", StringComparison.OrdinalIgnoreCase)) return Microsoft.UI.Colors.Black;
        var hex = value.TrimStart('#');
        if (hex.Length is not (6 or 8)) throw new ArgumentException("A palette color must contain six or eight hexadecimal digits.", nameof(value));
        var offset = hex.Length == 8 ? 2 : 0;
        var alpha = offset == 0 ? (byte)255 : byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Microsoft.UI.ColorHelper.FromArgb(alpha,
            byte.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.AsSpan(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }
}

/// <summary>Adapts density to the actual viewport and the user's Windows text size.</summary>
public sealed class TextAwareWidthTrigger : StateTriggerBase
{
    public static readonly DependencyProperty MinimumWidthProperty = DependencyProperty.Register(
        nameof(MinimumWidth), typeof(double), typeof(TextAwareWidthTrigger), new PropertyMetadata(0d, OnSizeChanged));
    public static readonly DependencyProperty AvailableWidthProperty = DependencyProperty.Register(
        nameof(AvailableWidth), typeof(double), typeof(TextAwareWidthTrigger), new PropertyMetadata(0d, OnSizeChanged));

    public double MinimumWidth { get => (double)GetValue(MinimumWidthProperty); set => SetValue(MinimumWidthProperty, value); }
    public double AvailableWidth { get => (double)GetValue(AvailableWidthProperty); set => SetValue(AvailableWidthProperty, value); }

    public TextAwareWidthTrigger()
    {
        NativeTheme.Changed += (_, _) => Update();
        Update();
    }

    private static void OnSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((TextAwareWidthTrigger)sender).Update();
    private void Update() => SetActive(AvailableWidth >= MinimumWidth * Math.Max(1, NativeTheme.TextScaleFactor));
}
