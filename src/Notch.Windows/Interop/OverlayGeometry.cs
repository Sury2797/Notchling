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

    private static int Pixels(double logicalPixels, double scale) =>
        checked((int)Math.Round(logicalPixels * scale, MidpointRounding.AwayFromZero));
}
