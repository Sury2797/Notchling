using Notch.Core;

namespace Notch.Windows.ViewModels;

public sealed partial class MainViewModel
{
    public string WorkspaceDirectory => _dataDirectory;
    internal CancellationToken ShelfLifetimeToken => _lifetimeToken;

    public int AddShelfBatch(IEnumerable<string> paths)
    {
        if (!ReadyForWorkspaceInput() || IsDemo || !RequirePremium(ModuleId.Shelf))
            throw new InvalidOperationException("The shelf is not ready to save files. Close the preview or finish workspace recovery first.");
        var items = new List<ShelfItem>();
        var knownPaths = Shelf.Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath) && !Directory.Exists(fullPath)) throw new FileNotFoundException("A dropped file is no longer available.", fullPath);
            if (knownPaths.Add(fullPath)) items.Add(new(Guid.NewGuid(), fullPath, DateTimeOffset.Now));
        }
        if (Shelf.Count + items.Count > 100) throw new InvalidOperationException("The file shelf is limited to 100 entries. Remove an item before dropping more files.");
        WorkspaceLimits.RequireStorageBudget(LocalSnapshot() with { Shelf = [.. items, .. Shelf] });
        foreach (var item in items.AsEnumerable().Reverse()) Shelf.Insert(0, item);
        return items.Count;
    }

    public async Task SaveShelfAsync()
    {
        if (!ReadyForWorkspaceInput() || IsDemo) throw new InvalidOperationException("The shelf cannot be saved until workspace recovery is complete and sample preview is off.");
        _saveDelay?.Cancel();
        await SaveLocalAsync(_lifetimeToken);
    }

    public void ReportShelfStatus(string message)
    {
        if (!_disposed) Status = message;
    }
}
