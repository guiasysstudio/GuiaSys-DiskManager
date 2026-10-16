using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Tests;

/// <summary>C10 analyzes synthetic inventories only; no command ever touches disks.</summary>
public sealed class DiskConversionAssessmentTests
{
    private const long GiB = 1_073_741_824;
    private static readonly SafetyService Safety = new('X');

    private static DiskInfo Disk(string style = "MBR", int number = 5,
        long size = 12 * GiB, int? sector = 512) => new()
    {
        Number = number, UniqueId = $"synthetic-conversion-{number}", FriendlyName = "Fake VHDX",
        PartitionStyle = style, Size = size, LogicalSectorSize = sector,
        BusType = "Virtual"
    };

    private static PartitionInfo Partition(int number = 1,
        long offset = GiB, long size = GiB, bool protectedPart = false) => new()
    {
        DiskNumber = 5, PartitionNumber = number, Offset = offset, Size = size,
        AccessPaths = "", DriveLetter = number == 1 ? "F" : "",
        Type = "Basic", FileSystem = "NTFS", IsSystem = protectedPart
    };

    private static StorageSnapshot State(DiskInfo disk, params PartitionInfo[] parts) =>
        new([disk], parts, DateTimeOffset.UtcNow);

    [Theory]
    [InlineData(DiskPartitionScheme.Gpt)]
    [InlineData(DiskPartitionScheme.Mbr)]
    public void Raw_empty_disk_is_only_an_initialization_candidate(DiskPartitionScheme desired)
    {
        var report = DiskConversionAssessment.Analyze(State(Disk("RAW")), 5, desired);
        Assert.True(report.GeometryLooksCompatible);
        Assert.True(report.IsEmptyRawInitializationCandidate);
        Assert.False(report.DataPreservingConversionSupported);
        Assert.Equal(0, report.PartitionCount);
        Assert.Contains("RAW VAZIO", report.StatusLabel);
        Assert.Contains("sem escrita", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DiskPartitionScheme.Gpt)]
    [InlineData(DiskPartitionScheme.Mbr)]
    public void Non_raw_disk_with_partitions_is_not_an_initialization_candidate(DiskPartitionScheme target)
    {
        var style = target == DiskPartitionScheme.Gpt ? "MBR" : "GPT";
        var report = DiskConversionAssessment.Analyze(State(Disk(style), Partition()), 5, target);
        Assert.False(report.IsEmptyRawInitializationCandidate);
        Assert.False(report.DataPreservingConversionSupported);
        Assert.Contains("preservação de dados NÃO IMPLEMENTADA", report.Summary);
        Assert.Single(new OperationPlanner(Safety).Plan(State(Disk(style), Partition()),
            [new StorageOperation
            {
                Type = target == DiskPartitionScheme.Gpt ? StorageOperationType.InitializeGpt : StorageOperationType.InitializeMbr,
                DiskNumber = 5, ExpectedDiskIdentity = Disk(style).Identity
            }]).Steps);
        Assert.False(Safety.Evaluate(new StorageOperation
        {
            Type = StorageOperationType.InitializeGpt,
            DiskNumber = 5, ExpectedDiskIdentity = Disk(style).Identity
        }, State(Disk(style), Partition())).Allowed);
    }

    [Fact]
    public void Mbr_classic_capacity_boundary_is_based_on_logical_sectors()
    {
        const long mbr512 = (long)uint.MaxValue * 512 + 512;
        var within = DiskConversionAssessment.Analyze(State(Disk("GPT", size: mbr512)), 5, DiskPartitionScheme.Mbr);
        Assert.DoesNotContain(within.Blockers, b => b.Contains("não endereça"));
        var tooLarge = DiskConversionAssessment.Analyze(State(Disk("GPT", size: mbr512 + 512)), 5, DiskPartitionScheme.Mbr);
        Assert.Contains(tooLarge.Blockers, b => b.Contains("não endereça"));
    }

    [Fact]
    public void Larger_4kn_sectors_have_a_different_addressability_boundary()
    {
        const long boundary4Kn = ((long)uint.MaxValue + 1) * 4096;
        var fits = DiskConversionAssessment.Analyze(
            State(Disk("GPT", size: boundary4Kn, sector: 4096)), 5, DiskPartitionScheme.Mbr);
        Assert.DoesNotContain(fits.Blockers, b => b.Contains("não endereça"));
        var tooBig = DiskConversionAssessment.Analyze(
            State(Disk("GPT", size: boundary4Kn + 4096, sector: 4096)), 5, DiskPartitionScheme.Mbr);
        Assert.Contains(tooBig.Blockers, b => b.Contains("não endereça"));
    }

    [Fact]
    public void More_than_four_partitions_block_classic_mbr_geometry()
    {
        var parts = Enumerable.Range(1, 5).Select(n =>
            Partition(number: n, offset: n * GiB)).ToArray();
        var result = DiskConversionAssessment.Analyze(State(Disk("GPT"), parts),
            5, DiskPartitionScheme.Mbr);
        Assert.False(result.GeometryLooksCompatible);
        Assert.Contains(result.Blockers, x => x.Contains("quatro entradas"));
    }

    [Fact]
    public void At_most_four_partitions_are_not_mistaken_for_safe_conversion()
    {
        var parts = Enumerable.Range(1, 4).Select(n =>
            Partition(number: n, offset: n * GiB)).ToArray();
        var result = DiskConversionAssessment.Analyze(State(Disk("GPT"), parts),
            5, DiskPartitionScheme.Mbr);
        Assert.True(result.GeometryLooksCompatible);
        Assert.False(result.IsEmptyRawInitializationCandidate);
        Assert.False(result.DataPreservingConversionSupported);
        Assert.Contains(result.Warnings, x => x.Contains("GUID"));
    }

    [Fact]
    public void Mbr_to_gpt_warns_about_boot_and_extended_partition_migration()
    {
        var report = DiskConversionAssessment.Analyze(State(Disk(), Partition()),
            5, DiskPartitionScheme.Gpt);
        Assert.True(report.GeometryLooksCompatible);
        Assert.Contains(report.Warnings, x => x.Contains("firmware"));
        Assert.Contains(report.Warnings, x => x.Contains("estendidas"));
        Assert.False(report.DataPreservingConversionSupported);
    }

    [Theory]
    [InlineData("GPT", DiskPartitionScheme.Gpt)]
    [InlineData("MBR", DiskPartitionScheme.Mbr)]
    public void Already_in_target_scheme_is_a_blocked_noop(string style, DiskPartitionScheme desired)
    {
        var report = DiskConversionAssessment.Analyze(State(Disk(style)), 5, desired);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("já utiliza"));
    }

    [Fact]
    public void Raw_disk_claiming_partitions_is_inconsistent_and_blocked()
    {
        var report = DiskConversionAssessment.Analyze(State(Disk("RAW"), Partition()),
            5, DiskPartitionScheme.Gpt);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("RAW com partições"));
    }

    [Fact]
    public void Protected_partitions_and_system_disks_are_blocked()
    {
        var report = DiskConversionAssessment.Analyze(
            State(Disk(), Partition(protectedPart: true)), 5, DiskPartitionScheme.Gpt);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("protegida"));

        var zero = Disk("RAW", number: 0);
        var zeroReport = DiskConversionAssessment.Analyze(State(zero), 0, DiskPartitionScheme.Gpt);
        Assert.False(zeroReport.IsEmptyRawInitializationCandidate);
        Assert.Contains(zeroReport.Blockers, x => x.Contains("Disco 0"));
    }

    [Fact]
    public void Missing_unique_id_offline_and_readonly_each_preclude_candidate()
    {
        var withoutUid = Disk("RAW") with { UniqueId = "" };
        Assert.False(DiskConversionAssessment.Analyze(State(withoutUid), 5, DiskPartitionScheme.Gpt)
            .GeometryLooksCompatible);
        var offline = Disk("RAW") with { IsOffline = true };
        Assert.False(DiskConversionAssessment.Analyze(State(offline), 5, DiskPartitionScheme.Gpt)
            .GeometryLooksCompatible);
        var ro = Disk("RAW") with { IsReadOnly = true };
        Assert.False(DiskConversionAssessment.Analyze(State(ro), 5, DiskPartitionScheme.Gpt)
            .GeometryLooksCompatible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(520)]
    [InlineData(131_072)]
    public void Invalid_or_unknown_sector_fails_closed(int? logicalSector)
    {
        var report = DiskConversionAssessment.Analyze(State(Disk("RAW", sector: logicalSector)),
            5, DiskPartitionScheme.Mbr);
        Assert.False(report.GeometryLooksCompatible);
        Assert.Contains(report.Blockers, x => x.Contains("Setor lógico"));
    }

    [Fact]
    public void Sector_misaligned_disk_size_and_partition_extents_are_blocked()
    {
        var disk = Disk("RAW", size: 12 * GiB + 1);
        var report = DiskConversionAssessment.Analyze(State(disk), 5, DiskPartitionScheme.Gpt);
        Assert.False(report.GeometryLooksCompatible);
        var p = Partition(size: GiB + 1);
        var gptToMbr = DiskConversionAssessment.Analyze(State(Disk("GPT"), p), 5, DiskPartitionScheme.Mbr);
        Assert.False(gptToMbr.GeometryLooksCompatible);
        Assert.Contains(gptToMbr.Blockers, x => x.Contains("alinhamento"));
    }

    [Fact]
    public void Invalid_snapshot_geometry_and_missing_disk_are_never_reported_as_compatible()
    {
        var part = Partition();
        var overlapping = Partition(number: 2, offset: GiB, size: GiB);
        var bad = DiskConversionAssessment.Analyze(State(Disk(), part, overlapping),
            5, DiskPartitionScheme.Gpt);
        Assert.False(bad.GeometryLooksCompatible);
        Assert.Contains(bad.Blockers, x => x.Contains("sobreposição"));
        var missing = DiskConversionAssessment.Analyze(State(Disk()), 123, DiskPartitionScheme.Gpt);
        Assert.False(missing.GeometryLooksCompatible);
    }

    [Fact]
    public void Unknown_styles_and_unsupported_target_are_rejected()
    {
        var unknown = DiskConversionAssessment.Analyze(State(Disk("SUSPICIOUS")), 5,
            DiskPartitionScheme.Gpt);
        Assert.False(unknown.GeometryLooksCompatible);
        var badTarget = DiskConversionAssessment.Analyze(State(Disk()), 5,
            (DiskPartitionScheme)99);
        Assert.False(badTarget.GeometryLooksCompatible);
    }

    [Fact]
    public void Conversion_inspection_never_authorizes_initialize_on_populated_gpt_or_mbr()
    {
        var p = Partition();
        foreach (var style in new[] { "GPT", "MBR" })
        {
            var snapshot = State(Disk(style), p);
            foreach (var kind in new[] { StorageOperationType.InitializeGpt, StorageOperationType.InitializeMbr })
            {
                var operation = new StorageOperation
                {
                    Type = kind, DiskNumber = 5, ExpectedDiskIdentity = Disk(style).Identity
                };
                Assert.False(Safety.Evaluate(operation, snapshot).Allowed);
            }
        }
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
    }
}
