using System.Text.Json.Nodes;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class StorageHealthAssessmentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static DiskInfo Disk() => new()
    {
        Number = 0, UniqueId = "disk-zero", FriendlyName = "Test disk",
        PartitionStyle = "GPT", Size = 4_000_000_000
    };
    private static StorageSnapshot Snapshot(DiskInfo disk, params PartitionInfo[] parts) =>
        new([disk], parts, At);

    private static PartitionInfo Volume() => new()
    {
        DiskNumber = 0, PartitionNumber = 1, Type = "Basic",
        AccessPaths = "", DriveLetter = "C", Size = 2_000_000_000,
        Offset = 1_048_576, FileSystem = "NTFS", HealthStatus = "Healthy"
    };

    [Fact]
    public void Missing_device_has_unknown_report_not_false_green()
    {
        var report = StorageHealthAssessment.Analyze(new StorageSnapshot([], [], At), 0);
        Assert.Equal(DiskHealthLevel.Unknown, report.Level);
        Assert.False(report.SmartVerified);
    }

    [Theory]
    [InlineData("Healthy", DiskHealthLevel.Normal)]
    [InlineData("Warning", DiskHealthLevel.Warning)]
    [InlineData("Unhealthy", DiskHealthLevel.Critical)]
    [InlineData("Unknown", DiskHealthLevel.Unknown)]
    [InlineData("", DiskHealthLevel.Unknown)]
    [InlineData("Unrecognized", DiskHealthLevel.Unknown)]
    public void Windows_device_health_is_not_implicitly_smart_certified(string status, DiskHealthLevel expected)
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with { HealthStatus = status }), 0);
        Assert.Equal(expected, report.Level);
        Assert.False(report.SmartVerified);
        Assert.Contains(report.Checks, c => c.Component.Contains("S.M.A.R.T.") && c.Status == "Não verificado");
        Assert.Contains(report.Checks, c => c.Component.Contains("Sistema de arquivos") && c.Status == "Não executado");
    }

    [Fact]
    public void Missing_counters_never_assume_zero_or_good_condition()
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk()), 0);
        Assert.Equal(DiskHealthLevel.Unknown, report.Level);
        Assert.Contains(report.Checks, c => c.Component == "Confiabilidade do hardware" &&
            c.Status == "Não disponível");
        Assert.Contains(report.Checks, c => c.Component == "Temperatura" && c.Status == "Não disponível");
    }

    [Theory]
    [InlineData(64, DiskHealthLevel.Normal)]
    [InlineData(65, DiskHealthLevel.Warning)]
    [InlineData(80, DiskHealthLevel.Warning)]
    public void Temperature_is_advisory_without_claiming_device_self_test(int temperature, DiskHealthLevel expected)
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with
        {
            HealthStatus = "Healthy", TemperatureC = temperature
        }), 0);
        Assert.Equal(expected, report.Level);
        Assert.False(report.SmartVerified);
    }

    [Theory]
    [InlineData("Online", DiskHealthLevel.Normal)]
    [InlineData("Unknown", DiskHealthLevel.Normal)]
    [InlineData("Degraded", DiskHealthLevel.Warning)]
    [InlineData("Online, Lost Communication", DiskHealthLevel.Warning)]
    [InlineData("Failed", DiskHealthLevel.Warning)]
    [InlineData("Offline", DiskHealthLevel.Warning)]
    public void Operational_driver_alerts_are_not_hidden_by_healthy_flag(string operational, DiskHealthLevel expected)
    {
        var disk = Disk() with { HealthStatus = "Healthy", OperationalStatus = operational };
        Assert.Equal(expected, StorageHealthAssessment.Analyze(Snapshot(disk), 0).Level);
    }

    [Fact]
    public void Unsupported_temperature_value_is_unknown_not_an_alarm()
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with
        {
            HealthStatus = "Healthy", TemperatureC = 999
        }), 0);
        Assert.Equal(DiskHealthLevel.Normal, report.Level);
        Assert.Contains(report.Checks, c => c.Component == "Temperatura" && c.Status == "Não disponível");
    }

    [Theory]
    [InlineData(94, DiskHealthLevel.Normal)]
    [InlineData(95, DiskHealthLevel.Warning)]
    public void Wear_is_used_only_when_reliability_probe_was_available(int wear, DiskHealthLevel expected)
    {
        var disk = Disk() with { HealthStatus = "Healthy", ReliabilityCountersAvailable = true, WearPercent = wear };
        var report = StorageHealthAssessment.Analyze(Snapshot(disk), 0);
        Assert.Equal(expected, report.Level);
        var ignored = StorageHealthAssessment.Analyze(Snapshot(disk with { ReliabilityCountersAvailable = false }), 0);
        Assert.Equal(DiskHealthLevel.Normal, ignored.Level);
    }

    [Fact]
    public void Historical_uncorrected_errors_warn_but_total_corrected_errors_do_not()
    {
        var disk = Disk() with { HealthStatus = "Healthy", ReliabilityCountersAvailable = true,
            ReadErrorsTotal = 500, WriteErrorsTotal = 20, ReadErrorsUncorrected = 0, WriteErrorsUncorrected = 0 };
        Assert.Equal(DiskHealthLevel.Normal, StorageHealthAssessment.Analyze(Snapshot(disk), 0).Level);
        var changed = disk with { ReadErrorsUncorrected = 1 };
        Assert.Equal(DiskHealthLevel.Warning, StorageHealthAssessment.Analyze(Snapshot(changed), 0).Level);
    }

    [Fact]
    public void Unhealthy_volume_is_critical_even_with_healthy_disk_flag()
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with { HealthStatus = "Healthy" },
            Volume() with { HealthStatus = "Unhealthy" }), 0);
        Assert.Equal(DiskHealthLevel.Critical, report.Level);
    }

    [Fact]
    public void Almost_full_volume_warns_about_space_not_physical_surface()
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with { HealthStatus = "Healthy" },
            Volume() with { VolumeCapacityBytes = 1_000_000, VolumeAvailableBytes = 30_000 }), 0);
        Assert.Equal(DiskHealthLevel.Warning, report.Level);
        Assert.Contains(report.Checks, c => c.Component == "Volumes e espaço livre" &&
            c.Detail.Contains("Uso de espaço não é falha física"));
    }

    [Fact]
    public void Offline_device_warns_even_if_windows_health_field_is_healthy()
    {
        var report = StorageHealthAssessment.Analyze(Snapshot(Disk() with
        { HealthStatus = "Healthy", IsOffline = true }), 0);
        Assert.Equal(DiskHealthLevel.Warning, report.Level);
    }

    private const string WindowsJson = """
        {
          "Disks": [{
            "Number": 0, "UniqueId": "strong-id", "FriendlyName": "Drive",
            "PartitionStyle": "GPT", "Size": 4000000000,
            "IsBoot": false, "IsSystem": false, "IsOffline": false, "IsReadOnly": false,
            "HealthStatus": "Healthy", "TemperatureC": 45,
            "ReliabilityCountersAvailable": true, "WearPercent": 92,
            "PowerOnHours": 12345, "ReadErrorsTotal": 12,
            "ReadErrorsUncorrected": 0, "WriteErrorsTotal": 4, "WriteErrorsUncorrected": 2
          }],
          "Partitions": []
        }
        """;

    [Fact]
    public void Parser_preserves_supported_optional_counters_and_health_evidence()
    {
        var snapshot = PowerShellDiskInventoryService.ParseInventory(WindowsJson, At);
        var disk = Assert.Single(snapshot.Disks);
        Assert.True(disk.ReliabilityCountersAvailable);
        Assert.Equal(92, disk.WearPercent);
        Assert.Equal(12_345, disk.PowerOnHours);
        Assert.Equal(0, disk.ReadErrorsUncorrected);
        Assert.Equal(2, disk.WriteErrorsUncorrected);
        Assert.Equal(DiskHealthLevel.Warning, StorageHealthAssessment.Analyze(snapshot, 0).Level);
    }

    [Fact]
    public void Missing_optional_metrics_remain_null_without_lying()
    {
        var json = JsonNode.Parse(WindowsJson)!.AsObject();
        var hardware = json["Disks"]!.AsArray()[0]!.AsObject();
        Assert.True(hardware.Remove("WearPercent"));
        Assert.True(hardware.Remove("PowerOnHours"));
        var disk = Assert.Single(PowerShellDiskInventoryService.ParseInventory(json.ToJsonString(), At).Disks);
        Assert.Null(disk.WearPercent);
        Assert.Null(disk.PowerOnHours);
    }

    [Theory]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("\"unsupported\"")]
    public void Out_of_range_or_invalid_optional_wear_is_unknown(string replacement)
    {
        var json = WindowsJson.Replace("\"WearPercent\": 92", "\"WearPercent\": " + replacement, StringComparison.Ordinal);
        var disk = Assert.Single(PowerShellDiskInventoryService.ParseInventory(json, At).Disks);
        Assert.Null(disk.WearPercent);
    }

    [Fact]
    public void Huge_or_negative_provider_counters_become_unknown_not_zero()
    {
        var json = WindowsJson.Replace("\"PowerOnHours\": 12345",
            "\"PowerOnHours\": 18446744073709551615", StringComparison.Ordinal)
            .Replace("\"ReadErrorsTotal\": 12", "\"ReadErrorsTotal\": -1", StringComparison.Ordinal);
        var disk = Assert.Single(PowerShellDiskInventoryService.ParseInventory(json, At).Disks);
        Assert.Null(disk.PowerOnHours);
        Assert.Null(disk.ReadErrorsTotal);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-999")]
    [InlineData("2147483648")]
    [InlineData("\"bad sensor\"")]
    public void Malformed_optional_temperature_does_not_discard_inventory(string value)
    {
        var json = WindowsJson.Replace("\"TemperatureC\": 45",
            "\"TemperatureC\": " + value, StringComparison.Ordinal);
        var disk = Assert.Single(PowerShellDiskInventoryService.ParseInventory(json, At).Disks);
        Assert.Null(disk.TemperatureC);
    }

    [Fact]
    public void Legacy_inventory_without_c13_fields_remains_compatible()
    {
        var json = JsonNode.Parse(WindowsJson)!.AsObject();
        var hardware = json["Disks"]!.AsArray()[0]!.AsObject();
        foreach (var field in new[] { "ReliabilityCountersAvailable", "WearPercent",
            "PowerOnHours", "ReadErrorsTotal", "ReadErrorsUncorrected",
            "WriteErrorsTotal", "WriteErrorsUncorrected" })
            Assert.True(hardware.Remove(field));
        var disk = Assert.Single(PowerShellDiskInventoryService.ParseInventory(json.ToJsonString(), At).Disks);
        Assert.False(disk.ReliabilityCountersAvailable);
        Assert.Null(disk.WearPercent);
    }
}
