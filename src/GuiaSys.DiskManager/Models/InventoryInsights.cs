using System.Globalization;

namespace GuiaSys.DiskManager.Models;

/// <summary>
/// Presentation-only metrics derived from an immutable, geometrically validated snapshot.
/// Unallocated DISK space is never confused with free bytes INSIDE a filesystem volume.
/// The Windows storage subsystem is the only source for volume usage.
/// </summary>
public sealed record DiskSpaceMetrics(
    int DiskNumber, int PartitionCount, long CapacityBytes, long AllocatedBytes,
    long UnallocatedBytes, long LargestUnallocatedExtentBytes)
{
    public double AllocatedPercent => CapacityBytes > 0
        ? Math.Clamp(100d * AllocatedBytes / CapacityBytes, 0d, 100d) : 0;
    public double UnallocatedPercent => CapacityBytes > 0
        ? Math.Clamp(100d * UnallocatedBytes / CapacityBytes, 0d, 100d) : 0;
    public string CapacityLabel => ByteSize.Format(CapacityBytes);
    public string AllocatedLabel => ByteSize.Format(AllocatedBytes);
    public string UnallocatedLabel => ByteSize.Format(UnallocatedBytes);
    public string LargestFreeLabel => ByteSize.Format(LargestUnallocatedExtentBytes);
    public string UtilizationLabel => string.Create(CultureInfo.CurrentCulture,
        $"{AllocatedPercent:N1}% particionado · {UnallocatedPercent:N1}% não alocado");
}

public sealed record StorageOverviewMetrics(
    int DiskCount, int PartitionCount, int VolumeMetricsCount,
    int UnknownIdentityCount, long? CapacityBytes, long? UnallocatedBytes)
{
    public static StorageOverviewMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0);
    public string TotalCapacityLabel => CapacityBytes.HasValue ? ByteSize.Format(CapacityBytes.Value) : "Capacidade acima do limite de exibição";
    public string UnallocatedLabel => UnallocatedBytes.HasValue ? ByteSize.Format(UnallocatedBytes.Value) : "Capacidade acima do limite de exibição";
    public string MonitoredVolumesLabel => VolumeMetricsCount == 1 ? "1 volume monitorado" : $"{VolumeMetricsCount} volumes monitorados";
    public string IdentityLabel => UnknownIdentityCount == 0
        ? "Todos os discos com ID"
        : $"{UnknownIdentityCount} disco(s) sem identidade forte";
}

public sealed record InventoryInsights(StorageOverviewMetrics Overview,
    IReadOnlyDictionary<int, DiskSpaceMetrics> ByDisk)
{
    public DiskSpaceMetrics? FindDisk(int number) => ByDisk.GetValueOrDefault(number);

    public static InventoryInsights Build(StorageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var geometry = StorageGeometry.Build(snapshot);
        var byDisk = new Dictionary<int, DiskSpaceMetrics>();
        long? capacity = 0;
        long? unallocated = 0;
        foreach (var layout in geometry.Disks)
        {
            var free = layout.TotalFreeBytes;
            var info = new DiskSpaceMetrics(layout.DiskNumber,
                snapshot.Partitions.Count(p => p.DiskNumber == layout.DiskNumber),
                layout.DiskSize, layout.DiskSize - free, free, layout.LargestFreeExtentBytes);
            byDisk.Add(layout.DiskNumber, info);
            capacity = TryAdd(capacity, layout.DiskSize);
            unallocated = TryAdd(unallocated, free);
        }
        return new(new StorageOverviewMetrics(snapshot.Disks.Count, snapshot.Partitions.Count,
            snapshot.Partitions.Count(p => p.HasVolumeUsage),
            snapshot.Disks.Count(d => !d.HasStrongIdentity), capacity, unallocated), byDisk);
    }

    private static long? TryAdd(long? current, long value)
    {
        if (current is not long amount || value < 0 || amount > long.MaxValue - value) return null;
        return amount + value;
    }
}
