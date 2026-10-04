using Notch.Core;

namespace Notch.Core.Tests;

internal sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    public TimeSpan Elapsed { get; private set; }
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        UtcNow += duration; Elapsed += duration;
    }
    public void AdjustWallClock(TimeSpan adjustment) => UtcNow += adjustment;
}

internal static class TimerCases
{
    public static void Register(TestSuite suite)
    {
        suite.Add("Countdown survives a long gap in UI ticks and completes once", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(25));
            timer.Start();
            clock.Advance(TimeSpan.FromMinutes(26));
            Check.Equal(TimeSpan.Zero, timer.Remaining);
            Check.Near(0, timer.Progress);
            Check.True(timer.Tick());
            Check.False(timer.Tick());
            Check.False(timer.IsRunning);
        });
        suite.Add("Countdown pause and resume excludes time while paused", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(42));
            timer.Pause();
            clock.Advance(TimeSpan.FromHours(3));
            Check.Equal(TimeSpan.FromSeconds(258), timer.Remaining);
            timer.Start();
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(8));
            Check.Equal(TimeSpan.FromSeconds(250), timer.Remaining);
            Check.Near(250d / 300, timer.Progress);
        });
        suite.Add("Countdown reset replaces duration and stops an active session", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            timer.Start();
            clock.Advance(TimeSpan.FromMinutes(1));
            timer.Reset(TimeSpan.FromMinutes(10));
            Check.False(timer.IsRunning);
            Check.Equal(TimeSpan.FromMinutes(10), timer.Remaining);
            clock.Advance(TimeSpan.FromMinutes(2));
            Check.Equal(TimeSpan.FromMinutes(10), timer.Remaining);
        });
        suite.Add("Countdown rejects unusable durations and can restart after expiry", () =>
        {
            var clock = new ManualClock();
            Check.Throws<ArgumentOutOfRangeException>(() => new CountdownTimer(clock, TimeSpan.Zero));
            Check.Throws<ArgumentOutOfRangeException>(() => new CountdownTimer(clock, TimeSpan.FromDays(2)));
            var timer = new CountdownTimer(clock, TimeSpan.FromSeconds(1));
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(2));
            timer.Tick();
            timer.Start();
            Check.Equal(TimeSpan.FromSeconds(1), timer.Remaining);
        });
        suite.Add("Running durations ignore forward and backward wall-clock corrections", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            var stopwatch = new StopwatchTimer(clock);
            timer.Start();
            stopwatch.Start();
            clock.Advance(TimeSpan.FromMinutes(2));
            clock.AdjustWallClock(TimeSpan.FromHours(-5));
            Check.Equal(TimeSpan.FromMinutes(3), timer.Remaining);
            Check.Equal(TimeSpan.FromMinutes(2), stopwatch.Elapsed);
            clock.AdjustWallClock(TimeSpan.FromHours(10));
            Check.Equal(TimeSpan.FromMinutes(3), timer.Remaining);
            Check.Equal(TimeSpan.FromMinutes(2), stopwatch.Elapsed);
            clock.Advance(TimeSpan.FromSeconds(10));
            Check.Equal(TimeSpan.FromSeconds(170), timer.Remaining);
            Check.Equal(TimeSpan.FromSeconds(130), stopwatch.Elapsed);
            Check.False(timer.Tick());
        });
        suite.Add("Default suspend policy includes sleep and completes an expired focus session once", () =>
        {
            var clock = new ManualClock();
            var focus = new FocusSession(clock);
            focus.Pomodoro.Start(); focus.Stopwatch.Start();
            clock.Advance(TimeSpan.FromMinutes(2));
            focus.OnSuspending();
            clock.AdjustWallClock(TimeSpan.FromHours(1)); // OS elapsed clocks may stop during sleep.
            Check.False(focus.Pomodoro.Tick());
            focus.OnResumed();
            Check.Equal(TimeSpan.Zero, focus.Pomodoro.Remaining);
            Check.True(focus.Pomodoro.Tick());
            Check.False(focus.Pomodoro.Tick());
            Check.Equal(TimeSpan.FromMinutes(62), focus.Stopwatch.Elapsed);
            Check.False(focus.Countdown.IsRunning);
        });
        suite.Add("Pause-on-suspend explicitly excludes sleep from countdown and stopwatch", () =>
        {
            var clock = new ManualClock();
            var focus = new FocusSession(clock, suspendPolicy: DurationSuspendPolicy.Pause);
            focus.Pomodoro.Start(); focus.Stopwatch.Start();
            clock.Advance(TimeSpan.FromMinutes(2));
            focus.OnSuspending();
            clock.Advance(TimeSpan.FromHours(3)); // Also works when the elapsed clock counts sleep.
            focus.OnResumed();
            Check.Equal(TimeSpan.FromMinutes(23), focus.Pomodoro.Remaining);
            Check.Equal(TimeSpan.FromMinutes(2), focus.Stopwatch.Elapsed);
            clock.Advance(TimeSpan.FromMinutes(1));
            Check.Equal(TimeSpan.FromMinutes(22), focus.Pomodoro.Remaining);
            Check.Equal(TimeSpan.FromMinutes(3), focus.Stopwatch.Elapsed);
        });
        suite.Add("Measured sleep duration is independent of wall-clock changes and not counted twice", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            var stopwatch = new StopwatchTimer(clock);
            timer.Start(); stopwatch.Start();
            clock.Advance(TimeSpan.FromSeconds(42));
            timer.OnSuspending(); stopwatch.OnSuspending();
            clock.Advance(TimeSpan.FromMinutes(3));
            clock.AdjustWallClock(TimeSpan.FromHours(-4));
            timer.OnResumed(TimeSpan.FromMinutes(3)); stopwatch.OnResumed(TimeSpan.FromMinutes(3));
            Check.Equal(TimeSpan.FromSeconds(78), timer.Remaining);
            Check.Equal(TimeSpan.FromSeconds(222), stopwatch.Elapsed);
            timer.OnResumed(TimeSpan.FromMinutes(3)); stopwatch.OnResumed(TimeSpan.FromMinutes(3));
            Check.Equal(TimeSpan.FromSeconds(78), timer.Remaining);
            Check.Equal(TimeSpan.FromSeconds(222), stopwatch.Elapsed);
            clock.Advance(TimeSpan.FromSeconds(1));
            Check.Equal(TimeSpan.FromSeconds(77), timer.Remaining);
            Check.Equal(TimeSpan.FromSeconds(223), stopwatch.Elapsed);
        });
        suite.Add("Suspend never restarts paused or reset timers", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            var stopwatch = new StopwatchTimer(clock);
            timer.Start(); stopwatch.Start(); clock.Advance(TimeSpan.FromSeconds(12));
            timer.Pause(); stopwatch.Pause();
            timer.OnSuspending(); stopwatch.OnSuspending();
            clock.Advance(TimeSpan.FromHours(1));
            timer.OnResumed(); stopwatch.OnResumed();
            Check.False(timer.IsRunning); Check.False(stopwatch.IsRunning);
            Check.Equal(TimeSpan.FromSeconds(288), timer.Remaining);
            Check.Equal(TimeSpan.FromSeconds(12), stopwatch.Elapsed);
            timer.Start(); stopwatch.Start(); timer.OnSuspending(); stopwatch.OnSuspending();
            timer.Reset(); stopwatch.Reset();
            timer.OnResumed(); stopwatch.OnResumed();
            Check.False(timer.IsRunning); Check.False(stopwatch.IsRunning);
            Check.Equal(TimeSpan.FromMinutes(5), timer.Remaining);
            Check.Equal(TimeSpan.Zero, stopwatch.Elapsed);
        });
        suite.Add("Repeated suspend messages keep the original interval and reject invalid measured sleep", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            var stopwatch = new StopwatchTimer(clock);
            timer.Start(); stopwatch.Start(); timer.OnSuspending(); stopwatch.OnSuspending();
            clock.AdjustWallClock(TimeSpan.FromMinutes(2));
            timer.OnSuspending(); stopwatch.OnSuspending();
            clock.AdjustWallClock(TimeSpan.FromMinutes(1));
            Check.Throws<ArgumentOutOfRangeException>(() => timer.OnResumed(TimeSpan.FromSeconds(-1)));
            Check.Throws<ArgumentOutOfRangeException>(() => stopwatch.OnResumed(TimeSpan.FromSeconds(-1)));
            timer.OnResumed(); stopwatch.OnResumed();
            Check.Equal(TimeSpan.FromMinutes(2), timer.Remaining);
            Check.Equal(TimeSpan.FromMinutes(3), stopwatch.Elapsed);
        });
        suite.Add("Stopwatch pauses, captures cumulative laps, and resets cleanly", () =>
        {
            var clock = new ManualClock();
            var timer = new StopwatchTimer(clock);
            timer.Lap();
            Check.Equal(0, timer.Laps.Count);
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(12));
            timer.Lap();
            timer.Pause();
            clock.Advance(TimeSpan.FromHours(1));
            Check.Equal(TimeSpan.FromSeconds(12), timer.Elapsed);
            timer.Start();
            clock.Advance(TimeSpan.FromSeconds(5));
            timer.Lap();
            Check.Equal(TimeSpan.FromSeconds(17), timer.Laps[1]);
            timer.Reset();
            Check.Equal(TimeSpan.Zero, timer.Elapsed);
            Check.False(timer.IsRunning);
            Check.Equal(0, timer.Laps.Count);
        });
        suite.Add("Focus sessions isolate simultaneous timers and format long durations", () =>
        {
            var clock = new ManualClock();
            var focus = new FocusSession(clock, focusMinutes: 45, hydrationMinutes: 15);
            focus.Pomodoro.Start();
            focus.Hydration.Start();
            clock.Advance(TimeSpan.FromMinutes(3));
            Check.Equal(TimeSpan.FromMinutes(42), focus.Pomodoro.Remaining);
            Check.Equal(TimeSpan.FromMinutes(12), focus.Hydration.Remaining);
            Check.Equal(TimeSpan.FromMinutes(5), focus.Countdown.Remaining);
            Check.Equal("01:05", FocusSession.Format(TimeSpan.FromSeconds(65)));
            Check.Equal("1:01:05", FocusSession.Format(TimeSpan.FromSeconds(3665)));
        });
    }
}
