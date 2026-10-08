using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

public interface IDiskInventoryService
{
    Task<(IReadOnlyList<DiskRecord> Disks, IReadOnlyList<PartitionRecord> Partitions)> ReadAsync(CancellationToken cancellationToken);
}
