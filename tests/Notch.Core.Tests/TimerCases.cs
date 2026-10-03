using Notch.Core;

namespace Notch.Core.Tests;

internal sealed class ManualClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);
    public void Advance(TimeSpan duration) => UtcNow += duration;
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
        suite.Add("A backward clock adjustment cannot increase remaining duration", () =>
        {
            var clock = new ManualClock();
            var timer = new CountdownTimer(clock, TimeSpan.FromMinutes(5));
            timer.Start();
            clock.Advance(TimeSpan.FromMinutes(-2));
            Check.Equal(timer.Duration, timer.Remaining);
            Check.Near(1, timer.Progress);
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
