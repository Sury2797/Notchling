namespace Notch.Core;

public enum DurationSuspendPolicy
{
    CountSuspendTime,
    Pause
}

public sealed class CountdownTimer
{
    private readonly IClock _clock;
    private TimeSpan _remaining;
    private TimeSpan? _startedAt;
    private DateTimeOffset? _suspendedAt;
    private bool _resumeAfterSuspend;
    public DurationSuspendPolicy SuspendPolicy { get; }
    public TimeSpan Duration { get; private set; }
    public bool IsRunning => _startedAt.HasValue;
    public TimeSpan Remaining => _startedAt is { } start
        ? MaxZero(_remaining - MaxZero(_clock.Elapsed - start)) : _remaining;
    public double Progress => Duration.Ticks == 0 ? 0 : Math.Clamp(Remaining.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);

    public CountdownTimer(IClock clock, TimeSpan duration, DurationSuspendPolicy suspendPolicy = DurationSuspendPolicy.CountSuspendTime)
    {
        _clock = clock; SuspendPolicy = suspendPolicy; Reset(duration);
    }

    public void Start()
    {
        if (IsRunning) return;
        if (_remaining <= TimeSpan.Zero) _remaining = Duration;
        if (_suspendedAt.HasValue) { _resumeAfterSuspend = true; return; }
        _startedAt = _clock.Elapsed;
    }
    public void Pause() { _remaining = Remaining; _startedAt = null; _resumeAfterSuspend = false; }
    public void Reset(TimeSpan? duration = null)
    {
        if (duration is { } value)
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = value;
        }
        _remaining = Duration; _startedAt = null; _suspendedAt = null; _resumeAfterSuspend = false;
    }
    public bool Tick()
    {
        if (!IsRunning || Remaining > TimeSpan.Zero) return false;
        _remaining = TimeSpan.Zero; _startedAt = null; return true;
    }

    public void OnSuspending()
    {
        if (_suspendedAt.HasValue) return;
        _resumeAfterSuspend = IsRunning;
        _remaining = Remaining; _startedAt = null; _suspendedAt = _clock.UtcNow;
    }

    // A host can supply measured sleep time. UTC is only the fallback for this explicit suspend
    // interval, never for duration calculations while a timer is running normally.
    public void OnResumed(TimeSpan? suspendedDuration = null)
    {
        if (_suspendedAt is not { } suspendedAt) return;
        if (suspendedDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(suspendedDuration));
        if (_resumeAfterSuspend && SuspendPolicy == DurationSuspendPolicy.CountSuspendTime)
            _remaining = MaxZero(_remaining - (suspendedDuration ?? MaxZero(_clock.UtcNow - suspendedAt)));
        _suspendedAt = null;
        if (_resumeAfterSuspend) _startedAt = _clock.Elapsed;
        _resumeAfterSuspend = false;
    }
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

public sealed class StopwatchTimer(IClock clock, DurationSuspendPolicy suspendPolicy = DurationSuspendPolicy.CountSuspendTime)
{
    private TimeSpan _elapsed;
    private TimeSpan? _startedAt;
    private DateTimeOffset? _suspendedAt;
    private bool _resumeAfterSuspend;
    private readonly List<TimeSpan> _laps = [];
    public DurationSuspendPolicy SuspendPolicy { get; } = suspendPolicy;
    public bool IsRunning => _startedAt.HasValue;
    public TimeSpan Elapsed => _elapsed + (_startedAt is { } start ? MaxZero(clock.Elapsed - start) : TimeSpan.Zero);
    public IReadOnlyList<TimeSpan> Laps => _laps.AsReadOnly();
    public void Start()
    {
        if (_suspendedAt.HasValue) { _resumeAfterSuspend = true; return; }
        _startedAt ??= clock.Elapsed;
    }
    public void Pause() { _elapsed = Elapsed; _startedAt = null; _resumeAfterSuspend = false; }
    public void Reset() { _elapsed = TimeSpan.Zero; _startedAt = null; _suspendedAt = null; _resumeAfterSuspend = false; _laps.Clear(); }
    public void Lap() { if (IsRunning) _laps.Add(Elapsed); }
    public void OnSuspending()
    {
        if (_suspendedAt.HasValue) return;
        _resumeAfterSuspend = IsRunning;
        _elapsed = Elapsed; _startedAt = null; _suspendedAt = clock.UtcNow;
    }
    public void OnResumed(TimeSpan? suspendedDuration = null)
    {
        if (_suspendedAt is not { } suspendedAt) return;
        if (suspendedDuration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(suspendedDuration));
        if (_resumeAfterSuspend && SuspendPolicy == DurationSuspendPolicy.CountSuspendTime)
            _elapsed += suspendedDuration ?? MaxZero(clock.UtcNow - suspendedAt);
        _suspendedAt = null;
        if (_resumeAfterSuspend) _startedAt = clock.Elapsed;
        _resumeAfterSuspend = false;
    }
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

public sealed class FocusSession
{
    public CountdownTimer Pomodoro { get; }
    public CountdownTimer Countdown { get; }
    public CountdownTimer Hydration { get; }
    public StopwatchTimer Stopwatch { get; }
    public FocusSession(IClock clock, int focusMinutes = 25, int hydrationMinutes = 30,
        DurationSuspendPolicy suspendPolicy = DurationSuspendPolicy.CountSuspendTime)
    {
        Pomodoro = new(clock, TimeSpan.FromMinutes(Math.Clamp(focusMinutes, 1, 180)), suspendPolicy);
        Countdown = new(clock, TimeSpan.FromMinutes(5), suspendPolicy);
        Hydration = new(clock, TimeSpan.FromMinutes(Math.Clamp(hydrationMinutes, 5, 180)), suspendPolicy);
        Stopwatch = new(clock, suspendPolicy);
    }
    public void OnSuspending()
    {
        Pomodoro.OnSuspending(); Countdown.OnSuspending(); Hydration.OnSuspending(); Stopwatch.OnSuspending();
    }
    public void OnResumed(TimeSpan? suspendedDuration = null)
    {
        Pomodoro.OnResumed(suspendedDuration); Countdown.OnResumed(suspendedDuration);
        Hydration.OnResumed(suspendedDuration); Stopwatch.OnResumed(suspendedDuration);
    }
    public static string Format(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
        : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
}
