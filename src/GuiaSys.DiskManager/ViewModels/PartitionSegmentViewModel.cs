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
        string kind, long size, double width, bool isSelected = false, long offset = 0)
    {
        Partition = partition;
        Title = title;
        Subtitle = subtitle;
        Kind = kind;
        Size = size;
        Width = width;
        Offset = offset;
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
