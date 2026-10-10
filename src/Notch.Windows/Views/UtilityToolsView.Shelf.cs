using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Notch.Core;
using Notch.Windows.Interop;
using Notch.Windows.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Notch.Windows.Views;

public sealed partial class UtilityToolsView
{
    private ShelfCaptureService? _shelfCaptureService;
    private TextBlock? _shelfStatusText;
    private string _shelfStatus = "Drop files, folders or an image from another app.";
    private bool _saveShelfCopies;
    private readonly CancellationTokenSource _shelfLifetime = new();
    private bool _shelfDisposed;
    public bool IsCapturingShelf { get; private set; }
    private ShelfCaptureService ShelfCaptures => _shelfCaptureService ??= new(_vm.WorkspaceDirectory);

    public static bool CanAcceptShelfDrop(DataPackageView data) => data.Contains(StandardDataFormats.StorageItems)
        || data.Contains(StandardDataFormats.Bitmap);

    public static DataPackageOperation GetShelfDropOperation(DataPackageView data, DataPackageOperation allowed)
    {
        if (!CanAcceptShelfDrop(data)) return DataPackageOperation.None;
        if ((allowed & DataPackageOperation.Copy) != 0) return DataPackageOperation.Copy;
        // A Move acknowledgement could let the source remove its original. Never request it.
        if (data.Contains(StandardDataFormats.StorageItems) && !data.Contains(StandardDataFormats.Bitmap)
            && (allowed & DataPackageOperation.Link) != 0)
            return DataPackageOperation.Link;
        return DataPackageOperation.None;
    }

    private void Shelf()
    {
        var message = new StackPanel { Spacing = 5 };
        message.Children.Add(LabelWithHelp("Drop files, folders or images", "Drop here or onto the compact notch. Files and folders are references by default; your originals are never moved. Enable Save file copies to keep an independent local file. Images supplied by another app are saved in Notchling's local shelf folder. Some browsers expose an image as a web link: use Copy image in the browser, then Paste image here. Notchling does not download image URLs. Copies are limited to 50 MB each, 250 MB and 100 files in total. Remove takes an item off the shelf and keeps its file; Reveal saved copies lets you manage retained copies.", "ShelfHelp", 16));
        message.Children.Add(Text("An instant place for files you need next. Originals stay untouched.", 12, true));
        var drop = Card(message);
        drop.MinHeight = 86; drop.AllowDrop = true;
        AutomationProperties.SetAutomationId(drop, "ShelfDropTarget");
        AutomationProperties.SetName(drop, "Drop files, folders or images onto the shelf");
        drop.DragOver += (_, args) =>
        {
            args.AcceptedOperation = IsCapturingShelf || HasOpenDialog || !_vm.IsReady || !_vm.WorkspaceReadable || _vm.IsDemo
                ? DataPackageOperation.None : GetShelfDropOperation(args.DataView, args.AllowedOperations);
            if (args.AcceptedOperation != DataPackageOperation.None)
            {
                args.DragUIOverride.Caption = _saveShelfCopies ? "Save a local copy to Notchling" : "Add to Notchling shelf";
                args.DragUIOverride.IsCaptionVisible = true;
            }
            args.Handled = true;
        };
        drop.Drop += async (_, args) =>
        {
            var operation = GetShelfDropOperation(args.DataView, args.AllowedOperations);
            if (operation == DataPackageOperation.None) return;
            args.AcceptedOperation = operation; args.Handled = true;
            var deferral = args.GetDeferral();
            try { await _vm.ExecuteAsync(() => HandleShelfDropAsync(args.DataView)); }
            finally { deferral.Complete(); }
        };
        ContentStack.Children.Add(drop);
        var copies = new CheckBox { Content = Text("Save file copies", 13), IsChecked = _saveShelfCopies };
        AutomationProperties.SetAutomationId(copies, "ShelfSaveCopies");
        copies.Checked += (_, _) => _saveShelfCopies = true;
        copies.Unchecked += (_, _) => _saveShelfCopies = false;
        ContentStack.Children.Add(copies);
        ContentStack.Children.Add(FormRow(Text("Add files or an image from your clipboard", 12, true),
            Button("Choose files", PickFilesAsync), Button("Paste image", PasteShelfImageAsync)));
        _shelfStatusText = Text(_shelfStatus, 12, true);
        AutomationProperties.SetAutomationId(_shelfStatusText, "ShelfStatus");
        AutomationProperties.SetLiveSetting(_shelfStatusText, AutomationLiveSetting.Polite);
        ContentStack.Children.Add(_shelfStatusText);
        var count = Text($"{_vm.Shelf.Count} {(_vm.Shelf.Count == 1 ? "item" : "items")} · 100 maximum", 11, true);
        AutomationProperties.SetAutomationId(count, "ShelfCount"); ContentStack.Children.Add(count);
        ShelfList();
        ContentStack.Children.Add(Text("Removing an item keeps its original and any saved copy. Folders stay as references; folder contents are not copied.", 11, true));
        ContentStack.Children.Add(Button("Reveal saved copies", () => { Directory.CreateDirectory(ShelfCaptures.DirectoryPath); OpenFile(ShelfCaptures.DirectoryPath); }));
    }

    public Task HandleShelfDropAsync(DataPackageView data) => CaptureShelfAsync(async token =>
    {
        if (data.Contains(StandardDataFormats.StorageItems))
        {
            var items = await data.GetStorageItemsAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(10), token);
            // Browser/image apps may expose a temporary StorageFile alongside
            // their bitmap. Save the image bytes, rather than a short-lived path.
            // A multi-file drop must still keep every original storage item.
            if (items.Count > 0 && !(items.Count == 1 && items[0] is StorageFile && data.Contains(StandardDataFormats.Bitmap)))
                return await CaptureStorageItemsAsync(items, token);
        }
        if (data.Contains(StandardDataFormats.Bitmap))
        {
            if (_vm.Shelf.Count >= 100) throw new InvalidOperationException("The file shelf is full. Remove an item before adding this image.");
            var bitmap = await data.GetBitmapAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(10), token);
            using var image = await bitmap.OpenReadAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(10), token);
            using var input = image.AsStreamForRead();
            var path = await ShelfCaptures.SaveImageAsync(input, token);
            return _vm.AddShelfBatch([path]);
        }
        throw new InvalidOperationException("This drag contains no file or image. For a browser image, choose Copy image in the browser and use Paste image here.");
    });

    private async Task<int> CaptureStorageItemsAsync(IReadOnlyList<IStorageItem> items, CancellationToken token)
    {
        if (items.Count > 100) throw new InvalidOperationException("Drop at most 100 items at a time.");
        var paths = new List<string>();
        var known = _vm.Shelf.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var needed = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Path) || _saveShelfCopies && item is StorageFile && !ShelfCaptures.Owns(item.Path)) needed++;
            else if (known.Add(Path.GetFullPath(item.Path))) needed++;
        }
        if (_vm.Shelf.Count + needed > 100) throw new InvalidOperationException("There is not enough room on the shelf for this drop. Remove items first; the shelf holds 100 entries.");
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(item.Path) && (!_saveShelfCopies || item is StorageFolder || ShelfCaptures.Owns(item.Path)))
                paths.Add(item.Path);
            else if (item is StorageFile file)
            {
                using var source = await file.OpenReadAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(10), token);
                using var input = source.AsStreamForRead();
                paths.Add(await ShelfCaptures.SaveStreamAsync(input, file.Name, token));
            }
            else throw new InvalidOperationException("This folder has no local path and cannot be added. Choose a local folder instead.");
        }
        return _vm.AddShelfBatch(paths);
    }

    private async Task CaptureShelfAsync(Func<CancellationToken, Task<int>> capture)
    {
        if (_shelfDisposed) throw new OperationCanceledException("Notchling is closing.");
        if (IsCapturingShelf || HasOpenDialog) throw new InvalidOperationException("Wait for the current file operation to finish before dropping another item.");
        if (!_vm.IsReady || !_vm.WorkspaceReadable || _vm.IsDemo || !_vm.CanAccessModule(ModuleId.Shelf))
            throw new InvalidOperationException("The shelf is not ready. Finish loading or workspace recovery, and turn off sample preview before saving files.");
        IsCapturingShelf = true; HasOpenDialog = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shelfLifetime.Token, _vm.ShelfLifetimeToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            ShelfStatus("Saving to your shelf…");
            var added = await capture(deadline.Token);
            await _vm.SaveShelfAsync();
            ShelfStatus(added == 0 ? "Already on your shelf. Saved locally." : $"Added {added} {(added == 1 ? "item" : "items")}. Saved locally.");
        }
        catch (Exception error)
        {
            ShelfStatus(error is OperationCanceledException ? "The file operation timed out. Retry this drop; the originals are untouched."
                : "Could not finish saving: " + error.Message + " Any completed copies are kept in Reveal saved copies.");
            if (error is OperationCanceledException && !_shelfDisposed && _vm.IsReady) throw new TimeoutException("The shelf file operation timed out. The original files are untouched.", error);
            throw;
        }
        finally
        {
            IsCapturingShelf = false; HasOpenDialog = false;
            if (!_shelfDisposed && _vm.IsReady && _vm.SelectedModule is ModuleId.Shelf or ModuleId.Files) Render();
        }
    }

    private void ShelfStatus(string message)
    {
        if (_shelfDisposed || !_vm.IsReady) return;
        _shelfStatus = message;
        if (_shelfStatusText is not null) _shelfStatusText.Text = message;
        _vm.ReportShelfStatus(message);
    }

    private void DisposeShelfCapture()
    {
        if (_shelfDisposed) return;
        _shelfDisposed = true; _shelfLifetime.Cancel(); _shelfLifetime.Dispose();
    }

    private async Task PasteShelfImageAsync()
    {
        var content = global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (!content.Contains(StandardDataFormats.Bitmap)) throw new InvalidOperationException("No image is on the clipboard. In your browser or image app, choose Copy image, then try Paste image again.");
        await HandleShelfDropAsync(content);
    }

    private async Task PickFilesAsync()
    {
        if (HasOpenDialog || IsCapturingShelf) return;
        IReadOnlyList<StorageFile> files;
        HasOpenDialog = true;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _vm.WindowHandle);
            files = await picker.PickMultipleFilesAsync();
        }
        finally { HasOpenDialog = false; }
        if (files.Count > 0) await CaptureShelfAsync(token => CaptureStorageItemsAsync(files, token));
    }

    private void ShelfList()
    {
        foreach (var item in _vm.Shelf)
        {
            var title = Path.GetFileName(item.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var detail = new StackPanel { Spacing = 4, MinWidth = 0 };
            var titleText = Text(string.IsNullOrWhiteSpace(title) ? item.Path : title, 13);
            AutomationProperties.SetAutomationId(titleText, "ShelfItemPath-" + item.Id.ToString("N"));
            AutomationProperties.SetName(titleText, item.Path);
            detail.Children.Add(titleText);
            var exists = File.Exists(item.Path) || Directory.Exists(item.Path);
            var location = Text(!exists ? "Unavailable · original moved or removed" : ShelfCaptures.Owns(item.Path) ? "Saved copy on this device" : Directory.Exists(item.Path) ? "Folder reference" : "File reference", 11, true);
            detail.Children.Add(location);
            ToolTipService.SetToolTip(detail, item.Path);
            var open = Button("Open", () => OpenFile(item.Path)); open.IsEnabled = exists;
            var reveal = Button("Reveal", () => Reveal(item.Path)); reveal.IsEnabled = exists;
            ContentStack.Children.Add(Card(FormRow(detail, open, reveal, Button("Remove", async () =>
            {
                _vm.RemoveShelf(item.Id); await _vm.SaveShelfAsync(); ShelfStatus("Removed from the shelf. The file is kept."); Render();
            }))));
        }
        if (_vm.Shelf.Count == 0) ContentStack.Children.Add(Text("Nothing on the shelf yet. Drop a file or paste an image to get started.", 13, true));
    }

    private void Files()
    {
        ContentStack.Children.Add(FormRow(Text("Your folders", 12, true),
            Button("Documents", () => OpenFile(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))),
            Button("Downloads", () => OpenFile(NativeFolders.DownloadsPath)),
            Button("Desktop", () => OpenFile(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)))));
        ContentStack.Children.Add(Button("Add a frequently used file", PickFilesAsync)); ShelfList();
    }
}
