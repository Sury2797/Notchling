namespace Notch.Core;

public sealed class CountdownTimer
{
    private readonly IClock _clock;
    private TimeSpan _remaining;
    private DateTimeOffset? _startedAt;
    public TimeSpan Duration { get; private set; }
    public bool IsRunning => _startedAt.HasValue;
    public TimeSpan Remaining => _startedAt is { } start
        ? MaxZero(_remaining - MaxZero(_clock.UtcNow - start)) : _remaining;
    public double Progress => Duration.Ticks == 0 ? 0 : Math.Clamp(Remaining.TotalMilliseconds / Duration.TotalMilliseconds, 0, 1);
    public CountdownTimer(IClock clock, TimeSpan duration) { _clock = clock; Reset(duration); }
    public void Start()
    {
        if (IsRunning) return;
        if (_remaining <= TimeSpan.Zero) _remaining = Duration;
        _startedAt = _clock.UtcNow;
    }
    public void Pause() { _remaining = Remaining; _startedAt = null; }
    public void Reset(TimeSpan? duration = null)
    {
        if (duration is { } value)
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(duration));
            Duration = value;
        }
        _remaining = Duration; _startedAt = null;
    }
    public bool Tick()
    {
        if (!IsRunning || Remaining > TimeSpan.Zero) return false;
        _remaining = TimeSpan.Zero; _startedAt = null; return true;
    }
    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

public sealed class StopwatchTimer(IClock clock)
{
    private TimeSpan _elapsed;
    private DateTimeOffset? _startedAt;
    private readonly List<TimeSpan> _laps = [];
    public bool IsRunning => _startedAt.HasValue;
    public TimeSpan Elapsed => _elapsed + (_startedAt is { } start && clock.UtcNow > start ? clock.UtcNow - start : TimeSpan.Zero);
    public IReadOnlyList<TimeSpan> Laps => _laps.AsReadOnly();
    public void Start() { _startedAt ??= clock.UtcNow; }
    public void Pause() { _elapsed = Elapsed; _startedAt = null; }
    public void Reset() { _elapsed = TimeSpan.Zero; _startedAt = null; _laps.Clear(); }
    public void Lap() { if (IsRunning) _laps.Add(Elapsed); }
}

public sealed class FocusSession
{
    public CountdownTimer Pomodoro { get; }
    public CountdownTimer Countdown { get; }
    public CountdownTimer Hydration { get; }
    public StopwatchTimer Stopwatch { get; }
    public FocusSession(IClock clock, int focusMinutes = 25, int hydrationMinutes = 30)
    {
        Pomodoro = new(clock, TimeSpan.FromMinutes(Math.Clamp(focusMinutes, 1, 180)));
        Countdown = new(clock, TimeSpan.FromMinutes(5));
        Hydration = new(clock, TimeSpan.FromMinutes(Math.Clamp(hydrationMinutes, 5, 180)));
        Stopwatch = new(clock);
    }
    public static string Format(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
        : $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
}
