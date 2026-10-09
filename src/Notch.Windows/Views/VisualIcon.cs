using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Notch.Core;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace Notch.Windows.Views;

/// <summary>Product symbols with a shared optical grid, independent of installed icon fonts.</summary>
public enum VisualIconKind
{
    Home, Music, Focus, Calendar, Weather, Sun, Cloud, Rain, Snow, Storm, Fog,
    Shelf, Clipboard, Servers, System, ScreenTime, Notes, Scratchpad, Files,
    Links, Emoji, Sounds, Convert, Awake, Settings, Tools, Revenue, Analytics,
    Coding, ChevronLeft, ChevronRight, ChevronDown, Refresh, Play, Pause, Previous, Next,
    Volume, Equalizer, Droplet, CheckCircle, Close, Pin, Notification, Download,
    Check, Info, Warning,
}

/// <summary>
/// A small native vector, rather than a platform font glyph. Immutable path tokens
/// are cached; each control owns its WinUI geometry. Foreground remains bound so
/// selected, disabled and contrast
/// states use the host control's current brush. Icons never receive input or a
/// separate accessibility stop; the containing control supplies the action name.
/// </summary>
public sealed class VisualIcon : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(VisualIconKind), typeof(VisualIcon), new PropertyMetadata(VisualIconKind.Home, OnKindChanged));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(VisualIcon), new PropertyMetadata(20d, OnSizeChanged));

    private readonly Path _path;
    private static readonly Dictionary<VisualIconKind, string[]> TokenCache = new();

    // Every path has an explicit command and stays within the 24-unit optical grid.
    // Rounded terminals and joins remain consistent from compact controls to cards.
    private static readonly IReadOnlyDictionary<VisualIconKind, string> Paths = new Dictionary<VisualIconKind, string>
    {
        [VisualIconKind.Home] = "M3 10.5 L12 3 L21 10.5 M5.5 9 L5.5 21 L10 21 L10 14 L14 14 L14 21 L18.5 21 L18.5 9",
        [VisualIconKind.Music] = "M10 17 L10 5 L20 3 L20 15 M10 8 L20 6 M10 17 C10 19 8 20 6 20 C4 20 3 19 3 17.5 C3 16 5 15 7 15 C9 15 10 16 10 17 M20 15 C20 17 18 18 16 18 C14 18 13 17 13 15.5 C13 14 15 13 17 13 C19 13 20 14 20 15",
        [VisualIconKind.Focus] = "M10 2 L14 2 M12 2 L12 5 M18 5 L20 3 M20 13 A8 8 0 1 1 4 13 A8 8 0 1 1 20 13 M12 8 L12 13 L15 15",
        [VisualIconKind.Calendar] = "M5 5 L19 5 Q21 5 21 7 L21 19 Q21 21 19 21 L5 21 Q3 21 3 19 L3 7 Q3 5 5 5 Z M7 3 L7 7 M17 3 L17 7 M3 10 L21 10 M7 14 L8 14 M12 14 L13 14 M17 14 L18 14 M7 18 L8 18 M12 18 L13 18",
        [VisualIconKind.Weather] = "M7 12 C5 12 3 14 3 16 C3 18.5 5 20 7.5 20 L18 20 C20 20 22 18.5 22 16.5 C22 14.5 20 13 18 13 C17.5 10 15 8.5 12.5 9 C10 9.5 8.5 11 8.5 13 M4.5 8 A3.5 3.5 0 0 1 10.5 5.5 M7 2 L7 3 M2 7 L3 7 M3.5 3.5 L4.3 4.3 M11 3 L10.3 3.7",
        [VisualIconKind.Sun] = "M17 12 A5 5 0 1 1 7 12 A5 5 0 1 1 17 12 M12 2 L12 4 M12 20 L12 22 M2 12 L4 12 M20 12 L22 12 M4.9 4.9 L6.3 6.3 M17.7 17.7 L19.1 19.1 M4.9 19.1 L6.3 17.7 M17.7 6.3 L19.1 4.9",
        [VisualIconKind.Cloud] = "M7 9 C4 9 2 11.5 2 14 C2 17 4.5 19 7.5 19 L18 19 C20.5 19 22 17 22 14.5 C22 12 20 10 17.5 10 C17 6.5 14 4.5 11 5 C8 5.5 6.5 8 7 11",
        [VisualIconKind.Rain] = "M6 7 C3.5 7 2 9 2 11.5 C2 14 4 15 6 15 L18 15 C20.5 15 22 13.5 22 11 C22 9 20 7 17.5 8 C17 5 14.5 3.5 12 4 C9 4.5 7.5 6.5 8 9 M7 18 L6 21 M12 18 L11 21 M17 18 L16 21",
        [VisualIconKind.Snow] = "M6 7 C3.5 7 2 9 2 11.5 C2 14 4 15 6 15 L18 15 C20.5 15 22 13.5 22 11 C22 9 20 7 17.5 8 C17 5 14.5 3.5 12 4 C9 4.5 7.5 6.5 8 9 M8 18 L8 22 M6.3 19 L9.7 21 M6.3 21 L9.7 19 M17 18 L17 22 M15.3 19 L18.7 21 M15.3 21 L18.7 19",
        [VisualIconKind.Storm] = "M6 7 C3.5 7 2 9 2 11.5 C2 14 4 15 6 15 L7 15 M17 15 L18 15 C20.5 15 22 13.5 22 11 C22 9 20 7 17.5 8 C17 5 14.5 3.5 12 4 C9 4.5 7.5 6.5 8 9 M13 11 L9 17 L13 17 L11 22 L17 15 L13 15 L15 11 Z",
        [VisualIconKind.Fog] = "M5 9 C4 7 6 4 9 5 C10 2.5 14 2.5 15.5 5.5 C18.5 4.5 21 6.5 20 9 M3 12 L21 12 M6 16 L18 16 M3 20 L21 20",
        [VisualIconKind.Shelf] = "M3 4 L21 4 L21 9 L3 9 Z M5 9 L5 21 L19 21 L19 9 M9 13 L15 13 M9 17 L12 17",
        [VisualIconKind.Clipboard] = "M9 4 L7 4 Q5 4 5 6 L5 20 Q5 22 7 22 L18 22 Q20 22 20 20 L20 6 Q20 4 18 4 L16 4 M10 2 L15 2 L16 6 L9 6 Z M9 11 L16 11 M9 15 L16 15 M9 19 L13 19",
        [VisualIconKind.Servers] = "M5 3 L19 3 L19 8 L5 8 Z M5 10 L19 10 L19 15 L5 15 Z M5 17 L19 17 L19 22 L5 22 Z M8 5.5 L8.1 5.5 M8 12.5 L8.1 12.5 M8 19.5 L8.1 19.5 M13 5.5 L16 5.5 M13 12.5 L16 12.5 M13 19.5 L16 19.5",
        [VisualIconKind.System] = "M6 6 L18 6 L18 18 L6 18 Z M9 9 L15 9 L15 15 L9 15 Z M9 2 L9 6 M15 2 L15 6 M9 18 L9 22 M15 18 L15 22 M2 9 L6 9 M2 15 L6 15 M18 9 L22 9 M18 15 L22 15",
        [VisualIconKind.ScreenTime] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M12 6 L12 12 L16 14",
        [VisualIconKind.Notes] = "M5 3 L19 3 Q21 3 21 5 L21 15 L15 21 L5 21 Q3 21 3 19 L3 5 Q3 3 5 3 Z M15 21 L15 15 L21 15 M7 8 L17 8 M7 12 L13 12",
        [VisualIconKind.Scratchpad] = "M4 17 L4 21 L8 21 L20 9 L16 5 Z M14 7 L18 11 M16 5 L18 3 Q19 2 20 3 L21 4 Q22 5 21 6 L20 9 M4 17 L8 21",
        [VisualIconKind.Files] = "M3 6 L9 6 L11 8 L21 8 L21 20 L3 20 Z M3 6 L3 4 L10 4 L12 6 L18 6",
        [VisualIconKind.Links] = "M10 7 L12 5 C14 3 17 3 19 5 C21 7 21 10 19 12 L16 15 C14 17 11 17 9 15 M14 17 L12 19 C10 21 7 21 5 19 C3 17 3 14 5 12 L8 9 C10 7 13 7 15 9 M9 15 L15 9",
        [VisualIconKind.Emoji] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M8 9 L8.1 9 M16 9 L16.1 9 M7.5 14 C9 18 15 18 16.5 14",
        [VisualIconKind.Sounds] = "M3 10 L3 14 M7.5 6 L7.5 18 M12 3 L12 21 M16.5 7 L16.5 17 M21 10 L21 14",
        [VisualIconKind.Convert] = "M4 7 L20 7 M16 3 L20 7 L16 11 M20 17 L4 17 M8 13 L4 17 L8 21",
        [VisualIconKind.Awake] = "M3 5 L17 5 L17 17 Q17 21 13 21 L7 21 Q3 21 3 17 Z M17 7 L19 7 C23 7 23 13 19 13 L17 13 M7 2 L7 3 M12 2 L12 3",
        [VisualIconKind.Settings] = "M9 3 L15 3 L16 6 L19 7 L22 10 L20 13 L20 16 L17 19 L14 18 L11 21 L8 20 L7 17 L4 16 L2 13 L4 10 L4 7 L7 5 Z M16 12 A4 4 0 1 1 8 12 A4 4 0 1 1 16 12",
        [VisualIconKind.Tools] = "M3 3 L10 3 L10 10 L3 10 Z M14 3 L21 3 L21 10 L14 10 Z M3 14 L10 14 L10 21 L3 21 Z M14 14 L21 14 L21 21 L14 21 Z",
        [VisualIconKind.Revenue] = "M4 5 L20 5 Q22 5 22 7 L22 17 Q22 19 20 19 L4 19 Q2 19 2 17 L2 7 Q2 5 4 5 Z M2 10 L22 10 M6 15 L9 15 M17 15 L18 15",
        [VisualIconKind.Analytics] = "M3 3 L3 21 L21 21 M7 17 L7 12 M12 17 L12 8 M17 17 L17 4 M5 9 L10 5 L14 7 L21 2",
        [VisualIconKind.Coding] = "M8 6 L2 12 L8 18 M16 6 L22 12 L16 18 M14 3 L10 21",
        [VisualIconKind.ChevronLeft] = "M15 5 L8 12 L15 19",
        [VisualIconKind.ChevronRight] = "M9 5 L16 12 L9 19",
        [VisualIconKind.ChevronDown] = "M5 9 L12 16 L19 9",
        [VisualIconKind.Refresh] = "M20 9 C18 3 9 2 5 7 C1 12 4 20 10 21 C14 22 18 20 20 17 M20 3 L20 9 L14 9",
        [VisualIconKind.Play] = "M8 4 L20 12 L8 20 Z",
        [VisualIconKind.Pause] = "M6 4 L9 4 L9 20 L6 20 Z M15 4 L18 4 L18 20 L15 20 Z",
        [VisualIconKind.Previous] = "M5 4 L5 20 M19 4 L8 12 L19 20 Z",
        [VisualIconKind.Next] = "M19 4 L19 20 M5 4 L16 12 L5 20 Z",
        [VisualIconKind.Volume] = "M3 9 L7 9 L12 5 L12 19 L7 15 L3 15 Z M16 8 C19 10 19 14 16 16 M19 5 C24 9 24 15 19 19",
        [VisualIconKind.Equalizer] = "M4 8 L4 16 M9.5 3 L9.5 21 M15 6 L15 18 M20 9 L20 15",
        [VisualIconKind.Droplet] = "M12 2 C10 6 5 10 5 15 C5 19 8 22 12 22 C16 22 19 19 19 15 C19 10 14 6 12 2 Z M8 15 C8 17 9.5 18.5 11 18.5",
        [VisualIconKind.CheckCircle] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M8 12 L11 15 L16 9",
        [VisualIconKind.Close] = "M6 6 L18 18 M18 6 L6 18",
        [VisualIconKind.Pin] = "M8 3 L16 3 M9 3 L9 8 L6 13 L6 15 L18 15 L18 13 L15 8 L15 3 M12 15 L12 22",
        [VisualIconKind.Notification] = "M5 16 L7 13 L7 9 C7 2 17 2 17 9 L17 13 L19 16 L5 16 Z M10 20 C11 22 13 22 14 20 M12 2 L12 3",
        [VisualIconKind.Download] = "M12 3 L12 16 M7 11 L12 16 L17 11 M3 16 L3 21 L21 21 L21 16",
        [VisualIconKind.Check] = "M4 12 L9 17 L20 6",
        [VisualIconKind.Info] = "M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 M12 11 L12 17 M12 7 L12.1 7",
        [VisualIconKind.Warning] = "M12 3 L22 21 L2 21 Z M12 9 L12 14 M12 17 L12.1 17",
    };

    public VisualIconKind Kind { get => (VisualIconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public VisualIcon()
    {
        Width = Height = 20;
        IsHitTestVisible = false;
        IsTabStop = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        _path = new Path
        {
            StrokeThickness = 1.65,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Data = GeometryFor(Kind),
        };
        _path.SetBinding(Path.StrokeProperty, new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
        var grid = new Grid { Width = 24, Height = 24 };
        grid.Children.Add(_path);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = grid };
    }

    public static VisualIconKind ForModule(ModuleId module) => module switch
    {
        ModuleId.Home => VisualIconKind.Home, ModuleId.Media => VisualIconKind.Music,
        ModuleId.Revenue => VisualIconKind.Revenue, ModuleId.Analytics => VisualIconKind.Analytics,
        ModuleId.Coding => VisualIconKind.Coding, ModuleId.Calendar => VisualIconKind.Calendar,
        ModuleId.Weather => VisualIconKind.Weather, ModuleId.Focus => VisualIconKind.Focus,
        ModuleId.Shelf => VisualIconKind.Shelf, ModuleId.Clipboard => VisualIconKind.Clipboard,
        ModuleId.Servers => VisualIconKind.Servers, ModuleId.System => VisualIconKind.System,
        ModuleId.ScreenTime => VisualIconKind.ScreenTime, ModuleId.Notes => VisualIconKind.Notes,
        ModuleId.Scratchpad => VisualIconKind.Scratchpad, ModuleId.Files => VisualIconKind.Files,
        ModuleId.Links => VisualIconKind.Links, ModuleId.Emoji => VisualIconKind.Emoji,
        ModuleId.Sounds => VisualIconKind.Sounds, ModuleId.Convert => VisualIconKind.Convert,
        ModuleId.Awake => VisualIconKind.Awake, ModuleId.Settings => VisualIconKind.Settings,
        ModuleId.Tools => VisualIconKind.Tools,
        _ => VisualIconKind.Tools,
    };

    public static VisualIconKind ForWeatherCode(int code) => code switch
    {
        0 or 1 => VisualIconKind.Sun, 2 => VisualIconKind.Weather, 3 => VisualIconKind.Cloud,
        45 or 48 => VisualIconKind.Fog,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => VisualIconKind.Rain,
        71 or 73 or 75 or 77 or 85 or 86 => VisualIconKind.Snow,
        95 or 96 or 99 => VisualIconKind.Storm,
        _ => VisualIconKind.Weather,
    };

    private static void OnKindChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) =>
        ((VisualIcon)sender)._path.Data = GeometryFor(((VisualIcon)sender).Kind);

    private static void OnSizeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _)
    {
        var icon = (VisualIcon)sender;
        var size = double.IsFinite(icon.Size) && icon.Size > 0 ? icon.Size : 20;
        icon.Width = icon.Height = size;
    }

    private static Geometry GeometryFor(VisualIconKind kind)
    {
        if (!TokenCache.TryGetValue(kind, out var tokens))
            TokenCache[kind] = tokens = Regex.Matches(Paths[kind], @"[MLCQAZ]|-?\d+(?:\.\d+)?").Select(match => match.Value).ToArray();
        // WinUI geometry is a DependencyObject with a single owner. Reusing one
        // PathGeometry across several Path.Data properties fails at native launch.
        var geometry = new PathGeometry();
        PathFigure? figure = null;
        var index = 0;
        double Number() => double.Parse(tokens[index++], CultureInfo.InvariantCulture);
        Point Point() => new(Number(), Number());
        while (index < tokens.Length)
        {
            var command = tokens[index++];
            if (command == "M")
            {
                figure = new PathFigure { StartPoint = Point(), IsFilled = false };
                geometry.Figures.Add(figure);
                continue;
            }
            if (figure is null) throw new InvalidOperationException("An icon path must begin with a move.");
            switch (command)
            {
                case "L": figure.Segments.Add(new LineSegment { Point = Point() }); break;
                case "C": figure.Segments.Add(new BezierSegment { Point1 = Point(), Point2 = Point(), Point3 = Point() }); break;
                case "Q": figure.Segments.Add(new QuadraticBezierSegment { Point1 = Point(), Point2 = Point() }); break;
                case "A":
                    var radius = new Size(Number(), Number());
                    var rotation = Number(); var large = Number(); var sweep = Number();
                    figure.Segments.Add(new ArcSegment { Size = radius, RotationAngle = rotation, IsLargeArc = large != 0,
                        SweepDirection = sweep != 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, Point = Point() });
                    break;
                case "Z": figure.IsClosed = true; break;
                default: throw new InvalidOperationException("Unsupported icon path command.");
            }
        }
        return geometry;
    }
}
