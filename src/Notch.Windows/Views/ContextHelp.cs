using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Notch.Windows.Views;

/// <summary>Quiet, local guidance with a small symbol and a usable native button target.</summary>
public sealed class ContextHelp : Button
{
    public const string QuickGuideText = "Your desktop tools, kept close in one native notch.\n\n" +
        "Open and move around\nHover or click the notch to open it. Choose a tool in the dock or open Tools for the full catalog. Pin keeps the panel open; Escape collapses it. Ctrl + Shift + Space reopens Notchling, and F2 opens Settings. The Windows tray also has Open, Settings and Quit.\n\n" +
        "Make it yours\nSettings lets you adjust display placement, hover navigation, motion and focus intervals. Home is a quick overview: choose a card to open its full controls.\n\n" +
        "Ready on your device\nFocus timers, notes, files and local utilities work without an account. Music follows a player that shares Windows media controls. Clipboard history is optional and starts turned off.\n\n" +
        "Connect when you need it\nCalendar and coding use files you import. Revenue and analytics need your own reporting connection; weather needs a configured service. An empty value means no data is available yet. Check connection status in Settings for guidance.";
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(ContextHelp), new PropertyMetadata("Help", ContentChanged));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ContextHelp), new PropertyMetadata("", ContentChanged));

    private readonly TextBlock _heading;
    private readonly TextBlock _description;
    private readonly TextBlock _tooltipText;
    private readonly ScrollViewer _scroll;
    private readonly Flyout _helpFlyout;
    private XamlRoot? _openRoot;
    private readonly List<(DependencyObject Element, long Token)> _visibilitySubscriptions = [];

    public string Heading { get => (string?)GetValue(HeadingProperty) ?? "Help"; set => SetValue(HeadingProperty, value); }
    public string Description { get => (string?)GetValue(DescriptionProperty) ?? ""; set => SetValue(DescriptionProperty, value); }

    public ContextHelp()
    {
        Style = (Style)Application.Current.Resources["NotchIconButtonStyle"];
        Width = Height = MinWidth = MinHeight = 28;
        Padding = new Thickness(6);
        CornerRadius = new CornerRadius(14);
        VerticalAlignment = VerticalAlignment.Center;
        Foreground = NativeTheme.Muted;
        Content = new VisualIcon { Kind = VisualIconKind.Help, Size = 14 };
        UseSystemFocusVisuals = true;

        _heading = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Foreground = NativeTheme.Foreground };
        _description = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = NativeTheme.Foreground };
        var content = new StackPanel { Spacing = 9 };
        content.Children.Add(_heading);
        content.Children.Add(_description);
        _scroll = new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            ZoomMode = ZoomMode.Disabled };
        _helpFlyout = new Flyout { Content = _scroll, Placement = FlyoutPlacementMode.Bottom };
        _helpFlyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FrameworkElement.MinWidthProperty, 0d),
                new Setter(Control.PaddingProperty, new Thickness(14)),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(14)),
                new Setter(Control.BackgroundProperty, NativeTheme.Card),
                new Setter(Control.ForegroundProperty, NativeTheme.Foreground),
                new Setter(Control.BorderBrushProperty, NativeTheme.Border),
                new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            },
        };
        _tooltipText = new TextBlock { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3, MaxWidth = 260 };
        ToolTipService.SetToolTip(this, new ToolTip { Content = _tooltipText });
        _helpFlyout.Opened += (_, _) => AttachPopupLifetime();
        _helpFlyout.Closed += (_, _) => DetachPopupLifetime();
        Click += (_, _) => ShowHelp();
        _scroll.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            _helpFlyout.Hide();
            args.Handled = true;
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Escape || !_helpFlyout.IsOpen) return;
            _helpFlyout.Hide();
            args.Handled = true;
        };
        Unloaded += (_, _) => { _helpFlyout.Hide(); DetachPopupLifetime(); };
        UpdateContent();
    }

    private static void ContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((ContextHelp)sender).UpdateContent();

    private void UpdateContent()
    {
        // A dependency-property callback can run while the base control initializes.
        if (_heading is null) return;
        _heading.Text = Heading;
        _description.Text = Description;
        // Hover offers a short preview. Full guidance opens only through an explicit action.
        _tooltipText.Text = Description.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Heading;
        AutomationProperties.SetName(this, "Help about " + Heading);
        AutomationProperties.SetHelpText(this, "Show an explanation. Press Escape to close it.");
    }

    private void ShowHelp()
    {
        if (_helpFlyout.IsOpen) { _helpFlyout.Hide(); return; }
        if (XamlRoot is null || XamlRoot.Size.Width <= 0 || XamlRoot.Size.Height <= 0) return;
        UpdatePopupBounds();
        var id = AutomationProperties.GetAutomationId(this);
        AutomationProperties.SetAutomationId(_heading, id + "Heading");
        AutomationProperties.SetAutomationId(_description, id + "Description");
        // A help explanation needs no extra motion; this also respects Reduce motion.
        _helpFlyout.AreOpenCloseAnimationsEnabled = false;
        // Pointer help leaves the existing keyboard focus alone. Keyboard activation
        // receives normal flyout focus, so a long guide can be scrolled and dismissed.
        _helpFlyout.ShowAt(this, new FlyoutShowOptions
        {
            ShowMode = FocusState == FocusState.Keyboard ? FlyoutShowMode.Standard : FlyoutShowMode.Transient,
        });
    }

    private void UpdatePopupBounds()
    {
        if (XamlRoot is not { } root) return;
        // Reserve room for the native presenter and its placement margin. A
        // minimum content size must never exceed a small, scaled display's host.
        _scroll.Width = Math.Min(300, Math.Max(1, root.Size.Width - 64));
        _scroll.MaxHeight = Math.Min(360, Math.Max(1, root.Size.Height - 76));
    }

    private void AttachPopupLifetime()
    {
        DetachPopupLifetime();
        _openRoot = XamlRoot;
        if (_openRoot is not null) _openRoot.Changed += HostChanged;
        NativeTheme.Changed += ThemeChanged;
        // Module trees stay loaded when another tool is selected. Close this
        // explanation as soon as its owner is hidden instead of leaving an
        // orphan popup over the newly selected tool.
        for (DependencyObject? element = this; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is not UIElement) continue;
            var token = element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (sender, _) =>
            {
                if (((UIElement)sender).Visibility != Visibility.Visible) _helpFlyout.Hide();
            });
            _visibilitySubscriptions.Add((element, token));
        }
        UpdatePopupBounds();
    }

    private void DetachPopupLifetime()
    {
        if (_openRoot is not null) _openRoot.Changed -= HostChanged;
        _openRoot = null;
        NativeTheme.Changed -= ThemeChanged;
        foreach (var (element, token) in _visibilitySubscriptions)
            element.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        _visibilitySubscriptions.Clear();
    }

    private void HostChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (!sender.IsHostVisible) _helpFlyout.Hide();
        else UpdatePopupBounds();
    }
    private void ThemeChanged(object? sender, EventArgs args) => UpdatePopupBounds();
}
