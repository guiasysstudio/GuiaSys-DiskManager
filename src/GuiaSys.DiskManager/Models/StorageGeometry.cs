using System.IO;

namespace GuiaSys.DiskManager.Models;

/// <summary>Half-open byte interval [Offset, EndExclusive). A null partition is unallocated space.</summary>
public sealed record StorageExtent(long Offset, long Length, int? PartitionNumber)
{
    public long EndExclusive => checked(Offset + Length);
    public bool IsUnallocated => PartitionNumber is null;
}

public sealed record DiskLayout(int DiskNumber, string DiskIdentity, long DiskSize, IReadOnlyList<StorageExtent> Extents)
{
    public long TotalFreeBytes => Extents.Where(x => x.IsUnallocated).Sum(x => x.Length);
    public long LargestFreeExtentBytes => Extents.Where(x => x.IsUnallocated).Select(x => x.Length).DefaultIfEmpty(0).Max();
}

public sealed record StorageLayout(IReadOnlyList<DiskLayout> Disks)
{
    public DiskLayout? FindDisk(int number) => Disks.FirstOrDefault(x => x.DiskNumber == number);
}

/// <summary>
/// Read-only geometry derived from Windows inventory. No partition table writes or assumptions
/// about filesystem shrinkability/alignment are made. Invalid/overlapping geometry fails closed.
/// </summary>
public static class StorageGeometry
{
    public static StorageLayout Build(StorageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Disks is null || snapshot.Partitions is null)
            throw new InvalidDataException("Inventário sem listas completas de discos e partições.");
        if (snapshot.Disks.Any(x => x.Number < 0 || x.Size <= 0) ||
            snapshot.Disks.GroupBy(x => x.Number).Any(x => x.Count() != 1))
            throw new InvalidDataException("Numeração ou tamanho de discos inválidos.");
        if (snapshot.Disks.Where(x => x.HasStrongIdentity)
            .GroupBy(x => x.Identity, StringComparer.Ordinal).Any(x => x.Count() != 1))
            throw new InvalidDataException("Identidade de disco ambígua.");
        if (snapshot.Partitions.Any(x => x.DiskNumber < 0 || x.PartitionNumber <= 0 ||
            x.Offset < 0 || x.Size <= 0))
            throw new InvalidDataException("Geometria ou numeração de partição inválida.");

        var layouts = new List<DiskLayout>(snapshot.Disks.Count);
        foreach (var disk in snapshot.Disks.OrderBy(x => x.Number))
        {
            var parts = snapshot.Partitions.Where(x => x.DiskNumber == disk.Number)
                .OrderBy(x => x.Offset).ThenBy(x => x.PartitionNumber).ToArray();
            if (parts.GroupBy(x => x.PartitionNumber).Any(x => x.Count() != 1))
                throw new InvalidDataException($"Partições duplicadas no disco {disk.Number}.");

            var extents = new List<StorageExtent>(parts.Length * 2 + 1);
            long cursor = 0;
            foreach (var part in parts)
            {
                if (part.Offset < cursor || part.Offset > disk.Size ||
                    part.Size > disk.Size - part.Offset)
                    throw new InvalidDataException($"Partições sobrepostas ou fora dos limites no disco {disk.Number}.");
                if (part.Offset > cursor)
                    extents.Add(new StorageExtent(cursor, part.Offset - cursor, null));
                extents.Add(new StorageExtent(part.Offset, part.Size, part.PartitionNumber));
                cursor = checked(part.Offset + part.Size);
            }
            if (cursor < disk.Size)
                extents.Add(new StorageExtent(cursor, disk.Size - cursor, null));
            layouts.Add(new DiskLayout(disk.Number, disk.Identity, disk.Size, extents.ToArray()));
        }

        if (snapshot.Partitions.Any(part => !layouts.Any(d => d.DiskNumber == part.DiskNumber)))
            throw new InvalidDataException("Partição sem disco correspondente.");
        return new StorageLayout(layouts.ToArray());
    }
}
