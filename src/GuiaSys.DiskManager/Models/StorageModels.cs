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
    /// <summary>Sector sizes as reported by MSFT_Disk; null means unavailable.</summary>
    public int? LogicalSectorSize { get; init; }
    public int? PhysicalSectorSize { get; init; }
    public long Size { get; init; }
    public bool IsBoot { get; init; }
    public bool IsSystem { get; init; }
    public bool IsOffline { get; init; }
    public bool IsReadOnly { get; init; }
    public string SizeLabel => ByteSize.Format(Size);
    public string DisplayName => $"Disco {Number} · {FriendlyName}";
    public string TemperatureLabel => TemperatureC is int value ? $"{value} °C" : "Não disponível";
    public bool HasStrongIdentity => !string.IsNullOrWhiteSpace(UniqueId);
    public string Identity => HasStrongIdentity ? $"uid:{UniqueId.Trim()}" : BuildWeakIdentity(Number, SerialNumber, Size);
    public string IdentityStrengthLabel => HasStrongIdentity ? "Identidade forte" : "Identidade insuficiente para gravação";
    public string TechnologyLabel => StorageDisplayLabels.Technology(MediaType, BusType);
    public string ConnectionLabel => StorageDisplayLabels.Bus(BusType);
    public string PhysicalSectorLabel => PhysicalSectorSize is > 0 ? $"{PhysicalSectorSize:N0} bytes" : "Não informado";
    public string LogicalSectorLabel => LogicalSectorSize is > 0 ? $"{LogicalSectorSize:N0} bytes" : "Não informado";
    public string StateLabel => IsOffline ? "Offline" : IsReadOnly ? "Online · somente leitura" : "Online";

    public static string BuildWeakIdentity(int number, string serialNumber, long size) =>
        $"weak:number:{number}|serial:{serialNumber.Trim()}|size:{size}";
}

public sealed record PartitionInfo
{
    public required int DiskNumber { get; init; }
    public required int PartitionNumber { get; init; }
    public string PartitionGuid { get; init; } = "";
    public string GptType { get; init; } = "";
    public bool HasStrongIdentity => PartitionIdentity.HasStrongIdentity(this);
    public string? Identity => PartitionIdentity.TryGetFingerprint(this);
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
    /// <summary>Get-Volume.Size/SizeRemaining; both null if Windows does not expose reliable counters.</summary>
    public long? VolumeCapacityBytes { get; init; }
    public long? VolumeAvailableBytes { get; init; }
    public string HealthStatus { get; init; } = "Unknown";
    public string SizeLabel => ByteSize.Format(Size);
    public string FreeLabel => HasVolumeUsage ? ByteSize.Format(VolumeAvailableBytes!.Value) : "Não disponível";
    public bool HasVolumeUsage => VolumeCapacityBytes is >= 0 &&
        VolumeAvailableBytes is >= 0 &&
        VolumeAvailableBytes <= VolumeCapacityBytes &&
        VolumeCapacityBytes > 0;
    public long? VolumeUsedBytes => HasVolumeUsage ? VolumeCapacityBytes!.Value - VolumeAvailableBytes!.Value : null;
    public double? UsagePercent => HasVolumeUsage
        ? Math.Clamp(100d * VolumeUsedBytes!.Value / VolumeCapacityBytes!.Value, 0d, 100d)
        : null;
    public string UsageLabel => UsagePercent is double percent ? $"{percent:N1}% em uso" : "Uso indisponível";
    public string VolumeCapacityLabel => HasVolumeUsage ? ByteSize.Format(VolumeCapacityBytes!.Value) : "Não disponível";
    public string UsedLabel => VolumeUsedBytes is long bytes ? ByteSize.Format(bytes) : "Não disponível";
    public string UsageHealthLabel => UsagePercent switch
    {
        >= 95d => "Crítico · pouco espaço livre",
        >= 85d => "Atenção · espaço reduzido",
        not null => "Capacidade monitorada",
        _ => "Métricas indisponíveis"
    };
    public string DisplayName => DriveLetter.Length > 0 ? $"{DriveLetter}:  {Label}" : $"Partição {PartitionNumber}";
    public string Kind => PartitionClassifier.Classify(Type, FileSystem, GptType);
    public string ProtectionLabel => IsBoot || IsSystem ? "Inicialização / Sistema" :
        IsHidden ? "Oculta" : Kind is "EFI" or "MSR" or "Recovery" ? "Reservada / Protegida" :
        IsReadOnly ? "Somente leitura" : "Padrão";
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

public static class StorageDisplayLabels
{
    public static string Technology(string? mediaType, string? busType)
    {
        var media = (mediaType ?? "").Trim().ToUpperInvariant();
        var bus = (busType ?? "").Trim().ToUpperInvariant();
        if (media is "SSD" or "SOLID STATE DRIVE") return "SSD (informado pelo Windows)";
        if (media is "HDD" or "HARD DISK DRIVE") return "HDD (informado pelo Windows)";
        if (media is "SCM" or "STORAGE CLASS MEMORY") return "Memória de classe de armazenamento";
        if (bus == "NVME") return "NVMe · tipo de mídia não informado";
        return "Tipo de mídia não informado";
    }

    public static string Bus(string? busType)
    {
        var bus = (busType ?? "").Trim();
        return bus.Length == 0 || bus.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "Conexão não informada" : bus.ToUpperInvariant() switch
            {
                "NVME" => "NVMe", "SATA" => "SATA", "SAS" => "SAS", "USB" => "USB",
                "RAID" => "RAID / controlador", "VIRTUAL" => "Virtual", "FILE BACKED VIRTUAL" => "Virtual em arquivo",
                _ => bus
            };
    }
}

public static class PartitionClassifier
{
    public static string Classify(string type, string fileSystem, string? gptType = null)
    {
        var guid = (gptType ?? "").Trim().Trim('{', '}');
        if (guid.Equals("c12a7328-f81f-11d2-ba4b-00a0c93ec93b", StringComparison.OrdinalIgnoreCase)) return "EFI";
        if (guid.Equals("e3c9e316-0b5c-4db8-817d-f92df00215ae", StringComparison.OrdinalIgnoreCase)) return "MSR";
        if (guid.Equals("de94bba4-06d1-4d40-a16a-bfd50179d6ac", StringComparison.OrdinalIgnoreCase)) return "Recovery";
        var combined = $"{type} {fileSystem}".ToUpperInvariant();
        if (type.Contains("RECOVERY", StringComparison.OrdinalIgnoreCase)) return "Recovery";
        if (type.Contains("RESERVED", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("MSR", StringComparison.OrdinalIgnoreCase)) return "MSR";
        if (type.Contains("SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            type.Contains("EFI", StringComparison.OrdinalIgnoreCase)) return "EFI";
        if (guid.Length != 0 &&
            !guid.Equals("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", StringComparison.OrdinalIgnoreCase))
            return "Other";
        if (combined.Contains("NTFS")) return "NTFS";
        if (combined.Contains("EXFAT")) return "exFAT";
        if (combined.Contains("FAT")) return "FAT32";
        if (combined.Contains("RAW")) return "RAW";
        return "Other";
    }
}
