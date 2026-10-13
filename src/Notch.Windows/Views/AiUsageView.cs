using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Notch.Core.AiUsage;
using Notch.Windows.ViewModels;

namespace Notch.Windows.Views;

/// <summary>Actual local observations, with account quota kept separate from token activity.</summary>
public sealed class AiUsageView : UserControl, IDisposable
{
    private readonly MainViewModel _vm;
    private readonly Grid _header = new() { ColumnSpacing = 12, RowSpacing = 10 };
    private readonly StackPanel _heading = new() { Spacing = 5 };
    private readonly StackPanel _refreshContent = new() { Orientation = Orientation.Horizontal, Spacing = 7 };
    private readonly Button _refresh;
    private readonly ProgressRing _busy = new() { Width = 16, Height = 16, IsActive = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = Text("", 12, muted: true);
    private readonly TextBlock _freshness = Text("Read-only · stays on this device", 12, muted: true);
    private readonly ProviderCard _codex = new(AiUsageProvider.Codex, "Codex");
    private readonly ProviderCard _claude = new(AiUsageProvider.Claude, "Claude");
    private bool _attached;
    private bool _disposed;

    public AiUsageView(MainViewModel viewModel)
    {
        _vm = viewModel;
        FontFamily = new FontFamily("Segoe UI");
        Foreground = NativeTheme.Foreground;
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        titleRow.Children.Add(Text("AI usage", 22, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        var help = new ContextHelp
        {
            Heading = "Real local AI usage",
            Description = "Notchling reads supported Codex and Claude CLI usage records on this device. It does not open credentials, sign into your account, upload transcripts or request provider APIs.\n\nToken totals describe observed local activity, not your remaining plan allowance. Cached input and reasoning output are subsets and must not be added again.\n\nCodex quota windows appear only when the provider recorded a real percentage. Their timestamp is shown, and old observations are marked stale. Claude plan quotas and other assistants have no supported local source yet. No number is estimated or simulated.\n\nSources are Codex sessions and archived_sessions under CODEX_HOME (normally your user-profile .codex folder), and Claude projects under CLAUDE_CONFIG_DIR (normally .claude). WSL-only, browser-only and other-device activity is not automatically scanned.\n\nChecks run every 45 seconds while this panel is open. Missing, incomplete and inaccessible records remain labelled; the displayed history may cover only the bounded set of readable local sessions.",
        };
        AutomationProperties.SetAutomationId(help, "AiUsageHelp");
        titleRow.Children.Add(help);
        _heading.Children.Add(titleRow);
        _heading.Children.Add(Text("Codex and Claude · actual local records", 13, muted: true));
        _header.Children.Add(_heading);
        _refreshContent.Children.Add(new VisualIcon { Kind = VisualIconKind.Refresh, Size = 16 });
        _refreshContent.Children.Add(Text("Refresh", 12, weight: Microsoft.UI.Text.FontWeights.SemiBold));
        _refreshContent.Children.Add(_busy);
        _refresh = new Button
        {
            Content = _refreshContent,
            Style = (Style)Application.Current.Resources["NotchFormButtonStyle"],
            MinWidth = 0, VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(_refresh, "AiUsageRefresh");
        AutomationProperties.SetName(_refresh, "Refresh actual local AI usage");
        ToolTipService.SetToolTip(_refresh, "Read supported local usage records again. No provider account request is sent.");
        _refresh.Click += async (_, _) => await _vm.RefreshAiUsageAsync();
        _header.Children.Add(_refresh);
        var content = new StackPanel { Spacing = 14, Padding = new Thickness(20, 20, 20, 18) };
        content.Children.Add(_header);
        AutomationProperties.SetAutomationId(_status, "AiUsageStatus");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        content.Children.Add(_status);
        content.Children.Add(_codex.Element);
        content.Children.Add(_claude.Element);
        content.Children.Add(_freshness);
        content.Children.Add(Text("Other assistants: no supported local usage adapter yet. Browser-only activity and account-wide limits cannot be inferred from CLI logs.", 12, muted: true));
        var scroll = new ScrollViewer
        {
            Content = content, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled, IsTabStop = false,
        };
        AutomationProperties.SetAutomationId(scroll, "ModuleScroll");
        AutomationProperties.SetName(scroll, "Notchling tool content");
        Content = scroll;
        _header.SizeChanged += (_, _) => ArrangeHeader();
        Loaded += (_, _) =>
        {
            if (_disposed) return;
            if (!_attached)
            {
                _vm.PropertyChanged += Changed;
                NativeTheme.Changed += ThemeChanged;
                _attached = true;
            }
            ArrangeHeader(); Update();
        };
        Unloaded += (_, _) =>
        {
            _vm.PropertyChanged -= Changed;
            NativeTheme.Changed -= ThemeChanged;
            _attached = false;
        };
        ArrangeHeader(); Update();
    }

    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.AiUsage) or nameof(MainViewModel.IsReadingAiUsage)
            or nameof(MainViewModel.AiUsageStatus) or nameof(MainViewModel.IsReady) or nameof(MainViewModel.IsDemo)) Update();
    }

    private void ThemeChanged(object? sender, EventArgs args)
    {
        ArrangeHeader(); _codex.Arrange(); _claude.Arrange();
    }

    private void ArrangeHeader()
    {
        var narrow = _header.ActualWidth < 410 * Math.Max(1, NativeTheme.TextScaleFactor);
        _header.RowDefinitions.Clear(); _header.ColumnDefinitions.Clear();
        _header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _header.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(_heading, 0); Grid.SetColumn(_heading, 0);
        if (narrow)
        {
            _header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(_refresh, 1); Grid.SetColumn(_refresh, 0);
            _refresh.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(_refresh, 0); Grid.SetColumn(_refresh, 1);
            _refresh.HorizontalAlignment = HorizontalAlignment.Right;
        }
    }

    private void Update()
    {
        if (_disposed) return;
        _refresh.IsEnabled = _vm.IsReady && !_vm.IsReadingAiUsage && !_vm.IsDemo;
        _busy.IsActive = _vm.IsReadingAiUsage;
        _busy.Visibility = _vm.IsReadingAiUsage ? Visibility.Visible : Visibility.Collapsed;
        var status = _vm.IsDemo ? "AI usage has no sample data. Exit sample-data preview to read real local records." : _vm.AiUsageStatus;
        if (_status.Text != status)
        {
            _status.Text = status;
            if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
                (FrameworkElementAutomationPeer.FromElement(_status) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_status))
                    ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        // Update existing native controls in place. A refresh must not lose scroll,
        // accessibility position, keyboard focus or an open help explanation.
        var snapshot = _vm.IsDemo ? null : _vm.AiUsage;
        _codex.Update(snapshot?.Providers.FirstOrDefault(provider => provider.Provider == AiUsageProvider.Codex));
        _claude.Update(snapshot?.Providers.FirstOrDefault(provider => provider.Provider == AiUsageProvider.Claude));
        _freshness.Text = snapshot is null ? "Read-only · stays on this device"
            : "Local check: " + Timestamp(snapshot.CheckedAt) + " · no account-wide total is inferred";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vm.PropertyChanged -= Changed;
        NativeTheme.Changed -= ThemeChanged;
        _attached = false;
        _busy.IsActive = false;
        _vm.SetAiUsagePanelVisible(false);
    }

    private static string Timestamp(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    private static string Count(long value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static TextBlock Text(string value, double size = 13, bool muted = false, global::Windows.UI.Text.FontWeight? weight = null)
        => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
            Foreground = muted ? NativeTheme.Muted : NativeTheme.Foreground,
            FontWeight = weight ?? Microsoft.UI.Text.FontWeights.Normal };

    private sealed class ProviderCard
    {
        private readonly AiUsageProvider _provider;
        private readonly TextBlock _state = Text("Not checked", 12, muted: true);
        private readonly TextBlock _total = Text("No reading yet", 27, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        private readonly TextBlock _summary = Text("", 12, muted: true);
        private readonly TextBlock _detail = Text("", 12, muted: true);
        private readonly TextBlock _source = Text("", 12, muted: true);
        private readonly Grid _metrics = new() { ColumnSpacing = 18, RowSpacing = 8 };
        private readonly TextBlock[] _values = [Text(""), Text(""), Text(""), Text(""), Text(""), Text("")];
        private readonly StackPanel _quota = new() { Spacing = 10 };
        private readonly Dictionary<string, QuotaRow> _quotaRows = new(StringComparer.Ordinal);
        private readonly TextBlock _missingQuota = Text("", 12, muted: true);
        private readonly TextBlock _quotaHeading = Text("Provider-reported quota", 13, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        public Border Element { get; }

        public ProviderCard(AiUsageProvider provider, string name)
        {
            _provider = provider;
            var stack = new StackPanel { Spacing = 10 };
            var heading = new Grid { ColumnSpacing = 10 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            // Neutral local vectors keep provider labels crisp without remote logos
            // or an installed glyph font. They do not claim an account connection.
            var icon = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10),
                Background = NativeTheme.Brush("#232323"), Child = new VisualIcon
                { Kind = provider == AiUsageProvider.Codex ? VisualIconKind.Coding : VisualIconKind.Analytics, Size = 19,
                    Foreground = NativeTheme.Foreground } };
            heading.Children.Add(icon);
            var label = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Text(name, 16, weight: Microsoft.UI.Text.FontWeights.SemiBold));
            label.Children.Add(_state); heading.Children.Add(label); Grid.SetColumn(label, 1);
            stack.Children.Add(heading);
            stack.Children.Add(Text("Observed local tokens", 12, muted: true));
            stack.Children.Add(_total); stack.Children.Add(_summary);
            foreach (var value in _values) _metrics.Children.Add(value);
            stack.Children.Add(_metrics); stack.Children.Add(_detail);
            stack.Children.Add(new Border { Height = 1, Background = NativeTheme.Border, Margin = new Thickness(0, 3, 0, 3) });
            stack.Children.Add(_quotaHeading); stack.Children.Add(_missingQuota); stack.Children.Add(_quota); stack.Children.Add(_source);
            Element = new Border { Background = NativeTheme.Card, BorderBrush = NativeTheme.Border,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = stack };
            AutomationProperties.SetAutomationId(Element, "AiUsage" + provider + "Card");
            AutomationProperties.SetAutomationId(_total, "AiUsage" + provider + "Tokens");
            AutomationProperties.SetAutomationId(_state, "AiUsage" + provider + "State");
            _metrics.SizeChanged += (_, _) => Arrange();
            Update(null);
        }

        public void Arrange()
        {
            var columns = _metrics.ActualWidth >= 340 * Math.Max(1, NativeTheme.TextScaleFactor) ? 2 : 1;
            _metrics.ColumnDefinitions.Clear(); _metrics.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++) _metrics.ColumnDefinitions.Add(new ColumnDefinition());
            for (var row = 0; row < (6 + columns - 1) / columns; row++)
                _metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < _values.Length; index++)
            { Grid.SetRow(_values[index], index / columns); Grid.SetColumn(_values[index], index % columns); }
        }

        public void Update(AiProviderUsage? reading)
        {
            _state.Text = reading?.State switch
            {
                AiUsageState.Available => "Local records available", AiUsageState.Partial => "Partial local reading",
                AiUsageState.NotFound => "No local records found", AiUsageState.NoUsage => "No token usage recorded",
                AiUsageState.Unavailable => "Local source unavailable", AiUsageState.Stale => "Previous local reading",
                _ => "Not checked",
            };
            _total.Text = reading?.Tokens is { } tokens ? Count(tokens.TotalTokens) : "No token reading";
            _summary.Text = reading is null ? "A supported CLI needs to have recorded local usage first."
                : reading.Sessions.ToString("N0", CultureInfo.CurrentCulture) + " observed local session"
                    + (reading.Sessions == 1 ? "" : "s")
                    + (reading.LastActivityAt is { } activity ? " · last activity " + Timestamp(activity) : "");
            _metrics.Visibility = reading?.Tokens is null ? Visibility.Collapsed : Visibility.Visible;
            if (reading?.Tokens is { } value)
            {
                string[] values = ["Input: " + Count(value.InputTokens), "Output: " + Count(value.OutputTokens),
                    "Cached input: " + Count(value.CachedInputTokens), "Cache writes: " + Count(value.CacheWriteInputTokens),
                    "Reasoning output: " + Count(value.ReasoningOutputTokens), "Input + output; subsets counted once"];
                for (var index = 0; index < _values.Length; index++) _values[index].Text = values[index];
            }
            _detail.Text = reading?.Detail ?? "Refresh reads supported local records without opening account credentials.";
            _source.Text = reading is null ? "Source: not checked" : "Source: " + reading.Source
                + (reading.LastSuccessfulRead is { } success ? " · last successful read " + Timestamp(success) : "");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var limit in reading?.Quotas ?? [])
            {
                for (var index = 0; index < limit.Windows.Count; index++)
                {
                    var key = limit.LimitId + "\0" + index;
                    keys.Add(key);
                    if (!_quotaRows.TryGetValue(key, out var row))
                    { _quotaRows.Add(key, row = new QuotaRow()); _quota.Children.Add(row.Element); }
                    row.Update(limit, limit.Windows[index]);
                }
            }
            foreach (var key in _quotaRows.Keys.Where(key => !keys.Contains(key)).ToArray())
            { _quota.Children.Remove(_quotaRows[key].Element); _quotaRows.Remove(key); }
            _missingQuota.Visibility = keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _missingQuota.Text = _provider == AiUsageProvider.Claude
                ? "Claude plan allowance is unavailable from supported local records. Token counts do not reveal your remaining quota."
                : "No provider-reported quota observation found. Run Codex normally, then refresh; no allowance is estimated.";
            Arrange();
        }
    }

    private sealed class QuotaRow
    {
        private readonly TextBlock _title = Text("", 12, weight: Microsoft.UI.Text.FontWeights.SemiBold);
        private readonly TextBlock _value = Text("", 13);
        private readonly TextBlock _observed = Text("", 12, muted: true);
        private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Height = 3,
            Foreground = NativeTheme.Brush("#68D391"), Background = NativeTheme.Brush("#333333") };
        public StackPanel Element { get; } = new() { Spacing = 5 };
        public QuotaRow()
        {
            Element.Children.Add(_title); Element.Children.Add(_value); Element.Children.Add(_bar); Element.Children.Add(_observed);
        }
        public void Update(AiUsageQuota quota, AiUsageWindow window)
        {
            var stale = quota.IsStale || window.ResetsAt is { } reset && reset <= DateTimeOffset.UtcNow;
            var duration = window.Duration is { } length ? length.TotalDays == 7 ? "weekly window"
                : length.TotalHours >= 1 ? length.TotalHours.ToString("0.##", CultureInfo.CurrentCulture) + "-hour window"
                : length.TotalMinutes.ToString("0.##", CultureInfo.CurrentCulture) + "-minute window" : window.Name + " window";
            _title.Text = (string.IsNullOrWhiteSpace(quota.Name) ? quota.LimitId : quota.Name) + " · " + duration;
            _value.Text = window.RemainingPercent.ToString("0.################", CultureInfo.CurrentCulture)
                + "% remaining" + (stale ? " at last observation · stale" : " at last observation");
            _bar.Value = Math.Clamp(window.RemainingPercent, 0, 100);
            _bar.Opacity = stale ? .55 : 1;
            AutomationProperties.SetName(_bar, _title.Text + ": " + _value.Text);
            _observed.Text = "Observed " + Timestamp(quota.ObservedAt)
                + (window.ResetsAt is { } resets ? " · reported reset " + Timestamp(resets) : " · reset not reported");
        }
    }
}
