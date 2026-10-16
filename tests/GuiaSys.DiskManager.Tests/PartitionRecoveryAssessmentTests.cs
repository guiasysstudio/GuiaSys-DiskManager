using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;

namespace GuiaSys.DiskManager.Tests;

public sealed class PartitionRecoveryAssessmentTests
{
    private static readonly DateTimeOffset Before = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset After = Before.AddMinutes(1);
    private const string Basic = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    private const string Efi = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    private static DiskInfo Disk(int number = 3) => new()
    {
        Number = number, UniqueId = "disk-identity", FriendlyName = "Test VHD",
        Size = 8_000_000_000, LogicalSectorSize = 512, PartitionStyle = "GPT"
    };
    private static PartitionInfo Part(int number = 3) => new()
    {
        DiskNumber = number, PartitionNumber = 1,
        PartitionGuid = "a377819c-9fd5-4dd7-a6b6-ea9d7dd255c4", GptType = Basic,
        Type = "Basic", Offset = 1_048_576, Size = 2_000_000_000, FileSystem = "NTFS",
        DriveLetter = "E", AccessPaths = ""
    };
    private static StorageSnapshot Snap(DateTimeOffset when, DiskInfo? disk = null,
        params PartitionInfo[] parts) => new([disk ?? Disk()], parts, when);

    [Fact]
    public void Baseline_requirements_reject_mbr_weak_id_and_missing_sector()
    {
        Assert.Throws<InvalidDataException>(() => PartitionRecoveryAssessment.Capture(
            Snap(Before, Disk() with { UniqueId = "" }, Part()), 3));
        Assert.Throws<InvalidDataException>(() => PartitionRecoveryAssessment.Capture(
            Snap(Before, Disk() with { PartitionStyle = "MBR" }, Part()), 3));
        Assert.Throws<InvalidDataException>(() => PartitionRecoveryAssessment.Capture(
            Snap(Before, Disk() with { LogicalSectorSize = null }, Part()), 3));
    }

    [Fact]
    public void Baseline_refuses_bad_alignment_and_duplicate_guids()
    {
        Assert.Throws<InvalidDataException>(() => PartitionRecoveryAssessment.Capture(
            Snap(Before, parts: [Part() with { Offset = 100 }]), 3));
        Assert.Throws<InvalidDataException>(() => PartitionRecoveryAssessment.Capture(
            Snap(Before, parts: [Part(), Part() with { PartitionNumber = 2,
                Offset = 4_000_000_000 }]), 3));
    }

    [Fact]
    public void Unchanged_table_entry_is_present_not_missing()
    {
        var reference = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var report = PartitionRecoveryAssessment.Compare(reference, Snap(After, parts: [Part()]));
        Assert.True(report.Comparable);
        Assert.Equal(RecoveryEntryState.Present, Assert.Single(report.Entries).State);
        Assert.Equal(0, report.MissingCount);
        Assert.False(report.CanRecover);
    }

    [Fact]
    public void Missing_gpt_entry_in_free_extent_does_not_prove_content_recovery()
    {
        var reference = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(reference, Snap(After));
        Assert.Equal(RecoveryEntryState.MissingFree, Assert.Single(result.Entries).State);
        Assert.True(result.Comparable);
        Assert.Equal(1, result.MissingCount);
        Assert.False(result.CanRecover);
        Assert.Contains(result.Warnings, w => w.Contains("não comprova"));
    }

    [Fact]
    public void Overwriting_table_entry_with_new_guid_is_occupied_conflict()
    {
        var reference = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var replacement = Part() with { PartitionGuid = "dca3e9ad-92de-4112-af92-64153bdcc94e" };
        var result = PartitionRecoveryAssessment.Compare(reference, Snap(After, parts: [replacement]));
        Assert.Equal(RecoveryEntryState.MissingOccupied, Assert.Single(result.Entries).State);
        Assert.Contains(result.Warnings, w => w.Contains("Novas entradas"));
        Assert.False(result.CanRecover);
    }

    [Fact]
    public void Relocation_preserving_guid_is_changed_not_missing()
    {
        var reference = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(reference,
            Snap(After, parts: [Part() with { Offset = 4_000_000_000 }]));
        Assert.Equal(RecoveryEntryState.Changed, Assert.Single(result.Entries).State);
        Assert.Equal(0, result.MissingCount);
    }

    [Fact]
    public void Efi_or_system_partition_on_disk_zero_is_never_recommended()
    {
        var efi = Part(0) with { GptType = Efi, IsSystem = true, FileSystem = "FAT32" };
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, Disk(0), efi), 0);
        var result = PartitionRecoveryAssessment.Compare(baseline, Snap(After, Disk(0)));
        Assert.Equal(RecoveryEntryState.ProtectedMissing, Assert.Single(result.Entries).State);
        Assert.False(result.CanRecover);
    }

    [Fact]
    public void Device_renumbering_requires_same_unique_id_not_old_disk_number()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(baseline, Snap(After, Disk(7), Part(7)));
        Assert.Equal(7, result.DiskNumber);
        Assert.Equal(RecoveryEntryState.Present, Assert.Single(result.Entries).State);
        Assert.Contains(result.Warnings, w => w.Contains("Número"));
    }

    [Fact]
    public void Replaced_disk_with_same_number_is_inconclusive()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(baseline,
            Snap(After, Disk() with { UniqueId = "other" }));
        Assert.False(result.Comparable);
        Assert.Empty(result.Entries);
        Assert.False(result.CanRecover);
    }

    [Theory]
    [InlineData("MBR", 8_000_000_000L, 512)]
    [InlineData("RAW", 8_000_000_000L, 512)]
    [InlineData("GPT", 7_000_000_000L, 512)]
    [InlineData("GPT", 8_000_000_000L, 4096)]
    public void Change_of_partition_style_sector_or_disk_size_blocks_comparison(
        string style, long size, int sector)
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(baseline,
            Snap(After, Disk() with { PartitionStyle = style, Size = size, LogicalSectorSize = sector }));
        Assert.False(result.Comparable);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Comparing_same_timestamp_cannot_claim_post_capture_change()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(baseline, Snap(Before));
        Assert.False(result.Comparable);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Unknown_current_guid_is_not_accepted_as_lost_entry_evidence()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var result = PartitionRecoveryAssessment.Compare(baseline,
            Snap(After, parts: [Part() with { PartitionGuid = "" }]));
        Assert.False(result.Comparable);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void Invalid_current_geometry_fails_closed()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        var second = Part() with { PartitionGuid = "83a0bc40-28ec-4de2-8fd9-c31b1222e891",
            PartitionNumber = 2, Offset = 1_048_576 };
        var report = PartitionRecoveryAssessment.Compare(baseline, Snap(After, parts: [Part(), second]));
        Assert.False(report.Comparable);
        Assert.Empty(report.Entries);
    }

    [Fact]
    public void Empty_reference_does_not_fabricate_lost_partitions()
    {
        var reference = PartitionRecoveryAssessment.Capture(Snap(Before), 3);
        var report = PartitionRecoveryAssessment.Compare(reference, Snap(After));
        Assert.Empty(report.Entries);
        Assert.Equal(0, report.MissingCount);
    }

    [Fact]
    public void Captured_metadata_cannot_be_mutated_through_collection_interface()
    {
        var baseline = PartitionRecoveryAssessment.Capture(Snap(Before, parts: [Part()]), 3);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<RecoveryOldPartition>)baseline.Partitions).Clear());
    }
}
