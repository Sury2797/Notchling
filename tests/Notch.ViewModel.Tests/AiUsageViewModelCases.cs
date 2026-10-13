using System.Reflection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Core.AiUsage;
using Notch.Windows.ViewModels;

internal static class AiUsageViewModelCases
{
    private static void Assert(bool value, string detail)
    { if (!value) throw new InvalidOperationException(detail); }
    private static DispatcherTimer Timer() => DispatcherTimer.Instances.Single(timer => timer.Interval == TimeSpan.FromSeconds(45));
    private static Task Pending(MainViewModel vm) => (Task?)typeof(MainViewModel)
        .GetField("_aiUsageReadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm) ?? Task.CompletedTask;
    private static MainViewModel Create(string data, UsageSource source) => new(new DispatcherQueue(), dataDirectory: data,
        mediaService: new ConnectionMediaDouble(), systemService: new ConnectionSystemDouble(),
        vault: new ConnectionVaultDouble(), aiUsageService: source);
    private static AiUsageSnapshot Observation()
    {
        var observed = new DateTimeOffset(2026, 10, 13, 12, 10, 0, TimeSpan.Zero);
        return new([
            new(AiUsageProvider.Codex, "Codex", AiUsageState.Partial, new(100, 50, 30, 0, 5), 2,
                observed, observed, observed, "Local Codex session records", "Only the readable local sessions are included.",
                [new("codex", "Codex", [new("primary", 74.25, TimeSpan.FromHours(5), observed.AddHours(1))], observed, true)]),
            new(AiUsageProvider.Claude, "Claude", AiUsageState.NotFound, null, 0, null, observed, null,
                "Local Claude project records", "No supported local usage records were found.", [])
        ], observed);
    }

    public static async Task RegisterAsync(Func<string, Func<Task>, Task> run, string data)
    {
        await run("AI usage publishes supplied observations without inventing tokens or account quotas", async () =>
        {
            var source = new UsageSource(); var observed = Observation(); source.Result = observed;
            await using var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); await Pending(vm);
            Assert(source.Reads == 1 && source.ForceRequests.SequenceEqual([false])
                && ReferenceEquals(vm.AiUsage, observed) && !vm.IsReadingAiUsage && Timer().IsEnabled,
                "The visible panel did not publish the adapter's original observed snapshot.");
            var codex = vm.AiUsage!.Providers.Single(provider => provider.Provider == AiUsageProvider.Codex);
            var claude = vm.AiUsage.Providers.Single(provider => provider.Provider == AiUsageProvider.Claude);
            Assert(codex.Tokens is { InputTokens: 100, OutputTokens: 50, CachedInputTokens: 30, ReasoningOutputTokens: 5, TotalTokens: 150 }
                && codex.State == AiUsageState.Partial && codex.Quotas.Single().IsStale
                && codex.Quotas.Single().Windows.Single().RemainingPercent == 25.75
                && claude.Tokens is null && claude.Quotas.Count == 0 && claude.State == AiUsageState.NotFound,
                "Subset tokens were double-counted or missing quota/counts were replaced with fabricated values.");
            await vm.RefreshAiUsageAsync(); Timer().Fire(); await Pending(vm);
            Assert(source.Reads == 3 && source.ForceRequests.SequenceEqual([false, true, false])
                && vm.AiUsage.CheckedAt == observed.CheckedAt,
                "Manual refresh did not bypass the adapter cache or periodic refresh changed an observation timestamp.");
            vm.SetAiUsagePanelVisible(false); Timer().Fire(); await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 3 && !Timer().IsEnabled, "Closing the panel continued local scanning.");
        });

        await run("AI usage refuses local reads outside its visible expanded panel", async () =>
        {
            var source = new UsageSource(); await using var vm = Create(data, source); await vm.InitializeAsync();
            await vm.RefreshAiUsageAsync(); vm.SetAiUsagePanelVisible(true); Timer().Fire();
            Assert(source.Reads == 0 && vm.AiUsage is null && !Timer().IsEnabled,
                "A hidden tool scanned local files or displayed sample usage.");
            vm.SelectModule(ModuleId.AiUsage); vm.Overlay.Collapse(); vm.SetAiUsagePanelVisible(true);
            await vm.RefreshAiUsageAsync(); Timer().Fire();
            Assert(source.Reads == 0 && !vm.IsReadingAiUsage, "The compact island initiated an AI disk read.");
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); await Pending(vm);
            vm.SelectModule(ModuleId.Notes); await vm.RefreshAiUsageAsync(); Timer().Fire();
            Assert(source.Reads == 1, "Selecting another tool left a reader scanning in the background.");
        });

        await run("AI usage native host visibility prevents hidden-window scanning and late publication", async () =>
        {
            var source = new UsageSource(); var visible = false;
            await using var vm = Create(data, source); vm.CanReadAiUsage = () => visible; await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 0 && !Timer().IsEnabled, "A hidden native window scanned local usage.");
            using var gate = new UsageGate(); source.Reader = gate.ReadAsync; visible = true;
            vm.SetAiUsagePanelVisible(true); var pending = Pending(vm); await gate.Started.Task;
            visible = false; gate.Release.TrySetResult(Observation()); await pending;
            Assert(vm.AiUsage is null && !vm.IsReadingAiUsage, "A reader published after its native host was hidden.");
            Timer().Fire(); await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 1 && !Timer().IsEnabled, "The native host visibility gate failed to stop periodic reading.");
            visible = true; source.Reader = null; vm.SetAiUsagePanelVisible(true); await Pending(vm);
            Assert(source.Reads == 2 && vm.AiUsage is not null, "Showing the actual native host could not resume local reads.");
        });

        await run("AI usage allows only one active refresh while a source read is pending", async () =>
        {
            using var gate = new UsageGate(); var source = new UsageSource { Reader = gate.ReadAsync };
            await using var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); var pending = Pending(vm);
            await gate.Started.Task;
            Assert(vm.IsReadingAiUsage && source.Reads == 1 && !pending.IsCompleted,
                "A pending source read did not expose its real busy state.");
            await vm.RefreshAiUsageAsync(); await vm.RefreshAiUsageAsync(); Timer().Fire();
            Assert(source.Reads == 1, "Repeated refresh actions overlapped a local scan.");
            gate.Release.TrySetResult(Observation()); await pending;
            Assert(!vm.IsReadingAiUsage && vm.AiUsage is not null, "A finished read left the panel permanently busy.");
            source.Reader = null; await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 2, "Refresh could not retry after the original operation finished.");
        });

        await run("AI usage discards a delayed result after the panel closes", async () =>
        {
            using var gate = new UsageGate(); var source = new UsageSource { Reader = gate.ReadAsync };
            await using var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); var pending = Pending(vm);
            await gate.Started.Task; vm.SetAiUsagePanelVisible(false);
            var latePublications = 0;
            vm.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(MainViewModel.AiUsage)) latePublications++; };
            gate.Release.TrySetResult(Observation()); await pending; await gate.Finished.Task;
            Assert(vm.AiUsage is null && latePublications == 0 && !vm.IsReadingAiUsage && !Timer().IsEnabled,
                "A cancelled source which finished late changed the hidden view.");
            source.Reader = null; vm.SetAiUsagePanelVisible(true); await Pending(vm);
            Assert(vm.AiUsage is not null && source.Reads == 2, "A closed panel could not resume with a fresh read.");
        });

        await run("AI usage never reads or fabricates data in sample preview", async () =>
        {
            using var gate = new UsageGate(); var source = new UsageSource { Reader = gate.ReadAsync };
            await using var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); var pending = Pending(vm);
            await gate.Started.Task; await vm.SetPreferencesAsync(vm.Preferences with { DemoMode = true });
            vm.SetAiUsagePanelVisible(true); gate.Release.TrySetResult(Observation()); await pending; await gate.Finished.Task;
            Timer().Fire(); await vm.RefreshAiUsageAsync();
            Assert(vm.AiUsage is null && source.Reads == 1 && !Timer().IsEnabled && !vm.IsReadingAiUsage,
                "Sample preview read local usage, published a late real response or supplied simulated tokens.");
            await vm.ExitDemoAsync(); source.Reader = null; vm.SelectModule(ModuleId.AiUsage);
            vm.SetAiUsagePanelVisible(true); await Pending(vm);
            Assert(source.Reads == 2 && vm.AiUsage is not null, "Real local usage could not resume after exiting sample preview.");
        });

        await run("AI usage source failure preserves the previous observation with truthful freshness", async () =>
        {
            var source = new UsageSource(); var observed = Observation(); source.Result = observed;
            await using var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); await Pending(vm);
            source.Reader = _ => Task.FromException<AiUsageSnapshot>(new IOException("Private path must not enter UI."));
            await vm.RefreshAiUsageAsync();
            Assert(ReferenceEquals(vm.AiUsage, observed) && vm.AiUsage!.CheckedAt == observed.CheckedAt
                && !vm.IsReadingAiUsage && vm.AiUsageStatus.Contains("could not be read", StringComparison.Ordinal)
                && !vm.AiUsageStatus.Contains("Private path", StringComparison.Ordinal) && vm.Error == "",
                "A failed check erased a real observation, falsely refreshed its time or exposed private exception text.");
            source.Reader = null; await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 3 && !vm.IsReadingAiUsage, "A recoverable source failure disabled retry.");
        });

        await run("AI usage shutdown cancels pending reads without publishing to released views", async () =>
        {
            using var gate = new UsageGate(); var source = new UsageSource { Reader = gate.ReadAsync };
            var vm = Create(data, source); await vm.InitializeAsync();
            vm.SelectModule(ModuleId.AiUsage); vm.SetAiUsagePanelVisible(true); var pending = Pending(vm);
            await gate.Started.Task;
            var lateNotifications = 0;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.AiUsage) or nameof(MainViewModel.IsReadingAiUsage)
                    or nameof(MainViewModel.AiUsageStatus)) lateNotifications++;
            };
            await vm.DisposeAsync(); gate.Release.TrySetResult(Observation()); await pending; await gate.Finished.Task;
            Assert(!vm.IsReady && vm.AiUsage is null && !vm.IsReadingAiUsage && !Timer().IsEnabled && lateNotifications == 0,
                "Shutdown published late usage or left a polling timer alive.");
            vm.SetAiUsagePanelVisible(true); Timer().Fire(); await vm.RefreshAiUsageAsync();
            Assert(source.Reads == 1, "A disposed ViewModel restarted local disk scanning.");
        });
    }

    private sealed class UsageSource : IAiUsageService
    {
        private int _reads;
        private readonly List<bool> _forceRequests = [];
        public int Reads => Volatile.Read(ref _reads);
        public IReadOnlyList<bool> ForceRequests { get { lock (_forceRequests) return _forceRequests.ToArray(); } }
        public AiUsageSnapshot Result { get; set; } = Observation();
        public Func<CancellationToken, Task<AiUsageSnapshot>>? Reader { get; set; }
        public Task<AiUsageSnapshot> ReadAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reads); lock (_forceRequests) _forceRequests.Add(forceRefresh);
            return Reader?.Invoke(cancellationToken) ?? Task.FromResult(Result);
        }
    }

    // Deliberately ignores cancellation, proving that lifetime/visibility guards
    // reject a late adapter result even when an external implementation misbehaves.
    private sealed class UsageGate : IDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AiUsageSnapshot> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<AiUsageSnapshot> ReadAsync(CancellationToken _)
        {
            Started.TrySetResult();
            try { return await Release.Task; }
            finally { Finished.TrySetResult(); }
        }
        public void Dispose() => Release.TrySetCanceled();
    }
}
