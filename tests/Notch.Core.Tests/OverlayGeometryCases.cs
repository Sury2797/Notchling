using Notch.Windows.Interop;

namespace Notch.Core.Tests;

internal static class OverlayGeometryCases
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Expanded Settings reserves its dock inside the monitor work area", () =>
        {
            var layout = OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1920, 1040, 1);
            Check.Equal(640, layout.Width); Check.Equal(610, layout.Height);
            Check.Equal(640d, layout.LogicalWidth); Check.Equal(540d, layout.LogicalBodyHeight);
            Check.Equal(640, layout.X); Check.Equal(0, layout.Y); Check.True(layout.ToolbarVisible);
            Inside(layout, 0, 0, 1920, 1040, 1);
        });
        suite.Add("Fractional DPI preserves content and dock dimensions without crossing work bounds", () =>
        {
            foreach (var scale in new[] { 1.25, 1.5, 1.75, 2.25 })
            {
                var layout = OverlayGeometry.Calculate(640, 540, true, true, 31, 57, 1600, 900, scale);
                Inside(layout, 31, 57, 1600, 900, scale);
                Check.True(layout.ToolbarVisible);
                Check.Near(layout.Width, layout.LogicalWidth * scale);
                Check.True(layout.LogicalBodyHeight <= 540);
                Check.Equal((int)Math.Round(70 * scale, MidpointRounding.AwayFromZero),
                    layout.Height - (int)Math.Round(layout.LogicalBodyHeight * scale));
            }
        });
        suite.Add("Mixed monitor coordinates keep the entire expanded notch on its selected monitor", () =>
        {
            foreach (var area in new[] { (-1920, 0, 1920, 1040), (-1080, -1600, 1080, 1600), (1920, -200, 1366, 728) })
            {
                var layout = OverlayGeometry.Calculate(720, 500, true, true,
                    area.Item1, area.Item2, area.Item3, area.Item4, 1.5, -100, 12);
                Inside(layout, area.Item1, area.Item2, area.Item3, area.Item4, 1.5);
                Check.True(layout.ToolbarVisible);
            }
        });
        suite.Add("Small high-DPI work areas shrink Settings while preserving body and dock", () =>
        {
            var layout = OverlayGeometry.Calculate(640, 540, true, true, -800, 40, 800, 600, 2);
            Inside(layout, -800, 40, 800, 600, 2);
            Check.Equal(768, layout.Width); Check.Equal(600, layout.Height);
            Check.Equal(230d, layout.LogicalBodyHeight); Check.True(layout.ToolbarVisible);
        });
        suite.Add("A saved extreme top offset leaves room for a usable panel and its dock", () =>
        {
            var layout = OverlayGeometry.Calculate(640, 540, true, true, 0, -700, 800, 600, 2,
                horizontalOffset: double.MaxValue, topOffset: double.MaxValue);
            Inside(layout, 0, -700, 800, 600, 2);
            Check.Equal(32, layout.X); Check.Equal(-320, layout.Y);
            Check.Equal(40d, layout.LogicalBodyHeight); Check.Equal(220, layout.Height);
            Check.True(layout.ToolbarVisible);
        });
        suite.Add("Extreme horizontal offsets clamp to either work-area edge", () =>
        {
            var left = OverlayGeometry.Calculate(640, 540, true, true, -900, 80, 900, 700, 1.25,
                horizontalOffset: -double.MaxValue);
            var right = OverlayGeometry.Calculate(640, 540, true, true, -900, 80, 900, 700, 1.25,
                horizontalOffset: double.MaxValue);
            Inside(left, -900, 80, 900, 700, 1.25); Inside(right, -900, 80, 900, 700, 1.25);
            Check.Equal(-900, left.X); Check.Equal(0, right.X + right.Width);
        });
        suite.Add("Unusable tiny work areas hide the dock and retain a positive in-bounds body", () =>
        {
            foreach (var area in new[] { (80, 70, 2d), (1, 1, 1.25), (300, 109, 1d) })
            {
                var layout = OverlayGeometry.Calculate(640, 540, true, true, -70, 18,
                    area.Item1, area.Item2, area.Item3, 9000, 9000);
                Inside(layout, -70, 18, area.Item1, area.Item2, area.Item3);
                Check.False(layout.ToolbarVisible);
                Check.Near(layout.Height, layout.LogicalBodyHeight * area.Item3);
            }
        });
        suite.Add("Collapsed layout ignores expanded sizing and never reserves a dock", () =>
        {
            var layout = OverlayGeometry.Calculate(720, 540, false, true, 100, 20, 1200, 800, 1.5);
            Inside(layout, 100, 20, 1200, 800, 1.5);
            Check.Equal(384, layout.Width); Check.Equal(60, layout.Height);
            Check.Equal(256d, layout.LogicalWidth); Check.Equal(40d, layout.LogicalBodyHeight);
            Check.False(layout.ToolbarVisible);
        });
        suite.Add("An explicitly hidden toolbar adds no invisible reserved space", () =>
        {
            var layout = OverlayGeometry.Calculate(640, 540, true, false, 0, 0, 1280, 720, 1);
            Inside(layout, 0, 0, 1280, 720, 1);
            Check.Equal(540, layout.Height); Check.False(layout.ToolbarVisible);
        });
        suite.Add("Nonfinite and negative saved offsets fall back to safe placement", () =>
        {
            var expected = OverlayGeometry.Calculate(640, 540, true, true, -100, 30, 1000, 700, 1.25);
            foreach (var offset in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                var actual = OverlayGeometry.Calculate(640, 540, true, true, -100, 30, 1000, 700, 1.25, offset, offset);
                Check.Equal(expected, actual);
            }
            var negativeTop = OverlayGeometry.Calculate(640, 540, true, true, -100, 30, 1000, 700, 1.25, 0, -9000);
            Check.Equal(expected, negativeTop);
        });
        suite.Add("Invalid requested dimensions and work areas fail before producing native coordinates", () =>
        {
            foreach (var value in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
            {
                Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Calculate(value, 540, true, true, 0, 0, 1280, 720, 1));
                Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Calculate(640, value, true, true, 0, 0, 1280, 720, 1));
                Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1280, 720, value));
            }
            Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 0, 720, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1280, -1, 1));
        });
        suite.Add("Collapse animation moves toward its actual compact target instead of snapping from expanded dimensions", () =>
        {
            var expanded = OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1920, 1040, 1);
            var collapsed = OverlayGeometry.Calculate(640, 540, false, true, 0, 0, 1920, 1040, 1);
            var midpoint = OverlayGeometry.Interpolate(expanded, collapsed, .5);
            Check.True(midpoint.Width < expanded.Width && midpoint.Width > collapsed.Width);
            Check.True(midpoint.Height < expanded.Height && midpoint.Height > collapsed.Height);
            var nearEnd = OverlayGeometry.Interpolate(expanded, collapsed, .99);
            Check.Equal(collapsed.Width, nearEnd.Width); Check.Equal(collapsed.Height, nearEnd.Height);
            Check.Equal(collapsed, OverlayGeometry.Interpolate(expanded, collapsed, 1));
        });
        suite.Add("Every native transition frame remains within a small high-DPI monitor work area", () =>
        {
            var first = OverlayGeometry.Calculate(760, 410, true, true, -900, -700, 900, 600, 1.5);
            var second = OverlayGeometry.Calculate(900, 540, true, true, -900, -700, 900, 600, 1.5, 500, 20);
            for (var frame = 0; frame <= 60; frame++)
            {
                var current = OverlayGeometry.Interpolate(first, second, frame / 60d);
                Check.True(current.Width > 0 && current.Height > 0);
                Check.True(current.X >= -900 && current.Y >= -700);
                Check.True(current.X + current.Width <= 0 && current.Y + current.Height <= -100);
            }
        });
        suite.Add("Interrupted transitions can restart from their visible native frame without returning to stale geometry", () =>
        {
            var compact = OverlayGeometry.Calculate(640, 540, false, true, 0, 0, 1920, 1040, 1);
            var media = OverlayGeometry.Calculate(600, 336, true, true, 0, 0, 1920, 1040, 1);
            var settings = OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1920, 1040, 1);
            var visible = OverlayGeometry.Interpolate(compact, media, .4);
            Check.Equal(visible, OverlayGeometry.Interpolate(visible, settings, 0));
            Check.Equal(settings, OverlayGeometry.Interpolate(visible, settings, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.Interpolate(visible, settings, double.NaN));
        });
        suite.Add("Short interrupted resize movements finish sooner with a DPI-independent bounded duration", () =>
        {
            var compact = OverlayGeometry.Calculate(640, 540, false, true, 0, 0, 1920, 1040, 1);
            var expanded = OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 1920, 1040, 1);
            var justOpened = OverlayGeometry.Interpolate(compact, expanded, .02);
            var fullDuration = OverlayGeometry.TransitionDuration(compact, expanded, 1);
            var reverseDuration = OverlayGeometry.TransitionDuration(justOpened, compact, 1);
            Check.True(reverseDuration < fullDuration);
            Check.True(reverseDuration >= TimeSpan.FromMilliseconds(110));
            Check.True(fullDuration <= TimeSpan.FromMilliseconds(180));
            Check.Equal(TimeSpan.Zero, OverlayGeometry.TransitionDuration(compact, compact, 1));
            var compactDense = OverlayGeometry.Calculate(640, 540, false, true, 0, 0, 3840, 2080, 2);
            var expandedDense = OverlayGeometry.Calculate(640, 540, true, true, 0, 0, 3840, 2080, 2);
            Check.Equal(fullDuration, OverlayGeometry.TransitionDuration(compactDense, expandedDense, 2));
            Check.Throws<ArgumentOutOfRangeException>(() => OverlayGeometry.TransitionDuration(compact, expanded, double.NaN));
        });
        suite.Add("Body-to-dock corridor preserves hover while invisible bottom flanks and tail dismiss", () =>
        {
            Check.True(OverlayGeometry.ContainsInteractionPoint(300, 305, 600, 300, true, 496));
            Check.True(OverlayGeometry.ContainsInteractionPoint(300, 334, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(12, 334, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(590, 305, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(300, 364, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(300, 305, 600, 300, false, 496));
        });
        suite.Add("Native interaction bounds exclude rounded panel corners and invalid pointer positions", () =>
        {
            Check.True(OverlayGeometry.ContainsInteractionPoint(1, 1, 600, 300, true, 496));
            Check.True(OverlayGeometry.ContainsInteractionPoint(26, 299, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(1, 299, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(600, 12, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(10, -1, 600, 300, true, 496));
            Check.False(OverlayGeometry.ContainsInteractionPoint(double.NaN, 1, 600, 300, true, 496));
        });
        suite.Add("An opening panel does not create a false dock corridor at its travelling bottom edge", () =>
        {
            Check.False(OverlayGeometry.ContainsInteractionPoint(300, 165, 600, 160, true, 496,
                toolbarBodyHeight: 300));
            Check.False(OverlayGeometry.ContainsInteractionPoint(300, 290, 600, 160, true, 496,
                toolbarBodyHeight: 300));
            Check.True(OverlayGeometry.ContainsInteractionPoint(300, 305, 600, 160, true, 496,
                toolbarBodyHeight: 300));
            Check.True(OverlayGeometry.ContainsInteractionPoint(300, 334, 600, 160, true, 496,
                toolbarBodyHeight: 300));
            Check.False(OverlayGeometry.ContainsInteractionPoint(12, 334, 600, 160, true, 496,
                toolbarBodyHeight: 300));
            // A correctly placed final dock is still outside the smaller HWND early in an opening.
            Check.False(OverlayGeometry.ContainsInteractionPoint(300, 334, 600, 160, true, 496,
                toolbarBodyHeight: 300, visibleWindowHeight: 230));
            Check.True(OverlayGeometry.ContainsInteractionPoint(300, 334, 600, 160, true, 496,
                toolbarBodyHeight: 300, visibleWindowHeight: 350));
        });
        suite.Add("Activity and compact interaction corners follow the same explicit radius as the visible surface", () =>
        {
            // This point belongs to a 20-DIP activity corner but is outside a 26-DIP corner.
            Check.True(OverlayGeometry.ContainsInteractionPoint(8, 138, 460, 144, false, 496, 20));
            Check.False(OverlayGeometry.ContainsInteractionPoint(8, 138, 460, 144, false, 496, 26));
            Check.False(OverlayGeometry.ContainsInteractionPoint(1, 39, 256, 40, false, 496, 20));
            Check.True(OverlayGeometry.ContainsInteractionPoint(20, 39, 256, 40, false, 496, 20));
            Check.False(OverlayGeometry.ContainsInteractionPoint(1, 1, 256, 40, false, 496, double.NaN));
        });
    }

    private static void Inside(OverlayLayout layout, int left, int top, int width, int height, double scale)
    {
        Check.True(layout.Width > 0 && layout.Height > 0, "A visible HWND must retain positive dimensions.");
        Check.True(layout.X >= left && layout.Y >= top, "The notch begins outside its selected work area.");
        Check.True((long)layout.X + layout.Width <= (long)left + width, "The dock or panel crosses the right work-area edge.");
        Check.True((long)layout.Y + layout.Height <= (long)top + height, "The dock or panel crosses the bottom work-area edge.");
        Check.True(double.IsFinite(layout.LogicalWidth) && double.IsFinite(layout.LogicalBodyHeight));
        Check.Near(layout.Width, layout.LogicalWidth * scale);
        Check.True(layout.LogicalBodyHeight > 0);
    }
}
