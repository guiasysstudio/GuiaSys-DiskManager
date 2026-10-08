using System.Collections.ObjectModel;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

public sealed class OperationQueue
{
    public ObservableCollection<StorageOperation> Pending { get; } = [];
    public void Enqueue(StorageOperation operation) => Pending.Add(operation ?? throw new ArgumentNullException(nameof(operation)));
    public bool UndoLast()
    {
        if (Pending.Count == 0) return false;
        Pending.RemoveAt(Pending.Count - 1);
        return true;
    }
    public void Clear() => Pending.Clear();
    public IReadOnlyList<StorageOperation> Snapshot() => Pending.ToArray();
}
