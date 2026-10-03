namespace Notch.Core;

public sealed class OverlayStateMachine(IClock clock)
{
    private readonly Queue<LiveActivity> _queue = new();
    private OverlayMode _beforeActivity = OverlayMode.Collapsed;
    public OverlayMode Mode { get; private set; } = OverlayMode.Collapsed;
    public ModuleId SelectedModule { get; private set; } = ModuleId.Home;
    public bool Pinned { get; set; }
    public LiveActivity? Activity { get; private set; }
    public event EventHandler? Changed;
    public void Expand(ModuleId module)
    {
        SelectedModule = module; Mode = OverlayMode.Expanded; Activity = null;
        _queue.Clear(); _beforeActivity = OverlayMode.Expanded;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public bool Collapse(bool force = false)
    {
        if (Pinned && !force) return false;
        Mode = OverlayMode.Collapsed; Activity = null;
        _queue.Clear(); _beforeActivity = OverlayMode.Collapsed;
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    public bool ShowActivity(LiveActivity activity)
    {
        if (activity.Duration <= TimeSpan.Zero || activity.Duration > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(activity));
        if (activity.CreatedAt + activity.Duration <= clock.UtcNow) return false;
        if (Activity?.Id == activity.Id || _queue.Any(queued => queued.Id == activity.Id)) return false;
        if (Activity is not null)
        {
            if (_queue.Count >= 16) _queue.Dequeue();
            _queue.Enqueue(activity); return true;
        }
        _beforeActivity = Mode; Activity = activity; Mode = OverlayMode.Activity;
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    public void DismissActivity()
    {
        if (Activity is null) return;
        Activity = null;
        while (_queue.TryDequeue(out var next))
        {
            if (next.CreatedAt + next.Duration <= clock.UtcNow) continue;
            Activity = next; Mode = OverlayMode.Activity;
            Changed?.Invoke(this, EventArgs.Empty); return;
        }
        Mode = _beforeActivity; Changed?.Invoke(this, EventArgs.Empty);
    }
    public bool Tick()
    {
        if (Activity is null || Activity.CreatedAt + Activity.Duration > clock.UtcNow) return false;
        DismissActivity(); return true;
    }
}
