using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;

namespace GuiaSys.DiskManager.Tests;

public sealed class ClonePreviewAssessmentTests
{
    private const long MiB = 1_048_576;
    private const long GiB = 1_073_741_824;

    private static DiskInfo Disk(int n, string style, long bytes = 8 * GiB,
        int? logicalSector = 512, int? physicalSector = 4096) => new()
    {
        Number = n, UniqueId = $"C11-TEST-DISK-{n}", FriendlyName = $"Synthetic Disk {n}",
        Size = bytes, PartitionStyle = style, LogicalSectorSize = logicalSector,
        PhysicalSectorSize = physicalSector
    };

    private static PartitionInfo Part(int disk = 2, int n = 1,
        long offset = GiB, long length = 2 * GiB,
        string fs = "NTFS", string gptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7") =>
        new()
        {
            DiskNumber = disk, PartitionNumber = n, Offset = offset, Size = length,
            PartitionGuid = n == 1 ? "69a22e8a-43f1-4c34-aabc-54112513b001" :
                "69a22e8a-43f1-4c34-aabc-54112513b002",
            GptType = gptType, Type = "Basic", FileSystem = fs, DriveLetter = "F",
            AccessPaths = ""
        };

    private static StorageSnapshot State(DiskInfo source, DiskInfo target,
        params PartitionInfo[] parts) => new([source, target], parts, DateTimeOffset.UtcNow);

    [Fact]
    public void Entire_gpt_disk_clone_raw_target_reports_all_sectors_not_only_used_bytes()
    {
        var source = Disk(2, "GPT");
        var destination = Disk(5, "RAW", 10 * GiB);
        var original = Part() with { VolumeCapacityBytes = GiB, VolumeAvailableBytes = GiB / 2 };
        var snapshot = State(source, destination, original);
        var result = ClonePreviewAssessment.Analyze(snapshot, 2, 5);
        Assert.True(result.GeometryLooksCompatible);
        Assert.Equal(ClonePreviewScope.Disk, result.Scope);
        Assert.Equal(8 * GiB, result.BytesToRead);
        Assert.Null(result.ProposedTargetOffsetBytes);
        Assert.Equal(snapshot.CapturedAt, result.CapturedAt);
        Assert.Equal(source.Identity, result.SourceIdentity);
        Assert.Equal(destination.Identity, result.TargetIdentity);
        Assert.False(result.CanExecute);
        Assert.Contains("SEM CÓPIA", result.Summary);
        Assert.Single(snapshot.Partitions);
    }

    [Fact]
    public void Mbr_source_is_only_a_geometry_study_even_if_raw_target_can_hold_all_bytes()
    {
        var source = Disk(2, "MBR");
        var target = Disk(5, "RAW");
        var result = ClonePreviewAssessment.Analyze(State(source, target, Part()), 2, 5);
        Assert.True(result.GeometryLooksCompatible);
        Assert.False(result.CanExecute);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2_147_483_648L)]
    [InlineData(8_589_934_591L)]
    public void Disk_clone_blocks_destination_smaller_than_source(long targetSize)
    {
        var state = State(Disk(2, "GPT"), Disk(5, "RAW", targetSize), Part());
        var report = ClonePreviewAssessment.Analyze(state, 2, 5);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, b => b.Contains("menor"));
    }

    [Fact]
    public void Equal_size_destination_is_acceptable_geometrically_but_never_executable()
    {
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "RAW"), Part()), 2, 5);
        Assert.True(report.GeometryLooksCompatible);
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Nonempty_target_and_gpt_initialized_target_are_not_valid_for_disk_level_clone()
    {
        var source = Disk(2, "GPT");
        Assert.False(ClonePreviewAssessment.Analyze(State(source, Disk(5, "GPT"), Part()), 2, 5)
            .GeometryLooksCompatible);
        Assert.False(ClonePreviewAssessment.Analyze(State(source, Disk(5, "RAW"), Part(), Part(5)), 2, 5)
            .GeometryLooksCompatible);
    }

    [Fact]
    public void Same_disk_and_cloned_unique_ids_are_rejected()
    {
        var src = Disk(2, "GPT");
        var dst = Disk(5, "RAW") with { UniqueId = src.UniqueId };
        Assert.False(ClonePreviewAssessment.Analyze(State(src, dst, Part()), 2, 5)
            .GeometryLooksCompatible);
        Assert.False(ClonePreviewAssessment.Analyze(State(src, Disk(5, "RAW"), Part()), 2, 2)
            .GeometryLooksCompatible);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(2, 0)]
    public void Disk_zero_is_never_accepted_as_clone_source_or_target(int from, int to)
    {
        var source = Disk(from, "GPT");
        var target = Disk(to, "RAW");
        var report = ClonePreviewAssessment.Analyze(State(source, target,
            Part(disk: from)), from, to);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("Disco 0"));
    }

    [Theory]
    [InlineData(512, 4096)]
    [InlineData(4096, 512)]
    public void Incompatible_logical_sector_sizes_are_not_assumed_byte_equivalent(int from, int to)
    {
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT", logicalSector: from),
                Disk(5, "RAW", logicalSector: to), Part()), 2, 5);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("Setores lógicos diferentes"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(131072)]
    public void Unknown_or_invalid_logical_sector_sizes_fail_closed(int? size)
    {
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT", logicalSector: size),
                Disk(5, "RAW"), Part()), 2, 5);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("Setores lógicos desconhecidos"));
    }

    [Fact]
    public void Physical_sector_mismatch_is_explicit_risk_warning()
    {
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "RAW", physicalSector: 512), Part()), 2, 5);
        Assert.Contains(report.Warnings, x => x.Contains("Setores físicos diferentes"));
        Assert.False(report.CanExecute);
    }

    [Fact]
    public void Source_with_protected_system_recovery_partition_is_denied()
    {
        var protectedSource = Part() with { GptType = "de94bba4-06d1-4d40-a16a-bfd50179d6ac" };
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "RAW"), protectedSource), 2, 5);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("EFI/MSR/Recovery"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Offline_source_and_readonly_target_are_blocked(bool offline, bool targetReadOnly)
    {
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT") with { IsOffline = offline },
                Disk(5, "RAW") with { IsReadOnly = targetReadOnly }, Part()), 2, 5);
        Assert.False(report.GeometryLooksCompatible);
    }

    [Fact]
    public void Partition_clone_finds_first_1mib_aligned_free_extent_in_target_gpt()
    {
        var result = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "GPT"), Part()), 2, 5, 1);
        Assert.True(result.GeometryLooksCompatible);
        Assert.Equal(ClonePreviewScope.Partition, result.Scope);
        Assert.Equal(MiB, result.ProposedTargetOffsetBytes);
        Assert.Equal(2 * GiB, result.BytesToRead);
        Assert.False(result.CanExecute);
        Assert.Contains("SEM CÓPIA", result.Summary);
    }

    [Fact]
    public void Partition_clone_chooses_first_sufficient_unallocated_extent_not_the_largest_display_segment()
    {
        var targetPart = Part(5, offset: 4 * GiB, length: 1 * GiB);
        var sourcePart = Part();
        var result = ClonePreviewAssessment.Analyze(State(Disk(2, "GPT"),
            Disk(5, "GPT"), sourcePart, targetPart), 2, 5, 1);
        Assert.True(result.GeometryLooksCompatible);
        Assert.Equal(MiB, result.ProposedTargetOffsetBytes);
    }

    [Fact]
    public void Partition_clone_moves_to_later_available_extent_if_early_space_is_too_small()
    {
        var sourcePart = Part(length: 3 * GiB);
        var targetPart = Part(5, offset: MiB, length: 2 * GiB);
        var result = ClonePreviewAssessment.Analyze(State(Disk(2, "GPT"),
            Disk(5, "GPT"), sourcePart, targetPart), 2, 5, 1);
        Assert.True(result.GeometryLooksCompatible);
        Assert.Equal(2 * GiB + MiB, result.ProposedTargetOffsetBytes);
    }

    [Fact]
    public void Partition_clone_rejects_destination_without_sufficient_single_contiguous_extent()
    {
        var sourcePart = Part(length: 6 * GiB);
        var targetPart = Part(5, offset: 3 * GiB, length: 1 * GiB);
        var result = ClonePreviewAssessment.Analyze(State(Disk(2, "GPT"),
            Disk(5, "GPT"), sourcePart, targetPart), 2, 5, 1);
        Assert.False(result.GeometryLooksCompatible);
        Assert.Contains(result.Blockers, x => x.Contains("faixa não alocada contígua"));
    }

    [Fact]
    public void Partition_clone_requires_initialized_gpt_target_and_basic_gpt_source()
    {
        var part = Part();
        Assert.False(ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "RAW"), part), 2, 5, 1)
            .GeometryLooksCompatible);
        Assert.False(ClonePreviewAssessment.Analyze(
            State(Disk(2, "MBR"), Disk(5, "GPT"), part), 2, 5, 1)
            .GeometryLooksCompatible);
        Assert.False(ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "GPT"), part with { PartitionGuid = "" }), 2, 5, 1)
            .GeometryLooksCompatible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RAW")]
    [InlineData("XFS")]
    public void Unknown_partition_filesystem_not_silently_accepted(string fs)
    {
        var report = ClonePreviewAssessment.Analyze(State(Disk(2, "GPT"),
            Disk(5, "GPT"), Part(fs: fs)), 2, 5, 1);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("Sistema de arquivos"));
    }

    [Fact]
    public void Missing_partition_and_unknown_source_disk_are_not_approved()
    {
        var state = State(Disk(2, "GPT"), Disk(5, "GPT"), Part());
        Assert.False(ClonePreviewAssessment.Analyze(state, 2, 5, 404).GeometryLooksCompatible);
        Assert.False(ClonePreviewAssessment.Analyze(state, 900, 5).GeometryLooksCompatible);
    }

    [Fact]
    public void Protected_target_partition_prevents_any_candidate_offset()
    {
        var source = Part();
        var recovery = Part(5, 2, 5 * GiB, MiB,
            gptType: "de94bba4-06d1-4d40-a16a-bfd50179d6ac");
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "GPT"), source, recovery), 2, 5, 1);
        Assert.False(report.GeometryLooksCompatible);
    }

    [Fact]
    public void Overlapping_or_ambiguous_geometry_is_a_hard_failure()
    {
        var source = Part();
        var overlapping = Part(n: 2, offset: 2 * GiB, length: 3 * GiB);
        var report = ClonePreviewAssessment.Analyze(
            State(Disk(2, "GPT"), Disk(5, "RAW"), source, overlapping), 2, 5);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("sobrepostas"));
    }

    [Fact]
    public void Clone_report_never_enables_write_gate_or_an_executable_operation()
    {
        var source = Disk(2, "GPT");
        var target = Disk(5, "RAW");
        var report = ClonePreviewAssessment.Analyze(State(source, target, Part()), 2, 5);
        Assert.True(report.GeometryLooksCompatible);
        Assert.False(report.CanExecute);
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
        Assert.DoesNotContain("CloneDisk", Enum.GetNames<StorageOperationType>());
        Assert.DoesNotContain("ClonePartition", Enum.GetNames<StorageOperationType>());
    }
}
