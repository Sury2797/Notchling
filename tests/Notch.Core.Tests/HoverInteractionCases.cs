using Notch.Core;

namespace Notch.Core.Tests;

internal static class HoverInteractionCases
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
        public TimeSpan Elapsed { get; private set; }
        public void Advance(TimeSpan amount) => Elapsed += amount;
    }

    public static void Register(TestSuite suite)
    {
        suite.Add("Hover leave collapses after its finite grace even without another pointer event", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false);
            Check.False(policy.ShouldCollapse(true, false, false, false));
            Check.False(policy.ShouldCollapse(false, false, false, false));
            clock.Advance(TimeSpan.FromMilliseconds(449));
            Check.False(policy.ShouldCollapse(false, false, false, false));
            clock.Advance(TimeSpan.FromMilliseconds(1));
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Reentering the panel cancels an earlier hover leave deadline", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.ShouldCollapse(false, false, false, false);
            clock.Advance(TimeSpan.FromSeconds(1));
            Check.False(policy.ShouldCollapse(true, false, false, false));
            Check.False(policy.ShouldCollapse(false, false, false, false));
            clock.Advance(HoverInteractionPolicy.LeaveGrace);
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Explicit tray or keyboard open waits for a pointer visit and then dismisses normally", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(true); policy.ShouldCollapse(false, false, false, false);
            clock.Advance(TimeSpan.FromMinutes(1));
            Check.False(policy.ShouldCollapse(false, false, false, false));
            Check.False(policy.ShouldCollapse(true, false, false, false));
            Check.False(policy.ShouldCollapse(false, false, false, false));
            clock.Advance(HoverInteractionPolicy.LeaveGrace);
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Deactivation releases an explicit lease without requiring a pointer visit", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(true); policy.ShouldCollapse(false, false, false, false);
            policy.ReleaseExplicitLease(); clock.Advance(HoverInteractionPolicy.LeaveGrace);
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Temporary dialog or drag protection is retried rather than leaving the panel stuck", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.ShouldCollapse(false, false, true, false);
            clock.Advance(TimeSpan.FromSeconds(4));
            Check.False(policy.ShouldCollapse(false, false, true, false));
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Pin protection survives a leave and releasing pin restores ordinary dismissal", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.ShouldCollapse(false, true, false, false);
            clock.Advance(TimeSpan.FromSeconds(10));
            Check.False(policy.ShouldCollapse(false, true, false, false));
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Focused controls alone cannot lease a hover panel forever", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.ShouldCollapse(false, false, false, true);
            clock.Advance(TimeSpan.FromSeconds(1));
            Check.True(policy.ShouldCollapse(false, false, false, true));
        });
        suite.Add("Actual keyboard edits protect an outside-pointer editor for six seconds then expire", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.RecordKeyboardInput(); policy.ShouldCollapse(false, false, false, true);
            clock.Advance(TimeSpan.FromSeconds(1));
            Check.False(policy.ShouldCollapse(false, false, false, true));
            clock.Advance(TimeSpan.FromSeconds(5));
            Check.True(policy.ShouldCollapse(false, false, false, true));
        });
        suite.Add("Further keyboard edits renew the lease and losing editor focus ends its protection", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.RecordKeyboardInput(); policy.ShouldCollapse(false, false, false, true);
            clock.Advance(TimeSpan.FromSeconds(5)); policy.RecordKeyboardInput();
            clock.Advance(TimeSpan.FromSeconds(2));
            Check.False(policy.ShouldCollapse(false, false, false, true));
            Check.True(policy.ShouldCollapse(false, false, false, false));
        });
        suite.Add("Wall-clock corrections cannot extend a hover leave or keyboard lease", () =>
        {
            var clock = new Clock(); var policy = new HoverInteractionPolicy(clock);
            policy.Begin(false); policy.RecordKeyboardInput(); policy.ShouldCollapse(false, false, false, true);
            clock.UtcNow = clock.UtcNow.AddYears(-1); clock.Advance(TimeSpan.FromSeconds(7));
            Check.True(policy.ShouldCollapse(false, false, false, true));
        });
    }
}
