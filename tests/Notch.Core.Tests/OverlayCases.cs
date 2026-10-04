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
            state.DismissActivity(); state.DismissActivity();
            Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Notes, state.SelectedModule);
            Check.True(state.Activity is null); Check.Equal(0, changed);
        });
        suite.Add("Forced collapse preserves current and queued reminders for later presentation", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock) { Pinned = true };
            state.Expand(ModuleId.Media);
            state.ShowActivity(Activity(clock, "Current"));
            state.ShowActivity(Activity(clock, "Queued"));
            clock.Advance(TimeSpan.FromSeconds(3));
            Check.True(state.Collapse(force: true));
            Check.Equal(OverlayMode.Collapsed, state.Mode); Check.Equal(2, state.PendingCount);
            state.DismissActivity(); // No visible activity to dismiss while navigation is deferred.
            clock.Advance(TimeSpan.FromHours(1));
            Check.True(state.Tick()); Check.Equal("Current", state.Activity?.Title);
            clock.Advance(TimeSpan.FromSeconds(7));
            Check.True(state.Tick()); Check.Equal("Queued", state.Activity?.Title);
            state.DismissActivity();
            Check.Equal(OverlayMode.Collapsed, state.Mode); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Explicit panel navigation preserves reminders and changes their restoration target", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Media);
            state.ShowActivity(Activity(clock, "Current"));
            state.ShowActivity(Activity(clock, "Queued"));
            state.Expand(ModuleId.Notes);
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(2, state.PendingCount);
            Check.True(state.Tick()); Check.Equal("Current", state.Activity?.Title);
            state.DismissActivity(); Check.Equal("Queued", state.Activity?.Title);
            state.DismissActivity();
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(ModuleId.Notes, state.SelectedModule);
            Check.True(state.Activity is null); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Multiple simultaneous reminders each receive their full visible duration", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Media);
            var activities = new[] { Activity(clock, "First", 8), Activity(clock, "Second", 8), Activity(clock, "Third", 8) };
            var presented = new List<Guid>(); var completed = new List<Guid>();
            state.ActivityPresented += (_, activity) => presented.Add(activity.Id);
            state.ActivityCompleted += (_, activity) => completed.Add(activity.Id);
            foreach (var activity in activities) Check.True(state.ShowActivity(activity));
            Check.Equal(1, presented.Count);
            foreach (var activity in activities)
            {
                Check.Equal(activity, state.Activity);
                clock.Advance(TimeSpan.FromSeconds(7)); Check.False(state.Tick());
                Check.Equal(activity, state.Activity);
                clock.Advance(TimeSpan.FromSeconds(1)); Check.True(state.Tick());
            }
            Check.Equal(3, presented.Count); Check.Equal(3, completed.Count);
            Check.Equal(activities[2].Id, presented[2]);
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(ModuleId.Media, state.SelectedModule);
            Check.True(state.Activity is null); Check.False(state.Tick());
        });
        suite.Add("Pin protects an expanded module until an explicit forced collapse", () =>
        {
            var state = new OverlayStateMachine(new ManualClock()) { Pinned = true };
            state.Expand(ModuleId.Calendar);
            Check.False(state.Collapse()); Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(ModuleId.Calendar, state.SelectedModule);
            Check.True(state.Collapse(force: true)); Check.Equal(OverlayMode.Collapsed, state.Mode);
        });
        suite.Add("Activity lifetime starts when visible and ignores wall-clock corrections", () =>
        {
            var clock = new ManualClock();
            var state = new OverlayStateMachine(clock);
            var old = Activity(clock) with { CreatedAt = clock.UtcNow - TimeSpan.FromDays(1) };
            Check.True(state.ShowActivity(old));
            clock.Advance(TimeSpan.FromSeconds(4));
            clock.AdjustWallClock(TimeSpan.FromHours(5)); Check.False(state.Tick());
            clock.AdjustWallClock(TimeSpan.FromHours(-10)); Check.False(state.Tick());
            clock.Advance(TimeSpan.FromSeconds(6)); Check.True(state.Tick());
            Check.Equal(OverlayMode.Collapsed, state.Mode); Check.True(state.Activity is null);
        });
        suite.Add("Duplicate activities and invalid durations do not alter the queue", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            var first = Activity(clock); Check.True(state.ShowActivity(first)); Check.False(state.ShowActivity(first));
            var queued = Activity(clock, "Meeting"); Check.True(state.ShowActivity(queued)); Check.False(state.ShowActivity(queued));
            Check.Equal(2, state.PendingCount);
            Check.Throws<ArgumentOutOfRangeException>(() => state.ShowActivity(Activity(clock, seconds: 0)));
            Check.Throws<ArgumentOutOfRangeException>(() => state.ShowActivity(Activity(clock, seconds: 301)));
            Check.Equal(2, state.PendingCount);
        });
        suite.Add("Queue overload rejects new events without evicting existing reminders", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            var first = Activity(clock, "First"); Check.True(state.ShowActivity(first));
            for (var index = 1; index < OverlayStateMachine.MaximumPendingActivities; index++)
                Check.True(state.ShowActivity(Activity(clock, $"Queued {index}")));
            var retry = Activity(clock, "Retry after capacity is available");
            Check.False(state.ShowActivity(retry)); Check.Equal(OverlayStateMachine.MaximumPendingActivities, state.PendingCount);
            Check.Equal("First", state.Activity?.Title);
            state.DismissActivity(); Check.Equal("Queued 1", state.Activity?.Title);
            Check.True(state.ShowActivity(retry));
            var count = 0;
            while (state.Activity is not null)
            {
                count++; state.DismissActivity();
                Check.True(count <= OverlayStateMachine.MaximumPendingActivities, "Activity queue failed to drain");
            }
            Check.Equal(OverlayStateMachine.MaximumPendingActivities, count); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Interaction suppression defers every reminder without acknowledging premature delivery", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Notes); state.SetInteractionSuppressed(true);
            var presented = 0; state.ActivityPresented += (_, _) => presented++;
            state.ShowActivity(Activity(clock, "First", 8)); state.ShowActivity(Activity(clock, "Second", 8));
            clock.Advance(TimeSpan.FromHours(4));
            Check.False(state.Tick()); Check.Equal(OverlayMode.Expanded, state.Mode);
            Check.Equal(0, presented); Check.Equal(2, state.PendingCount);
            state.SetInteractionSuppressed(false);
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.True(state.Tick()); Check.Equal(1, presented);
            clock.Advance(TimeSpan.FromSeconds(8)); Check.True(state.Tick()); Check.Equal("Second", state.Activity?.Title);
            Check.Equal(2, presented); clock.Advance(TimeSpan.FromSeconds(8)); Check.True(state.Tick());
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Suppressing an active presentation preserves remaining duration and acknowledges only once", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            var presented = 0; state.ActivityPresented += (_, _) => presented++;
            state.ShowActivity(Activity(clock)); clock.Advance(TimeSpan.FromSeconds(3));
            state.SetInteractionSuppressed(true); Check.True(state.Activity is null);
            clock.Advance(TimeSpan.FromHours(1)); Check.False(state.Tick());
            state.SetInteractionSuppressed(false); Check.True(state.Tick()); Check.Equal(1, presented);
            clock.Advance(TimeSpan.FromSeconds(6)); Check.False(state.Tick());
            clock.Advance(TimeSpan.FromSeconds(1)); Check.True(state.Tick());
            Check.Equal(0, state.PendingCount); Check.Equal(1, presented);
        });
        suite.Add("Native-focus reconciliation can suppress before the first delivery acknowledgment", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            var first = true; var presented = 0;
            state.Changed += (_, _) =>
            {
                if (state.Mode != OverlayMode.Activity || !first) return;
                first = false; state.SetInteractionSuppressed(true);
            };
            state.ActivityPresented += (_, _) => presented++;
            state.ShowActivity(Activity(clock));
            Check.Equal(0, presented); Check.True(state.Activity is null); Check.Equal(1, state.PendingCount);
            state.SetInteractionSuppressed(false); Check.True(state.Tick()); Check.Equal(1, presented);
            state.DismissActivity(); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Canceling a queued reminder never acknowledges it and preserves the remaining order", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            var first = Activity(clock, "First"); var removed = Activity(clock, "Removed"); var last = Activity(clock, "Last");
            var presented = new List<Guid>(); var completed = 0;
            state.ActivityPresented += (_, item) => presented.Add(item.Id);
            state.ActivityCompleted += (_, _) => completed++;
            state.ShowActivity(first); state.ShowActivity(removed); state.ShowActivity(last);
            Check.True(state.CancelActivity(removed.Id)); Check.False(state.CancelActivity(removed.Id));
            Check.Equal(2, state.PendingCount); Check.Equal(first, state.Activity);
            state.DismissActivity(); Check.Equal(last, state.Activity);
            state.DismissActivity(); Check.Equal(2, presented.Count); Check.Equal(2, completed);
            Check.False(presented.Contains(removed.Id)); Check.Equal(0, state.PendingCount);
        });
        suite.Add("Canceling an active reminder gives the next event a full duration without a completion", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Calendar);
            var first = Activity(clock, "First"); var next = Activity(clock, "Next", 8);
            var completed = 0; state.ActivityCompleted += (_, _) => completed++;
            state.ShowActivity(first); state.ShowActivity(next); clock.Advance(TimeSpan.FromSeconds(9));
            Check.True(state.CancelActivity(first.Id)); Check.Equal(next, state.Activity); Check.Equal(0, completed);
            clock.Advance(TimeSpan.FromSeconds(7)); Check.False(state.Tick());
            clock.Advance(TimeSpan.FromSeconds(1)); Check.True(state.Tick()); Check.Equal(1, completed);
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(ModuleId.Calendar, state.SelectedModule);
        });
        suite.Add("Clearing deferred reminders leaves the selected editor intact and emits no false deliveries", () =>
        {
            var clock = new ManualClock(); var state = new OverlayStateMachine(clock);
            state.Expand(ModuleId.Notes); state.SetInteractionSuppressed(true);
            var presented = 0; var completed = 0;
            state.ActivityPresented += (_, _) => presented++; state.ActivityCompleted += (_, _) => completed++;
            state.ShowActivity(Activity(clock)); state.ShowActivity(Activity(clock));
            state.ClearActivities(); state.SetInteractionSuppressed(false);
            Check.False(state.Tick()); Check.Equal(0, state.PendingCount);
            Check.Equal(0, presented); Check.Equal(0, completed);
            Check.Equal(OverlayMode.Expanded, state.Mode); Check.Equal(ModuleId.Notes, state.SelectedModule);
            state.ShowActivity(Activity(clock)); Check.Equal(1, presented);
            state.ClearActivities(); Check.Equal(0, completed); Check.Equal(OverlayMode.Expanded, state.Mode);
        });
    }
}
