using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Notch.Core;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Notch.Windows.Views;

/// <summary>
/// Crisp local source marks on a 24-unit grid. Provider identity comes from Windows
/// or a clearly labelled track-specific user choice; this control never fetches a favicon.
/// Album/video thumbnails belong to the expanded artwork surface, not this mark.
/// </summary>
public sealed class MediaSourceIcon : UserControl
{
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(MediaSourceIcon), new PropertyMetadata(24d, OnSizeChanged));

    private readonly Grid _drawing = new() { Width = 24, Height = 24 };
    private MediaSnapshot? _media;
    private MediaSourceSelection? _selection;
    private MediaSourceBrand? _renderedBrand;
    private string? _renderedPath;
    private string? _renderedTooltip;
    private long _imageRequest;
    private bool _contrast;
    private bool _themeSubscribed;

    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public MediaSourceIcon()
    {
        Width = Height = 24;
        IsHitTestVisible = false;
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _drawing };
        Loaded += (_, _) =>
        {
            if (!_themeSubscribed) { NativeTheme.Changed += OnThemeChanged; _themeSubscribed = true; }
            Render(force: true);
        };
        Unloaded += (_, _) =>
        {
            if (_themeSubscribed) { NativeTheme.Changed -= OnThemeChanged; _themeSubscribed = false; }
            _imageRequest++;
        };
        Render();
    }

    public void SetSource(MediaSnapshot? media, MediaSourceSelection? selection = null)
    {
        _media = media;
        _selection = selection;
        Render();
    }

    private void OnThemeChanged(object? sender, EventArgs args) => Render(force: true);

    private void Render(bool force = false)
    {
        var identity = MediaSourceIdentity.Resolve(_media, _selection);
        if (_renderedTooltip != identity.Tooltip)
        {
            _renderedTooltip = identity.Tooltip;
            ToolTipService.SetToolTip(this, identity.Tooltip);
        }
        // Known identities get a local vector. Unknown registered applications can
        // use the exact Windows-provided logo cached by WindowsMediaService.
        var path = identity.Brand == MediaSourceBrand.Unknown ? _media?.SourceIconPath : null;
        if (!force && identity.Brand == _renderedBrand && path == _renderedPath) return;
        _renderedBrand = identity.Brand;
        _renderedPath = path;
        _imageRequest++;
        _contrast = NativeTheme.IsHighContrast;
        _drawing.Children.Clear();
        switch (identity.Brand)
        {
            case MediaSourceBrand.YouTube:
                Fill("M5 5 C2 5 1 6 1 9 L1 15 C1 18 2 19 5 19 L19 19 C22 19 23 18 23 15 L23 9 C23 6 22 5 19 5 Z", "#FF0033");
                Fill("M10 8 L16 12 L10 16 Z", "#FFFFFF");
                break;
            case MediaSourceBrand.YouTubeMusic:
                Circle(1, 1, 22, "#FF0033");
                Circle(5, 5, 14, null, "#FFFFFF", 1.2);
                Fill("M10 8 L16 12 L10 16 Z", "#FFFFFF");
                break;
            case MediaSourceBrand.Spotify:
                Circle(1, 1, 22, "#1ED760");
                Stroke("M5.5 8.4 C9.7 6.7 15.3 7.1 18.8 9", "#121212", 1.9);
                Stroke("M6.4 12 C10.1 10.5 14.6 11 17.8 12.7", "#121212", 1.7);
                Stroke("M7.3 15.6 C10.6 14.4 13.6 14.7 16.7 16.1", "#121212", 1.5);
                break;
            case MediaSourceBrand.Chrome:
                Fill("M12 12 L21.5 6.5 C19.5 3 16.1 1 12 1 C7.9 1 4.4 3.2 2.5 6.5 L7.2 14.8 Z", "#EA4335");
                Fill("M12 12 L7.2 14.8 L2.5 6.5 C-0.5 11.7 1.3 18.5 6.5 21.5 C9.9 23.5 14.1 23.5 17.5 21.5 L12 12 Z", "#34A853");
                Fill("M12 12 L17.5 21.5 C20.9 19.5 23 16.1 23 12 C23 10 22.5 8.1 21.5 6.5 L12 6.5 Z", "#FBBC05");
                Circle(6.5, 6.5, 11, "#FFFFFF");
                Circle(8, 8, 8, "#4285F4");
                break;
            case MediaSourceBrand.Edge:
                Fill("M2 14 C0 8 4.5 1 12 1 C19 1 23 6 23 12 C21 8 17 7 14 9 C11 11 13 13 16 13 C19 13 21 12 22 11 C22 17 17.5 20 12 20 C7 20 4 18 2 14 Z", "#0AA9D6");
                Fill("M2 14 C2 10 6 7 10 8 C5 10 5 16 11 17 C15 18 19 16 21 14 C20 20 16 23 11 23 C6 23 2 19 2 14 Z", "#087CD3");
                Fill("M4 7 C7 1 14 0 19 4 C21 6 23 9 22 12 C19 7 14 6 10 8 C7 9 5 12 5 15 C2 13 2 10 4 7 Z", "#28C6A5");
                break;
            case MediaSourceBrand.Firefox:
                Circle(2, 2, 20, "#6750D8");
                Fill("M4 4 L8 6 C10 4 14 4 17 6 L18 1 L21 5 C24 9 23 16 19 20 C15 24 8 23 4 19 C1 16 1 10 4 4 Z", "#FF9419");
                Fill("M8 6 L5 8 L8 12 L13 11 L12 14 L8 16 C11 20 17 19 19 15 C21 12 20 8 18 6 C18 11 15 14 12 12 L9 9 Z", "#FFCA54");
                Fill("M10 10 L15 10 L13 12 L10 12 Z", "#6750D8");
                break;
            case MediaSourceBrand.Vlc:
                Fill("M10 2 L14 2 L20 20 L4 20 Z", "#FF870C");
                Fill("M8.7 6 L15.3 6 L16.3 9 L7.7 9 Z", "#FFFFFF");
                Fill("M6.7 12 L17.3 12 L18.3 15 L5.7 15 Z", "#FFFFFF");
                Fill("M3 19 L21 19 L22 22 L2 22 Z", "#E76E09");
                break;
            case MediaSourceBrand.AppleMusic:
                Fill("M6 1 L18 1 C21.5 1 23 2.5 23 6 L23 18 C23 21.5 21.5 23 18 23 L6 23 C2.5 23 1 21.5 1 18 L1 6 C1 2.5 2.5 1 6 1 Z", "#FA3B58");
                Fill("M10 6 L19 4 L19 16 C19 19 14 20 14 17 C14 15 17 14 17.5 15 L17.5 8 L11.5 9.4 L11.5 18 C11.5 21 6.5 22 6.5 19 C6.5 17 9.5 16 10 17 Z", "#FFFFFF");
                break;
            case MediaSourceBrand.MediaPlayer:
                Circle(1, 1, 22, "#167CDA");
                Fill("M8 5 L20 12 L8 19 Z", "#FFB744");
                Fill("M10 8 L17 12 L10 16 Z", "#FFFFFF");
                break;
            default:
                var fallback = new VisualIcon { Kind = VisualIconKind.Music, Size = 20, Foreground = NativeTheme.Foreground };
                _drawing.Children.Add(fallback);
                if (!_contrast && !string.IsNullOrWhiteSpace(path)) _ = LoadNativeIconAsync(path, _imageRequest);
                break;
        }
    }

    private async Task LoadNativeIconAsync(string path, long request)
    {
        try
        {
            // Only a bounded local logo from WindowsMediaService is eligible; a media
            // title/URL can never become a file name or initiate a remote image request.
            if (!System.IO.Path.IsPathFullyQualified(path) || !File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > 4 * 1024 * 1024) return;
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            using var stream = await file.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var bitmap = new BitmapImage { DecodePixelWidth = 96 };
            await bitmap.SetSourceAsync(stream).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            if (request != _imageRequest || _contrast) return;
            _drawing.Children.Clear();
            _drawing.Children.Add(new Image { Width = 24, Height = 24, Stretch = Stretch.Uniform, Source = bitmap });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or TimeoutException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        { /* The source mark is optional; retain its crisp neutral fallback. */ }
    }

    private Brush Paint(string color) => _contrast
        ? color is "#FFFFFF" or "#121212" ? NativeTheme.Background : NativeTheme.Foreground
        : new SolidColorBrush(Color.FromArgb(255, byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber),
            byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber), byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber)));

    private void Circle(double x, double y, double diameter, string? fill, string? stroke = null, double thickness = 0)
    {
        var shape = new Ellipse { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(x, y, 0, 0), StrokeThickness = thickness };
        if (fill is not null) shape.Fill = Paint(fill);
        if (stroke is not null) shape.Stroke = _contrast ? NativeTheme.Background : Paint(stroke);
        _drawing.Children.Add(shape);
    }

    private void Fill(string data, string color) => _drawing.Children.Add(new Path { Data = Geometry(data), Fill = Paint(color) });
    private void Stroke(string data, string color, double thickness) => _drawing.Children.Add(new Path
    {
        Data = Geometry(data), Stroke = Paint(color), StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
    });

    private static Geometry Geometry(string data)
    {
        var tokens = Regex.Matches(data, @"[MLCQZ]|-?\d+(?:\.\d+)?").Select(match => match.Value).ToArray();
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        var index = 0;
        double Number() => double.Parse(tokens[index++], CultureInfo.InvariantCulture);
        Point Point() => new(Number(), Number());
        while (index < tokens.Length)
        {
            switch (tokens[index++])
            {
                case "M": figure = new PathFigure { StartPoint = Point(), IsFilled = true }; geometry.Figures.Add(figure); break;
                case "L": figure!.Segments.Add(new LineSegment { Point = Point() }); break;
                case "C": figure!.Segments.Add(new BezierSegment { Point1 = Point(), Point2 = Point(), Point3 = Point() }); break;
                case "Q": figure!.Segments.Add(new QuadraticBezierSegment { Point1 = Point(), Point2 = Point() }); break;
                case "Z": figure!.IsClosed = true; break;
                default: throw new InvalidOperationException("Unsupported media mark path command.");
            }
        }
        return geometry;
    }

    private static void OnSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
    {
        var icon = (MediaSourceIcon)sender;
        var size = double.IsFinite(icon.Size) && icon.Size > 0 ? icon.Size : 24;
        icon.Width = icon.Height = size;
    }
}
