using System.ComponentModel;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Notch.Core;
using Notch.Core.Providers;
using Notch.Windows.ViewModels;
using Windows.Foundation;

namespace Notch.Windows.Views;

/// <summary>Native, data-driven layouts for the primary dashboard tools.</summary>
public sealed partial class FeaturedToolsView : UserControl, IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<ModuleId, Grid> _views;
    private bool _rendering;
    private bool _subscribed;
    private bool _dialogOpen;
    public bool HasOpenDialog => _dialogOpen;
    private int _revenueDays = 30;
    private RevenueProvider _provider = RevenueProvider.Stripe;
    private DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calendarDate = DateTime.Today;
    private CancellationTokenSource? _seekDelay;
    private CancellationTokenSource? _volumeDelay;
    private string? _artworkPath;
    private int _artworkRequest;
    private double? _renderedFocusProgress;
    private readonly DispatcherTimer _liveTick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly StackPanel _freeView = new() { Spacing = 14, Padding = new Thickness(16) };
    private TextBlock? _freeFocusText;
    private ModuleId? _freeModule;
    private long _mediaProjectionStart;
    private MediaSnapshot? _projectionSnapshot;
    private TimeSpan _projectionPosition;
    private int _freshnessTicks;
    // WinUI returns no capture collection when a control has not captured a pointer.
    public bool IsManipulating => MediaSeekSlider?.PointerCaptures?.Count > 0 || VolumeSlider?.PointerCaptures?.Count > 0;


    public FeaturedToolsView(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _provider = viewModel.Revenue?.Provider ?? RevenueProvider.Stripe;
        _views = new()
        {
            [ModuleId.Home] = HomeView, [ModuleId.Media] = MediaView,
            [ModuleId.Revenue] = RevenueView, [ModuleId.Analytics] = AnalyticsView,
            [ModuleId.Coding] = CodingView, [ModuleId.Calendar] = CalendarView,
            [ModuleId.Weather] = WeatherView, [ModuleId.Focus] = FocusView,
        };
        ModuleRoot.Children.Add(_freeView);
        _liveTick.Tick += (_, _) =>
        {
            if (_viewModel.SelectedModule == ModuleId.Media && _viewModel.IsPremium)
            {
                _rendering = true; try { RenderMediaPosition(); } finally { _rendering = false; }
            }
            if (++_freshnessTicks % 240 == 0 && _viewModel.SelectedModule == ModuleId.Analytics) RenderAnalyticsFreshness();
        };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RenderSelected();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _viewModel.PropertyChanged += ViewModelChanged;
            _viewModel.Reminders.CollectionChanged += RemindersChanged;
            _subscribed = true;
        }
        RenderSelected();
        _liveTick.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            _viewModel.PropertyChanged -= ViewModelChanged;
            _viewModel.Reminders.CollectionChanged -= RemindersChanged;
            _subscribed = false;
        }
        _liveTick.Stop();
        _seekDelay?.Cancel();
        _volumeDelay?.Cancel();
        _artworkRequest++;
        _artworkPath = null;
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => RefreshProperty(e.PropertyName));
            return;
        }
        RefreshProperty(e.PropertyName);
    }

    private void RemindersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            if (_viewModel.SelectedModule == ModuleId.Calendar) RenderCalendar();
        }
        else DispatcherQueue.TryEnqueue(() => { if (_viewModel.SelectedModule == ModuleId.Calendar) RenderCalendar(); });
    }

    private void RefreshProperty(string? property)
    {
        // Timer ticks do not reconstruct charts, calendar cells, or payment rows.
        if (property is nameof(MainViewModel.FocusTime) or nameof(MainViewModel.CountdownTime) or nameof(MainViewModel.StopwatchTime)
            or nameof(MainViewModel.HydrationTime) or nameof(MainViewModel.FocusProgress) or nameof(MainViewModel.FocusRunning)
            or nameof(MainViewModel.StopwatchRunning) or nameof(MainViewModel.StopwatchLaps))
        {
            if (_viewModel.SelectedModule == ModuleId.Focus) { if (_viewModel.IsPremium) RefreshFocusProperty(property); else if (_freeFocusText is not null) _freeFocusText.Text = _viewModel.FocusTime; }
            return;
        }
        if (property is "Status" or "Error")
        {
            SetError(_viewModel.Error);
            return;
        }
        var relevant = property is nameof(MainViewModel.SelectedModule) or nameof(MainViewModel.Preferences) or nameof(MainViewModel.IsReady) or nameof(MainViewModel.IsPremium)
            || _viewModel.SelectedModule switch
            {
                ModuleId.Home => property is nameof(MainViewModel.System) or nameof(MainViewModel.ListeningPorts) or nameof(MainViewModel.Analytics) or nameof(MainViewModel.Revenue) or nameof(MainViewModel.Weather),
                ModuleId.Media => property is nameof(MainViewModel.Media) or nameof(MainViewModel.System),
                ModuleId.Revenue => property == nameof(MainViewModel.Revenue),
                ModuleId.Analytics => property == nameof(MainViewModel.Analytics),
                ModuleId.Coding => property == nameof(MainViewModel.Coding),
                ModuleId.Calendar => property == nameof(MainViewModel.CalendarEvents),
                ModuleId.Weather => property == nameof(MainViewModel.Weather),
                _ => false,
            };
        if (relevant) RenderSelected();
    }

    private void RenderSelected()
    {
        ModuleRoot.MinHeight = ModuleCatalog.Get(_viewModel.SelectedModule).Height;
        var free = !_viewModel.IsPremium;
        foreach (var (module, view) in _views)
            view.Visibility = !free && module == _viewModel.SelectedModule ? Visibility.Visible : Visibility.Collapsed;
        _freeView.Visibility = free ? Visibility.Visible : Visibility.Collapsed;
        if (free) { RenderFree(); SetError(_viewModel.Error); return; }
        _freeModule = null;
        _rendering = true;
        try
        {
            switch (_viewModel.SelectedModule)
            {
                case ModuleId.Home: RenderHome(); break;
                case ModuleId.Media: RenderMedia(); break;
                case ModuleId.Revenue: RenderRevenue(); break;
                case ModuleId.Analytics: RenderAnalytics(); break;
                case ModuleId.Coding: RenderCoding(); break;
                case ModuleId.Calendar: RenderCalendar(); break;
                case ModuleId.Weather: RenderWeather(); break;
                case ModuleId.Focus: RenderFocus(); break;
            }
            SetError(_viewModel.Error);
        }
        finally { _rendering = false; }
    }

    private string DemoSuffix => _viewModel.Preferences.DemoMode ? " · Demo data" : "";

    private void RenderHome()
    {
        var now = DateTime.Now;
        GreetingText.Text = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        HomeDateText.Text = now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture) + DemoSuffix;
        var ports = _viewModel.ListeningPorts;
        HomeServersText.Text = ports.Count.ToString(CultureInfo.CurrentCulture);
        HomePortsText.Text = ports.Count == 0 ? "No local listeners" : string.Join("  ", ports.Take(3).Select(port => $":{port}"));
        HomeSummaryText.Text = $"{ports.Count} listening ports" + (_viewModel.Analytics is { } active ? $" · {active.ActiveUsers:N0} on your site" : "");
        HomeVisitorsText.Text = _viewModel.Analytics?.PageViews.ToString("N0") ?? "—";
        HomeVisitorsLiveText.Text = _viewModel.Analytics is { } analytics ? $"● {analytics.ActiveUsers:N0} live" : "";
        HomeVisitorsCaption.Text = _viewModel.Analytics is null ? "Connect analytics" : "Page views · current snapshot";
        HomeSystemRings.Children.Clear();
        var system = _viewModel.System;
        HomeSystemRings.Children.Add(CreateRing(system?.CpuPercent, "CPU", false));
        HomeSystemRings.Children.Add(CreateRing(system?.MemoryPercent, "RAM", false));
        HomeSystemRings.Children.Add(CreateRing(system?.BatteryPercent, "BAT", true));
        HomeScreenTimeText.Text = system is null ? "—" : $"{(int)system.SessionScreenTime.TotalHours}h {system.SessionScreenTime.Minutes}m";
        HomeRevenueText.Text = _viewModel.Revenue is { } revenue ? Money(revenue.Total, revenue.Currency) : "—";
        HomeRevenueCaption.Text = _viewModel.Revenue is { } source ? $"{source.Provider} · current range" : "Connect a payment provider";
        HomeWeatherText.Text = _viewModel.Weather is { } weather ? $"{weather.Temperature:0}°C" : "—";
        HomeWeatherCaption.Text = _viewModel.Weather is { } conditions ? $"{conditions.City} · {Condition(conditions.Code)}" : "Choose your city";
    }

    private static StackPanel CreateRing(double? percent, string label, bool green)
    {
        var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var ring = new Grid { Width = 47, Height = 47 };
        ring.Children.Add(new Ellipse { Stroke = Brush("#333333"), StrokeThickness = 3 });
        if (percent is { } value)
            ring.Children.Add(new Path { Data = ArcGeometry(23.5, 20.5, Math.Clamp(value / 100, 0, 1)), Stroke = Brush(green ? "#68D391" : "#FFFFFF"), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        ring.Children.Add(new TextBlock { Text = percent is null ? "—" : $"{percent:0}%", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(ring);
        content.Children.Add(new TextBlock { Text = label, FontSize = 9, Foreground = Brush("#919191"), HorizontalAlignment = HorizontalAlignment.Center });
        AutomationProperties.SetName(content, $"{label}: {(percent is null ? "unavailable" : $"{percent:0} percent")}");
        return content;
    }

    private void RenderMedia()
    {
        var media = _viewModel.Media;
        MediaTitleText.Text = media?.Title ?? "Nothing playing";
        MediaArtistText.Text = media is null ? "Start playback in a Windows media app" : media.Artist + DemoSuffix;
        PlayingGlyph.Visibility = media?.IsPlaying == true ? Visibility.Visible : Visibility.Collapsed;
        PlayPauseIcon.Glyph = media?.IsPlaying == true ? "\uE769" : "\uE768";
        AutomationProperties.SetName(PlayPauseButton, media?.IsPlaying == true ? "Pause playback" : "Play playback");
        PlayPauseButton.IsEnabled = media is not null && (media.IsPlaying ? media.CanPause : media.CanPlay);
        PreviousButton.IsEnabled = media?.CanPrevious == true; NextButton.IsEnabled = media?.CanNext == true;
        MediaSeekSlider.IsEnabled = media is { CanSeek: true } && media.Duration > TimeSpan.Zero && !_viewModel.Preferences.DemoMode;
        if (!ReferenceEquals(_projectionSnapshot, media))
        {
            _projectionSnapshot = media; _mediaProjectionStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var age = media?.PositionUpdatedAt is { } updated ? Math.Clamp((DateTimeOffset.UtcNow - updated).TotalSeconds, 0, 60) : 0;
            _projectionPosition = media is { IsPlaying: true } ? media.Position + TimeSpan.FromSeconds(age * media.PlaybackRate) : media?.Position ?? TimeSpan.Zero;
        }
        RenderMediaPosition();
        MediaDurationText.Text = media is null ? "0:00" : Clock(media.Duration);
        VolumeSlider.IsEnabled = _viewModel.System is { OutputDevice: not "Unavailable" and not "No output device" };
        if (_volumeDelay is null && (VolumeSlider.PointerCaptures?.Count ?? 0) == 0 && VolumeSlider.FocusState == FocusState.Unfocused)
            VolumeSlider.Value = Math.Clamp(_viewModel.System?.Volume ?? 0, 0, 1);
        OutputDeviceText.Text = _viewModel.System?.OutputDevice ?? "No output device";
        if (_artworkPath != media?.ArtworkPath)
        {
            _artworkPath = media?.ArtworkPath;
            _ = LoadArtworkAsync(_artworkPath);
        }
    }

    private void RenderMediaPosition()
    {
        var media = _viewModel.Media;
        var position = _projectionPosition;
        if (media?.IsPlaying == true && _projectionSnapshot == media)
            position += System.Diagnostics.Stopwatch.GetElapsedTime(_mediaProjectionStart) * media.PlaybackRate;
        if (media is not null) position = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0, Math.Max(0, media.Duration.Ticks)));
        if (_seekDelay is null && (MediaSeekSlider.PointerCaptures?.Count ?? 0) == 0 && MediaSeekSlider.FocusState == FocusState.Unfocused)
            MediaSeekSlider.Value = media is not null && media.Duration > TimeSpan.Zero ? Math.Clamp(position.TotalSeconds / media.Duration.TotalSeconds, 0, 1) : 0;
        MediaPositionText.Text = Clock(position);
    }
    private void RenderFree()
    {
        if (_freeModule == _viewModel.SelectedModule && _viewModel.SelectedModule == ModuleId.Focus) { if (_freeFocusText is not null) _freeFocusText.Text = _viewModel.FocusTime; return; }
        _freeModule = _viewModel.SelectedModule; _freeView.Children.Clear();
        _freeView.Children.Add(new TextBlock { Text = ProductIdentity.DisplayName + " Free", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Button Action(string text, Func<Task> action) { var button = new Button { Content = text, Style = (Style)Application.Current.Resources["NotchButtonStyle"] }; button.Click += async (_, _) => await SafeAsync(action); return button; }
        if (_viewModel.SelectedModule == ModuleId.Media)
        {
            var media = _viewModel.Media;
            _freeView.Children.Add(new TextBlock { Text = media?.Title ?? "Nothing playing", FontSize = 18, TextWrapping = TextWrapping.Wrap });
            _freeView.Children.Add(Caption(media?.Artist ?? "Start playback in a Windows media app"));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            var previous = Action("Previous", _viewModel.PreviousAsync); previous.IsEnabled = media?.CanPrevious == true;
            var play = Action(media?.IsPlaying == true ? "Pause" : "Play", _viewModel.PlayPauseAsync); play.IsEnabled = media is not null && (media.IsPlaying ? media.CanPause : media.CanPlay);
            var next = Action("Next", _viewModel.NextAsync); next.IsEnabled = media?.CanNext == true;
            row.Children.Add(previous); row.Children.Add(play); row.Children.Add(next); _freeView.Children.Add(row);
        }
        else if (_viewModel.SelectedModule == ModuleId.Focus)
        {
            _freeFocusText = new TextBlock { Text = _viewModel.FocusTime, FontSize = 44, FontFamily = new FontFamily("Consolas") };
            _freeView.Children.Add(_freeFocusText);
            _freeView.Children.Add(Action("Start / pause Pomodoro", () => { _viewModel.ToggleFocus(); return Task.CompletedTask; }));
            _freeView.Children.Add(Action("Reset", () => { _viewModel.ResetFocus(); return Task.CompletedTask; }));
        }
        else
        {
            _freeView.Children.Add(Caption("Quick playback, one Pomodoro and a local scratchpad. No account required.", wrap: true));
            foreach (var module in new[] { ModuleId.Media, ModuleId.Focus, ModuleId.Scratchpad })
                _freeView.Children.Add(Action(ModuleCatalog.Get(module).Title, () => { _viewModel.SelectModule(module); return Task.CompletedTask; }));
        }
        _freeView.Children.Add(Action("Premium — US$2/month and recovery settings", () => { _viewModel.SelectModule(ModuleId.Settings); return Task.CompletedTask; }));
    }
    private async Task LoadArtworkAsync(string? path)
    {
        var request = ++_artworkRequest;
        MediaArtwork.Source = null;
        ArtworkPlaceholder.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(System.IO.Path.GetFullPath(path));
            using var stream = await file.OpenReadAsync();
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            if (request != _artworkRequest) return;
            MediaArtwork.Source = image;
            ArtworkPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception) { /* An unavailable thumbnail must not interrupt playback controls. */ }
    }

    private void RenderRevenue()
    {
        foreach (var button in new[] { StripeButton, PolarButton, DodoButton, AdSenseButton })
            SelectButton(button, button.Tag?.ToString() == _provider.ToString());
        foreach (var button in new[] { RevenueTodayButton, RevenueWeekButton, RevenueMonthButton })
            SelectButton(button, button.Tag?.ToString() == _revenueDays.ToString(CultureInfo.InvariantCulture));
        var revenue = _viewModel.Revenue is { } snapshot && snapshot.Provider == _provider && (_viewModel.IsDemo || snapshot.RequestedDays == _revenueDays) ? snapshot : null;
        foreach (var button in new[] { PolarButton, DodoButton, AdSenseButton }) { button.IsEnabled = false; ToolTipService.SetToolTip(button, "This provider is not integrated yet."); }
        RevenueRangeText.Text = revenue is null ? $"{_provider} · awaiting connection" : $"{_provider} · updated {Relative(revenue.UpdatedAt)}" + DemoSuffix;
        RevenueTotalText.Text = revenue is null ? "—" : Money(revenue.Total, revenue.Currency);
        RevenueOrdersText.Text = revenue is null ? "Real payments, close at hand" : $"{revenue.Payments.Count:N0} loaded payments" + (revenue.Complete ? "" : " · partial response");
        RevenueConnectPanel.Visibility = revenue is null ? Visibility.Visible : Visibility.Collapsed;
        RevenueChart.Visibility = revenue is null ? Visibility.Collapsed : Visibility.Visible;
        RenderBars(RevenueChart, revenue?.DailyAmounts.Select(amount => (double)amount).ToArray() ?? [], "Revenue by day");
        PaymentsPanel.Children.Clear();
        if (revenue is null)
        {
            PaymentsPanel.Children.Add(Caption("No payments loaded for this range. Configure a restricted Stripe read-only credential in Settings, then refresh.", wrap: true));
            return;
        }
        if (revenue.Payments.Count == 0) PaymentsPanel.Children.Add(Caption("No payments in this response.", wrap: true));
        foreach (var payment in revenue.Payments.OrderByDescending(item => item.CreatedAt))
        {
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new Border { Width = 29, Height = 29, CornerRadius = new CornerRadius(15), Background = Brush("#242424"), Child = new FontIcon { Glyph = "\uE96E", FontSize = 12 } });
            var description = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            description.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(payment.Description) ? "Payment" : payment.Description, FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            description.Children.Add(Caption(Relative(payment.CreatedAt), 10));
            Grid.SetColumn(description, 1); row.Children.Add(description);
            var amount = new TextBlock { Text = Money(payment.Amount, payment.Currency), FontSize = 11, Foreground = Brush("#68D391"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(amount, 2); row.Children.Add(amount);
            PaymentsPanel.Children.Add(new Border { BorderBrush = Brush("#262626"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
        }
    }

    private void RenderAnalyticsFreshness()
    {
        var analytics = _viewModel.Analytics;
        AnalyticsLiveText.Text = _viewModel.IsDemo ? "DEMO" : analytics is null ? "NOT CONNECTED" : DateTimeOffset.UtcNow - analytics.UpdatedAt > TimeSpan.FromMinutes(5) ? "STALE" : "SNAPSHOT";
        AnalyticsUpdatedText.Text = analytics is null ? "Configure an HTTPS endpoint and required bearer credential" : "Updated " + Relative(analytics.UpdatedAt) + " · manual refresh";
    }
    private void RenderAnalytics()
    {
        var analytics = _viewModel.Analytics;
        AnalyticsSiteText.Text = analytics?.Site ?? "Your website";
        RenderAnalyticsFreshness();
        AnalyticsRealtimeText.Text = analytics?.ActiveUsers.ToString("N0") ?? "—";
        AnalyticsUsersText.Text = analytics?.ActiveUsers.ToString("N0") ?? "—";
        AnalyticsViewsText.Text = analytics?.PageViews.ToString("N0") ?? "—";
        AnalyticsNewUsersText.Text = analytics?.NewUsers.ToString("N0") ?? "—";
        RenderBars(AnalyticsChart, analytics?.Timeline.Select(value => (double)value).ToArray() ?? [], "Live activity reported by your endpoint", "#D9D9D9");
        AnalyticsChartCaption.Text = analytics is null ? "Live activity appears after connection" : "Reported timeline · latest on the right";
        AnalyticsPagesPanel.Children.Clear();
        if (analytics is null || analytics.Pages.Count == 0)
            AnalyticsPagesPanel.Children.Add(Caption(analytics is null ? "Connect analytics to see page activity." : "No page breakdown in this snapshot.", wrap: true));
        else
        {
            var maximum = Math.Max(1, analytics.Pages.Max(page => page.Value));
            foreach (var page in analytics.Pages.OrderByDescending(page => page.Value).Take(20))
            {
                var row = new Grid { Padding = new Thickness(9, 6, 9, 6), ColumnSpacing = 6 };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var background = new Border { CornerRadius = new CornerRadius(8), Background = Brush("#333333"), HorizontalAlignment = HorizontalAlignment.Stretch, Opacity = .3 + .7 * Math.Clamp((double)page.Value / maximum, 0, 1) };
                Grid.SetColumnSpan(background, 2); row.Children.Add(background);
                row.Children.Add(new TextBlock { Text = page.Key, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
                var number = new TextBlock { Text = page.Value.ToString("N0"), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
                Grid.SetColumn(number, 1); row.Children.Add(number);
                AnalyticsPagesPanel.Children.Add(row);
            }
        }
    }

    private void RenderCoding()
    {
        var coding = _viewModel.Coding;
        CodingProviderText.Text = coding is null ? "Connect your local usage logs" : $"● {coding.Provider}" + DemoSuffix;
        CodingProviderText.Foreground = Brush(coding is null ? "#FFFFFF" : "#68D391");
        CodingSourceText.Text = coding?.SourcePath ?? "Import a supported JSONL log. Tokens remain on this computer.";
        CodingTokensText.Text = coding is null ? "—" : Compact(coding.InputTokens + coding.OutputTokens);
        CodingTokenDetailText.Text = coding is null ? "No usage log imported" : $"{Compact(coding.InputTokens)} input · {Compact(coding.OutputTokens)} output";
        CodingSessionsText.Text = coding is null ? "—" : $"{coding.Sessions:N0} sessions";
        CodingDaysText.Text = coding is null ? "Local records only" : $"{coding.Days.Count(day => day.InputTokens + day.OutputTokens > 0):N0} active days in the log";
        RenderHeatmap(coding);
    }

    private void RenderHeatmap(CodingSnapshot? coding)
    {
        CodingHeatmap.Children.Clear(); CodingHeatmap.ColumnDefinitions.Clear(); CodingHeatmap.RowDefinitions.Clear();
        if (coding is null)
        {
            CodingHeatmap.Children.Add(new TextBlock { Text = "Your coding rhythm, once a log is imported.", FontSize = 13, Foreground = Brush("#919191"), VerticalAlignment = VerticalAlignment.Center });
            CodingHeatmapCaption.Text = "No account quotas or activity are estimated.";
            return;
        }
        var last = DateOnly.FromDateTime(DateTime.Today);
        var first = last.AddDays(-363);
        while (first.DayOfWeek != DayOfWeek.Monday) first = first.AddDays(-1);
        var dates = coding.Days.GroupBy(day => day.Date).ToDictionary(group => group.Key, group => group.Sum(day => (double)day.InputTokens + day.OutputTokens));
        var maximum = Math.Max(1, dates.Values.DefaultIfEmpty().Max());
        var totalDays = last.DayNumber - first.DayNumber + 1;
        var columns = (int)Math.Ceiling(totalDays / 7d);
        for (var week = 0; week < columns; week++) CodingHeatmap.ColumnDefinitions.Add(new ColumnDefinition());
        for (var day = 0; day < 7; day++) CodingHeatmap.RowDefinitions.Add(new RowDefinition());
        var colors = new[] { "#292422", "#55382D", "#8A5139", "#BD7854", "#EFA278" };
        for (var offset = 0; offset < totalDays; offset++)
        {
            var date = first.AddDays(offset);
            dates.TryGetValue(date, out var tokens);
            var level = tokens <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(Math.Sqrt(tokens / maximum) * 4), 1, 4);
            var cell = new Rectangle { Fill = Brush(colors[level]), RadiusX = 2, RadiusY = 2, Margin = new Thickness(1.5), MinHeight = 8 };
            ToolTipService.SetToolTip(cell, $"{date:MMM d, yyyy}: {tokens:N0} tokens");
            AutomationProperties.SetName(cell, $"{date:MMMM d, yyyy}, {tokens:N0} tokens");
            Grid.SetColumn(cell, offset / 7); Grid.SetRow(cell, offset % 7); CodingHeatmap.Children.Add(cell);
        }
        CodingHeatmapCaption.Text = $"{first:MMM yyyy} — {last:MMM yyyy} · tokens recorded in local logs";
    }

    private void RenderCalendar()
    {
        CalendarMonthText.Text = _calendarMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        CalendarDayText.Text = _calendarDate.ToString("dddd", CultureInfo.CurrentCulture);
        CalendarDateText.Text = _calendarDate.ToString("d MMMM", CultureInfo.CurrentCulture) + DemoSuffix;
        CalendarDaysGrid.Children.Clear(); CalendarDaysGrid.ColumnDefinitions.Clear(); CalendarDaysGrid.RowDefinitions.Clear();
        for (var column = 0; column < 7; column++) CalendarDaysGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < 7; row++) CalendarDaysGrid.RowDefinitions.Add(new RowDefinition());
        var weekdays = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        for (var column = 0; column < 7; column++)
        {
            var label = Caption(weekdays[column][..Math.Min(1, weekdays[column].Length)], 10);
            label.HorizontalAlignment = HorizontalAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, column); CalendarDaysGrid.Children.Add(label);
        }
        var offset = (int)_calendarMonth.DayOfWeek;
        var count = DateTime.DaysInMonth(_calendarMonth.Year, _calendarMonth.Month);
        for (var number = 1; number <= count; number++)
        {
            var date = _calendarMonth.AddDays(number - 1);
            var selected = date.Date == _calendarDate.Date;
            var button = new Button { Content = number.ToString(CultureInfo.CurrentCulture), Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = selected ? Brush("#FFFFFF") : new SolidColorBrush(Colors.Transparent), Foreground = selected ? Brush("#050505") : Brush(date == DateTime.Today ? "#FFFFFF" : "#A8A8A8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            AutomationProperties.SetName(button, date.ToString("D", CultureInfo.CurrentCulture));
            button.Click += (_, _) => { _calendarDate = date; RenderCalendar(); };
            Grid.SetColumn(button, (number - 1 + offset) % 7); Grid.SetRow(button, (number - 1 + offset) / 7 + 1); CalendarDaysGrid.Children.Add(button);
        }
        var selectedDay = DateOnly.FromDateTime(_calendarDate);
        var events = CalendarDay.EventsForDay(_viewModel.CalendarEvents, selectedDay, TimeZoneInfo.Local).ToArray();
        CalendarConnectPanel.Visibility = _viewModel.CalendarEvents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CalendarEventsPanel.Children.Clear();
        if (_viewModel.CalendarEvents.Count > 0)
        {
            CalendarEventsPanel.Children.Add(Caption(events.Length == 0 ? (_viewModel.CalendarRangeLoaded(_calendarDate) || _viewModel.IsDemo ? "No events on this day" : "This date has not been loaded. Navigate or re-import to refresh.") : "DAY’S EVENTS", 10));
            foreach (var calendarEvent in events)
            {
                var content = new StackPanel { Spacing = 4 };
                content.Children.Add(new TextBlock { Text = calendarEvent.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                content.Children.Add(Caption(CalendarDay.TimeLabel(calendarEvent, selectedDay, TimeZoneInfo.Local), 10));
                if (Uri.TryCreate(calendarEvent.MeetingUrl, UriKind.Absolute, out var meeting) && meeting.Scheme is "http" or "https")
                {
                    var join = new Button { Content = "Open meeting", Style = (Style)Application.Current.Resources["NotchButtonStyle"], HorizontalAlignment = HorizontalAlignment.Left };
                    join.Click += async (_, _) => await SafeAsync(async () => { await global::Windows.System.Launcher.LaunchUriAsync(meeting); });
                    content.Children.Add(join);
                }
                CalendarEventsPanel.Children.Add(new Border { Padding = new Thickness(12), Background = Brush("#141414"), CornerRadius = new CornerRadius(15), Child = content });
            }
            var import = new Button { Content = "Import another calendar", Style = (Style)Application.Current.Resources["NotchButtonStyle"] };
            import.Click += ImportCalendar_Click; CalendarEventsPanel.Children.Add(import);
        }
        var reminders = _viewModel.Reminders.Where(reminder => reminder.DueAt.LocalDateTime.Date == _calendarDate.Date && !reminder.Completed).OrderBy(reminder => reminder.DueAt).ToArray();
        ReminderCountText.Text = $"{reminders.Length} reminders";
        RemindersPanel.Children.Clear();
        if (reminders.Length == 0) RemindersPanel.Children.Add(Caption("Nothing to remember yet.", 11));
        foreach (var reminder in reminders)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var complete = new Button { Style = (Style)Application.Current.Resources["NotchIconButtonStyle"], Background = new SolidColorBrush(Colors.Transparent), Content = new FontIcon { Glyph = "\uEA3A", FontSize = 17 } };
            AutomationProperties.SetName(complete, $"Complete reminder: {reminder.Title}"); complete.Click += (_, _) => _viewModel.CompleteReminder(reminder.Id); row.Children.Add(complete);
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = reminder.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(Caption(reminder.DueAt.LocalDateTime.ToString("t", CultureInfo.CurrentCulture), 10)); Grid.SetColumn(text, 1); row.Children.Add(text);
            var remove = new Button { Style = (Style)Application.Current.Resources["NotchIconButtonStyle"], Content = new FontIcon { Glyph = "\uE711", FontSize = 12 } };
            AutomationProperties.SetName(remove, $"Remove reminder: {reminder.Title}"); remove.Click += (_, _) => _viewModel.RemoveReminder(reminder.Id); Grid.SetColumn(remove, 2); row.Children.Add(remove);
            RemindersPanel.Children.Add(row);
        }
    }

    private static DateOnly CityToday(WeatherSnapshot weather)
    {
        var now = DateTimeOffset.UtcNow;
        if (weather.TimeZoneId is { } zoneId)
            try { return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return DateOnly.FromDateTime(now.ToOffset(weather.UtcOffset ?? TimeSpan.Zero).DateTime);
    }
    private void RenderWeather()
    {
        var weather = _viewModel.Weather;
        WeatherCityText.Text = (weather?.City ?? _viewModel.Preferences.WeatherCity) + DemoSuffix;
        WeatherTemperatureText.Text = weather is null ? "—" : $"{weather.Temperature:0}°C";
        WeatherCurrentIcon.Glyph = WeatherGlyph(weather?.Code ?? -1);
        WeatherConditionText.Text = weather is null ? "Refresh to load the forecast" : $"{weather.City} · {Condition(weather.Code)}";
        WeatherDetailText.Text = weather is null ? "Licensed weather service awaits deployment configuration" : $"Feels {weather.FeelsLike:0}° · Humidity {weather.Humidity}% · Wind {weather.Wind:0} km/h";
        WeatherHoursGrid.Children.Clear(); WeatherHoursGrid.ColumnDefinitions.Clear();
        WeatherDaysGrid.Children.Clear(); WeatherDaysGrid.ColumnDefinitions.Clear();
        if (weather is null)
        {
            WeatherHoursGrid.Children.Add(Caption("Hourly weather appears after refresh.", wrap: true));
            WeatherDaysGrid.Children.Add(Caption("Your next seven days, in one glance.", wrap: true));
            return;
        }
        var hours = weather.Hours.Where(hour => hour.Time >= DateTimeOffset.Now.AddHours(-1)).Take(7).ToArray();
        if (hours.Length == 0) hours = weather.Hours.Take(7).ToArray();
        for (var index = 0; index < hours.Length; index++)
        {
            WeatherHoursGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var hour = hours[index];
            var column = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(Caption(hour.Time.DateTime.ToString("htt", CultureInfo.CurrentCulture), 10));
            column.Children.Add(new FontIcon { Glyph = WeatherGlyph(hour.Code), FontSize = 19 });
            column.Children.Add(new TextBlock { Text = $"{hour.Temperature:0}°", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            Grid.SetColumn(column, index); WeatherHoursGrid.Children.Add(column);
        }
        var days = weather.Days.Take(7).ToArray();
        var maximum = Math.Max(1, days.Select(day => day.Maximum - day.Minimum).DefaultIfEmpty().Max());
        for (var index = 0; index < days.Length; index++)
        {
            WeatherDaysGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = days[index];
            var column = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(Caption(day.Date == CityToday(weather) ? "Today" : day.Date.ToString("ddd", CultureInfo.CurrentCulture), 10));
            column.Children.Add(new FontIcon { Glyph = WeatherGlyph(day.Code), FontSize = 18 });
            column.Children.Add(new TextBlock { Text = $"{day.Maximum:0}°", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
            column.Children.Add(new Border { Width = 4, Height = 13 + 14 * Math.Clamp((day.Maximum - day.Minimum) / maximum, 0, 1), CornerRadius = new CornerRadius(2), Background = Brush("#D5D5D5"), HorizontalAlignment = HorizontalAlignment.Center });
            column.Children.Add(Caption($"{day.Minimum:0}°", 10));
            Grid.SetColumn(column, index); WeatherDaysGrid.Children.Add(column);
        }
    }

    private void RenderFocus()
    {
        FocusTimeText.Text = _viewModel.FocusTime;
        RenderFocusState();
        RenderFocusProgress();
        CountdownTimeText.Text = _viewModel.CountdownTime;
        StopwatchTimeText.Text = _viewModel.StopwatchTime;
        RenderStopwatchState();
        RenderStopwatchLaps();
        HydrationTimeText.Text = _viewModel.HydrationTime;
        HydrationIntervalText.Text = $"Nudges every {_viewModel.Preferences.HydrationMinutes}m";
    }

    private void RefreshFocusProperty(string property)
    {
        switch (property)
        {
            case nameof(MainViewModel.FocusTime): FocusTimeText.Text = _viewModel.FocusTime; break;
            case nameof(MainViewModel.CountdownTime): CountdownTimeText.Text = _viewModel.CountdownTime; break;
            case nameof(MainViewModel.StopwatchTime): StopwatchTimeText.Text = _viewModel.StopwatchTime; break;
            case nameof(MainViewModel.HydrationTime): HydrationTimeText.Text = _viewModel.HydrationTime; break;
            case nameof(MainViewModel.FocusProgress): RenderFocusProgress(); break;
            case nameof(MainViewModel.FocusRunning): RenderFocusState(); break;
            case nameof(MainViewModel.StopwatchRunning): RenderStopwatchState(); break;
            case nameof(MainViewModel.StopwatchLaps): RenderStopwatchLaps(); break;
        }
    }

    private void RenderFocusState()
    {
        FocusToggleButton.Content = _viewModel.FocusRunning ? "Pause" : "Start";
        FocusStatusText.Text = _viewModel.FocusRunning ? "Pomodoro running" : "Make a little room to focus";
    }

    private void RenderFocusProgress()
    {
        var progress = Math.Clamp(_viewModel.FocusProgress, 0, 1);
        if (_renderedFocusProgress == progress) return;
        var dialSize = 81 * Math.Max(1, NativeTheme.TextScaleFactor);
        FocusDial.Width = FocusDial.Height = dialSize;
        FocusArc.Data = ArcGeometry(dialSize / 2, dialSize / 2 - 2, progress);
        _renderedFocusProgress = progress;
    }

    private void RenderStopwatchState()
    {
        StopwatchToggleButton.Content = _viewModel.StopwatchRunning ? "Pause" : "Start";
        StopwatchLapButton.IsEnabled = _viewModel.StopwatchRunning;
    }

    private void RenderStopwatchLaps()
    {
        var laps = _viewModel.StopwatchLaps;
        StopwatchLapButton.Content = laps.Count == 0 ? "Record lap" : $"Lap {laps.Count + 1}";
        ToolTipService.SetToolTip(StopwatchLapButton, laps.Count == 0 ? "No laps recorded" : string.Join("\n", laps.TakeLast(8).Select((lap, index) => $"Lap {Math.Max(1, laps.Count - 7) + index}: {lap:mm\\:ss\\.ff}")));
    }

    private static void RenderBars(Grid chart, IReadOnlyList<double> values, string accessibilityLabel, string color = "#737373")
    {
        chart.Children.Clear(); chart.ColumnDefinitions.Clear();
        AutomationProperties.SetName(chart, accessibilityLabel);
        var data = values.TakeLast(60).ToArray();
        if (data.Length == 0) { chart.Children.Add(Caption("No chart data yet", 11)); return; }
        var maximum = Math.Max(1, data.Max());
        for (var index = 0; index < data.Length; index++)
        {
            chart.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = new Grid { Margin = new Thickness(1.5, 0, 1.5, 0) };
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(.001, 1 - Math.Max(0, data[index]) / maximum), GridUnitType.Star) });
            cell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(.012, Math.Max(0, data[index]) / maximum), GridUnitType.Star) });
            var bar = new Rectangle { Fill = Brush(index == data.Length - 1 ? "#FFFFFF" : color), RadiusX = 2, RadiusY = 2 };
            ToolTipService.SetToolTip(bar, data[index].ToString("N0", CultureInfo.CurrentCulture));
            Grid.SetRow(bar, 1); cell.Children.Add(bar); Grid.SetColumn(cell, index); chart.Children.Add(cell);
        }
    }

    private static PathGeometry ArcGeometry(double center, double radius, double fraction)
    {
        var geometry = new PathGeometry();
        if (fraction <= 0) return geometry;
        fraction = Math.Min(fraction, .9999);
        var radians = fraction * Math.PI * 2 - Math.PI / 2;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment { Point = new Point(center + Math.Cos(radians) * radius, center + Math.Sin(radians) * radius), Size = new Size(radius, radius), IsLargeArc = fraction > .5, SweepDirection = SweepDirection.Clockwise });
        geometry.Figures.Add(figure); return geometry;
    }

    private static TextBlock Caption(string text, double size = 12, bool wrap = false) => new() { Text = text, FontSize = Math.Max(12, size), Foreground = Brush("#919191"), TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis };
    private static SolidColorBrush Brush(string hex) => NativeTheme.Brush(hex);
    private static string Clock(TimeSpan duration) => duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"m\:ss");
    private static string Compact(long count) => count >= 1_000_000 ? $"{count / 1_000_000d:0.0}M" : count >= 1_000 ? $"{count / 1_000d:0.0}K" : count.ToString("N0");
    private static string Money(decimal value, string currency) => currency.ToUpperInvariant() switch { "USD" => "$" + value.ToString("N2"), "EUR" => "€" + value.ToString("N2"), "GBP" => "£" + value.ToString("N2"), "INR" => "₹" + value.ToString("N2"), _ => $"{currency.ToUpperInvariant()} {value:N2}" };
    private static string Relative(DateTimeOffset timestamp)
    {
        var age = DateTimeOffset.UtcNow - timestamp;
        return age < TimeSpan.FromMinutes(1) ? "just now" : age < TimeSpan.FromHours(1) ? $"{Math.Max(1, (int)age.TotalMinutes)}m ago" : age < TimeSpan.FromDays(1) ? $"{(int)age.TotalHours}h ago" : timestamp.LocalDateTime.ToString("MMM d", CultureInfo.CurrentCulture);
    }
    private static string Condition(int code) => code switch { 0 => "Clear sky", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast", 45 or 48 => "Fog", 51 or 53 or 55 or 56 or 57 => "Drizzle", 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "Rain", 71 or 73 or 75 or 77 or 85 or 86 => "Snow", 95 or 96 or 99 => "Thunderstorms", _ => "Conditions unavailable" };
    private static string WeatherGlyph(int code) => code switch { 0 or 1 => "\uE706", 2 => "\uE9BD", 3 or 45 or 48 => "\uE753", 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "\uE9C4", 71 or 73 or 75 or 77 or 85 or 86 => "\uE9C6", 95 or 96 or 99 => "\uE945", _ => "\uE9BD" };
    private static void SelectButton(Button button, bool selected)
    {
        button.Background = Brush(selected ? "#FFFFFF" : "#141414");
        button.Foreground = Brush(selected ? "#050505" : "#A8A8A8");
        AutomationProperties.SetHelpText(button, selected ? "Selected" : "");
    }

    private void SetError(string? message)
    {
        LocalErrorText.Text = message ?? "";
        LocalErrorText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }
    private async Task SafeAsync(Func<Task> action)
    {
        try { await _viewModel.ExecuteAsync(action); }
        catch (Exception exception) { SetError(exception.Message); }
    }

    private void HomeCard_Click(object sender, RoutedEventArgs e) { if (sender is Button button && Enum.TryParse<ModuleId>(button.Tag?.ToString(), out var module)) _viewModel.SelectModule(module); }
    private void HomeSettings_Click(object sender, RoutedEventArgs e) => _viewModel.SelectModule(ModuleId.Settings);
    private async void PlayPause_Click(object sender, RoutedEventArgs e) => await SafeAsync(_viewModel.PlayPauseAsync);
    private async void Previous_Click(object sender, RoutedEventArgs e) => await SafeAsync(_viewModel.PreviousAsync);
    private async void Next_Click(object sender, RoutedEventArgs e) => await SafeAsync(_viewModel.NextAsync);

    private async void MediaSeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_rendering || _viewModel.Media is not { CanSeek: true } || _viewModel.Preferences.DemoMode) return;
        _seekDelay?.Cancel(); _seekDelay?.Dispose(); _seekDelay = new CancellationTokenSource();
        var delay = _seekDelay;
        try { await Task.Delay(180, delay.Token); await SafeAsync(() => _viewModel.SeekMediaAsync(e.NewValue)); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_seekDelay, delay)) { _seekDelay = null; delay.Dispose(); } }
    }
    private async void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_rendering || _viewModel.System is null) return;
        _volumeDelay?.Cancel(); _volumeDelay?.Dispose(); _volumeDelay = new CancellationTokenSource();
        var delay = _volumeDelay;
        try { await Task.Delay(80, delay.Token); await SafeAsync(() => _viewModel.SetVolumeAsync(e.NewValue)); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(_volumeDelay, delay)) { _volumeDelay = null; delay.Dispose(); } }
    }
    private async void RevenueProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse<RevenueProvider>(button.Tag?.ToString(), out var provider)) return;
        _provider = provider; RenderRevenue(); await SafeAsync(() => _viewModel.RefreshRevenueAsync(provider, _revenueDays));
    }
    private async void RevenueRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !int.TryParse(button.Tag?.ToString(), out var days)) return;
        _revenueDays = days; RenderRevenue(); await SafeAsync(() => _viewModel.RefreshRevenueAsync(_provider, days));
    }
    private async void RevenueRefresh_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.RefreshRevenueAsync(_provider, _revenueDays));
    private async void AnalyticsRefresh_Click(object sender, RoutedEventArgs e) => await SafeAsync(_viewModel.RefreshAnalyticsAsync);
    private async void WeatherRefresh_Click(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.RefreshWeatherAsync());
    private async void CodingRefresh_Click(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.Coding?.SourcePath ?? _viewModel.Preferences.CodingPath;
        if (!string.IsNullOrWhiteSpace(path)) await SafeAsync(() => _viewModel.ImportCodingAsync(path));
        else ImportCoding_Click(sender, e);
    }
    private async void PreviousMonth_Click(object sender, RoutedEventArgs e) { _calendarMonth = _calendarMonth.AddMonths(-1); _calendarDate = _calendarMonth; RenderCalendar(); await SafeAsync(() => _viewModel.EnsureCalendarMonthAsync(_calendarMonth)); RenderCalendar(); }
    private async void NextMonth_Click(object sender, RoutedEventArgs e) { _calendarMonth = _calendarMonth.AddMonths(1); _calendarDate = _calendarMonth; RenderCalendar(); await SafeAsync(() => _viewModel.EnsureCalendarMonthAsync(_calendarMonth)); RenderCalendar(); }
    private void FocusToggle_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleFocus();
    private void FocusReset_Click(object sender, RoutedEventArgs e) => _viewModel.ResetFocus();
    private void Countdown_Click(object sender, RoutedEventArgs e) { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var minutes)) _viewModel.StartCountdown(minutes); }
    private void StopwatchToggle_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleStopwatch();
    private void StopwatchReset_Click(object sender, RoutedEventArgs e) => _viewModel.ResetStopwatch();
    private void StopwatchLap_Click(object sender, RoutedEventArgs e) => _viewModel.LapStopwatch();
    private void DrankWater_Click(object sender, RoutedEventArgs e) => _viewModel.DrankWater();

    private async void ImportCoding_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".jsonl", ".json"], "Import coding usage", _viewModel.Preferences.CodingPath);
        if (!string.IsNullOrWhiteSpace(path)) await SafeAsync(() => _viewModel.ImportCodingAsync(path));
    }
    private async void ImportCalendar_Click(object sender, RoutedEventArgs e)
    {
        var path = await PickFileAsync([".ics"], "Import calendar", _viewModel.Preferences.CalendarPath);
        if (!string.IsNullOrWhiteSpace(path)) await SafeAsync(() => _viewModel.ImportCalendarAsync(path));
    }
    private async Task<string?> PickFileAsync(string[] extensions, string title, string? current)
    {
        if (_dialogOpen) return null;
        if (_viewModel.WindowHandle == 0) return await PromptAsync(title, "Absolute path to the file", current ?? "", "Import");
        _dialogOpen = true;
        try
        {
            var picker = new global::Windows.Storage.Pickers.FileOpenPicker { ViewMode = global::Windows.Storage.Pickers.PickerViewMode.List, SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _viewModel.WindowHandle);
            foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
            return (await picker.PickSingleFileAsync())?.Path;
        }
        catch (Exception exception) { SetError(exception.Message); return null; }
        finally { _dialogOpen = false; }
    }
    private async void ChangeCity_Click(object sender, RoutedEventArgs e)
    {
        var city = await PromptAsync("Weather city", "City or city, country", _viewModel.Weather?.City ?? _viewModel.Preferences.WeatherCity, "Update weather");
        if (!string.IsNullOrWhiteSpace(city)) await SafeAsync(() => _viewModel.RefreshWeatherAsync(city));
    }
    private async void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        var title = new TextBox { PlaceholderText = "What do you want to remember?", MaxLength = 200, Style = (Style)Application.Current.Resources["NotchTextBoxStyle"] };
        var time = new TimePicker { ClockIdentifier = "24HourClock", Time = DateTime.Now.TimeOfDay, HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(Caption(_calendarDate.ToString("D", CultureInfo.CurrentCulture), wrap: true)); content.Children.Add(title); content.Children.Add(time);
        var dialog = new ContentDialog { Title = "Add reminder", Content = content, PrimaryButtonText = "Add", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot, RequestedTheme = ElementTheme.Dark, IsPrimaryButtonEnabled = false };
        title.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(title.Text);
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            var localDue = DateTime.SpecifyKind(_calendarDate.Date + time.Time, DateTimeKind.Local);
            _viewModel.AddReminder(title.Text.Trim(), new DateTimeOffset(localDue)); RenderCalendar();
        }
        catch (Exception exception) { SetError(exception.Message); }
        finally { _dialogOpen = false; }
    }
    private async Task<string?> PromptAsync(string title, string placeholder, string current, string confirm)
    {
        if (_dialogOpen) return null;
        _dialogOpen = true;
        var input = new TextBox { Text = current, PlaceholderText = placeholder, MaxLength = 1024, Style = (Style)Application.Current.Resources["NotchTextBoxStyle"] };
        var dialog = new ContentDialog { Title = title, Content = input, PrimaryButtonText = confirm, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot, RequestedTheme = ElementTheme.Dark, IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(current) };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        try { return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null; }
        catch (Exception exception) { SetError(exception.Message); return null; }
        finally { _dialogOpen = false; }
    }
    public void Dispose()
    {
        _liveTick.Stop(); _seekDelay?.Cancel(); _volumeDelay?.Cancel();
        _viewModel.PropertyChanged -= ViewModelChanged; _viewModel.Reminders.CollectionChanged -= RemindersChanged;
        _seekDelay?.Dispose(); _volumeDelay?.Dispose();
    }
}
