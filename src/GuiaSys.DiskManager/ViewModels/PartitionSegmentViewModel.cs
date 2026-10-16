using GuiaSys.DiskManager.Infrastructure;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.ViewModels;

/// <summary>
/// Stable, observable presentation model for a single half-open disk extent.
/// Selection updates must not replace the WPF Button that currently holds keyboard focus.
/// </summary>
public sealed class PartitionSegmentViewModel : ObservableObject
{
    private bool _isSelected;

    public PartitionSegmentViewModel(PartitionInfo? partition, string title, string subtitle,
        string kind, long size, double width, bool isSelected = false, long offset = 0, int diskNumber = -1)
    {
        Partition = partition;
        Title = title;
        Subtitle = subtitle;
        Kind = kind;
        Size = size;
        Width = width;
        Offset = offset;
        DiskNumber = diskNumber;
        _isSelected = isSelected;
    }

    public PartitionInfo? Partition { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string Kind { get; }
    public long Size { get; }
    public long Offset { get; }
    public double Width { get; }
    public bool IsUnallocated => Partition is null;
    /// <summary>The owning disk in the multi-disk visual map; -1 for the legacy selected-disk map.</summary>
    public int DiskNumber { get; }
    public bool HasVolumeUsage => Partition?.HasVolumeUsage ?? false;
    /// <summary>Only a real Get-Volume reading supplies file usage; unknown remains hidden in XAML.</summary>
    public double UsagePercent => Partition?.UsagePercent ?? 0d;

    /// <summary>
    /// Two concise, legible lines as in traditional partition managers:
    /// C:(NTFS) / 478 GB; (EFI) / 209 MB; (MSR) / 16 MB.
    /// Complete Windows labels and byte offsets remain in the tooltip.
    /// </summary>
    public string MapTypeLabel => Partition is null ? "Não alocado" :
        Partition.Kind switch
        {
            "Recovery" => "(Recuperação)",
            "EFI" => "(EFI)",
            "MSR" => "(MSR)",
            _ => Partition.DriveLetter.Length > 0
                ? $"{Partition.DriveLetter.TrimEnd(':')}:({Partition.FileSystem switch { "" => "Outro", var fs => fs }})"
                : $"({Partition.FileSystem switch { "" => Partition.Kind, var fs => fs }})"
        };
    public string MapSizeLabel => ByteSize.Format(Size);
    public bool IsSelected => _isSelected;

    public string ToolTip => $"{Title}\n{Subtitle}\nOffset: {Offset:N0} bytes\nTamanho exato: {ByteSize.Format(Size)}";
    public string AccessibleName => Partition is null
        ? $"Espaço não alocado, {ByteSize.Format(Size)}, início {Offset:N0} bytes. Selecionar disco inteiro."
        : $"Partição {Partition.PartitionNumber}, {Title}, {Subtitle}, início {Offset:N0} bytes" +
          (_isSelected ? ", selecionada" : ", não selecionada");

    public void SetSelected(bool selected)
    {
        if (SetProperty(ref _isSelected, selected, nameof(IsSelected)))
            OnPropertyChanged(nameof(AccessibleName));
    }
}
