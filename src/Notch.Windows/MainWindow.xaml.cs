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
    private readonly DispatcherTimer _openDelay = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer _closeDelay = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly DispatcherTimer _switchDelay = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private ModuleId? _pendingModule;
    private bool _quitting;
    private bool _started;
    private bool _active;
    private bool _shutdownDialogOpen;
    private bool _closingAttempt;
    public MainWindow()
    {
        InitializeComponent();
        NativeTheme.Initialize(DispatcherQueue);
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
            if (!_active) { _closeDelay.Stop(); _closeDelay.Start(); }
        };
        Closed += async (_, _) => { if (!_quitting) await QuitAsync(); };
        _openDelay.Tick += (_, _) => { _openDelay.Stop(); Open(); };
        _closeDelay.Tick += (_, _) => { _closeDelay.Stop(); if (!_vm.Preferences.Pinned && !EditorHasFocus()) _vm.Overlay.Collapse(); };
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
        Item("Open " + ProductIdentity.DisplayName, Open); Item("Settings", OpenSettings); Item("Hide " + ProductIdentity.DisplayName, _host.Hide); menu.Items.Add(new MenuFlyoutSeparator()); Item("Quit " + ProductIdentity.DisplayName, () => _ = QuitAsync()); return menu;
    }
    private void BuildToolbar()
    {
        foreach (var module in ModuleCatalog.Toolbar)
        {
            var definition = ModuleCatalog.Get(module);
            NavigationButtons.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var button = new Button
            {
                Content = new FontIcon { Glyph = definition.Glyph, FontSize = 16 },
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
    private void OnThemeChanged(object? sender, EventArgs args) { if (!_quitting) RenderShell(false); }
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
        if (args.PropertyName is nameof(MainViewModel.Preferences) or nameof(MainViewModel.IsDemo) or nameof(MainViewModel.IsReady) or nameof(MainViewModel.IsPremium)) RenderShell(false);
        else if (args.PropertyName is nameof(MainViewModel.Media) or nameof(MainViewModel.FocusTime)) CompactTitle.Text = _vm.Media?.Title ?? (_vm.FocusRunning ? "Focus · " + _vm.FocusTime : ProductIdentity.DisplayName);
        else if (args.PropertyName == nameof(MainViewModel.Error)) { ErrorBar.Message = _vm.Error; ErrorBar.IsOpen = !string.IsNullOrWhiteSpace(_vm.Error); }
        else if (args.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.SaveState) or nameof(MainViewModel.PlanStatus)) UpdateStatus();
    }
    private void RenderShell(bool animate, bool reposition = true)
    {
        if (_quitting) return;
        var mode = _vm.Overlay.Mode;
        var expanded = mode == OverlayMode.Expanded;
        var activity = mode == OverlayMode.Activity;
        var definition = ModuleCatalog.Get(_vm.SelectedModule);
        var requestedWidth = activity ? 460 : definition.Width;
        var requestedHeight = activity ? 144 : definition.Height + 22;
        if (reposition)
            _host.ResizeAndPlace(requestedWidth, requestedHeight, expanded || activity, _vm.Preferences.Pinned, _vm.Preferences.ActiveMonitor,
                showToolbar: expanded, monitorDeviceId: _vm.Preferences.MonitorDeviceId, horizontalOffset: _vm.Preferences.HorizontalOffset,
                topOffset: _vm.Preferences.TopOffset, suppressInFullscreen: _vm.Preferences.HideInFullscreen,
                animate: animate && !_vm.Preferences.ReducedMotion && NativeTheme.AnimationsEnabled, geometryChanged: UpdateShellGeometry);
        BodyRow.Height = new(_host.LogicalPanelHeight);
        GapRow.Height = new(expanded ? 10 : 0); ToolbarRow.Height = new(expanded ? 48 : 0); TailRow.Height = new(expanded ? 12 : 0);
        PanelSurface.Width = _host.LogicalWidth;
        CompactButton.Visibility = !expanded && !activity ? Visibility.Visible : Visibility.Collapsed;
        ExpandedContent.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ActivityContent.Visibility = activity ? Visibility.Visible : Visibility.Collapsed;
        ToolbarGrid.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        var toolbarWidth = Math.Min(720, _host.LogicalWidth);
        ToolbarGrid.Width = toolbarWidth; NavigationColumn.Width = new(Math.Max(0, toolbarWidth - 112));
        ToolContent.Content = !expanded ? null : _vm.SelectedModule is ModuleId.Home or ModuleId.Media or ModuleId.Revenue or ModuleId.Analytics or ModuleId.Coding or ModuleId.Calendar or ModuleId.Weather or ModuleId.Focus
            ? _featured ??= new(_vm) : _utilities ??= new(_vm);
        ToolContent.IsEnabled = _vm.IsReady && !_closingAttempt;
        PinButton.IsEnabled = _vm.IsReady && !_closingAttempt;
        foreach (var (id, button) in _buttons)
        {
            var selected = id == _vm.SelectedModule;
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
        CompactTitle.Text = _vm.Media?.Title ?? (_vm.FocusRunning ? "Focus · " + _vm.FocusTime : ProductIdentity.DisplayName);
        if (activity && _vm.Overlay.Activity is { } notification)
        {
            ActivityOpenButton.Visibility = notification.Destination is null ? Visibility.Collapsed : Visibility.Visible;
            ActivitySource.Text = notification.Source.ToUpperInvariant(); ActivityTitle.Text = notification.Title; ActivityDetail.Text = notification.Detail ?? "";
            ActivityGlyph.Glyph = notification.Kind switch { ActivityKind.Focus => "\uE916", ActivityKind.Meeting => "\uE787", ActivityKind.Sale => "\uE8C7", _ => "\uE8EA" };
        }
        if (animate && expanded && !_vm.Preferences.ReducedMotion && NativeTheme.AnimationsEnabled) AnimateContent();
        if (_started) _host.Show(userRequested: false);
        if (_vm.IsReady && _vm.Preferences.MonitorDeviceId is null && _host.ActiveMonitorDeviceId is { } deviceId)
            _ = _vm.ExecuteAsync(() => _vm.SetPreferencesAsync(_vm.Preferences with { MonitorDeviceId = deviceId }));
    }
    private void UpdateStatus()
    {
        StatusText.Text = _vm.IsDemo ? "DEMO — sample data" : _vm.PlanStatus + " · " + _vm.SaveState + (string.IsNullOrWhiteSpace(_vm.Status) ? "" : " · " + _vm.Status);
        ToolTipService.SetToolTip(StatusText, StatusText.Text);
    }
    private void UpdateShellGeometry()
    {
        BodyRow.Height = new(_host.LogicalPanelHeight);
        PanelSurface.Width = _host.LogicalWidth;
        var width = Math.Min(720, _host.LogicalWidth);
        ToolbarGrid.Width = width; NavigationColumn.Width = new(Math.Max(0, width - 112));
    }
    private void AnimateContent()
    {
        var visual = ElementCompositionPreview.GetElementVisual(ToolContent);
        var compositor = visual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, .7f), new(.2f, 1));
        var opacity = compositor.CreateScalarKeyFrameAnimation(); opacity.InsertKeyFrame(0, .35f); opacity.InsertKeyFrame(1, 1, easing); opacity.Duration = TimeSpan.FromMilliseconds(160);
        var scale = compositor.CreateVector3KeyFrameAnimation(); scale.InsertKeyFrame(0, new(.985f, .985f, 1)); scale.InsertKeyFrame(1, Vector3.One, easing); scale.Duration = TimeSpan.FromMilliseconds(180);
        visual.CenterPoint = new((float)ToolContent.ActualWidth / 2, 0, 0);
        visual.StartAnimation("Opacity", opacity); visual.StartAnimation("Scale", scale);
    }
    private void Open() { _host.Show(); _vm.Overlay.SetInteractionSuppressed(false); _vm.Overlay.Expand(_vm.CanAccessModule(_vm.SelectedModule) ? _vm.SelectedModule : ModuleId.Settings); _host.Show(); }
    private void OpenSettings() { _vm.SelectModule(ModuleId.Settings); _host.Show(); Activate(); }
    private void Toggle()
    {
        if (HasOpenDialog) return;
        if (_host.IsHidden || _vm.Overlay.Mode == OverlayMode.Collapsed) { Open(); Activate(); }
        else _vm.Overlay.Collapse(force: true);
    }
    private bool HasOpenDialog => _shutdownDialogOpen || _featured?.HasOpenDialog == true || _utilities?.HasOpenDialog == true;
    private bool EditorHasFocus() => HasOpenDialog || (_active && RootGrid.XamlRoot is not null && FocusManager.GetFocusedElement(RootGrid.XamlRoot) is TextBox or PasswordBox or NumberBox or Slider) || _featured?.IsManipulating == true || _utilities?.IsManipulating == true;
    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _closeDelay.Stop();
        if (_vm.Overlay.Mode == OverlayMode.Collapsed) { _openDelay.Stop(); _openDelay.Start(); }
    }
    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        _openDelay.Stop(); _closeDelay.Stop(); _closeDelay.Start();
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && !HasOpenDialog) { _vm.Overlay.Collapse(force: true); args.Handled = true; }
        if (args.Key == VirtualKey.F2) { OpenSettings(); args.Handled = true; }
    }
    private void OnOpenClick(object sender, RoutedEventArgs args) { Open(); Activate(); }
    private void OnSettingsClick(object sender, RoutedEventArgs args) => OpenSettings();
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
            _openDelay.Stop(); _closeDelay.Stop(); _switchDelay.Stop();
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
