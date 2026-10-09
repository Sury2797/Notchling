namespace Notch.Core;

/// <summary>Finite hover and keyboard leases, measured independently from the wall clock.</summary>
public sealed class HoverInteractionPolicy(IClock clock)
{
    public static readonly TimeSpan LeaveGrace = TimeSpan.FromMilliseconds(450);
    public static readonly TimeSpan KeyboardGrace = TimeSpan.FromSeconds(6);
    private bool _visited;
    private TimeSpan? _outsideSince;
    private TimeSpan? _lastKeyboardInput;

    public bool HasRecentKeyboardInput => _lastKeyboardInput is { } last
        && clock.Elapsed - last < KeyboardGrace;

    public void Begin(bool explicitOpen)
    {
        // A keyboard/tray request can open while the pointer is elsewhere. Give the
        // person a chance to reach the panel before applying ordinary hover dismissal.
        _visited = !explicitOpen;
        _outsideSince = null;
        _lastKeyboardInput = null;
    }

    public void RecordKeyboardInput() => _lastKeyboardInput = clock.Elapsed;

    public void ReleaseExplicitLease()
    {
        _visited = true;
        _lastKeyboardInput = null;
    }

    public bool ShouldCollapse(bool pointerInside, bool pinned, bool protectedInteraction,
        bool keyboardEditorFocused)
    {
        if (pointerInside)
        {
            _visited = true;
            _outsideSince = null;
            return false;
        }
        _outsideSince ??= clock.Elapsed;
        if (!_visited || pinned || protectedInteraction
            || keyboardEditorFocused && HasRecentKeyboardInput) return false;
        return clock.Elapsed - _outsideSince.Value >= LeaveGrace;
    }
}
