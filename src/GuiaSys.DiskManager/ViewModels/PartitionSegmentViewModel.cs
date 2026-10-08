using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.ViewModels;

public sealed record PartitionSegmentViewModel(
    PartitionInfo? Partition,
    string Title,
    string Subtitle,
    string Kind,
    long Size,
    double Width)
{
    public bool IsUnallocated => Partition is null;
    public string ToolTip => $"{Title}\n{Subtitle}\n{ByteSize.Format(Size)}";
}
