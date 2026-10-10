using Microsoft.UI.Xaml;
using Notch.Core;
using Notch.Windows.Views;
using Windows.ApplicationModel.DataTransfer;

namespace Notch.Windows;

public sealed partial class MainWindow
{
    private bool _shelfDragging;
    private bool CanSaveShelfDrop => !_quitting && !_closingAttempt && _vm.IsReady && _vm.WorkspaceReadable
        && !_vm.IsDemo && _vm.CanAccessModule(ModuleId.Shelf) && !HasOpenDialog && _utilities?.IsCapturingShelf != true;

    private void OnShelfDragOver(object sender, DragEventArgs args)
    {
        var operation = UtilityToolsView.GetShelfDropOperation(args.DataView, args.AllowedOperations);
        if (operation == DataPackageOperation.None) return;
        if (!CanSaveShelfDrop) { args.AcceptedOperation = DataPackageOperation.None; return; }

        _shelfDragging = true;
        _openDelay.Stop();
        _switchDelay.Stop();
        _pendingModule = null;
        args.AcceptedOperation = operation;
        args.DragUIOverride.Caption = "Save to Notchling Shelf";
        args.DragUIOverride.IsCaptionVisible = true;
        // Select once while a native drag crosses the compact target. Keep the
        // real payload for Drop: DragOver must never read or save its contents.
        if (_vm.SelectedModule != ModuleId.Shelf || _vm.Overlay.Mode != OverlayMode.Expanded)
            _vm.SelectModule(ModuleId.Shelf);
    }

    private void OnShelfDragLeave(object sender, DragEventArgs args)
    {
        // Routed leaves from a child also occur during the opening resize.
        // Release the drag guard only after the cursor leaves the actual HWND.
        if (!_host.IsPointerInsideWindow) _shelfDragging = false;
    }

    private async void OnShelfDrop(object sender, DragEventArgs args)
    {
        _shelfDragging = false;
        // The visible Shelf target owns handled drops; release the shell drag
        // guard without importing the same payload twice.
        if (args.Handled) return;
        var operation = UtilityToolsView.GetShelfDropOperation(args.DataView, args.AllowedOperations);
        if (!CanSaveShelfDrop || operation == DataPackageOperation.None) return;
        args.Handled = true;
        args.AcceptedOperation = operation;
        var deferral = args.GetDeferral();
        try
        {
            _vm.SelectModule(ModuleId.Shelf);
            await _vm.ExecuteAsync(() => (_utilities ??= new(_vm)).HandleShelfDropAsync(args.DataView));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException)
        {
            _vm.ShowError("The drop could not be saved: " + error.Message);
        }
        finally
        {
            _shelfDragging = false;
            deferral.Complete();
            CheckHoverDismissal();
        }
    }
}
