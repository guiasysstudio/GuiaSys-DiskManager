using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;

namespace GuiaSys.DiskManager.Tests;

public sealed class WindowsMigrationAssessmentTests
{
    private const long MiB = 1_048_576;
    private const long GiB = 1_073_741_824;
    private const string Efi = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    private const string Msr = "e3c9e316-0b5c-4db8-817d-f92df00215ae";
    private const string Basic = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    private const string WinRe = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";

    private static DiskInfo Disk(int number, string style = "GPT", long size = 40 * GiB, int? sector = 512) =>
        new()
        {
            Number = number, UniqueId = $"C12-TEST-{number}", FriendlyName = "Synthetic disk",
            Size = size, PartitionStyle = style, LogicalSectorSize = sector,
            PhysicalSectorSize = 4096, IsBoot = number == 0, IsSystem = number == 0
        };

    private static PartitionInfo Part(int disk, int number, long offset, long size,
        string type, string filesystem = "", bool boot = false, bool system = false) => new()
    {
        DiskNumber = disk, PartitionNumber = number, Offset = offset, Size = size,
        PartitionGuid = $"ea06a2af-d0ba-434e-8ad1-1f492220000{number}",
        GptType = type, Type = "Basic", FileSystem = filesystem,
        DriveLetter = boot ? "C" : "", AccessPaths = "",
        IsBoot = boot, IsSystem = system
    };

    private static StorageSnapshot Valid(bool recovery = true) =>
        new([Disk(0), Disk(5, "RAW", 50 * GiB) with { IsBoot = false, IsSystem = false }],
            recovery
                ? [Part(0, 1, MiB, 260 * MiB, Efi, "FAT32", system: true),
                   Part(0, 2, 261 * MiB, 16 * MiB, Msr),
                   Part(0, 3, 277 * MiB, 30 * GiB, Basic, "NTFS", boot: true),
                   Part(0, 4, 31 * GiB, 800 * MiB, WinRe, "NTFS")]
                : [Part(0, 1, MiB, 260 * MiB, Efi, "FAT32", system: true),
                   Part(0, 2, 261 * MiB, 16 * MiB, Msr),
                   Part(0, 3, 277 * MiB, 30 * GiB, Basic, "NTFS", boot: true)],
            DateTimeOffset.UtcNow);

    private static WindowsMigrationReport Analyze(StorageSnapshot s) =>
        WindowsMigrationAssessment.Analyze(s, 0, 5);

    [Fact]
    public void Running_windows_on_disk_zero_is_safe_to_inspect_but_not_migrate()
    {
        var state = Valid();
        var result = Analyze(state);
        Assert.True(result.TopologyLooksPlausible);
        Assert.False(result.CanExecute);
        Assert.Equal(7, result.Checks.Count);
        Assert.Equal(state.CapturedAt, result.CapturedAt);
        Assert.Contains("SEM MIGRAÇÃO EXECUTÁVEL", result.Summary);
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
        Assert.DoesNotContain("MigrateWindows", Enum.GetNames<StorageOperationType>());
    }

    [Theory]
    [InlineData("Partição Windows", WindowsMigrationEvidence.Present)]
    [InlineData("Partição EFI (ESP)", WindowsMigrationEvidence.Present)]
    [InlineData("Microsoft Reserved (MSR)", WindowsMigrationEvidence.Present)]
    [InlineData("Partição Recovery / WinRE", WindowsMigrationEvidence.Present)]
    [InlineData("BitLocker / criptografia", WindowsMigrationEvidence.Unknown)]
    [InlineData("BCD / firmware UEFI / Secure Boot", WindowsMigrationEvidence.Unknown)]
    [InlineData("Consistência dos arquivos / VSS", WindowsMigrationEvidence.Unknown)]
    public void Every_boot_encryption_or_snapshot_check_is_explicit(string name, WindowsMigrationEvidence evidence)
    {
        var check = Assert.Single(Analyze(Valid()).Checks.Where(c => c.Component == name));
        Assert.Equal(evidence, check.Evidence);
    }

    [Fact]
    public void Missing_recovery_is_warning_never_proof_of_winre()
    {
        var report = Analyze(Valid(recovery: false));
        Assert.True(report.TopologyLooksPlausible);
        Assert.Equal(WindowsMigrationEvidence.Unknown,
            report.Checks.Single(c => c.Component.Contains("Recovery")).Evidence);
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Missing_os_boot_signal_fails_closed()
    {
        var s = Valid();
        var altered = s.Partitions.Select(p => p.IsBoot ? p with { IsBoot = false } : p).ToArray();
        Assert.Contains(Analyze(s with { Partitions = altered }).Blockers,
            b => b.Contains("IsBoot"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NTFS")]
    [InlineData("exFAT")]
    public void Efi_must_have_known_fat32_filesystem(string filesystem)
    {
        var s = Valid();
        var altered = s.Partitions.Select(p => p.GptType == Efi
            ? p with { FileSystem = filesystem } : p).ToArray();
        var report = Analyze(s with { Partitions = altered });
        Assert.False(report.TopologyLooksPlausible);
        Assert.Contains(report.Blockers, b => b.Contains("FAT32"));
    }

    [Fact]
    public void Absent_or_ambiguous_msr_cannot_be_ignored()
    {
        var s = Valid();
        var removed = s.Partitions.Where(p => p.GptType != Msr).ToArray();
        Assert.False(Analyze(s with { Partitions = removed }).TopologyLooksPlausible);
        var duplicate = s.Partitions.Select(p => p.GptType == WinRe ? p with { GptType = Msr } : p).ToArray();
        Assert.False(Analyze(s with { Partitions = duplicate }).TopologyLooksPlausible);
    }

    [Fact]
    public void Windows_ntfs_and_basic_gpt_type_are_both_required()
    {
        var s = Valid();
        var wrongFs = s.Partitions.Select(p => p.IsBoot ? p with { FileSystem = "FAT32" } : p).ToArray();
        var wrongGuid = s.Partitions.Select(p => p.IsBoot ? p with { GptType = Efi } : p).ToArray();
        Assert.False(Analyze(s with { Partitions = wrongFs }).TopologyLooksPlausible);
        Assert.False(Analyze(s with { Partitions = wrongGuid }).TopologyLooksPlausible);
    }

    [Fact]
    public void Weak_partition_guid_and_an_external_boot_disk_are_blocked()
    {
        var s = Valid();
        var bad = s.Partitions.Select(p => p.IsBoot ? p with { PartitionGuid = "" } : p).ToArray();
        Assert.False(Analyze(s with { Partitions = bad }).TopologyLooksPlausible);
        var otherSystem = Part(7, 1, MiB, 260 * MiB, Efi, "FAT32", system: true);
        var spanning = s with
        {
            Disks = [.. s.Disks, Disk(7) with { IsSystem = true }],
            Partitions = [.. s.Partitions, otherSystem]
        };
        Assert.Contains(Analyze(spanning).Blockers, b => b.Contains("outro disco"));
    }

    [Fact]
    public void Legacy_mbr_has_no_claimed_uefi_boot_validation()
    {
        var s = Valid();
        var src = s.Disks[0] with { PartitionStyle = "MBR" };
        var r = Analyze(s with { Disks = [src, s.Disks[1]] });
        Assert.False(r.TopologyLooksPlausible);
        Assert.Contains(r.Blockers, b => b.Contains("Legacy/MBR"));
    }

    [Theory]
    [InlineData("GPT")]
    [InlineData("MBR")]
    public void Existing_destination_partition_never_gets_overwritten(string style)
    {
        var s = Valid();
        var target = s.Disks[1] with { PartitionStyle = style };
        var existing = Part(5, 1, MiB, GiB, Basic, "NTFS");
        var r = Analyze(s with { Disks = [s.Disks[0], target], Partitions = [.. s.Partitions, existing] });
        Assert.False(r.TopologyLooksPlausible);
        Assert.Contains(r.Blockers, b => b.Contains("RAW e sem partições"));
    }

    [Fact]
    public void Destination_smaller_than_entire_source_is_not_validated_by_free_ntfs_space()
    {
        var s = Valid();
        var dest = s.Disks[1] with { Size = 32 * GiB };
        Assert.Contains(Analyze(s with { Disks = [s.Disks[0], dest] }).Blockers,
            b => b.Contains("menor"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(131072)]
    public void Invalid_sector_sizes_never_approve_a_migration_layout(int? sector)
    {
        var s = Valid();
        Assert.False(Analyze(s with { Disks = [s.Disks[0] with { LogicalSectorSize = sector }, s.Disks[1]] })
            .TopologyLooksPlausible);
    }

    [Fact]
    public void Mismatched_logical_sectors_and_readonly_target_are_blocked()
    {
        var s = Valid();
        var sectors = s.Disks[1] with { LogicalSectorSize = 4096 };
        var readonlyTarget = s.Disks[1] with { IsReadOnly = true };
        Assert.False(Analyze(s with { Disks = [s.Disks[0], sectors] }).TopologyLooksPlausible);
        Assert.False(Analyze(s with { Disks = [s.Disks[0], readonlyTarget] }).TopologyLooksPlausible);
        Assert.True(Analyze(s with { Disks = [s.Disks[0] with { IsReadOnly = true }, s.Disks[1]] })
            .TopologyLooksPlausible);
    }

    [Fact]
    public void Same_source_and_target_disk_zero_target_or_duplicate_id_is_refused()
    {
        var s = Valid();
        Assert.False(WindowsMigrationAssessment.Analyze(s, 0, 0).TopologyLooksPlausible);
        Assert.False(WindowsMigrationAssessment.Analyze(s, 0, 404).TopologyLooksPlausible);
        var sameId = s.Disks[1] with { UniqueId = s.Disks[0].UniqueId };
        Assert.False(Analyze(s with { Disks = [s.Disks[0], sameId] }).TopologyLooksPlausible);
        var reversed = new StorageSnapshot([s.Disks[1], s.Disks[0]], s.Partitions, s.CapturedAt);
        Assert.Contains(WindowsMigrationAssessment.Analyze(reversed, 5, 0).Blockers,
            b => b.Contains("Disco 0"));
    }

    [Fact]
    public void Corrupt_overlapping_geometry_is_blocked()
    {
        var s = Valid();
        var overlap = s.Partitions.Select(p => p.GptType == WinRe
            ? p with { Offset = 300 * MiB } : p).ToArray();
        Assert.False(Analyze(s with { Partitions = overlap }).TopologyLooksPlausible);
    }
}
