using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Notch.Core;
using Notch.Windows.Interop;
using Notch.Windows.ViewModels;
using Notch.Windows.Views;
using Notch.Windows.Services;
using System.ComponentModel;
using System.Numerics;
using Windows.System;

namespace Notch.Windows;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly OverlayHost _host;
    private readonly TrayService _tray;
    private readonly WindowIcon _windowIcon;
    private FeaturedToolsView? _featured;
    private UtilityToolsView? _utilities;
    private readonly Dictionary<ModuleId, Button> _buttons = [];
    private readonly DispatcherTimer _openDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer _hoverMonitor = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly HoverInteractionPolicy _hoverInteraction = new(new SystemClock());
    private readonly DispatcherTimer _switchDelay = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private ModuleId? _pendingModule;
    private bool _quitting;
    private bool _started;
    private bool _active;
    private bool _shutdownDialogOpen;
    private bool _closingAttempt;
    private bool? _dockPremium;
    private OverlayMode _renderedMode;
    private ModuleId? _renderedModule;
    private bool _explicitOpening;
    private int _hoverDiagnosticCount;
    public MainWindow()
    {
        InitializeComponent();
        NativeTheme.Initialize(DispatcherQueue);
        ElementCompositionPreview.SetIsTranslationEnabled(ToolContent, true);
        NativeTheme.ApplyCardFeedback(RootGrid);
        _host = new(this);
        _vm = new(DispatcherQueue) { WindowHandle = _host.Handle };
        Title = ProductIdentity.WindowTitle;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Notchling.ico");
        try { AppWindow.SetIcon(iconPath); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            // A damaged optional shell asset must not prevent reaching repair/settings controls.
            StartupDiagnostics.Write("MainWindow.AppWindowIcon", error);
        }
        _windowIcon = new(_host.Handle, iconPath);
        _tray = new(_host.Handle, () => { Open(); Activate(); }, OpenSettings, () => _ = QuitAsync(), _windowIcon.SmallIcon);
        _host.ToggleRequested += (_, _) => Toggle();
        _host.ShowRequested += (_, _) => { Open(); Activate(); };
        _host.DisplayChanged += OnDisplayChanged;
        _host.Error += (_, message) => _vm.ShowError(message);
        _host.PowerStateChanged += (_, transition) => { if (transition.Suspended) _vm.OnSuspending(); else _vm.OnResumed(transition.SuspendedFor); };
        _vm.CanPresentActivity = () => _host.IsVisible && !EditorHasFocus();
        NativeTheme.Changed += OnThemeChanged;
        _vm.Overlay.Changed += (_, _) => RenderShell(true);
        _vm.PropertyChanged += OnViewModelChanged;
        AppWindow.Closing += (window, args) =>
        {
            if (_quitting) return;
            args.Cancel = true;
            if (_closingAttempt) return;
            if (_tray.IsAvailable) _host.Hide();
            else _ = QuitAsync(); // Keep the HWND alive for save/export recovery when no tray is available.
        };
        Activated += (_, args) =>
        {
            _active = args.WindowActivationState != WindowActivationState.Deactivated;
            if (!_active) _hoverInteraction.ReleaseExplicitLease();
        };
        Closed += async (_, _) => { if (!_quitting) await QuitAsync(); };
        // Observe native pointer position while expanded as well as routed events.
        // Resizing, clipped dock gaps and transient drag/dialog guards can lose a
        // PointerExited event; a one-shot timer would then leave the island stuck.
        _openDelay.Tick += (_, _) =>
        {
            _openDelay.Stop();
            TraceHover("OpenDelayTick");
            if (_vm.Overlay.Mode == OverlayMode.Collapsed && _host.IsPointerInsideWindow && !_quitting)
                Open(hover: true);
        };
        _hoverMonitor.Tick += (_, _) => CheckHoverDismissal();
        RootGrid.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyboardInteraction), true);
        // Native buttons may class-handle pointer events before an ordinary XAML
        // handler on their ancestor receives them. Observe handled events too and
        // bind the compact target directly; hovering must not depend on bubbling.
        RootGrid.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
        RootGrid.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        RootGrid.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        CompactButton.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointerEntered), true);
        CompactButton.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointerExited), true);
        CompactButton.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
        _switchDelay.Tick += (_, _) =>
        {
            _switchDelay.Stop();
            if (_pendingModule is { } module && _vm.Preferences.HoverNavigation && !EditorHasFocus()) _vm.SelectModule(module);
        };
        BuildToolbar(); RenderShell(false);
        RootGrid.ContextFlyout = MakeContextMenu();
    }
    public async void Start()
    {
        if (_started) return; _started = true;
        try
        {
            // Launch is an explicit request: remain reachable even when another app is fullscreen.
            // Capture that foreground app before Activate changes it, as the tray Open path does.
            _host.Show();
            Activate();
            await _vm.InitializeAsync(); RenderShell(false);
            if (!_host.HotkeyRegistered) _vm.ShowError($"Ctrl+Shift+Space is already registered by another app. Open {ProductIdentity.DisplayName} from the tray.");
            if (!_tray.IsAvailable) { _vm.ShowError($"The tray icon is unavailable. Right-click {ProductIdentity.DisplayName} for Settings or Quit."); _vm.Overlay.Expand(ModuleId.Home); }
            StartupDiagnostics.Write("MainWindow.Ready", null, $"Visible: {_host.IsVisible}; tray: {_tray.IsAvailable}; hotkey: {_host.HotkeyRegistered}");
        }
        catch (Exception error)
        {
            StartupDiagnostics.ReportFatal("MainWindow.Start", error);
            Application.Current.Exit();
        }
    }
    private MenuFlyout MakeContextMenu()
    {
        var menu = new MenuFlyout();
        void Item(string name, Action action) { var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Item("Open " + ProductIdentity.DisplayName, () => Open()); Item("Settings", OpenSettings); Item("Hide " + ProductIdentity.DisplayName, _host.Hide); menu.Items.Add(new MenuFlyoutSeparator()); Item("Quit " + ProductIdentity.DisplayName, () => _ = QuitAsync()); return menu;
    }
    private void BuildToolbar()
    {
        _pendingModule = null; _switchDelay.Stop();
        NavigationButtons.Children.Clear(); NavigationButtons.ColumnDefinitions.Clear(); _buttons.Clear();
        _dockPremium = _vm.CanUseExtendedTools;
        ModuleId[] modules = _vm.CanUseExtendedTools
            ? [ModuleId.Home, ModuleId.Media, ModuleId.Focus, ModuleId.Calendar, ModuleId.Shelf, ModuleId.Clipboard, ModuleId.Tools]
            : [ModuleId.Home, ModuleId.Media, ModuleId.Focus, ModuleId.Scratchpad, ModuleId.Tools];
        foreach (var module in modules)
        {
            var definition = ModuleCatalog.Get(module);
            NavigationButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button
            {
                Content = new VisualIcon { Kind = VisualIcon.ForModule(module), Size = 19 },
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), CornerRadius = new(20),
                Padding = new(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                UseSystemFocusVisuals = true,
            };
            AutomationProperties.SetName(button, definition.Title); ToolTipService.SetToolTip(button, definition.Title);
            button.Click += (_, _) => { _vm.SelectModule(module); Activate(); };
            button.PointerEntered += (_, _) => { _pendingModule = module; _switchDelay.Stop(); _switchDelay.Start(); };
            button.PointerExited += (_, _) => { if (_pendingModule == module) { _pendingModule = null; _switchDelay.Stop(); } };
            Grid.SetColumn(button, _buttons.Count); _buttons.Add(module, button); NavigationButtons.Children.Add(button);
        }
    }
    private void OnThemeChanged(object? sender, EventArgs args) { if (!_quitting) { NativeTheme.ApplyCardFeedback(RootGrid); RenderShell(false); } }
    private void OnDisplayChanged(object? sender, EventArgs args)
    {
        if (_quitting) return;
        try { _windowIcon.RefreshForDpi(_tray.SetIcon); }
        catch (Exception error) { System.Diagnostics.Debug.WriteLine($"App icon DPI refresh: {error.Message}"); }
        // Refresh also publishes the existing theme event, which renders the shell once.
        NativeTheme.Refresh();
    }
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_quitting) return;
        if (args.PropertyName is nameof(MainViewModel.Preferences) or nameof(MainViewModel.IsDemo) or nameof(MainViewModel.IsReady) or nameof(MainViewModel.IsPremium) or nameof(MainViewModel.CanUseExtendedTools)) RenderShell(false);
        else if (args.PropertyName is nameof(MainViewModel.Media) or nameof(MainViewModel.FocusTime) or nameof(MainViewModel.FocusRunning) or nameof(MainViewModel.MediaSourceSelection) or nameof(MainViewModel.HasAvailableUpdate)) UpdateCompactTitle();
        else if (args.PropertyName == nameof(MainViewModel.Error)) { ErrorBar.Message = _vm.Error; ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(_vm.Error); }
        else if (args.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.SaveState) or nameof(MainViewModel.PlanStatus) or nameof(MainViewModel.ShellStatus) or nameof(MainViewModel.HasUnsavedChanges)) UpdateStatus();
    }
    private void RenderShell(bool animate, bool reposition = true)
    {
        if (_quitting) return;
        if (_dockPremium != _vm.CanUseExtendedTools) BuildToolbar();
        var mode = _vm.Overlay.Mode;
        var expanded = mode == OverlayMode.Expanded;
        var contentChanged = _renderedMode != mode || _renderedModule != _vm.SelectedModule;
        if (expanded && _renderedMode != OverlayMode.Expanded) _hoverInteraction.Begin(_explicitOpening);
        if (expanded && !_vm.Preferences.Pinned) _hoverMonitor.Start(); else _hoverMonitor.Stop();
        var activity = mode == OverlayMode.Activity;
        var definition = ModuleCatalog.Get(_vm.SelectedModule);
        var (contentWidth, contentHeight) = _vm.SelectedModule switch
        {
            ModuleId.Settings => (640d, 540d),
            ModuleId.Tools => (640d, 470d),
            ModuleId.Home when !_vm.CanUseExtendedTools => (600d, 330d),
            ModuleId.Media => (600d, _vm.CanUseExtendedTools ? 310d : 250d),
            ModuleId.Focus when !_vm.CanUseExtendedTools => (520d, 310d),
            _ => (definition.Width, definition.Height),
        };
        var requestedWidth = activity ? 460 : contentWidth;
        var requestedHeight = activity ? 144 : contentHeight + (_vm.IsDemo ? 66 : 26);
        if (reposition)
            _host.ResizeAndPlace(requestedWidth, requestedHeight, expanded || activity, _vm.Preferences.Pinned, _vm.Preferences.ActiveMonitor,
                showToolbar: expanded, monitorDeviceId: _vm.Preferences.MonitorDeviceId, horizontalOffset: _vm.Preferences.HorizontalOffset,
                topOffset: _vm.Preferences.TopOffset, suppressInFullscreen: _vm.Preferences.HideInFullscreen,
                animate: animate && !_vm.Preferences.ReducedMotion && NativeTheme.AnimationsEnabled, geometryChanged: UpdateShellGeometry,
                toolbarWidth: _vm.CanUseExtendedTools ? 496 : 400, cornerRadius: expanded ? 26 : 20,
                animationsEnabled: !_vm.Preferences.ReducedMotion && NativeTheme.AnimationsEnabled);
        UpdateShellGeometry();
        PanelSurface.CornerRadius = new(0, 0, expanded ? 26 : 20, expanded ? 26 : 20);
        CompactButton.Visibility = !expanded && !activity ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ActivityContent.Visibility = activity ? Visibility.Visible : Visibility.Collapsed;
        ToolbarGrid.Visibility = expanded && _host.ToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
        object? content = !expanded ? null : _vm.SelectedModule is ModuleId.Home or ModuleId.Media or ModuleId.Revenue or ModuleId.Analytics or ModuleId.Coding or ModuleId.Calendar or ModuleId.Weather or ModuleId.Focus
            ? _featured ??= new(_vm) : _utilities ??= new(_vm);
        if (!ReferenceEquals(ToolContent.Content, content)) ToolContent.Content = content;
        ToolContent.IsEnabled = _vm.IsReady && !_closingAttempt;
        PinButton.IsEnabled = _vm.IsReady && !_closingAttempt;
        foreach (var (id, button) in _buttons)
        {
            var selected = id == _vm.SelectedModule || id == ModuleId.Tools && !_buttons.ContainsKey(_vm.SelectedModule) && _vm.SelectedModule != ModuleId.Settings;
            button.IsEnabled = !_closingAttempt;
            button.Background = selected ? NativeTheme.SelectedBackground : NativeTheme.Brush("Transparent");
            button.Foreground = selected ? NativeTheme.SelectedForeground : NativeTheme.Foreground;
            ToolTipService.SetToolTip(button, ModuleCatalog.Get(id).Title + (_vm.CanAccessModule(id) ? "" : " — Premium"));
        }
        PinButton.IsChecked = _vm.Preferences.Pinned;
        PinButton.Background = _vm.Preferences.Pinned ? NativeTheme.SelectedBackground : NativeTheme.Brush("Transparent");
        PinButton.Foreground = _vm.Preferences.Pinned ? NativeTheme.SelectedForeground : NativeTheme.Foreground;
        UpdateStatus();
        ErrorBar.Message = _vm.Error; ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(_vm.Error);
        UpdateCompactTitle();
        if (activity && _vm.Overlay.Activity is { } notification)
        {
            ActivityOpenButton.Visibility = notification.Destination is null ? Visibility.Collapsed : Visibility.Visible;
            ActivitySource.Text = notification.Source.ToUpperInvariant(); ActivityTitle.Text = notification.Title; ActivityDetail.Text = notification.Detail ?? "";
            ActivityGlyph.Kind = notification.Kind switch { ActivityKind.Focus => VisualIconKind.Focus, ActivityKind.Meeting => VisualIconKind.Calendar, ActivityKind.Sale => VisualIconKind.Revenue, _ => VisualIconKind.Notification };
        }
        if (animate && contentChanged && expanded && !_vm.Preferences.ReducedMotion && NativeTheme.AnimationsEnabled)
        {
            AnimateContent(_renderedMode != OverlayMode.Expanded);
            if (_renderedMode != OverlayMode.Expanded) AnimateDock();
        }
        if (!expanded || _vm.Preferences.ReducedMotion || !NativeTheme.AnimationsEnabled) ResetShellAnimations();
        _renderedMode = mode; _renderedModule = _vm.SelectedModule;
        if (_started) _host.Show(userRequested: false);
        if (_vm.IsReady && _vm.Preferences.MonitorDeviceId is null && _host.ActiveMonitorDeviceId is { } deviceId)
            _ = _vm.ExecuteAsync(() => _vm.SetPreferencesAsync(_vm.Preferences with { MonitorDeviceId = deviceId }));
    }
    private void UpdateStatus()
    {
        DemoStrip.Visibility = _vm.IsDemo ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _vm.ShellStatus;
        StatusText.Visibility = string.IsNullOrWhiteSpace(StatusText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(StatusText, StatusText.Text);
    }
    private void UpdateCompactTitle()
    {
        var media = _vm.Media;
        var title = media is not null ? MediaPresentation.Title(media) : _vm.FocusRunning ? "Focus · " + _vm.FocusTime : ProductIdentity.DisplayName;
        CompactTitle.Text = _vm.IsDemo ? "Preview · " + title : title;
        CompactGlyph.Kind = media is not null ? VisualIconKind.Music : _vm.FocusRunning ? VisualIconKind.Focus : VisualIconKind.ChevronDown;
        CompactArtwork.Visibility = media is null ? Visibility.Visible : Visibility.Collapsed;
        CompactSourceIcon.Visibility = media is null ? Visibility.Collapsed : Visibility.Visible;
        CompactSourceIcon.SetSource(media, _vm.MediaSourceSelection);
        UpdateBadge.Visibility = _vm.HasAvailableUpdate ? Visibility.Visible : Visibility.Collapsed;
        var detail = media is null ? CompactTitle.Text : string.Join(" · ", new[] {
            MediaPresentation.Title(media), media.Artist, media.AlbumTitle,
            MediaSourceIdentity.Resolve(media, _vm.MediaSourceSelection).Tooltip
        }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));
        if (_vm.HasAvailableUpdate) detail += " · Notchling " + _vm.AvailableUpdateVersion + " is available in Settings";
        ToolTipService.SetToolTip(CompactButton, detail);
    }
    private void UpdateShellGeometry()
    {
        static void Row(RowDefinition row, double height)
        {
            if (row.Height.GridUnitType != GridUnitType.Pixel || row.Height.Value != height)
                row.Height = new(height);
        }
        Row(BodyRow, _host.LogicalPanelHeight);
        if (PanelSurface.Width != _host.LogicalWidth) PanelSurface.Width = _host.LogicalWidth;
        Row(GapRow, _host.ToolbarVisible ? 10 : 0);
        Row(ToolbarRow, _host.ToolbarVisible ? 48 : 0);
        Row(TailRow, _host.ToolbarVisible ? 12 : 0);
        ToolbarGrid.Visibility = _host.ToolbarVisible ? Visibility.Visible : Visibility.Collapsed;
        var width = Math.Min(_vm.CanUseExtendedTools ? 496 : 400, _host.LogicalWidth);
        if (ToolbarGrid.Width != width) ToolbarGrid.Width = width;
        var navigationWidth = Math.Max(0, width - 112);
        if (NavigationColumn.Width.Value != navigationWidth) NavigationColumn.Width = new(navigationWidth);
    }
    private void AnimateContent(bool opening)
    {
        var visual = ElementCompositionPreview.GetElementVisual(ToolContent);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, .7f), new(.2f, 1));
        // Keep text and vectors at their final raster scale throughout the reveal.
        visual.StopAnimation("Scale"); visual.Scale = Vector3.One;
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, opening ? 0 : .78f); opacity.InsertKeyFrame(1, 1, easing);
        opacity.DelayTime = opening ? TimeSpan.FromMilliseconds(35) : TimeSpan.Zero;
        opacity.Duration = TimeSpan.FromMilliseconds(opening ? 145 : 130);
        var translation = compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new(0, opening ? 5 : 2, 0)); translation.InsertKeyFrame(1, Vector3.Zero, easing);
        translation.DelayTime = opacity.DelayTime; translation.Duration = opacity.Duration;
        visual.StartAnimation("Opacity", opacity); visual.StartAnimation("Translation", translation);
    }
    private void ResetShellAnimations()
    {
        foreach (var element in new UIElement[] { ToolContent, ToolbarGrid })
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Opacity"); visual.StopAnimation("Scale"); visual.StopAnimation("Translation");
            visual.Opacity = 1; visual.Scale = Vector3.One;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }
    private void AnimateDock()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ToolbarGrid);
        var compositor = visual.Compositor;
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, 0); opacity.InsertKeyFrame(1, 1);
        opacity.DelayTime = TimeSpan.FromMilliseconds(100);
        opacity.Duration = TimeSpan.FromMilliseconds(140);
        visual.StartAnimation("Opacity", opacity);
    }
    private void Open(bool hover = false)
    {
        _openDelay.Stop();
        _host.Show();
        _vm.Overlay.SetInteractionSuppressed(false);
        _explicitOpening = !hover;
        _hoverInteraction.Begin(_explicitOpening);
        try { _vm.Overlay.Expand(_vm.CanAccessModule(_vm.SelectedModule) ? _vm.SelectedModule : ModuleId.Settings); }
        finally { _explicitOpening = false; }
        _host.Show();
    }
    private void OpenSettings()
    {
        _openDelay.Stop();
        _explicitOpening = true;
        _hoverInteraction.Begin(true);
        try { _vm.SelectModule(ModuleId.Settings); }
        finally { _explicitOpening = false; }
        _host.Show(); Activate();
    }
    private void Toggle()
    {
        if (HasOpenDialog) return;
        if (_host.IsHidden || _vm.Overlay.Mode == OverlayMode.Collapsed) { Open(); Activate(); }
        else _vm.Overlay.Collapse(force: true);
    }
    private bool HasOpenDialog => _shutdownDialogOpen || _featured?.HasOpenDialog == true || _utilities?.HasOpenDialog == true;
    private bool KeyboardEditorFocused()
    {
        if (!_active || RootGrid.XamlRoot is null) return false;
        var element = FocusManager.GetFocusedElement(RootGrid.XamlRoot) as DependencyObject;
        while (element is not null)
        {
            if (element is TextBox or PasswordBox or NumberBox or Slider) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
    private bool HasProtectedInteraction() => HasOpenDialog || _closingAttempt
        || _featured?.IsManipulating == true || _utilities?.IsManipulating == true
        || RootGrid.XamlRoot is not null && VisualTreeHelper.GetOpenPopupsForXamlRoot(RootGrid.XamlRoot)
            .Any(popup => popup.IsOpen && popup.Child is not ToolTip);
    private bool EditorHasFocus() => HasProtectedInteraction()
        || KeyboardEditorFocused() && _hoverInteraction.HasRecentKeyboardInput;
    private void CheckHoverDismissal()
    {
        if (_quitting || _vm.Overlay.Mode != OverlayMode.Expanded || _vm.Preferences.Pinned)
        { _hoverMonitor.Stop(); return; }
        if (!_host.IsVisible) return;
        var inside = _host.IsPointerInsideWindow;
        if (_hoverInteraction.ShouldCollapse(inside, _vm.Preferences.Pinned,
            !inside && HasProtectedInteraction(), !inside && _active && RootGrid.XamlRoot is not null
                && FocusManager.GetFocusedElement(RootGrid.XamlRoot) is not null))
            _vm.Overlay.Collapse();
    }
    private void OnKeyboardInteraction(object sender, KeyRoutedEventArgs args)
    {
        if (_active && _vm.Overlay.Mode == OverlayMode.Expanded
            && args.Key is not VirtualKey.Escape and not VirtualKey.F2)
            _hoverInteraction.RecordKeyboardInput();
    }
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        var inside = _host.IsPointerInsideWindow;
        if (inside) _hoverInteraction.ShouldCollapse(true, _vm.Preferences.Pinned, false, false);
        if (_vm.Overlay.Mode == OverlayMode.Collapsed && inside && !_openDelay.IsEnabled) _openDelay.Start();
        TraceHover("PointerEntered");
    }
    private void OnPointerMoved(object sender, PointerRoutedEventArgs args)
    {
        if (_vm.Overlay.Mode != OverlayMode.Collapsed || _openDelay.IsEnabled
            || !_host.IsPointerInsideWindow) return;
        // Movement also covers re-entering a native region after a resize when
        // WinUI retains its previous pointer-over target and emits no new enter.
        _openDelay.Start();
        TraceHover("PointerMoved");
    }
    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (!_host.IsPointerInsideWindow) _openDelay.Stop();
        TraceHover("PointerExited");
        CheckHoverDismissal();
    }
    private void TraceHover(string stage)
    {
        if (_quitting || _hoverDiagnosticCount >= 24
            || _vm.Overlay.Mode != OverlayMode.Collapsed && stage != "OpenDelayTick") return;
        _hoverDiagnosticCount++;
        StartupDiagnostics.Write("MainWindow.Hover." + stage, null,
            $"Mode: {_vm.Overlay.Mode}; pointer inside: {_host.IsPointerInsideWindow}; visible: {_host.IsVisible}; " +
            $"logical target: {_host.LogicalWidth:0.##}x{_host.LogicalPanelHeight:0.##}; pending open: {_openDelay.IsEnabled}");
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && !HasOpenDialog) { _vm.Overlay.Collapse(force: true); args.Handled = true; }
        if (args.Key == VirtualKey.F2) { OpenSettings(); args.Handled = true; }
    }
    private void OnOpenClick(object sender, RoutedEventArgs args) { Open(); Activate(); }
    private void OnSettingsClick(object sender, RoutedEventArgs args) => OpenSettings();
    private async void OnExitDemoClick(object sender, RoutedEventArgs args) => await _vm.ExecuteAsync(_vm.ExitDemoAsync);
    private async void OnPinToggled(object sender, RoutedEventArgs args)
    {
        // Toggle state changes also come from accessibility clients, without a Click event.
        if (_quitting || _closingAttempt || _vm is null || !_vm.IsReady ||
            sender is not Microsoft.UI.Xaml.Controls.Primitives.ToggleButton button ||
            button.IsChecked is not bool pinned || pinned == _vm.Preferences.Pinned) return;
        await _vm.ExecuteAsync(() => _vm.SetPreferencesAsync(_vm.Preferences with { Pinned = pinned }));
    }
    private void OnDismissActivity(object sender, RoutedEventArgs args) => _vm.Overlay.DismissActivity();
    private void OnOpenActivity(object sender, RoutedEventArgs args)
    {
        if (_vm.Overlay.Activity?.Destination is { } module) { _vm.SelectModule(module); Activate(); }
    }
    private void OnErrorClosed(InfoBar sender, object args) => _vm.ShowError("");
    private async Task QuitAsync()
    {
        if (_quitting || _closingAttempt) return;
        _closingAttempt = true;
        ToolContent.IsEnabled = false; PinButton.IsEnabled = false;
        foreach (var button in _buttons.Values) button.IsEnabled = false;
        var discard = false;
        try
        {
            try { _utilities?.FlushDrafts(); }
            catch (Exception error) { _vm.ShowError("Your draft is preserved: " + error.Message); }
            var draftFailure = _utilities?.HasInvalidDrafts == true;
            if (draftFailure || !await _vm.SaveBeforeExitAsync())
            {
                OpenSettings();
                var export = new Button { Content = "Export notebook and unsaved drafts", Style = (Style)Application.Current.Resources["NotchButtonStyle"] };
                export.Click += async (_, _) => { try { if (_utilities is not null) await _utilities.ExportAllAsync(includeDrafts: true); } catch (Exception error) { _vm.ShowError(error.Message); } };
                var content = new StackPanel { Spacing = 12 };
                content.Children.Add(new TextBlock { Text = "Your latest changes could not be saved. Retry after correcting storage or content limits, export a recovery copy, or explicitly discard changes.", TextWrapping = TextWrapping.Wrap });
                content.Children.Add(export);
                var dialog = new ContentDialog { XamlRoot = RootGrid.XamlRoot, Title = "Keep your work safe", Content = content,
                    PrimaryButtonText = "Retry save", SecondaryButtonText = "Discard changes and quit", CloseButtonText = "Keep editing", DefaultButton = ContentDialogButton.Close };
                _shutdownDialogOpen = true;
                ContentDialogResult result;
                try { result = await dialog.ShowAsync(); } finally { _shutdownDialogOpen = false; }
                if (result == ContentDialogResult.Secondary) discard = true;
                else if (result == ContentDialogResult.Primary)
                {
                    try { _utilities?.FlushDrafts(); } catch (Exception error) { _vm.ShowError(error.Message); return; }
                    if (!await _vm.SaveBeforeExitAsync()) return;
                }
                else return;
            }
            _quitting = true;
            _openDelay.Stop(); _hoverMonitor.Stop(); _switchDelay.Stop();
            if (discard) await _vm.DiscardAndDisposeAsync(); else await _vm.DisposeAsync();
            _vm.PropertyChanged -= OnViewModelChanged;
            NativeTheme.Changed -= OnThemeChanged;
            _utilities?.Dispose(); _featured?.Dispose(); _tray.Dispose(); _windowIcon.Dispose(); _host.Dispose(); Close(); Application.Current.Exit();
        }
        catch (Exception error)
        {
            _quitting = false; _vm.ShowError(ProductIdentity.DisplayName + " could not finish closing: " + error.Message); Open();
        }
        finally { _closingAttempt = false; if (!_quitting) { ToolContent.IsEnabled = _vm.IsReady; PinButton.IsEnabled = _vm.IsReady; foreach (var button in _buttons.Values) button.IsEnabled = true; } }
    }
}
