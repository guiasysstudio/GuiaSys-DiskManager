namespace GuiaSys.DiskManager.Models;

public sealed record DiskRecord(
    int Number,
    string FriendlyName,
    string SerialNumber,
    string PartitionStyle,
    long Size,
    bool IsBoot,
    bool IsSystem,
    bool IsOffline,
    bool IsReadOnly)
{
    public string SizeLabel => Size < 0 ? "Desconhecido" : $"{Size / 1_000_000_000d:N2} GB";
}

public sealed record PartitionRecord(
    int DiskNumber,
    int PartitionNumber,
    string DriveLetter,
    string Type,
    long Size,
    bool IsBoot,
    bool IsSystem)
{
    public string SizeLabel => Size < 0 ? "Desconhecido" : $"{Size / 1_000_000_000d:N2} GB";
}
