using System.Collections.ObjectModel;
using GuiaSys.DiskManager.Infrastructure;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.ViewModels;

/// <summary>
/// One read-only physical disk strip, like the disk rows in desktop partition managers.
/// The identity/geometry comes only from a validated Windows inventory snapshot.
/// </summary>
public sealed class DiskMapRowViewModel : ObservableObject
{
    private bool _isSelected;
    public DiskMapRowViewModel(DiskInfo disk, IReadOnlyList<PartitionSegmentViewModel> segments)
    {
        Disk = disk ?? throw new ArgumentNullException(nameof(disk));
        Segments = segments ?? throw new ArgumentNullException(nameof(segments));
    }

    public DiskInfo Disk { get; }
    public IReadOnlyList<PartitionSegmentViewModel> Segments { get; }
    public bool IsSelected => _isSelected;
    public string Label => $"Disco {Disk.Number}";
    public string DeviceLabel => Disk.FriendlyName;
    public string Summary => $"{Disk.PartitionStyle}  ·  {Disk.SizeLabel}";
    public string SafetyLabel => Disk.HasStrongIdentity ? Disk.ConnectionLabel :
        "Identidade insuficiente · somente leitura";

    public void UpdateSelection(int? diskNumber, PartitionInfo? partition)
    {
        SetProperty(ref _isSelected, diskNumber == Disk.Number, nameof(IsSelected));
        foreach (var segment in Segments)
            segment.SetSelected(diskNumber == Disk.Number &&
                segment.Partition is not null && partition is not null &&
                segment.Partition.PartitionNumber == partition.PartitionNumber &&
                string.Equals(segment.Partition.Identity, partition.Identity, StringComparison.Ordinal));
    }

    /// <summary>
    /// Reconstructs rows from checked half-open extents. Free space is represented
    /// only where confirmed by StorageGeometry; no guessed partitions or bytes.
    /// </summary>
    public static IReadOnlyList<DiskMapRowViewModel> Build(StorageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var layout = StorageGeometry.Build(snapshot);
        var result = new List<DiskMapRowViewModel>(layout.Disks.Count);
        foreach (var disk in snapshot.Disks.OrderBy(d => d.Number))
        {
            var rowLayout = layout.FindDisk(disk.Number)
                ?? throw new InvalidOperationException("Disk geometry unavailable after validation.");
            var segments = new List<PartitionSegmentViewModel>(rowLayout.Extents.Count);
            foreach (var extent in rowLayout.Extents)
            {
                var part = extent.PartitionNumber is int number
                    ? snapshot.FindPartition(disk.Number, number) : null;
                var title = part is null ? "Não alocado" :
                    string.IsNullOrWhiteSpace(part.DriveLetter) ? part.Kind :
                    part.DriveLetter.TrimEnd(':') + ":";
                var subtitle = part is null ? ByteSize.Format(extent.Length) :
                    $"{part.FileSystem switch { "" => part.Kind, var fs => fs }} · {part.SizeLabel}";
                segments.Add(new PartitionSegmentViewModel(
                    part, title, subtitle, part?.Kind ?? "Unallocated", extent.Length,
                    0d, offset: extent.Offset, diskNumber: disk.Number));
            }
            result.Add(new DiskMapRowViewModel(disk, segments));
        }
        return result;
    }
}
