using Notch.Core;

namespace Notch.Core.Tests;

internal static class OverlayCases
{
    private static LiveActivity Activity(ManualClock clock, string title = "Sale", int seconds = 10) =>
        new(Guid.NewGuid(), ActivityKind.Information, "Fixture", title, null, clock.UtcNow, TimeSpan.FromSeconds(seconds));

    public static void Register(TestSuite suite)
    {
        suite.Add("Dismissing an inactive activity leaves the selected panel and emits no change", () =>
        {
            var state = new OverlayStateMachine(new ManualClock());
            state.Expand(ModuleId.Notes);
            var changed = 0;
            state.Changed += (_, _) => changed++;

            state.DismissActivity();
            state.DismissActivity();

            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Notes, state.SelectedModule);
            Check.True(state.Activity is null);
            Check.Equal(0, changed);
        });
        suite.Add("Forced collapse dismisses current and queued activities without resurrecting a panel", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock) { Pinned = true };
            state.Expand(ModuleId.Media);
            state.ShowActivity(Activity(clock, "Current"));
            state.ShowActivity(Activity(clock, "Queued"));

            Check.True(state.Collapse(force: true));
            state.DismissActivity();
            clock.Advance(TimeSpan.FromSeconds(11));

            Check.False(state.Tick());
            Check.Equal(OverlayMode.Collapsed, state.Mode);
            Check.True(state.Activity is null);
            state.ShowActivity(Activity(clock, "New after collapse"));
            state.DismissActivity();
            Check.Equal(OverlayMode.Collapsed, state.Mode);
            Check.True(state.Activity is null);
        });
        suite.Add("Explicit panel navigation cancels activities and becomes the next restoration target", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Media);
            state.ShowActivity(Activity(clock, "Current"));
            state.ShowActivity(Activity(clock, "Queued"));

            state.Expand(ModuleId.Notes);
            state.DismissActivity();

            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Notes, state.SelectedModule);
            Check.True(state.Activity is null);
            state.ShowActivity(Activity(clock, "New after navigation"));
            state.DismissActivity();
            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Notes, state.SelectedModule);
            Check.True(state.Activity is null);
        });
        suite.Add("Valid queued activities drain in arrival order and restore the original panel once", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Media);
            var activities = new[] { Activity(clock, "First"), Activity(clock, "Second"), Activity(clock, "Third") };
            foreach (var activity in activities) Check.True(state.ShowActivity(activity));

            foreach (var activity in activities)
            {
                Check.Equal(OverlayMode.Activity, state.Mode);
                Check.Equal(activity, state.Activity);
                state.DismissActivity();
            }

            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Media, state.SelectedModule);
            Check.True(state.Activity is null);
            var changed = 0;
            state.Changed += (_, _) => changed++;
            state.DismissActivity();
            Check.Equal(0, changed);
            Check.Equal(OverlayMode.Expanded, state.Mode);
        });
        suite.Add("Pin protects an expanded module until an explicit forced collapse", () =>
        {
            var state = new OverlayStateMachine(new ManualClock()) { Pinned = true };
            state.Expand(ModuleId.Calendar);
            Check.False(state.Collapse());
            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Calendar, state.SelectedModule);
            Check.True(state.Collapse(force: true));
            Check.Equal(OverlayMode.Collapsed, state.Mode);
        });
        suite.Add("Live activity expiry restores the previously expanded panel", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Media);
            Check.True(state.ShowActivity(Activity(clock)));
            Check.Equal(OverlayMode.Activity, state.Mode);
            clock.Advance(TimeSpan.FromSeconds(11));
            Check.True(state.Tick());
            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Media, state.SelectedModule);
            Check.True(state.Activity is null);
            Check.False(state.Tick());
        });
        suite.Add("Expired and duplicate activities do not interrupt current work", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            var first = Activity(clock);
            Check.True(state.ShowActivity(first));
            Check.False(state.ShowActivity(first));
            var queued = Activity(clock, "Meeting");
            Check.True(state.ShowActivity(queued));
            Check.False(state.ShowActivity(queued));
            Check.False(state.ShowActivity(Activity(clock) with { CreatedAt = clock.UtcNow - TimeSpan.FromMinutes(1) }));
            Check.Throws<ArgumentOutOfRangeException>(() => state.ShowActivity(Activity(clock, seconds: 0)));
            Check.Throws<ArgumentOutOfRangeException>(() => state.ShowActivity(Activity(clock, seconds: 301)));
        });
        suite.Add("Activity queue is bounded and drops the oldest queued event under load", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.ShowActivity(Activity(clock, "Current", seconds: 120));
            for (var index = 1; index <= 17; index++) state.ShowActivity(Activity(clock, $"Queued {index}", seconds: 120));
            state.DismissActivity();
            Check.Equal("Queued 2", state.Activity?.Title);
            var count = 0;
            while (state.Activity is not null)
            {
                count++;
                state.DismissActivity();
                Check.True(count < 30, "Activity queue failed to drain");
            }
            Check.Equal(16, count);
            Check.Equal(OverlayMode.Collapsed, state.Mode);
        });
        suite.Add("Queued activities that expire while waiting are skipped", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.ShowActivity(Activity(clock, "Current", seconds: 30));
            state.ShowActivity(Activity(clock, "Expired in queue", seconds: 5));
            state.ShowActivity(Activity(clock, "Still useful", seconds: 30));
            clock.Advance(TimeSpan.FromSeconds(10));
            state.DismissActivity();
            Check.Equal("Still useful", state.Activity?.Title);
        });
    }
}
