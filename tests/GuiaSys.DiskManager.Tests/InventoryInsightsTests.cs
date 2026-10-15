using System.IO;
using System.Text.Json;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class InventoryInsightsTests
{
    private const string Basic = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    private const string Recovery = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private static readonly DateTimeOffset At = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private static DiskInfo Disk(int n = 2, long bytes = 1_000_000_000) => new()
    {
        Number = n, UniqueId = $"test-disk-{n}", FriendlyName = "Virtual",
        Size = bytes, PartitionStyle = "GPT", BusType = "NVMe", MediaType = "Unspecified"
    };

    private static PartitionInfo Part(long size = 600_000_000, long offset = 100_000_000) => new()
    {
        DiskNumber = 2, PartitionNumber = 1, Type = "Basic",
        PartitionGuid = "5597cacb-cc13-46b8-8d1d-0083ece64230",
        GptType = Basic, AccessPaths = "", DriveLetter = "E",
        Offset = offset, Size = size, FileSystem = "NTFS",
        VolumeCapacityBytes = 590_000_000, VolumeAvailableBytes = 150_000_000
    };

    private static StorageSnapshot Snapshot(params PartitionInfo[] parts) =>
        new([Disk()], parts, At);

    [Fact]
    public void Disk_inventory_reports_allocated_space_separately_from_volume_free_space()
    {
        var report = InventoryInsights.Build(Snapshot(Part()));
        var disk = report.FindDisk(2)!;
        Assert.Equal(1_000_000_000, disk.CapacityBytes);
        Assert.Equal(600_000_000, disk.AllocatedBytes);
        Assert.Equal(400_000_000, disk.UnallocatedBytes);
        Assert.Equal(300_000_000, disk.LargestUnallocatedExtentBytes);
        Assert.Equal(60d, disk.AllocatedPercent);
        Assert.Equal(40d, disk.UnallocatedPercent);
        Assert.Equal(150_000_000, Part().VolumeAvailableBytes);
        Assert.NotEqual(disk.UnallocatedBytes, Part().VolumeAvailableBytes);
        Assert.Equal(1, report.Overview.VolumeMetricsCount);
    }

    [Fact]
    public void Physical_disk_totals_and_missing_identity_are_aggregated()
    {
        var disk1 = Disk();
        var disk2 = Disk(4, 2_000_000_000) with { UniqueId = "" };
        var snapshot = new StorageSnapshot([disk1, disk2], [Part()], At);
        var report = InventoryInsights.Build(snapshot).Overview;
        Assert.Equal(2, report.DiskCount);
        Assert.Equal(1, report.PartitionCount);
        Assert.Equal(3_000_000_000, report.CapacityBytes);
        Assert.Equal(2_400_000_000, report.UnallocatedBytes);
        Assert.Equal(1, report.UnknownIdentityCount);
        Assert.Contains("sem identidade", report.IdentityLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Aggregate_overflow_marks_summary_unknown_without_corrupting_geometry()
    {
        var snapshot = new StorageSnapshot([Disk(bytes: long.MaxValue), Disk(4, 1024)], [], At);
        var report = InventoryInsights.Build(snapshot);
        Assert.Null(report.Overview.CapacityBytes);
        Assert.Null(report.Overview.UnallocatedBytes);
        Assert.Equal(long.MaxValue, report.FindDisk(2)!.UnallocatedBytes);
    }

    [Fact]
    public void Empty_inventory_does_not_report_that_all_disks_have_stable_ids()
    {
        var empty = InventoryInsights.Build(new StorageSnapshot([], [], At));
        Assert.Equal(0, empty.Overview.DiskCount);
        Assert.Contains("Nenhum dispositivo", empty.Overview.IdentityLabel);
        Assert.Equal(0L, empty.Overview.CapacityBytes);
        Assert.Null(empty.FindDisk(2));
    }

    [Fact]
    public void All_empty_disk_space_is_unallocated_not_free_in_a_volume()
    {
        var report = InventoryInsights.Build(Snapshot());
        Assert.Equal(0, report.Overview.VolumeMetricsCount);
        Assert.Equal(0, report.FindDisk(2)!.PartitionCount);
        Assert.Equal(1_000_000_000, report.FindDisk(2)!.UnallocatedBytes);
    }

    [Theory]
    [InlineData(0L, 100L, false)]
    [InlineData(100L, 0L, true)]
    [InlineData(100L, 100L, true)]
    [InlineData(100L, 101L, false)]
    [InlineData(-1L, 0L, false)]
    [InlineData(100L, -1L, false)]
    public void Volume_usage_obeys_capacity_invariants(long capacity, long free, bool expected)
    {
        var p = Part() with { VolumeCapacityBytes = capacity, VolumeAvailableBytes = free };
        Assert.Equal(expected, p.HasVolumeUsage);
        Assert.Equal(expected, p.UsagePercent.HasValue);
        if (!expected) Assert.Equal("Uso indisponível", p.UsageLabel);
    }

    [Fact]
    public void Completely_full_volume_reports_zero_free_not_unavailable()
    {
        var p = Part() with { VolumeCapacityBytes = 100_000, VolumeAvailableBytes = 0 };
        Assert.True(p.HasVolumeUsage);
        Assert.Equal(100d, p.UsagePercent);
        Assert.Equal(100_000, p.VolumeUsedBytes);
        Assert.Contains("Crítico", p.UsageHealthLabel);
        Assert.NotEqual("Não disponível", p.FreeLabel);
    }

    [Theory]
    [InlineData(95_000L, "Crítico")]
    [InlineData(87_000L, "Atenção")]
    [InlineData(80_000L, "Capacidade monitorada")]
    public void Threshold_labels_are_derived_from_reported_filesystem_use(long used, string label)
    {
        var p = Part() with { VolumeCapacityBytes = 100_000, VolumeAvailableBytes = 100_000 - used };
        Assert.Contains(label, p.UsageHealthLabel);
    }

    [Fact]
    public void Missing_counters_do_not_fake_a_full_volume()
    {
        var p = Part() with { VolumeCapacityBytes = null, VolumeAvailableBytes = null,
            VolumeSizeRemaining = 0 };
        Assert.False(p.HasVolumeUsage);
        Assert.Null(p.VolumeUsedBytes);
        Assert.Null(p.UsagePercent);
        Assert.Equal("Não disponível", p.FreeLabel);
    }

    [Theory]
    [InlineData("SSD", "USB", "SSD (informado pelo Windows)")]
    [InlineData("HDD", "SATA", "HDD (informado pelo Windows)")]
    [InlineData("Unspecified", "NVMe", "NVMe · tipo de mídia não informado")]
    [InlineData("", "Unknown", "Tipo de mídia não informado")]
    [InlineData("Unspecified", "USB", "Tipo de mídia não informado")]
    public void Hardware_technology_never_infers_ssd_from_bus_type(string media, string bus, string expected)
    {
        Assert.Equal(expected, StorageDisplayLabels.Technology(media, bus));
    }

    [Fact]
    public void Gpt_partition_type_has_priority_over_filesystem()
    {
        Assert.Equal("Recovery", PartitionClassifier.Classify("Basic", "NTFS", Recovery));
        Assert.Equal("EFI", PartitionClassifier.Classify("Basic", "FAT32",
            "c12a7328-f81f-11d2-ba4b-00a0c93ec93b"));
        Assert.Equal("MSR", PartitionClassifier.Classify("Basic", "",
            "{e3c9e316-0b5c-4db8-817d-f92df00215ae}"));
        Assert.Equal("NTFS", PartitionClassifier.Classify("Basic", "NTFS", Basic));
        Assert.Equal("Other", PartitionClassifier.Classify("Basic", "NTFS",
            "d0e98700-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void Device_sector_metrics_do_not_invent_missing_values()
    {
        var disk = Disk();
        Assert.Equal("Não informado", disk.PhysicalSectorLabel);
        Assert.Contains("não informado", disk.TechnologyLabel, StringComparison.OrdinalIgnoreCase);
        var known = disk with { LogicalSectorSize = 512, PhysicalSectorSize = 4096,
            MediaType = "SSD" };
        Assert.Contains("512", known.LogicalSectorLabel);
        Assert.Contains("4.096", known.PhysicalSectorLabel.Replace(",", "."));
        Assert.Contains("SSD", known.TechnologyLabel);
    }

    private const string InventoryJson = """
    {
      "Disks": [{
        "Number": 2, "UniqueId": "uid-disk-2", "FriendlyName": "Example",
        "PartitionStyle": "GPT", "Size": 1000000000,
        "IsBoot": false, "IsSystem": false, "IsOffline": false, "IsReadOnly": false,
        "BusType": "NVMe", "MediaType": "Unspecified",
        "LogicalSectorSize": 512, "PhysicalSectorSize": 4096, "TemperatureC": null
      }],
      "Partitions": [{
        "DiskNumber": 2, "PartitionNumber": 1,
        "PartitionGuid": "5597cacb-cc13-46b8-8d1d-0083ece64230",
        "GptType": "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        "AccessPaths": "", "DriveLetter": "E", "Type": "Basic",
        "Offset": 100000000, "Size": 600000000,
        "IsBoot": false, "IsSystem": false, "IsHidden": false,
        "IsReadOnly": false, "IsOffline": false,
        "FileSystem": "NTFS", "VolumeCapacityBytes": 590000000,
        "VolumeAvailableBytes": 150000000, "VolumeSizeRemaining": 150000000
      }]
    }
    """;

    [Fact]
    public void Windows_json_parser_preserves_actual_optional_counters()
    {
        var snapshot = PowerShellDiskInventoryService.ParseInventory(InventoryJson, At);
        Assert.Equal(At, snapshot.CapturedAt);
        var disk = Assert.Single(snapshot.Disks);
        var partition = Assert.Single(snapshot.Partitions);
        Assert.Equal(512, disk.LogicalSectorSize);
        Assert.Equal(4096, disk.PhysicalSectorSize);
        Assert.Equal(590_000_000L, partition.VolumeCapacityBytes);
        Assert.Equal(150_000_000L, partition.VolumeAvailableBytes);
        Assert.Equal(600_000_000L, InventoryInsights.Build(snapshot).FindDisk(2)!.AllocatedBytes);
    }

    [Fact]
    public void Older_windows_payload_without_optional_counters_remains_readable()
    {
        using var root = JsonDocument.Parse(InventoryJson);
        var raw = root.RootElement.ToString();
        raw = raw.Replace("    \"LogicalSectorSize\": 512, \"PhysicalSectorSize\": 4096, \"TemperatureC\": null",
            "    \"TemperatureC\": null");
        raw = raw.Replace("    \"FileSystem\": \"NTFS\", \"VolumeCapacityBytes\": 590000000,",
            "    \"FileSystem\": \"NTFS\",");
        raw = raw.Replace("    \"VolumeAvailableBytes\": 150000000, \"VolumeSizeRemaining\": 150000000",
            "    \"VolumeSizeRemaining\": 150000000");
        var snapshot = PowerShellDiskInventoryService.ParseInventory(raw, At);
        Assert.Null(snapshot.Disks[0].LogicalSectorSize);
        Assert.False(snapshot.Partitions[0].HasVolumeUsage);
    }

    [Fact]
    public void Invalid_optional_sector_payload_fails_closed_without_synthetic_assumptions()
    {
        var bad = InventoryJson.Replace("\"LogicalSectorSize\": 512",
            "\"LogicalSectorSize\": \"unknown\"");
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ParseInventory(bad, At));
    }

    [Fact]
    public void Missing_partition_array_is_rejected()
    {
        var bad = InventoryJson.Replace("\"Partitions\": [{", "\"NotPartitions\": [{");
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ParseInventory(bad, At));
    }
}
