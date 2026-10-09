using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Notch.Core;
using Notch.Windows.ViewModels;
using Notch.Windows.Interop;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Notch.Windows.Views;

public sealed partial class UtilityToolsView : UserControl
{
    private readonly MainViewModel _vm;
    private Guid? _editingNote;
    private TextBox? _noteTitle;
    private TextBox? _noteBody;
    private bool _attached;
    public bool HasOpenDialog { get; private set; }
    private TextBlock? _cpuText, _memoryText, _batteryText, _outputText;
    private Slider? _systemVolume;
    private bool _updatingSystemVolume;
    private readonly Dictionary<Guid, (string Title, string Body)> _noteDrafts = [];
    private readonly Dictionary<string, string> _formDrafts = [];
    private readonly Dictionary<string, string> _credentialDrafts = [];
    private ModuleId? _renderedModule;
    private string? _scratchpadDraft;
    private bool _invalidDrafts;
    private TextBlock? _saveStateText;
    private TextBlock? _updateStatusText;
    private Button? _checkUpdatesButton, _installUpdateButton, _cancelUpdateButton;
    private StackPanel? _notificationPanel;
    private TextBlock? _screenTimeText;
    private TextBlock? _subscriptionStatusText;
    private readonly Dictionary<string, (TextBlock State, TextBlock Detail)> _connectionRows = [];
    private Button? _checkConnectionsButton;
    private readonly Dictionary<string, ToggleSwitch> _settingsToggles = [];
    private bool _updatingSettingsToggles;
    private readonly Dictionary<string, bool> _expandedSettings = [];
    public bool HasInvalidDrafts => _invalidDrafts;
    public bool IsManipulating => _systemVolume?.PointerCaptures?.Count > 0;

    public UtilityToolsView(MainViewModel viewModel)
    {
        InitializeComponent(); _vm = viewModel;
        Loaded += (_, _) =>
        {
            if (!_attached) { _vm.PropertyChanged += Changed; _vm.NotificationHistory.CollectionChanged += NotificationsChanged; _attached = true; }
            if (_renderedModule != _vm.SelectedModule) Render();
            else if (_vm.SelectedModule == ModuleId.Settings) UpdateNotifications();
        };
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _vm.NotificationHistory.CollectionChanged -= NotificationsChanged; _attached = false; };
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedModule)) { if (_renderedModule != _vm.SelectedModule) Render(); }
        else if (args.PropertyName == nameof(MainViewModel.IsReady) || args.PropertyName == nameof(MainViewModel.CanUseExtendedTools)) Render();
        else if (args.PropertyName == nameof(MainViewModel.Preferences)) { if (_vm.SelectedModule == ModuleId.Settings) UpdateSettingsToggles(); else Render(); }
        else if (_vm.SelectedModule == ModuleId.System && args.PropertyName == nameof(MainViewModel.System)) UpdateSystemPanel();
        else if (_vm.SelectedModule == ModuleId.Servers && args.PropertyName == nameof(MainViewModel.ListeningPorts)) Render();
        else if (_vm.SelectedModule == ModuleId.ScreenTime && args.PropertyName == nameof(MainViewModel.System) && _screenTimeText is not null) _screenTimeText.Text = _vm.System is { } system ? FocusSession.Format(system.SessionScreenTime) : "00:00";
        else if (_vm.SelectedModule == ModuleId.Clipboard && args.PropertyName == nameof(MainViewModel.Clipboard)) Render();
        else if (_vm.SelectedModule == ModuleId.Settings && args.PropertyName == nameof(MainViewModel.SaveState) && _saveStateText is not null) _saveStateText.Text = _vm.SaveState;
        else if (_vm.SelectedModule == ModuleId.Settings && args.PropertyName == nameof(MainViewModel.UpdateStatus) && _updateStatusText is not null)
        {
            // A shorter pending message must not shrink scrolled content and
            // clamp the viewport before the final update guidance arrives.
            _updateStatusText.MinHeight = Math.Max(_updateStatusText.MinHeight, _updateStatusText.ActualHeight);
            _updateStatusText.Text = _vm.UpdateStatus;
            if (Microsoft.UI.Xaml.Automation.Peers.AutomationPeer.ListenerExists(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged))
            {
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_updateStatusText)
                    ?? Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.CreatePeerForElement(_updateStatusText);
                peer?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
            }
        }
        else if (_vm.SelectedModule == ModuleId.Settings && args.PropertyName == nameof(MainViewModel.SubscriptionStatus) && _subscriptionStatusText is not null) _subscriptionStatusText.Text = _vm.SubscriptionStatus;
        else if (_vm.SelectedModule == ModuleId.Settings && args.PropertyName is nameof(MainViewModel.IsCheckingUpdates) or nameof(MainViewModel.IsInstallingUpdate)
            or nameof(MainViewModel.CanInstallAvailableUpdate) or nameof(MainViewModel.HasAvailableUpdate)) UpdateUpdateControls();
        else if (_vm.SelectedModule == ModuleId.Settings && args.PropertyName is nameof(MainViewModel.ConnectionStatuses) or nameof(MainViewModel.IsCheckingConnections)) UpdateConnectionStatus();
    }
    private TextBlock Text(string value, double size = 13, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? NativeTheme.Muted : NativeTheme.Foreground,
    };
    private Button Button(string title, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Style = (Style)Application.Current.Resources[_vm.SelectedModule == ModuleId.Settings
                ? primary ? "NotchFormPrimaryButtonStyle" : "NotchFormButtonStyle"
                : primary ? "NotchPrimaryButtonStyle" : "NotchButtonStyle"],
            MinWidth = 0,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        button.Click += async (_, _) => { button.IsEnabled = false; try { await _vm.ExecuteAsync(action); } finally { button.IsEnabled = true; } };
        return button;
    }
    private Button Button(string title, Action action, bool primary = false) => Button(title, () => { action(); return Task.CompletedTask; }, primary);
    private static Grid FormRow(FrameworkElement input, params Button[] actions)
    {
        // Keep action buttons at their natural size instead of distributing the
        // entire row among star cells. Labels live outside this row so fields and
        // actions share a baseline, including PasswordBox and NumberBox inputs.
        var row = new Grid { ColumnSpacing = 10, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        input.MinWidth = 0; input.Width = double.NaN; input.HorizontalAlignment = HorizontalAlignment.Stretch;
        var actionGroup = new Grid { ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions)
        {
            action.MinWidth = 64; action.Width = double.NaN;
            action.HorizontalAlignment = HorizontalAlignment.Stretch;
            action.VerticalAlignment = VerticalAlignment.Center;
            actionGroup.Children.Add(action);
        }
        row.Children.Add(input); row.Children.Add(actionGroup);
        bool? stacked = null;
        bool? actionsStacked = null;
        void Arrange()
        {
            if (row.ActualWidth <= 0) return;
            var scale = Math.Max(1, NativeTheme.TextScaleFactor);
            var actionWidth = actions.Sum(action => Math.Clamp((action.Content as TextBlock)?.Text.Length * 6.5 * scale + 24 ?? 80, 68, 210 * scale)) + Math.Max(0, actions.Length - 1) * 8;
            var next = row.ActualWidth < 180 * scale + actionWidth + 10;
            if (stacked != next)
            {
                stacked = next; row.ColumnDefinitions.Clear(); row.RowDefinitions.Clear();
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.RowDefinitions.Add(new() { Height = GridLength.Auto });
                if (next)
                {
                    row.RowDefinitions.Add(new() { Height = GridLength.Auto });
                    Grid.SetColumn(actionGroup, 0); Grid.SetRow(actionGroup, 1);
                }
                else
                {
                    row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    Grid.SetColumn(actionGroup, 1); Grid.SetRow(actionGroup, 0);
                }
            }
            var stackActions = row.ActualWidth < actionWidth;
            if (actionsStacked == stackActions) return;
            actionsStacked = stackActions;
            actionGroup.ColumnDefinitions.Clear(); actionGroup.RowDefinitions.Clear();
            for (var i = 0; i < actions.Length; i++)
            {
                if (stackActions) actionGroup.RowDefinitions.Add(new() { Height = GridLength.Auto });
                else actionGroup.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                Grid.SetColumn(actions[i], stackActions ? 0 : i); Grid.SetRow(actions[i], stackActions ? i : 0);
            }
        }
        row.Loaded += (_, _) => Arrange(); row.SizeChanged += (_, _) => Arrange();
        return row;
    }
    private StackPanel FormField(string label, FrameworkElement input, params Button[] actions)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(Text(label, 12));
        panel.Children.Add(FormRow(input, actions));
        return panel;
    }
    private static Grid Row(params UIElement[] elements)
    {
        // A horizontal StackPanel measures children with infinite width. That defeated
        // text wrapping and made Settings wider than the notch. Use finite star cells,
        // then stack complete controls when there is insufficient room for a row.
        var row = new Grid { ColumnSpacing = 10, RowSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        var actionsOnly = elements.All(element => element is Button);
        var preferred = elements.Select(element => element switch
        {
            FrameworkElement control when double.IsFinite(control.Width) => control.Width,
            Button { Content: TextBlock label } => Math.Clamp(label.Text.Length * 6.5 * NativeTheme.TextScaleFactor + 34, 90, 270),
            TextBox or PasswordBox or NumberBox or ComboBox => 200d,
            _ => 160d,
        }).ToArray();
        foreach (var element in elements)
        {
            if (element is FrameworkElement control) { control.Width = double.NaN; control.MinWidth = 0; control.HorizontalAlignment = actionsOnly ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; }
            row.Children.Add(element);
        }
        bool? stacked = null;
        UIElement[] arranged = [];
        void Arrange()
        {
            if (row.ActualWidth <= 0) return;
            var visible = elements.Where(element => element.Visibility != Visibility.Collapsed).ToArray();
            var desiredWidth = elements.Select((element, index) => element.Visibility == Visibility.Collapsed ? 0 : preferred[index]).Sum();
            var next = row.ActualWidth < desiredWidth + Math.Max(0, visible.Length - 1) * 10;
            if (stacked == next && arranged.SequenceEqual(visible)) return;
            stacked = next;
            arranged = visible;
            row.ColumnDefinitions.Clear(); row.RowDefinitions.Clear();
            foreach (var element in elements) { Grid.SetColumn((FrameworkElement)element, 0); Grid.SetRow((FrameworkElement)element, 0); }
            if (next)
            {
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                for (var i = 0; i < visible.Length; i++) { row.RowDefinitions.Add(new() { Height = GridLength.Auto }); Grid.SetColumn((FrameworkElement)visible[i], 0); Grid.SetRow((FrameworkElement)visible[i], i); }
            }
            else
            {
                row.RowDefinitions.Add(new() { Height = GridLength.Auto });
                for (var i = 0; i < visible.Length; i++) { row.ColumnDefinitions.Add(new() { Width = actionsOnly ? GridLength.Auto : new GridLength(preferred[Array.IndexOf(elements, visible[i])], GridUnitType.Star) }); Grid.SetColumn((FrameworkElement)visible[i], i); Grid.SetRow((FrameworkElement)visible[i], 0); }
            }
        }
        foreach (var element in elements) element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Arrange());
        row.SizeChanged += (_, _) => Arrange();
        row.Loaded += (_, _) => Arrange();
        return row;
    }
    private static Grid AdaptiveGrid(int maximumColumns, double minimumColumnWidth, params UIElement[] elements)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var element in elements) grid.Children.Add(element);
        var columns = 0;
        void Arrange()
        {
            var next = Math.Clamp((int)((grid.ActualWidth + 10) / (minimumColumnWidth * NativeTheme.TextScaleFactor + 10)), 1, maximumColumns);
            if (columns == next) return;
            columns = next; grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < (elements.Length + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var i = 0; i < elements.Length; i++) { Grid.SetColumn((FrameworkElement)elements[i], i % columns); Grid.SetRow((FrameworkElement)elements[i], i / columns); }
        }
        Arrange(); grid.SizeChanged += (_, _) => Arrange(); grid.Loaded += (_, _) => Arrange();
        return grid;
    }
    private Border Card(UIElement child) => new()
    {
        Style = (Style)Application.Current.Resources[_vm.SelectedModule == ModuleId.Settings ? "NotchFormCardStyle" : "NotchCardStyle"],
        Child = child,
    };
    private TextBox Input(string placeholder, string value = "", bool multiline = false) => new()
    {
        PlaceholderText = placeholder, Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Style = (Style)Application.Current.Resources[_vm.SelectedModule == ModuleId.Settings ? "NotchFormTextBoxStyle" : "NotchTextBoxStyle"], MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private TextBox DraftInput(string key, string placeholder, string value = "", bool multiline = false)
    {
        var input = Input(placeholder, _formDrafts.GetValueOrDefault(key, value), multiline);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, placeholder);
        input.TextChanged += (_, _) => _formDrafts[key] = input.Text;
        return input;
    }
    private void Header(string title, string description)
    {
        ContentStack.Children.Add(Text(title, 22)); ContentStack.Children.Add(Text(description, 12, true));
    }
    private void Render()
    {
        var sameModule = _renderedModule == _vm.SelectedModule;
        var previousOffset = sameModule ? ContentScroll.VerticalOffset : 0;
        _renderedModule = _vm.SelectedModule;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ContentScroll, _vm.SelectedModule == ModuleId.Settings ? "Notchling settings content" : "Notchling tool content");
        ContentStack.Children.Clear();
        _settingsToggles.Clear();
        _updateStatusText = null;
        _checkUpdatesButton = _installUpdateButton = _cancelUpdateButton = null;
        _notificationPanel = null;
        _subscriptionStatusText = null;
        _connectionRows.Clear(); _checkConnectionsButton = null;
        var definition = ModuleCatalog.Get(_vm.SelectedModule);
        Header(definition.Title, _vm.SelectedModule == ModuleId.Tools
            ? _vm.IsPublicTesting ? "All 21 tools are free during public testing. Choose what you need." : "Choose a tool. Availability and connection requirements are shown below."
            : definition.Description);
        switch (_vm.SelectedModule)
        {
            case ModuleId.Tools: Tools(); break;
            case ModuleId.Notes: Notes(); break;
            case ModuleId.Scratchpad: Scratchpad(); break;
            case ModuleId.Shelf: Shelf(); break;
            case ModuleId.Files: Files(); break;
            case ModuleId.Clipboard: Clipboard(); break;
            case ModuleId.Links: Links(); break;
            case ModuleId.System: SystemPanel(); break;
            case ModuleId.Servers: Servers(); break;
            case ModuleId.ScreenTime: ScreenTime(); break;
            case ModuleId.Emoji: Emoji(); break;
            case ModuleId.Sounds: Sounds(); break;
            case ModuleId.Convert: ConvertUnits(); break;
            case ModuleId.Awake: Awake(); break;
            case ModuleId.Settings: Settings(); break;
        }
        ContentScroll.ChangeView(null, previousOffset, null, true);
    }
    private static bool NeedsConnection(ModuleId module) => module is ModuleId.Revenue or ModuleId.Analytics or ModuleId.Coding or ModuleId.Calendar or ModuleId.Weather;
    private static string ConnectionRequirement(ModuleId module) => module switch
    {
        ModuleId.Revenue => "Connect your Stripe reporting account",
        ModuleId.Analytics => "Connect a compatible analytics endpoint",
        ModuleId.Coding => "Import your local coding activity file",
        ModuleId.Calendar => "Import an ICS calendar file",
        ModuleId.Weather => "Requires the configured licensed weather service",
        ModuleId.Media => "Uses a supported Windows media player",
        ModuleId.Clipboard => "Opt-in plain-text history; cleared on exit",
        _ => ModuleCatalog.Get(module).Description,
    };
    private void Tools()
    {
        var modules = ModuleCatalog.All.Where(item => item.Id is not ModuleId.Tools and not ModuleId.Settings).ToArray();
        AddToolGroup("Everyday tools", "Music controls, focus sessions and your scratchpad.", modules.Where(module => !Notch.Core.Commerce.FeaturePolicy.RequiresPremium(module.Id)));
        AddToolGroup(_vm.IsPublicTesting ? "Local utilities" : "Premium utilities", _vm.IsPublicTesting ? "Files, notes and useful controls that stay on your device." : _vm.CanUseExtendedTools ? "Your extended local tools are available." : "US$2/month. " + (_vm.BillingConfigured ? "Open Settings to purchase or restore access." : "Purchasing will open when subscriptions launch."), modules.Where(module => Notch.Core.Commerce.FeaturePolicy.RequiresPremium(module.Id) && !NeedsConnection(module.Id)));
        AddToolGroup("Connected tools", _vm.IsPublicTesting ? "Connect your own provider or import a file to see real data." : "Premium access plus your own provider connection or imported file. No sample data is shown unless you enable the preview.", modules.Where(module => NeedsConnection(module.Id)));
        ContentStack.Children.Add(Text("Export and recovery are always available in Settings. Samples appear only in the explicit preview.", 12, true));
    }
    private void AddToolGroup(string title, string description, IEnumerable<ModuleDefinition> modules)
    {
        ContentStack.Children.Add(Text(title, 15));
        ContentStack.Children.Add(Text(description, 12, true));
        var cards = modules.Select(module =>
        {
            var premium = Notch.Core.Commerce.FeaturePolicy.RequiresPremium(module.Id);
            var available = _vm.CanAccessModule(module.Id);
            var content = new StackPanel { Spacing = 10 };
            var heading = new Grid { ColumnSpacing = 10 };
            heading.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
            heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new VisualIcon { Kind = VisualIcon.ForModule(module.Id), Size = 22, Foreground = available ? NativeTheme.Foreground : NativeTheme.Muted };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(icon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            heading.Children.Add(icon);
            var name = Text(module.Title, 14); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            Grid.SetColumn(name, 1); heading.Children.Add(name); content.Children.Add(heading);
            var badge = Text(_vm.IsPublicTesting ? "Public testing · unlocked" : !premium ? "Free" : available ? (_vm.IsDevelopmentBuild ? "Premium · development access" : "Premium") : "Premium · locked", 11, !available);
            badge.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            // Access is explained once above the catalog; per-tool cards explain their purpose.
            // Keep the full access description in the action's accessible name.
            if (!_vm.IsPublicTesting) content.Children.Add(badge);
            content.Children.Add(Text(ConnectionRequirement(module.Id), 11, true));
            var button = Button(module.Title, () => _vm.SelectModule(available ? module.Id : ModuleId.Settings));
            button.Style = (Style)Application.Current.Resources["NotchCardButtonStyle"];
            button.Content = content; button.MinHeight = 104; button.MinWidth = 0;
            NativeTheme.ApplyCardFeedback(button);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, module.Title + ". " + badge.Text + ". " + ConnectionRequirement(module.Id) + (available ? "" : ". Opens plan settings."));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "Tool" + module.Id);
            ToolTipService.SetToolTip(button, available ? ConnectionRequirement(module.Id) : "Premium access required. View plan details in Settings.");
            return (UIElement)button;
        }).ToArray();
        ContentStack.Children.Add(AdaptiveGrid(3, 176, cards));
    }
    private void Notes()
    {
        var list = new StackPanel { Spacing = 6 };
        if (_vm.CanUndoNoteDeletion) list.Children.Add(Button("Undo last deletion", () => { _vm.UndoNoteDeletion(); Render(); }));
        list.Children.Add(Button("+ New note", () => { _editingNote = null; Render(); }));
        foreach (var note in _vm.Notes)
        {
            var button = Button(note.Title, () => { _editingNote = note.Id; Render(); }); button.HorizontalAlignment = HorizontalAlignment.Stretch; list.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 230, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var selected = _vm.Notes.FirstOrDefault(note => note.Id == _editingNote);
        var draftKey = _editingNote ?? Guid.Empty;
        var initial = _noteDrafts.GetValueOrDefault(draftKey, (selected?.Title ?? "", selected?.Text ?? ""));
        var editor = new StackPanel { Spacing = 10 };
        _noteTitle = Input("Title", initial.Item1); _noteBody = Input("A thought worth keeping…", initial.Item2, true); _noteBody.Height = 144;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_noteTitle, "Note title");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_noteBody, "Note body");
        var titleInput = _noteTitle; var bodyInput = _noteBody;
        titleInput.TextChanged += (_, _) => _noteDrafts[draftKey] = (titleInput.Text, bodyInput.Text);
        bodyInput.TextChanged += (_, _) => _noteDrafts[draftKey] = (titleInput.Text, bodyInput.Text);
        editor.Children.Add(_noteTitle); editor.Children.Add(_noteBody);
        editor.Children.Add(Text("Title: 80 characters. Body: 500,000 characters. Oversized drafts stay available for export.", 11, true));
        editor.Children.Add(Row(Button("Save note", () =>
        {
            if (_editingNote is { } id) _vm.UpdateNote(id, _noteTitle.Text, _noteBody.Text);
            else { if (!_vm.CanAccessModule(ModuleId.Notes)) throw new InvalidOperationException("Premium expired. Export your full draft from Settings before quitting."); _vm.AddNote(_noteTitle.Text, _noteBody.Text); _editingNote = _vm.Notes[0].Id; }
            _noteDrafts.Remove(draftKey);
            Render();
        }, true), Button("Delete", () => { if (_editingNote is { } id) _vm.RemoveNote(id); _noteDrafts.Remove(draftKey); _editingNote = null; Render(); })));
        ContentStack.Children.Add(AdaptiveGrid(2, 240, scroll, editor));
    }
    private void Scratchpad()
    {
        var input = Input("Let your thoughts land here…", _scratchpadDraft ?? _vm.Scratchpad, true); input.Height = 220;
        input.TextChanged += (_, _) =>
        {
            _scratchpadDraft = input.Text;
            _ = _vm.ExecuteAsync(() => { _vm.Scratchpad = input.Text; _scratchpadDraft = null; return Task.CompletedTask; });
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(input, "Scratchpad");
        ContentStack.Children.Add(input); ContentStack.Children.Add(Text("Saved automatically on this device. Plain text; no sync or account. Limit: 500,000 characters; validation errors preserve the full draft.", 12, true));
    }
    private void Shelf()
    {
        var drop = Card(Text("Drop files or folders here", 17)); drop.Height = 80; drop.AllowDrop = true;
        drop.DragOver += (_, args) => { args.AcceptedOperation = DataPackageOperation.Link; args.DragUIOverride.Caption = "Add to shelf"; };
        drop.Drop += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try { await _vm.ExecuteAsync(async () => { foreach (var item in await args.DataView.GetStorageItemsAsync()) _vm.AddShelf(item.Path); Render(); }); }
            finally { deferral.Complete(); }
        };
        ContentStack.Children.Add(drop);
        ContentStack.Children.Add(Button("Choose files", PickFilesAsync));
        ShelfList(); ContentStack.Children.Add(Text("The shelf stores references. Removing an item leaves the original file untouched.", 11, true));
    }
    private async Task PickFilesAsync()
    {
        if (HasOpenDialog) return;
        HasOpenDialog = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _vm.WindowHandle);
            foreach (var file in await picker.PickMultipleFilesAsync()) _vm.AddShelf(file.Path);
            Render();
        }
        finally { HasOpenDialog = false; }
    }
    private void ShelfList()
    {
        foreach (var item in _vm.Shelf)
        {
            var text = Text(Path.GetFileName(item.Path), 13); text.Width = 280;
            ContentStack.Children.Add(Row(text, Button("Open", () => OpenFile(item.Path)), Button("Reveal", () => Reveal(item.Path)), Button("Remove", () => { _vm.RemoveShelf(item.Id); Render(); })));
        }
        if (_vm.Shelf.Count == 0) ContentStack.Children.Add(Text("Nothing on the shelf yet.", 13, true));
    }
    private void Files()
    {
        ContentStack.Children.Add(Row(
            Button("Documents", () => OpenFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))),
            Button("Downloads", () => OpenFile(NativeFolders.DownloadsPath)),
            Button("Desktop", () => OpenFile(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))));
        ContentStack.Children.Add(Button("Add a frequently used file", PickFilesAsync)); ShelfList();
    }
    private void Clipboard()
    {
        if (!_vm.Preferences.CaptureClipboard)
        {
            ContentStack.Children.Add(Card(Text("Clipboard history is off. Enable it to keep up to 50 plain-text items in memory. History clears when you quit or turn it off.", 14)));
            ContentStack.Children.Add(Button("Enable clipboard history", () => _vm.SetPreferencesAsync(_vm.Preferences with { CaptureClipboard = true }), true)); return;
        }
        ContentStack.Children.Add(Row(Button("Clear history", () => { _vm.ClipboardService.Clear(); Render(); }), Button("Turn off", () => _vm.SetPreferencesAsync(_vm.Preferences with { CaptureClipboard = false }))));
        foreach (var item in _vm.Clipboard)
        {
            var preview = item.Text.Replace('\r', ' ').Replace('\n', ' '); if (preview.Length > 95) preview = preview[..95] + "…";
            var text = Text(preview); text.Width = 540;
            ContentStack.Children.Add(Card(Row(text, Button("Copy", () => _vm.ClipboardService.CopyAsync(item.Text)))));
        }
        if (_vm.Clipboard.Count == 0) ContentStack.Children.Add(Text("Copy some text to see it here.", 13, true));
    }
    private void Links()
    {
        var title = DraftInput("link-title", "Title"); title.Width = 200;
        var url = DraftInput("link-url", "https://…"); url.Width = 330;
        ContentStack.Children.Add(Row(title, url, Button("Add", () => { _vm.AddLink(title.Text, url.Text); _formDrafts.Remove("link-title"); _formDrafts.Remove("link-url"); Render(); }, true)));
        foreach (var item in _vm.Links)
        {
            var text = Text(item.Title); text.Width = 320;
            ContentStack.Children.Add(Row(text, Button("Open", () => OpenLink(item.Url)), Button("Remove", () => { _vm.RemoveLink(item.Id); Render(); })));
        }
    }
    private void SystemPanel()
    {
        var system = _vm.System;
        var metrics = new[] { Metric("CPU", out _cpuText), Metric("Memory", out _memoryText), Metric("Battery", out _batteryText) };
        ContentStack.Children.Add(AdaptiveGrid(3, 132, metrics));
        _outputText = Text(system?.OutputDevice ?? "Audio output unavailable", 13, true); ContentStack.Children.Add(_outputText);
        var volume = _systemVolume = new Slider { Minimum = 0, Maximum = 1, StepFrequency = .01, Value = system?.Volume ?? 0, IsEnabled = system is not null && system.OutputDevice != "Audio output unavailable" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(volume, "System volume");
        volume.ValueChanged += async (_, args) => { if (!_updatingSystemVolume) await _vm.ExecuteAsync(() => _vm.SetVolumeAsync(args.NewValue)); }; ContentStack.Children.Add(volume);
        ContentStack.Children.Add(Button("Refresh", _vm.RefreshAsync));
        UpdateSystemPanel();
    }
    private Border Metric(string label, out TextBlock value)
    {
        value = Text("—", 26); var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(Text(label, 12, true)); panel.Children.Add(value); return Card(panel);
    }
    private void UpdateSystemPanel()
    {
        var system = _vm.System;
        if (_cpuText is not null) _cpuText.Text = system is null ? "—" : $"{system.CpuPercent:0}%";
        if (_memoryText is not null) _memoryText.Text = system is null ? "—" : $"{system.MemoryPercent:0}%";
        if (_batteryText is not null) _batteryText.Text = system?.BatteryPercent is { } battery ? $"{battery}%" : "No battery";
        if (_outputText is not null) _outputText.Text = system?.OutputDevice ?? "Audio output unavailable";
        if (_systemVolume is not null && _systemVolume.FocusState == FocusState.Unfocused && (_systemVolume.PointerCaptures?.Count ?? 0) == 0)
        {
            _updatingSystemVolume = true;
            try { _systemVolume.Value = system?.Volume ?? 0; _systemVolume.IsEnabled = system is not null && system.OutputDevice != "Audio output unavailable"; }
            finally { _updatingSystemVolume = false; }
        }
    }
    private void Servers()
    {
        ContentStack.Children.Add(Text($"{_vm.ListeningPorts.Count} TCP ports listening", 26));
        foreach (var port in _vm.ListeningPorts.Take(30)) ContentStack.Children.Add(Row(Text($":{port}", 16), Button("Open localhost", () => OpenLink($"http://localhost:{port}"))));
        ContentStack.Children.Add(Text("These are listening ports, not verified web servers. A port can belong to any application.", 12, true));
        ContentStack.Children.Add(Button("Refresh ports", _vm.RefreshAsync));
    }
    private void ScreenTime()
    {
        _screenTimeText = Text(_vm.System is { } system ? FocusSession.Format(system.SessionScreenTime) : "00:00", 46);
        ContentStack.Children.Add(Card(_screenTimeText));
        ContentStack.Children.Add(Text($"Active screen time since {ProductIdentity.DisplayName} started. No app names, window titles, or screenshots are recorded. This is a session counter, not a historical activity tracker.", 14, true));
    }
    private void Emoji()
    {
        string[] emoji = ["😊","✨","👍","🎉","❤️","🔥","🚀","🙏","😂","💡","✅","👀","💪","🌿","☕","💻","🌈","🎧","📌","🫶","🌻","🍀","⭐","🌙"];
        var buttons = new List<UIElement>();
        for (var i = 0; i < emoji.Length; i++)
        {
            var value = emoji[i]; var button = Button(value, async () => { await _vm.ClipboardService.CopyAsync(value); _vm.Activity(Notch.Core.ActivityKind.Information, "Emoji", "Copied " + value, null); }); button.FontSize = 25; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            buttons.Add(button);
        }
        ContentStack.Children.Add(AdaptiveGrid(8, 52, buttons.ToArray())); ContentStack.Children.Add(Button("Open Windows emoji picker", () => NativeInput.OpenEmojiPicker()));
    }
    private AmbientSound? _sound;
    private AmbientSound Sound
    {
        get
        {
            if (_sound is null) { _sound = new(); _sound.Error += (_, error) => DispatcherQueue.TryEnqueue(() => _vm.ShowError(error)); }
            return _sound;
        }
    }
    private void Sounds()
    {
        ContentStack.Children.Add(Text("A quiet backdrop for focused work.", 16));
        if (!Sound.IsAvailable) { ContentStack.Children.Add(Text(Sound.UnavailableReason ?? "Audio is unavailable. Install the optional Windows media components, then retry.", 13, true)); return; }
        ContentStack.Children.Add(Row(Button("White noise", () => Sound.PlayAsync(false)), Button("Brown noise", () => Sound.PlayAsync(true)), Button("Stop", () => Sound.Stop())));
        ContentStack.Children.Add(Text("Generated locally. No streaming, tracking, or external audio library.", 12, true));
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = Sound.Volume, StepFrequency = .01 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, "Ambient sound volume"); slider.ValueChanged += (_, args) => Sound.Volume = args.NewValue; ContentStack.Children.Add(slider);
    }
    private void ConvertUnits()
    {
        var input = Input("Value", "1"); input.Width = 130;
        var from = new ComboBox { Width = 135 }; var to = new ComboBox { Width = 135 };
        foreach (var unit in UnitConverter.Units) from.Items.Add(unit.Symbol);
        from.SelectedItem = "m"; var result = Text("100 cm", 30); var detail = Text("Length", 12, true);
        void Calculate()
        {
            try
            {
                if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)) { result.Text = "Enter a number"; return; }
                if (from.SelectedItem is string a && to.SelectedItem is string b) result.Text = $"{UnitConverter.Convert(value, a, b):G10} {b}";
            }
            catch (ArgumentException error) { result.Text = error.Message; }
            catch (OverflowException error) { result.Text = error.Message; }
        }
        void Refill()
        {
            var selected = UnitConverter.Units.First(unit => unit.Symbol == (string)from.SelectedItem);
            to.Items.Clear(); foreach (var unit in UnitConverter.Units.Where(unit => unit.Category == selected.Category)) to.Items.Add(unit.Symbol);
            to.SelectedIndex = Math.Min(1, to.Items.Count - 1); detail.Text = selected.Category; Calculate();
        }
        Refill(); from.SelectionChanged += (_, _) => Refill(); to.SelectionChanged += (_, _) => Calculate(); input.TextChanged += (_, _) => Calculate();
        ContentStack.Children.Add(Row(input, from, Text("→", 22), to)); ContentStack.Children.Add(Card(result)); ContentStack.Children.Add(detail);
        ContentStack.Children.Add(Text("Length, mass, temperature, and data sizes. KB uses 1000 bytes; KiB uses 1024 bytes.", 12, true));
    }
    private void Awake()
    {
        ContentStack.Children.Add(Card(Text(_vm.Awake ? "Your display stays awake." : "Let your desktop rest naturally.", 22)));
        ContentStack.Children.Add(Button(_vm.Awake ? "Stop keeping awake" : "Keep awake", () => { _vm.SetAwake(!_vm.Awake); Render(); }, true));
        ContentStack.Children.Add(Text("Uses Windows' power-management API and restores normal behavior when you stop or quit. It does not prevent a manually requested sleep.", 13, true));
    }
    private Border SettingsSection(string title, string description, params UIElement[] controls)
    {
        var panel = new StackPanel { Spacing = 12 };
        var heading = Text(title, 15); heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        panel.Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(description)) panel.Children.Add(Text(description, 12, true));
        foreach (var control in controls) panel.Children.Add(control);
        return Card(panel);
    }
    private Expander SettingsDetails(string key, string title, string description, params UIElement[] controls)
    {
        var heading = new StackPanel { Spacing = 4 };
        var label = Text(title, 14); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; heading.Children.Add(label);
        heading.Children.Add(Text(description, 12, true));
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(0, 4, 0, 0) };
        foreach (var control in controls) panel.Children.Add(control);
        var expander = new Expander
        {
            Header = heading, Content = panel, IsExpanded = _expandedSettings.GetValueOrDefault(key),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(14),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(expander, title);
        expander.Expanding += (_, _) => _expandedSettings[key] = true;
        expander.Collapsed += (_, _) => _expandedSettings[key] = false;
        return expander;
    }
    private NumberBox SettingsNumber(string key, string label, double value, double minimum, double maximum)
    {
        var box = new NumberBox { Header = Text(label, 12), Value = DraftNumber(key, value), Minimum = minimum, Maximum = maximum, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, label);
        TrackNumber(key, box); return box;
    }
    private void Settings()
    {
        var preferences = _vm.Preferences;
        var identity = new Grid { ColumnSpacing = 14 };
        identity.ColumnDefinitions.Add(new() { Width = new GridLength(44) });
        identity.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var appIcon = new Image { Source = new BitmapImage(new Uri("ms-appx:///Assets/Notchling.png")), Width = 44, Height = 44, Stretch = Stretch.Uniform };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(appIcon, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        identity.Children.Add(appIcon);
        var about = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        about.Children.Add(Text(ProductIdentity.DisplayName, 20));
        about.Children.Add(Text("Native desktop notch · " + typeof(App).Assembly.GetName().Version?.ToString(3), 12, true));
        Grid.SetColumn(about, 1); identity.Children.Add(about); ContentStack.Children.Add(Card(identity));

        _subscriptionStatusText = Text(_vm.SubscriptionStatus, 12, true);
        ContentStack.Children.Add(SettingsSection(_vm.IsPublicTesting ? "Public testing · All tools unlocked" : _vm.IsDevelopmentBuild ? "Development access" : _vm.IsPremium ? "Your Premium plan" : "Your Free plan",
            _vm.IsPublicTesting ? "Try every local and connected tool for free. Provider setup and Windows permissions still apply." : _vm.IsDevelopmentBuild ? "All tools are enabled for development. This build does not establish a paid subscription." : _vm.IsPremium ? "US$2/month · Your verified access unlocks the extended tool suite." : "Basic music playback, one Pomodoro and a local scratchpad. No account needed.",
            _subscriptionStatusText,
            Text(_vm.IsPublicTesting ? "Purchasing is paused during public testing. Billing can be introduced in a later release." : _vm.BillingConfigured ? "Premium is US$2/month. Purchase, restore and cancellation are available below." : "Premium is planned at US$2/month. Purchasing is not available in this build yet.", 12, true)));
        if (_vm.BillingConfigured)
        {
            var email = DraftInput("billing-email", _vm.IsPublicTesting ? "Email for online services" : "Email for purchase or restore");
            var code = DraftInput("billing-code", "One-time email code");
            var accountControls = new List<UIElement>
            {
                FormField("Account email", email, Button("Send sign-in code", () => _vm.RequestLoginAsync(email.Text))),
                FormField("Email verification code", code, Button(_vm.IsPublicTesting ? "Sign in" : "Sign in / restore", async () => { await _vm.VerifyLoginAsync(email.Text, code.Text); _formDrafts.Remove("billing-code"); Render(); })),
            };
            if (_vm.PurchasingAvailable)
                accountControls.Add(Row(Button("Upgrade — US$2/month", async () => { var uri = await _vm.CheckoutAsync(); await global::Windows.System.Launcher.LaunchUriAsync(uri); }), Button("Manage / cancel", async () => { var uri = await _vm.CustomerPortalAsync(); await global::Windows.System.Launcher.LaunchUriAsync(uri); })));
            var signOut = Button("Sign out", async () => { await _vm.SignOutAsync(); Render(); });
            accountControls.Add(_vm.IsPublicTesting ? signOut : Row(Button("Refresh access", _vm.RefreshSubscriptionAsync), signOut));
            ContentStack.Children.Add(SettingsDetails("account", _vm.IsPublicTesting ? "Service account" : "Account and subscription",
                _vm.IsPublicTesting ? "Optional sign-in for configured online services. Local tools do not require an account." : "Sign in, restore purchases or manage renewal.", accountControls.ToArray()));
        }

        ContentStack.Children.Add(SettingsSection("Appearance and behavior", "Set how the notch responds while you work.",
            Toggle("Keep expanded", "Stay open until you collapse the panel.", preferences.Pinned, value => _vm.Preferences with { Pinned = value }),
            Toggle("Switch tools on hover", "Move between available tools by hovering the dock.", preferences.HoverNavigation, value => _vm.Preferences with { HoverNavigation = value }),
            Toggle("Reduce motion", "Use quieter transitions. Windows accessibility settings are respected.", preferences.ReducedMotion, value => _vm.Preferences with { ReducedMotion = value }),
            Toggle("Hide during fullscreen apps", "Give games and presentations the full display.", preferences.HideInFullscreen, value => _vm.Preferences with { HideInFullscreen = value })));

        var focus = SettingsNumber("focus-minutes", "Pomodoro length (minutes)", preferences.FocusMinutes, 1, 180);
        var hydration = SettingsNumber("hydration-minutes", _vm.IsPublicTesting ? "Hydration interval (minutes)" : "Hydration interval (minutes) · Premium", preferences.HydrationMinutes, 5, 180);
        hydration.IsEnabled = _vm.CanUseExtendedTools;
        ContentStack.Children.Add(SettingsSection("Focus", _vm.IsPublicTesting ? "Set your Pomodoro length and hydration reminder interval." : "Set your Pomodoro length. Hydration reminders are included in Premium.",
            AdaptiveGrid(2, 190, focus, hydration),
            Button("Save intervals", () => _vm.SetPreferencesAsync(_vm.Preferences with { FocusMinutes = double.IsFinite(focus.Value) ? (int)focus.Value : 25, HydrationMinutes = double.IsFinite(hydration.Value) ? (int)hydration.Value : preferences.HydrationMinutes }))));

        _saveStateText = Text(_vm.SaveState, 12, true);
        ContentStack.Children.Add(SettingsSection("Notebook and recovery", "Your notes stay on this device. Export and recovery remain available on every plan.",
            _saveStateText,
            Row(Button("Save now", async () => { FlushDrafts(); await _vm.SaveBeforeExitAsync(); }), Button("Export notebook", () => ExportAllAsync(includeDrafts: true)), Button("Restore export", ImportWorkspaceAsync))));
        if (!_vm.WorkspaceReadable)
            ContentStack.Children.Add(SettingsSection("Notebook needs recovery", "Preserve the unreadable file before starting a recovered notebook.", Button("Preserve file and start recovery", _vm.RecoverWorkspaceAsync, true)));

        ContentStack.Children.Add(SettingsSection("Clipboard privacy", "Clipboard history is opt-in. Up to 50 plain-text items stay in memory and clear on exit.",
            Toggle(_vm.IsPublicTesting ? "Capture clipboard text" : "Capture clipboard text · Premium", "Capture plain text only. Turning this off clears the saved history.", preferences.CaptureClipboard && _vm.CanUseExtendedTools, value => _vm.Preferences with { CaptureClipboard = value }, _vm.CanUseExtendedTools),
            Button("Clear clipboard history", () => _vm.ClipboardService.Clear())));

        var monitor = SettingsNumber("monitor", "Display number (0 is primary)", preferences.ActiveMonitor, 0, 16);
        var horizontal = SettingsNumber("offset-x", "Horizontal adjustment", preferences.HorizontalOffset, -1000, 1000);
        var top = SettingsNumber("offset-y", "Distance from top", preferences.TopOffset, 0, 1000);
        monitor.Header = null;
        ContentStack.Children.Add(SettingsDetails("position", "Display and position", "Choose a display or fine-tune the notch placement.",
            FormField("Display number (0 is primary)", monitor, Button("Move to display", () => _vm.SetPreferencesAsync(_vm.Preferences with { ActiveMonitor = double.IsFinite(monitor.Value) ? (int)monitor.Value : 0, MonitorDeviceId = null }))),
            AdaptiveGrid(2, 180, horizontal, top),
            Text("Position adjustments use Windows logical pixels and adapt to display scaling.", 11, true),
            Row(Button("Apply position", () => _vm.SetPreferencesAsync(_vm.Preferences with { HorizontalOffset = horizontal.Value, TopOffset = top.Value })),
                Button("Reset placement", async () =>
                {
                    monitor.Value = 0; horizontal.Value = 0; top.Value = 0;
                    await _vm.SetPreferencesAsync(_vm.Preferences with { ActiveMonitor = 0, MonitorDeviceId = null, HorizontalOffset = 0, TopOffset = 0 });
                }))));

        var endpoint = DraftInput("analytics-endpoint", "HTTPS analytics endpoint", preferences.AnalyticsEndpoint ?? "");
        ContentStack.Children.Add(SettingsDetails("connections", _vm.IsPublicTesting ? "Provider connections" : "Advanced connections · Premium", "Connect your own reporting providers. Keys stay hidden.",
            Text("Revenue reads your Stripe reporting account with a restricted read-only key. Analytics requires a compatible JSON endpoint. These credentials connect data, not billing.", 12, true),
            Credential("Stripe read-only key", "stripe"),
            Text("Give the Stripe restricted key permission to read charges. A test-mode key reports test payments.", 11, true),
            Credential("Analytics bearer token", "analytics"),
            FormField("Analytics HTTPS endpoint", endpoint, Button("Save endpoint", () => _vm.SetPreferencesAsync(_vm.Preferences with { AnalyticsEndpoint = endpoint.Text }))),
            Text("Provider credentials are stored using Windows protection. Coding and calendar accept imported files in their tools. Weather requires a configured licensed service.", 12, true),
            Button("Provider setup guide", () => OpenLink("https://github.com/Sury2797/Notchling/blob/main/src/Notch.Core/Providers/README.md"))));

        var connections = new StackPanel { Spacing = 12 };
        foreach (var connection in _vm.ConnectionStatuses)
        {
            var status = Text("", 11); status.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(status, "ConnectionState" + connection.Id);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            var detail = Text("", 12, true);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(detail, "ConnectionDetail" + connection.Id);
            var heading = new Grid { ColumnSpacing = 10 };
            heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var name = Text(connection.Name, 13); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; heading.Children.Add(name);
            Grid.SetColumn(status, 1); heading.Children.Add(status);
            var entry = new StackPanel { Spacing = 5 }; entry.Children.Add(heading); entry.Children.Add(detail);
            connections.Children.Add(entry); _connectionRows[connection.Id] = (status, detail);
        }
        _checkConnectionsButton = Button("Check connections", _vm.RefreshConnectionsAsync);
        ContentStack.Children.Add(SettingsDetails("connection-status", "Connection status", "Check real media, local imports and configured providers.",
            connections, _checkConnectionsButton,
            Text("A configured key is not proof of a working connection. Checks use your saved setup; no sample data is used.", 11, true)));
        UpdateConnectionStatus();

        ContentStack.Children.Add(SettingsDetails("preview", "Sample-data preview", "See labeled sample states without connecting an account.",
            Toggle("Preview sample data for this session", "Samples are labeled. This does not start music or control your real player.", preferences.DemoMode, value => _vm.Preferences with { DemoMode = value })));
        _updateStatusText = Text(_vm.UpdateStatus, 12, true);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_updateStatusText, "UpdateStatus");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_updateStatusText, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _checkUpdatesButton = UpdateActionButton("Check for updates", "CheckUpdates", _vm.CheckForUpdatesAsync);
        _installUpdateButton = UpdateActionButton("Install verified update", "InstallUpdate", _vm.InstallAvailableUpdateAsync, primary: true);
        _cancelUpdateButton = UpdateActionButton("Cancel", "CancelUpdate", () => { _vm.CancelUpdateCheck(); return Task.CompletedTask; });
        ContentStack.Children.Add(SettingsDetails("support", "Updates and troubleshooting", "Get updates or reconnect optional Windows services.",
            Toggle("Check for updates daily", "Checks only when Notchling is running. Installation always needs your action.", preferences.CheckForUpdatesAutomatically,
                value => _vm.Preferences with { CheckForUpdatesAutomatically = value }),
            Row(_checkUpdatesButton, _installUpdateButton, _cancelUpdateButton),
            _updateStatusText,
            Text("Signed updates are verified before installation. Evaluation releases are downloaded from the release page.", 11, true),
            Button("Release page", () => OpenLink(_vm.UpdateReleasePage)),
            Button("Retry media and clipboard services", _vm.RetryNativeServicesAsync),
            Text("Ctrl + Shift + Space opens Notchling. Esc collapses the panel. You can also reopen it from the Windows tray.", 12, true)));
        UpdateUpdateControls();
        _notificationPanel = new StackPanel { Spacing = 12 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_notificationPanel, "RecentNotifications");
        ContentStack.Children.Add(SettingsDetails("notifications", "Recent notifications", "Your last ten notices from this session. Choose a notice to open its tool.", _notificationPanel));
        UpdateNotifications();
    }
    private void NotificationsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs args)
    {
        if (_vm.SelectedModule != ModuleId.Settings) return;
        if (DispatcherQueue.HasThreadAccess) UpdateNotifications();
        else DispatcherQueue.TryEnqueue(() => { if (_vm.SelectedModule == ModuleId.Settings) UpdateNotifications(); });
    }
    private void UpdateNotifications()
    {
        if (_notificationPanel is null) return;
        _notificationPanel.Children.Clear();
        foreach (var activity in _vm.NotificationHistory.Take(10))
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new VisualIcon
            {
                Kind = activity.Kind switch
                {
                    Notch.Core.ActivityKind.Sale => VisualIconKind.Revenue, Notch.Core.ActivityKind.Meeting => VisualIconKind.Calendar,
                    Notch.Core.ActivityKind.Agent => VisualIconKind.Coding, Notch.Core.ActivityKind.Focus => VisualIconKind.Focus,
                    _ => VisualIconKind.Notification,
                },
                Size = 18, Foreground = NativeTheme.Muted,
            });
            var detail = new StackPanel { Spacing = 4 };
            detail.Children.Add(Text(activity.Title, 12));
            detail.Children.Add(Text(activity.Source + " · " + activity.CreatedAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), 11, true));
            Grid.SetColumn(detail, 1); row.Children.Add(detail);
            if (activity.Destination is { } destination)
            {
                var action = Button(activity.Title, () => _vm.SelectModule(destination));
                action.Content = row; action.HorizontalAlignment = HorizontalAlignment.Stretch; action.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(action, activity.Title + ". Open " + ModuleCatalog.Get(destination).Title);
                _notificationPanel.Children.Add(action);
            }
            else _notificationPanel.Children.Add(row);
        }
        if (_notificationPanel.Children.Count == 0) _notificationPanel.Children.Add(Text("No notifications yet. Focus, reminder and update notices appear here.", 12, true));
    }
    private Grid Toggle(string label, string description, bool value, Func<bool, AppPreferences> update, bool enabled = true)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(Text(label)); text.Children.Add(Text(description, 11, true)); grid.Children.Add(text);
        var toggle = new ToggleSwitch { IsOn = value, IsEnabled = enabled, OnContent = "", OffContent = "", MinWidth = 44, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
        _settingsToggles[label] = toggle;
        toggle.Toggled += async (_, _) => { if (!_updatingSettingsToggles) await _vm.ExecuteAsync(() => _vm.SetPreferencesAsync(update(toggle.IsOn))); };
        Grid.SetColumn(toggle, 1); grid.Children.Add(toggle); return grid;
    }
    private void UpdateSettingsToggles()
    {
        var preferences = _vm.Preferences;
        var values = new (string Name, bool Value)[]
        {
            ("Keep expanded", preferences.Pinned), ("Switch tools on hover", preferences.HoverNavigation),
            ("Reduce motion", preferences.ReducedMotion), ("Hide during fullscreen apps", preferences.HideInFullscreen),
            (_vm.IsPublicTesting ? "Capture clipboard text" : "Capture clipboard text · Premium", preferences.CaptureClipboard && _vm.CanUseExtendedTools),
            ("Preview sample data for this session", preferences.DemoMode),
            ("Check for updates daily", preferences.CheckForUpdatesAutomatically),
        };
        _updatingSettingsToggles = true;
        try { foreach (var (name, value) in values) if (_settingsToggles.TryGetValue(name, out var toggle)) toggle.IsOn = value; }
        finally { _updatingSettingsToggles = false; }
    }
    private Button UpdateActionButton(string title, string automationId, Func<Task> action, bool primary = false)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Style = (Style)Application.Current.Resources[primary ? "NotchFormPrimaryButtonStyle" : "NotchFormButtonStyle"],
            MinWidth = 0,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, automationId);
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await _vm.ExecuteAsync(action); }
            finally { UpdateUpdateControls(); }
        };
        return button;
    }
    private void UpdateUpdateControls()
    {
        // Keep these controls alive during progress. Rebuilding Settings would
        // discard a credential draft and reset the user's current viewport.
        var busy = _vm.IsCheckingUpdates || _vm.IsInstallingUpdate;
        if (_checkUpdatesButton is not null) _checkUpdatesButton.IsEnabled = !busy;
        if (_installUpdateButton is not null)
        {
            _installUpdateButton.Visibility = _vm.CanInstallAvailableUpdate || _vm.IsInstallingUpdate ? Visibility.Visible : Visibility.Collapsed;
            _installUpdateButton.IsEnabled = _vm.CanInstallAvailableUpdate && !busy;
        }
        if (_cancelUpdateButton is not null)
        {
            _cancelUpdateButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _cancelUpdateButton.IsEnabled = busy;
        }
    }
    private void UpdateConnectionStatus()
    {
        foreach (var connection in _vm.ConnectionStatuses)
        {
            if (!_connectionRows.TryGetValue(connection.Id, out var row)) continue;
            var state = connection.State switch
            {
                ConnectionState.NotChecked => "Not checked",
                ConnectionState.Checking => "Checking…",
                ConnectionState.NeedsSetup => "Setup needed",
                ConnectionState.Ready => "Ready",
                ConnectionState.Connected => "Connected",
                ConnectionState.Unavailable => "Unavailable",
                ConnectionState.Failed => "Needs attention",
                _ => "Not checked",
            };
            if (row.State.Text != state) row.State.Text = state;
            var foreground = connection.State switch
            {
                ConnectionState.Ready or ConnectionState.Connected => NativeTheme.Brush("#68D391"),
                ConnectionState.Failed => NativeTheme.Brush("#FFA6A6"),
                _ => NativeTheme.Muted,
            };
            if (!ReferenceEquals(row.State.Foreground, foreground)) row.State.Foreground = foreground;
            if (row.Detail.Text != connection.Detail) row.Detail.Text = connection.Detail;
        }
        if (_checkConnectionsButton is not null) _checkConnectionsButton.IsEnabled = !_vm.IsCheckingConnections;
    }
    private StackPanel Credential(string label, string provider)
    {
        var password = new PasswordBox { Style = (Style)Application.Current.Resources["NotchFormPasswordBoxStyle"], MinWidth = 0, PasswordRevealMode = PasswordRevealMode.Hidden, Password = _credentialDrafts.GetValueOrDefault(provider, ""), PlaceholderText = "Enter a new key to save or replace" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(password, label);
        password.PasswordChanged += (_, _) => _credentialDrafts[provider] = password.Password;
        var save = Button("Save", () => { _vm.SaveCredential(provider, password.Password); password.Password = ""; _credentialDrafts.Remove(provider); });
        var remove = Button("Remove", () => { _vm.DeleteCredential(provider); password.Password = ""; _credentialDrafts.Remove(provider); });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(save, "Save " + label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove " + label);
        ToolTipService.SetToolTip(save, "Save or replace this provider credential in Windows Credential Locker.");
        ToolTipService.SetToolTip(remove, "Delete the stored credential for this provider.");
        return FormField(label, password, save, remove);
    }
    private static void OpenFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("This file or folder is no longer available.");
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }
    private static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ArgumentException("Use a web link without embedded credentials.");
        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }
    private static void Reveal(string path)
    {
        if (path.Contains('"')) throw new ArgumentException("Invalid file path.");
        Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + Path.GetFullPath(path) + "\"", UseShellExecute = true });
    }
    private double DraftNumber(string key, double fallback) => _formDrafts.TryGetValue(key, out var text) && double.TryParse(text, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private void TrackNumber(string key, NumberBox box) => box.ValueChanged += (_, _) => _formDrafts[key] = box.Value.ToString(CultureInfo.InvariantCulture);
    public void FlushDrafts()
    {
        _invalidDrafts = false;
        try
        {
            if (_scratchpadDraft is { } scratchpad) { _vm.Scratchpad = scratchpad; _scratchpadDraft = null; }
            foreach (var (id, draft) in _noteDrafts.ToArray())
            {
                if (id == Guid.Empty)
                {
                    if (string.IsNullOrWhiteSpace(draft.Title) && string.IsNullOrWhiteSpace(draft.Body)) { _noteDrafts.Remove(id); continue; }
                    if (!_vm.CanAccessModule(ModuleId.Notes)) throw new InvalidOperationException("Your new note draft is preserved. Export it before quitting or restore Premium access.");
                    _vm.AddNote(draft.Title, draft.Body);
                }
                else _vm.UpdateNote(id, draft.Title, draft.Body);
                _noteDrafts.Remove(id);
            }
        }
        catch { _invalidDrafts = true; throw; }
    }
    public async Task ExportAllAsync(bool includeDrafts = false)
    {
        HasOpenDialog = true;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = ProductIdentity.DisplayName + "-notebook" };
            picker.FileTypeChoices.Add(ProductIdentity.DisplayName + " notebook JSON", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _vm.WindowHandle);
            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await _vm.ExportWorkspaceAsync(file.Path);
            if (includeDrafts && (_noteDrafts.Count > 0 || _scratchpadDraft is not null))
            {
                var draftPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file.Path)!, System.IO.Path.GetFileNameWithoutExtension(file.Path) + "-unsaved-drafts.json");
                var drafts = new { Notes = _noteDrafts.Select(item => new { Id = item.Key, item.Value.Title, Text = item.Value.Body }), Scratchpad = _scratchpadDraft };
                await File.WriteAllBytesAsync(draftPath, WorkspaceLimits.SerializeExport(drafts));
                _vm.ShowError("Notebook and unsaved draft recovery files exported. The drafts file is separate so content over normal limits is preserved in full.");
            }
        }
        finally { HasOpenDialog = false; }
    }
    private async Task ImportWorkspaceAsync()
    {
        HasOpenDialog = true;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _vm.WindowHandle);
            var file = await picker.PickSingleFileAsync(); if (file is null) return;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Restore notebook?", Content = "This replaces the current notebook. A backup of the previous notebook will be kept. Export or save your unfinished drafts first.", PrimaryButtonText = "Restore", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            FlushDrafts(); await _vm.RestoreWorkspaceAsync(file.Path); Render();
        }
        finally { HasOpenDialog = false; }
    }
    public void Dispose() { _sound?.Dispose(); _credentialDrafts.Clear(); _vm.PropertyChanged -= Changed; }
}
