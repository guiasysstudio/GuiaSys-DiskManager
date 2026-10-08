using System.Globalization;

namespace GuiaSys.DiskManager.Models;

public sealed record DiskInfo
{
    public required int Number { get; init; }
    public required string UniqueId { get; init; }
    public required string FriendlyName { get; init; }
    public string Manufacturer { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string PartitionStyle { get; init; } = "RAW";
    public string BusType { get; init; } = "Unknown";
    public string MediaType { get; init; } = "Unspecified";
    public string HealthStatus { get; init; } = "Unknown";
    public string OperationalStatus { get; init; } = "Unknown";
    public int? TemperatureC { get; init; }
    public long Size { get; init; }
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool IsOffline { get; init; }
    public bool IsReadOnly { get; init; }
    public string SizeLabel => ByteSize.Format(Size);
    public string DisplayName => $"Disco {Number} · {FriendlyName}";
    public string TemperatureLabel => TemperatureC is int value ? $"{value} °C" : "Não disponível";
    public string Identity => string.IsNullOrWhiteSpace(UniqueId) ? $"number:{Number}|serial:{SerialNumber}|size:{Size}" : UniqueId.Trim();
}

public sealed record PartitionInfo
{
    public required int DiskNumber { get; init; }
    public required int PartitionNumber { get; init; }
    public required string AccessPaths { get; init; }
    public required string DriveLetter { get; init; }
    public required string Type { get; init; }
    public required long Offset { get; init; }
    public required long Size { get; init; }
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool IsHidden { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsOffline { get; init; }
    public string FileSystem { get; init; } = "";
    public string Label { get; init; } = "";
    public long VolumeSizeRemaining { get; init; }
    public string HealthStatus { get; init; } = "Unknown";
    public string SizeLabel => ByteSize.Format(Size);
    public string FreeLabel => FileSystem.Length == 0 ? "—" : ByteSize.Format(VolumeSizeRemaining);
    public string DisplayName => DriveLetter.Length > 0 ? $"{DriveLetter}:  {Label}" : $"Partição {PartitionNumber}";
    public string Kind => PartitionClassifier.Classify(Type, FileSystem);
}

public sealed record StorageSnapshot(
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<PartitionInfo> Partitions,
    DateTimeOffset CapturedAt)
{
    public DiskInfo? FindDisk(int number) => Disks.FirstOrDefault(d => d.Number == number);
    public PartitionInfo? FindPartition(int diskNumber, int partitionNumber) =>
        Partitions.FirstOrDefault(p => p.DiskNumber == diskNumber && p.PartitionNumber == partitionNumber);
}

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 0) return "Desconhecido";
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < Units.Length - 1) { value /= 1000; unit++; }
        return string.Create(CultureInfo.CurrentCulture, $"{value:N2} {Units[unit]}");
    }
}

public static class PartitionClassifier
{
    public static string Classify(string type, string fileSystem)
    {
        var combined = $"{type} {fileSystem}".ToUpperInvariant();
        if (combined.Contains("SYSTEM") || combined.Contains("EFI")) return "EFI";
        if (combined.Contains("RESERVED") || combined.Contains("MSR")) return "MSR";
        if (combined.Contains("RECOVERY")) return "Recovery";
        if (combined.Contains("NTFS")) return "NTFS";
        if (combined.Contains("EXFAT")) return "exFAT";
        if (combined.Contains("FAT")) return "FAT32";
        if (combined.Contains("RAW")) return "RAW";
        return "Other";
    }
}
