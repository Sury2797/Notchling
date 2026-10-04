namespace Notch.Core;

public sealed class OverlayStateMachine(IClock clock)
{
    public const int MaximumPendingActivities = 128;
    private sealed class Presentation(LiveActivity activity)
    {
        public LiveActivity Activity { get; } = activity;
        public TimeSpan Remaining { get; set; } = activity.Duration;
        public bool HasBeenPresented { get; set; }
    }

    private readonly Queue<Presentation> _queue = new();
    private readonly HashSet<Guid> _pendingIds = [];
    private Presentation? _active;
    private TimeSpan? _displayedAt;
    private OverlayMode _baseMode = OverlayMode.Collapsed;
    private bool _interactionSuppressed;
    public OverlayMode Mode { get; private set; } = OverlayMode.Collapsed;
    public ModuleId SelectedModule { get; private set; } = ModuleId.Home;
    public bool Pinned { get; set; }
    public bool InteractionSuppressed => _interactionSuppressed;
    public int PendingCount => _pendingIds.Count;
    public LiveActivity? Activity => Mode == OverlayMode.Activity ? _active?.Activity : null;
    public event EventHandler? Changed;
    public event EventHandler<LiveActivity>? ActivityPresented;
    public event EventHandler<LiveActivity>? ActivityCompleted;

    public void Expand(ModuleId module)
    {
        PausePresentation();
        SelectedModule = module; _baseMode = Mode = OverlayMode.Expanded;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public bool Collapse(bool force = false)
    {
        if (Pinned && !force) return false;
        PausePresentation();
        _baseMode = Mode = OverlayMode.Collapsed;
        Changed?.Invoke(this, EventArgs.Empty); return true;
    }

    // Form editing, open dialogs, and suspend can defer presentation without discarding events.
    // Clearing suppression waits for the next Tick so focus changes cannot synchronously replace
    // the editor that is handling the current input event.
    public void SetInteractionSuppressed(bool suppressed)
    {
        if (_interactionSuppressed == suppressed) return;
        _interactionSuppressed = suppressed;
        if (!suppressed || Mode != OverlayMode.Activity) return;
        PausePresentation(); Mode = _baseMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool ShowActivity(LiveActivity activity)
    {
        if (activity.Duration <= TimeSpan.Zero || activity.Duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(activity));
        if (_pendingIds.Contains(activity.Id) || PendingCount >= MaximumPendingActivities) return false;
        _pendingIds.Add(activity.Id);
        _queue.Enqueue(new(activity));
        if (_active is null && !_interactionSuppressed) StartPresentation();
        return true;
    }
    public void DismissActivity()
    {
        if (_active is null || Mode != OverlayMode.Activity) return;
        CompleteActive();
        if (!_interactionSuppressed && StartPresentation()) return;
        Mode = _baseMode;
        Changed?.Invoke(this, EventArgs.Empty);
    }
    // Removed/completed reminders are canceled without acknowledging or completing delivery.
    public bool CancelActivity(Guid id)
    {
        if (!_pendingIds.Remove(id)) return false;
        if (_active?.Activity.Id != id)
        {
            var retained = _queue.Where(item => item.Activity.Id != id).ToArray();
            _queue.Clear();
            foreach (var item in retained) _queue.Enqueue(item);
            return true;
        }
        _active = null; _displayedAt = null;
        if (Mode != OverlayMode.Activity) return true;
        if (!_interactionSuppressed && StartPresentation()) return true;
        Mode = _baseMode; Changed?.Invoke(this, EventArgs.Empty); return true;
    }
    public void ClearActivities()
    {
        _queue.Clear(); _pendingIds.Clear(); _active = null; _displayedAt = null;
        if (Mode != OverlayMode.Activity) return;
        Mode = _baseMode; Changed?.Invoke(this, EventArgs.Empty);
    }
    public bool Tick()
    {
        if (_interactionSuppressed) return false;
        if (Mode != OverlayMode.Activity)
        {
            if (_active is { Remaining: var remaining } && remaining <= TimeSpan.Zero) CompleteActive();
            return StartPresentation();
        }
        if (_active is null || _displayedAt is not { } started || clock.Elapsed - started < _active.Remaining) return false;
        DismissActivity(); return true;
    }

    private void PausePresentation()
    {
        if (_active is not null && _displayedAt is { } started)
            _active.Remaining = MaxZero(_active.Remaining - MaxZero(clock.Elapsed - started));
        _displayedAt = null;
    }
    private bool StartPresentation()
    {
        if (_active is null && !_queue.TryDequeue(out _active)) return false;
        var presentation = _active;
        _displayedAt = clock.Elapsed; Mode = OverlayMode.Activity;
        Changed?.Invoke(this, EventArgs.Empty);
        // A Changed subscriber may suppress or navigate away while it reconciles native focus.
        // Do not acknowledge delivery until that reconciliation leaves this activity visible.
        if (ReferenceEquals(_active, presentation) && Mode == OverlayMode.Activity && !presentation.HasBeenPresented)
        {
            presentation.HasBeenPresented = true;
            ActivityPresented?.Invoke(this, presentation.Activity);
        }
        return true;
    }
    private void CompleteActive()
    {
        var completed = _active;
        _active = null; _displayedAt = null;
        if (completed is null) return;
        _pendingIds.Remove(completed.Activity.Id);
        ActivityCompleted?.Invoke(this, completed.Activity);
    }
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
