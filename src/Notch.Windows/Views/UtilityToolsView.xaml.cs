using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    public bool HasInvalidDrafts => _invalidDrafts;
    public bool IsManipulating => _systemVolume?.PointerCaptures.Count > 0;

    public UtilityToolsView(MainViewModel viewModel)
    {
        InitializeComponent(); _vm = viewModel;
        Loaded += (_, _) => { if (!_attached) { _vm.PropertyChanged += Changed; _attached = true; } if (_renderedModule != _vm.SelectedModule) Render(); };
        Unloaded += (_, _) => { _vm.PropertyChanged -= Changed; _attached = false; };
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.SelectedModule)) { if (_renderedModule != _vm.SelectedModule) Render(); }
        else if (args.PropertyName == nameof(MainViewModel.IsReady) || args.PropertyName == nameof(MainViewModel.IsPremium)) Render();
        else if (args.PropertyName == nameof(MainViewModel.Preferences) && _vm.SelectedModule != ModuleId.Settings) Render();
        else if (_vm.SelectedModule == ModuleId.System && args.PropertyName == nameof(MainViewModel.System)) UpdateSystemPanel();
        else if (_vm.SelectedModule is ModuleId.Servers or ModuleId.ScreenTime && args.PropertyName is nameof(MainViewModel.System) or nameof(MainViewModel.ListeningPorts)) Render();
        else if (_vm.SelectedModule == ModuleId.Clipboard && args.PropertyName == nameof(MainViewModel.Clipboard)) Render();
    }
    private TextBlock Text(string value, double size = 13, bool muted = false) => new()
    {
        Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap,
        Foreground = muted ? NativeTheme.Muted : NativeTheme.Foreground,
    };
    private Button Button(string title, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = title, Style = (Style)Application.Current.Resources[primary ? "NotchPrimaryButtonStyle" : "NotchButtonStyle"] };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await _vm.ExecuteAsync(action); } finally { button.IsEnabled = true; } };
        return button;
    }
    private Button Button(string title, Action action, bool primary = false) => Button(title, () => { action(); return Task.CompletedTask; }, primary);
    private static StackPanel Row(params UIElement[] elements)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        foreach (var element in elements) row.Children.Add(element);
        row.SizeChanged += (_, _) =>
        {
            var needed = elements.Sum(element => element is FrameworkElement control && double.IsFinite(control.Width) ? control.Width : element.DesiredSize.Width) + Math.Max(0, elements.Length - 1) * 10;
            row.Orientation = row.ActualWidth > 0 && row.ActualWidth < needed ? Orientation.Vertical : Orientation.Horizontal;
            foreach (var element in elements)
                if (element is FrameworkElement control && double.IsFinite(control.Width)) control.MaxWidth = Math.Max(150, row.ActualWidth);
        };
        return row;
    }
    private Border Card(UIElement child) => new() { Style = (Style)Application.Current.Resources["NotchCardStyle"], Child = child, Padding = new Thickness(16) };
    private TextBox Input(string placeholder, string value = "", bool multiline = false) => new()
    {
        PlaceholderText = placeholder, Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        Style = (Style)Application.Current.Resources["NotchTextBoxStyle"], MinWidth = 150,
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
        _renderedModule = _vm.SelectedModule;
        ContentStack.Children.Clear();
        var definition = ModuleCatalog.Get(_vm.SelectedModule);
        Header(definition.Title, definition.Description);
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
    }
    private void Tools()
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var modules = ModuleCatalog.All.Where(item => item.Id is not ModuleId.Tools and not ModuleId.Settings).ToArray();
        for (var i = 0; i < modules.Length; i++)
        {
            var module = modules[i]; var content = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(new FontIcon { Glyph = module.Glyph, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(Text(module.Title, 11));
            var button = Button(module.Title, () => _vm.SelectModule(module.Id)); button.Content = content; button.Height = 86; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, module.Title + (_vm.CanAccessModule(module.Id) ? "" : " — Premium"));
            Grid.SetColumn(button, i % 7); Grid.SetRow(button, i / 7); grid.Children.Add(button);
        }
        ContentStack.Children.Add(grid);
        ContentStack.Children.Add(Text("External services are optional. Your local tools work without an account.", 12, true));
    }
    private void Notes()
    {
        var grid = new Grid { ColumnSpacing = 18 }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var list = new StackPanel { Spacing = 6 };
        if (_vm.CanUndoNoteDeletion) list.Children.Add(Button("Undo last deletion", () => { _vm.UndoNoteDeletion(); Render(); }));
        list.Children.Add(Button("+ New note", () => { _editingNote = null; Render(); }));
        foreach (var note in _vm.Notes)
        {
            var button = Button(note.Title, () => { _editingNote = note.Id; Render(); }); button.HorizontalAlignment = HorizontalAlignment.Stretch; list.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 230 }; grid.Children.Add(scroll);
        var selected = _vm.Notes.FirstOrDefault(note => note.Id == _editingNote);
        var draftKey = _editingNote ?? Guid.Empty;
        var initial = _noteDrafts.GetValueOrDefault(draftKey, (selected?.Title ?? "", selected?.Text ?? ""));
        var editor = new StackPanel { Spacing = 10 };
        _noteTitle = Input("Title", initial.Item1); _noteBody = Input("A thought worth keeping…", initial.Item2, true); _noteBody.Height = 144;
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
        Grid.SetColumn(editor, 1); grid.Children.Add(editor); ContentStack.Children.Add(grid);
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
        var cards = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 3; i++) cards.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var metrics = new[] { Metric("CPU", out _cpuText), Metric("Memory", out _memoryText), Metric("Battery", out _batteryText) };
        for (var i = 0; i < metrics.Length; i++) { Grid.SetColumn(metrics[i], i); cards.Children.Add(metrics[i]); }
        ContentStack.Children.Add(cards);
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
        if (_systemVolume is not null && _systemVolume.FocusState == FocusState.Unfocused && _systemVolume.PointerCaptures.Count == 0)
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
        ContentStack.Children.Add(Card(Text(_vm.System is { } system ? FocusSession.Format(system.SessionScreenTime) : "00:00", 46)));
        ContentStack.Children.Add(Text("Active screen time since Notch started. No app names, window titles, or screenshots are recorded. This is a session counter, not a historical activity tracker.", 14, true));
    }
    private void Emoji()
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        for (var i = 0; i < 8; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        string[] emoji = ["😊","✨","👍","🎉","❤️","🔥","🚀","🙏","😂","💡","✅","👀","💪","🌿","☕","💻","🌈","🎧","📌","🫶","🌻","🍀","⭐","🌙"];
        for (var i = 0; i < emoji.Length; i++)
        {
            var value = emoji[i]; var button = Button(value, async () => { await _vm.ClipboardService.CopyAsync(value); _vm.Activity(Notch.Core.ActivityKind.Information, "Emoji", "Copied " + value, null); }); button.FontSize = 25; button.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(button, i % 8); Grid.SetRow(button, i / 8); grid.Children.Add(button);
        }
        ContentStack.Children.Add(grid); ContentStack.Children.Add(Button("Open Windows emoji picker", () => NativeInput.OpenEmojiPicker()));
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
    private void Settings()
    {
        var preferences = _vm.Preferences;
        ContentStack.Children.Add(Row(Button("Check for verified updates", _vm.CheckForUpdatesAsync), Button("Release page", () => OpenLink("https://github.com/SuryaK999/Notch-win-linux/releases"))));
        ContentStack.Children.Add(Text("Behavior", 15));
        Toggle("Pin the expanded notch", preferences.Pinned, value => _vm.Preferences with { Pinned = value });
        Toggle("Switch tools on hover", preferences.HoverNavigation, value => _vm.Preferences with { HoverNavigation = value });
        Toggle("Reduce motion", preferences.ReducedMotion, value => _vm.Preferences with { ReducedMotion = value });
        Toggle("Capture clipboard text (memory only)", preferences.CaptureClipboard, value => _vm.Preferences with { CaptureClipboard = value });
        Toggle("Demo mode — show clearly labeled sample data", preferences.DemoMode, value => _vm.Preferences with { DemoMode = value });
        var focus = new NumberBox { Header = "Focus length (minutes)", Value = DraftNumber("focus-minutes", preferences.FocusMinutes), Minimum = 1, Maximum = 180, Width = 190 };
        var hydration = new NumberBox { Header = "Hydration interval (minutes)", Value = DraftNumber("hydration-minutes", preferences.HydrationMinutes), Minimum = 5, Maximum = 180, Width = 210 };
        TrackNumber("focus-minutes", focus); TrackNumber("hydration-minutes", hydration);
        ContentStack.Children.Add(Row(focus, hydration, Button("Save intervals", () => _vm.SetPreferencesAsync(_vm.Preferences with { FocusMinutes = double.IsFinite(focus.Value) ? (int)focus.Value : 25, HydrationMinutes = double.IsFinite(hydration.Value) ? (int)hydration.Value : 30 }))));
        var monitor = new NumberBox { Header = "Monitor index (0 = primary)", Minimum = 0, Maximum = 16, Value = DraftNumber("monitor", preferences.ActiveMonitor), Width = 260 };
        TrackNumber("monitor", monitor);
        ContentStack.Children.Add(Row(monitor, Button("Move notch", () => _vm.SetPreferencesAsync(_vm.Preferences with { ActiveMonitor = double.IsFinite(monitor.Value) ? (int)monitor.Value : 0, MonitorDeviceId = null }))));
        Toggle("Hide during fullscreen apps", preferences.HideInFullscreen, value => _vm.Preferences with { HideInFullscreen = value });
        var horizontal = new NumberBox { Header = "Horizontal offset (DIPs)", Value = DraftNumber("offset-x", preferences.HorizontalOffset), Minimum = -1000, Maximum = 1000, Width = 210 };
        var top = new NumberBox { Header = "Top offset (DIPs)", Value = DraftNumber("offset-y", preferences.TopOffset), Minimum = 0, Maximum = 1000, Width = 210 };
        TrackNumber("offset-x", horizontal); TrackNumber("offset-y", top);
        ContentStack.Children.Add(Row(horizontal, top, Button("Apply position", () => _vm.SetPreferencesAsync(_vm.Preferences with { HorizontalOffset = horizontal.Value, TopOffset = top.Value }))));
        ContentStack.Children.Add(Button("Retry media and clipboard services", _vm.RetryNativeServicesAsync));
        ContentStack.Children.Add(Text("Notebook and recovery", 15));
        ContentStack.Children.Add(Text(_vm.SaveState, 12, true));
        ContentStack.Children.Add(Row(Button("Save now", async () => { FlushDrafts(); await _vm.SaveBeforeExitAsync(); }), Button("Export notebook", () => ExportAllAsync(includeDrafts: true)), Button("Restore export", ImportWorkspaceAsync)));
        if (!_vm.WorkspaceReadable) ContentStack.Children.Add(Button("Preserve unreadable file and start recovery", _vm.RecoverWorkspaceAsync));
        ContentStack.Children.Add(Text("Connections", 15));
        Credential("Stripe read-only key", "stripe"); Credential("Analytics bearer token", "analytics");
        var endpoint = DraftInput("analytics-endpoint", "HTTPS analytics endpoint", preferences.AnalyticsEndpoint ?? ""); endpoint.Width = 430;
        ContentStack.Children.Add(Row(endpoint, Button("Save endpoint", () => _vm.SetPreferencesAsync(_vm.Preferences with { AnalyticsEndpoint = endpoint.Text }))));
        ContentStack.Children.Add(Text("Analytics expects the documented normalized JSON contract. Polar, Dodo, AdSense, Google OAuth and commercial weather licensing remain release work; no connection is implied.", 12, true));
        ContentStack.Children.Add(Text("Free + Premium", 15));
        ContentStack.Children.Add(Card(Text(_vm.PlanStatus + ". " + _vm.SubscriptionStatus, 13)));
        ContentStack.Children.Add(Text("Free includes basic playback, one Pomodoro and a scratchpad. Premium is US$2/month. Purchase and cancellation use the secure browser checkout; stored data can always be exported.", 12, true));
        if (_vm.BillingConfigured)
        {
            var email = DraftInput("billing-email", "Email for purchase or restore");
            var code = DraftInput("billing-code", "One-time email code");
            ContentStack.Children.Add(Row(email, Button("Send sign-in code", () => _vm.RequestLoginAsync(email.Text))));
            ContentStack.Children.Add(Row(code, Button("Sign in / restore", async () => { await _vm.VerifyLoginAsync(email.Text, code.Text); _formDrafts.Remove("billing-code"); Render(); })));
            ContentStack.Children.Add(Row(Button("Upgrade — US$2/month", async () => { var uri = await _vm.CheckoutAsync(); await global::Windows.System.Launcher.LaunchUriAsync(uri); }), Button("Manage / cancel", async () => { var uri = await _vm.CustomerPortalAsync(); await global::Windows.System.Launcher.LaunchUriAsync(uri); })));
            ContentStack.Children.Add(Row(Button("Refresh access", _vm.RefreshSubscriptionAsync), Button("Sign out", async () => { await _vm.SignOutAsync(); Render(); })));
        }
        else ContentStack.Children.Add(Text("Live subscriptions are awaiting server configuration. Free features work without an account.", 12, true));
        ContentStack.Children.Add(Text("Recent notifications", 15));
        foreach (var activity in _vm.NotificationHistory.Take(10)) ContentStack.Children.Add(Text(activity.Source + ": " + activity.Title, 12, true));
        ContentStack.Children.Add(Text("Ctrl + Shift + Space opens the notch. Esc collapses. Hover navigation never approves an agent command or launches a payment action.", 12, true));
    }
    private void Toggle(string label, bool value, Func<bool, AppPreferences> update)
    {
        var toggle = new ToggleSwitch { Header = label, IsOn = value };
        toggle.Toggled += async (_, _) => await _vm.ExecuteAsync(() => _vm.SetPreferencesAsync(update(toggle.IsOn))); ContentStack.Children.Add(toggle);
    }
    private void Credential(string label, string provider)
    {
        var password = new PasswordBox { Header = label, Width = 430, PasswordRevealMode = PasswordRevealMode.Hidden, Password = _credentialDrafts.GetValueOrDefault(provider, "") };
        password.PasswordChanged += (_, _) => _credentialDrafts[provider] = password.Password;
        ContentStack.Children.Add(Row(password, Button("Save", () => { _vm.SaveCredential(provider, password.Password); _credentialDrafts.Remove(provider); password.Password = ""; }), Button("Remove", () => _vm.DeleteCredential(provider))));
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
            var picker = new FileSavePicker { SuggestedFileName = "Notch-notebook" };
            picker.FileTypeChoices.Add("Notch notebook JSON", new List<string> { ".json" });
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
