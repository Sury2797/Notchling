namespace Notch.Windows.Interop;

/// <summary>Work-area placement shared by the native HWND and its XAML content.</summary>
public sealed record OverlayLayout(double LogicalWidth, double LogicalBodyHeight,
    int X, int Y, int Width, int Height, bool ToolbarVisible);

public static class OverlayGeometry
{
    public static OverlayLayout Calculate(double requestedWidth, double requestedBodyHeight,
        bool expanded, bool showToolbar, int workLeft, int workTop, int workWidth, int workHeight,
        double scale, double horizontalOffset = 0, double topOffset = 0)
    {
        if (!double.IsFinite(requestedWidth) || requestedWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedWidth));
        if (!double.IsFinite(requestedBodyHeight) || requestedBodyHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedBodyHeight));
        if (!double.IsFinite(scale) || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (workWidth <= 0 || workHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(workWidth), "The monitor work area must have a positive width and height.");

        var availableWidth = Math.Max(1 / scale, workWidth / scale - 16);
        var logicalWidth = expanded
            ? Math.Clamp(requestedWidth, Math.Min(256, availableWidth), availableWidth)
            : Math.Min(256, availableWidth);
        var physicalWidth = Math.Clamp(Pixels(logicalWidth, scale), 1, workWidth);

        // Keep a usable body above the dock even when a saved offset comes from a larger monitor.
        // An extremely small remote/virtual work area cannot show the 70-DIP dock safely.
        var toolbarVisible = expanded && showToolbar && workHeight / scale >= 110;
        var toolbarHeight = toolbarVisible ? Pixels(70, scale) : 0;
        var minimumBody = Math.Clamp(Pixels(Math.Min(40, workHeight / scale), scale), 1, workHeight);
        var maximumTopOffset = Math.Max(0, workHeight - minimumBody - toolbarHeight);
        var requestedTop = double.IsFinite(topOffset) ? Math.Max(0, topOffset) : 0;
        var top = Pixels(Math.Min(requestedTop, maximumTopOffset / scale), scale);
        var availableBody = Math.Max(1, workHeight - top - toolbarHeight);
        var desiredBody = expanded ? requestedBodyHeight : 40;
        var physicalBody = Math.Clamp(Pixels(Math.Clamp(desiredBody,
            Math.Min(40, availableBody / scale), availableBody / scale), scale), 1, availableBody);
        var physicalHeight = physicalBody + toolbarHeight;
        var offset = double.IsFinite(horizontalOffset)
            ? Pixels(Math.Clamp(horizontalOffset, -workWidth / scale, workWidth / scale), scale) : 0;
        var centeredLeft = (long)workLeft + (workWidth - physicalWidth) / 2 + offset;
        var x = checked((int)Math.Clamp(centeredLeft, workLeft, (long)workLeft + workWidth - physicalWidth));
        var y = checked(workTop + top);
        return new(physicalWidth / scale, physicalBody / scale, x, y,
            physicalWidth, physicalHeight, toolbarVisible);
    }

    /// <summary>Interpolate already-clamped native targets; collapse never targets expanded dimensions.</summary>
    public static OverlayLayout Interpolate(OverlayLayout start, OverlayLayout target, double progress)
    {
        if (!double.IsFinite(progress)) throw new ArgumentOutOfRangeException(nameof(progress));
        progress = Math.Clamp(progress, 0, 1);
        if (progress == 0) return start;
        if (progress == 1) return target;
        var eased = 1 - Math.Pow(1 - progress, 3);
        int Pixel(int from, int to) => checked((int)Math.Round(from + ((double)to - from) * eased,
            MidpointRounding.AwayFromZero));
        double Logical(double from, double to) => from + (to - from) * eased;
        return new(Logical(start.LogicalWidth, target.LogicalWidth),
            Logical(start.LogicalBodyHeight, target.LogicalBodyHeight),
            Pixel(start.X, target.X), Pixel(start.Y, target.Y),
            Math.Max(1, Pixel(start.Width, target.Width)), Math.Max(1, Pixel(start.Height, target.Height)),
            target.ToolbarVisible);
    }

    /// <summary>The body and short path into the dock form one interaction surface; empty flanks do not.</summary>
    public static bool ContainsInteractionPoint(double x, double y, double width, double bodyHeight,
        bool toolbarVisible, double toolbarWidth, double cornerRadius = 26)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width)
            || !double.IsFinite(bodyHeight) || !double.IsFinite(toolbarWidth)
            || width <= 0 || bodyHeight <= 0 || toolbarWidth <= 0
            || x < 0 || y < 0 || x >= width) return false;
        if (y < bodyHeight)
        {
            var radius = Math.Clamp(cornerRadius, 0, Math.Min(width / 2, bodyHeight / 2));
            if (y < bodyHeight - radius || x >= radius && x < width - radius) return true;
            var centerX = x < radius ? radius : width - radius;
            return Math.Pow(x - centerX, 2) + Math.Pow(y - (bodyHeight - radius), 2) <= radius * radius;
        }
        if (!toolbarVisible || y >= bodyHeight + 58) return false;
        var dockWidth = Math.Min(toolbarWidth, width);
        var left = (width - dockWidth) / 2;
        if (x < left || x >= left + dockWidth) return false;
        // The ten-DIP vertical gap and the separator between the two dock capsules
        // are an intentional pointer corridor, retained by the same finite leave grace.
        if (y < bodyHeight + 10) return true;
        var dockX = x - left;
        var dockY = y - bodyHeight - 10;
        var dockRadius = Math.Min(24, dockWidth / 2);
        if (dockX >= dockRadius && dockX < dockWidth - dockRadius) return true;
        var center = dockX < dockRadius ? dockRadius : dockWidth - dockRadius;
        return Math.Pow(dockX - center, 2) + Math.Pow(dockY - 24, 2) <= 24 * 24;
    }

    private static int Pixels(double logicalPixels, double scale) =>
        checked((int)Math.Round(logicalPixels * scale, MidpointRounding.AwayFromZero));
}
