using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;

namespace GuiaSys.DiskManager.Tests;

public sealed class MaintenanceAssessmentTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
    private const string Basic = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";

    private static DiskInfo Disk(int number = 4) => new()
    {
        Number = number, UniqueId = $"strong-disk-{number}", FriendlyName = "Synthetic SSD",
        PartitionStyle = "GPT", Size = 4_000_000_000, MediaType = "SSD",
        LogicalSectorSize = 512, HealthStatus = "Healthy"
    };
    private static PartitionInfo Part(int diskNumber = 4) => new()
    {
        DiskNumber = diskNumber, PartitionNumber = 1, PartitionGuid = "67174eda-aa2e-402d-8a7c-c640a561701c",
        GptType = Basic, Type = "Basic", Offset = 1_048_576, Size = 2_000_000_000,
        FileSystem = "NTFS", AccessPaths = "", DriveLetter = "E",
        VolumeCapacityBytes = 1_900_000_000, VolumeAvailableBytes = 100_000_000
    };
    private static StorageSnapshot Snapshot(DiskInfo? disk = null, PartitionInfo? part = null) =>
        new([disk ?? Disk()], [part ?? Part()], At);

    [Theory]
    [InlineData(MaintenanceTask.InspectVolume, MaintenanceRisk.MetadataOnly)]
    [InlineData(MaintenanceTask.OptimizeVolume, MaintenanceRisk.ChangesData)]
    [InlineData(MaintenanceTask.ReviewTemporaryFiles, MaintenanceRisk.ChangesData)]
    [InlineData(MaintenanceTask.WipeVolumeFreeSpace, MaintenanceRisk.Destructive)]
    public void Every_volume_maintenance_mode_is_non_executable(MaintenanceTask task, MaintenanceRisk expectedRisk)
    {
        var result = MaintenanceAssessment.Analyze(Snapshot(), 4, 1, task);
        Assert.Equal(expectedRisk, result.Risk);
        Assert.False(result.CanExecute);
        Assert.Contains("EXECUÇÃO BLOQUEADA", result.Summary);
        Assert.NotEmpty(result.Checks);
    }

    [Fact]
    public void Erase_disk_is_non_executable_even_when_empty()
    {
        var result = MaintenanceAssessment.Analyze(new StorageSnapshot([Disk()], [], At), 4, null,
            MaintenanceTask.EraseEntireDisk);
        Assert.False(result.CanExecute);
        Assert.Equal(MaintenanceRisk.Destructive, result.Risk);
        Assert.Contains(result.Checks, c => c.Component == "Sanitização confiável" &&
            c.Status == "Não verificada");
    }

    [Fact]
    public void Erase_disk_zero_with_boot_is_explicitly_blocked()
    {
        var boot = Part(0) with { IsBoot = true, DriveLetter = "C" };
        var result = MaintenanceAssessment.Analyze(Snapshot(Disk(0) with { IsBoot = true }, boot),
            0, null, MaintenanceTask.EraseEntireDisk);
        Assert.NotEmpty(result.Blockers);
        Assert.Contains(result.Blockers, x => x.Contains("Disco 0"));
        Assert.False(result.CanExecute);
    }

    [Fact]
    public void Disk_erase_rejects_specific_partition_selection()
    {
        var result = MaintenanceAssessment.Analyze(Snapshot(), 4, 1, MaintenanceTask.EraseEntireDisk);
        Assert.Contains(result.Blockers, b => b.Contains("disco, não uma partição"));
    }

    [Theory]
    [InlineData(MaintenanceTask.InspectVolume)]
    [InlineData(MaintenanceTask.OptimizeVolume)]
    [InlineData(MaintenanceTask.ReviewTemporaryFiles)]
    [InlineData(MaintenanceTask.WipeVolumeFreeSpace)]
    public void Volume_requests_require_a_selected_partition(MaintenanceTask kind)
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(), 4, null, kind);
        Assert.False(report.CanExecute);
        Assert.Contains(report.Blockers, b => b.Contains("Selecione uma partição"));
    }

    [Fact]
    public void Missing_partition_or_disk_is_not_retargeted()
    {
        var missingDisk = MaintenanceAssessment.Analyze(Snapshot(), 9, 1, MaintenanceTask.InspectVolume);
        var missingPart = MaintenanceAssessment.Analyze(Snapshot(), 4, 99, MaintenanceTask.InspectVolume);
        Assert.False(missingDisk.CanExecute);
        Assert.False(missingPart.CanExecute);
        Assert.NotEmpty(missingDisk.Blockers);
        Assert.NotEmpty(missingPart.Blockers);
    }

    [Fact]
    public void Disk_zero_can_only_inspect_current_metadata_without_disk_writes()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(Disk(0) with { IsBoot = true },
            Part(0) with { IsBoot = true, DriveLetter = "C" }), 0, 1, MaintenanceTask.InspectVolume);
        Assert.False(report.CanExecute);
        Assert.Contains(report.Checks, c => c.Component == "Verificação de integridade" && c.Status == "Não executada");
        Assert.Contains(report.Warnings, x => x.Contains("Volume protegido"));
    }

    [Fact]
    public void No_optimization_without_supported_ntfs_and_media_evidence()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(Disk() with { MediaType = "Unspecified" },
            Part() with { FileSystem = "exFAT" }), 4, 1, MaintenanceTask.OptimizeVolume);
        Assert.False(report.CanExecute);
        Assert.Contains(report.Blockers, x => x.Contains("NTFS"));
        Assert.Contains(report.Warnings, x => x.Contains("Tecnologia física"));
        Assert.Contains(report.Checks, c => c.Detail.Contains("não escolher TRIM"));
    }

    [Fact]
    public void Filesystem_inspection_does_not_claim_to_have_run_chkdsk()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(), 4, 1, MaintenanceTask.InspectVolume);
        Assert.True(report.IsMetadataPlausible);
        Assert.False(report.CanExecute);
        Assert.Contains(report.Checks, c => c.Detail.Contains("Nenhum CHKDSK"));
    }

    [Fact]
    public void Cleanup_does_not_fabricate_space_savings()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(), 4, 1, MaintenanceTask.ReviewTemporaryFiles);
        Assert.Contains(report.Checks, c => c.Component == "Economia estimada" &&
            c.Status == "Desconhecida");
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void High_volume_usage_only_adds_capacity_warning()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(part: Part() with
            { VolumeCapacityBytes = 1_000_000, VolumeAvailableBytes = 30_000 }),
            4, 1, MaintenanceTask.InspectVolume);
        Assert.Contains(report.Checks, c => c.Component == "Ocupação" && c.Status == "Atenção");
        Assert.Contains(report.Warnings, w => w.Contains("95%"));
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Unknown_usage_does_not_become_zero()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(part: Part() with
            { VolumeCapacityBytes = null, VolumeAvailableBytes = null }),
            4, 1, MaintenanceTask.ReviewTemporaryFiles);
        Assert.Contains(report.Checks, c => c.Component == "Ocupação" && c.Status == "Não disponível");
    }

    [Fact]
    public void Invalid_geometry_is_blocked_before_any_maintenance()
    {
        var invalid = Snapshot(part: Part() with { Offset = -2 });
        var report = MaintenanceAssessment.Analyze(invalid, 4, 1, MaintenanceTask.WipeVolumeFreeSpace);
        Assert.Contains(report.Blockers, x => x.Contains("Geometria/identidades"));
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Unknown_filesystem_or_weak_identity_never_gets_approval()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(Disk() with { UniqueId = "" },
            Part() with { PartitionGuid = "", FileSystem = "RAW" }), 4, 1, MaintenanceTask.OptimizeVolume);
        Assert.True(report.Blockers.Count >= 3);
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Read_only_volume_is_allowed_for_metadata_inspection_but_not_optimization()
    {
        var snapshot = Snapshot(Disk() with { IsReadOnly = true }, Part() with { IsReadOnly = true });
        var inspect = MaintenanceAssessment.Analyze(snapshot, 4, 1, MaintenanceTask.InspectVolume);
        var optimize = MaintenanceAssessment.Analyze(snapshot, 4, 1, MaintenanceTask.OptimizeVolume);
        Assert.True(inspect.IsMetadataPlausible);
        Assert.False(optimize.IsMetadataPlausible);
        Assert.False(inspect.CanExecute);
    }

    [Fact]
    public void Safe_preview_is_deterministic_and_does_not_modify_snapshot()
    {
        var snapshot = Snapshot();
        var first = MaintenanceAssessment.Analyze(snapshot, 4, 1, MaintenanceTask.InspectVolume);
        var second = MaintenanceAssessment.Analyze(snapshot, 4, 1, MaintenanceTask.InspectVolume);
        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.Checks, second.Checks);
        Assert.Equal(At, snapshot.CapturedAt);
        Assert.Equal("NTFS", snapshot.Partitions[0].FileSystem);
    }

    [Fact]
    public void Unknown_enum_fails_closed()
    {
        var report = MaintenanceAssessment.Analyze(Snapshot(), 4, 1, (MaintenanceTask)999);
        Assert.False(report.CanExecute);
        Assert.NotEmpty(report.Blockers);
    }
}
