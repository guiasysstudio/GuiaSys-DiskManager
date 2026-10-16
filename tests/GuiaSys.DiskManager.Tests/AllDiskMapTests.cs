using System.IO;
using GuiaSys.DiskManager.Controls;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager.Tests;

/// <summary>H03-02: synthetic read-only all-disk GUI model checks; no Windows disk access.</summary>
public sealed class AllDiskMapTests
{
    private static DiskInfo Disk(int n, long capacity = 10_000L) => new()
    {
        Number = n, UniqueId = "fake-id-" + n, FriendlyName = "Synthetic",
        Size = capacity, PartitionStyle = "GPT"
    };

    private static PartitionInfo Part(int disk, int number, long offset, long length,
        long? volumeSize = null, long? free = null) => new()
    {
        DiskNumber = disk, PartitionNumber = number,
        PartitionGuid = $"3ad5{disk:0000}-{number:0000}-45de-b673-a807f3907842",
        GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        AccessPaths = "", DriveLetter = number == 1 ? "C" : "",
        Type = "Basic", FileSystem = "NTFS",
        Offset = offset, Size = length, VolumeCapacityBytes = volumeSize,
        VolumeAvailableBytes = free
    };

    [Fact]
    public void All_disks_have_separate_rows_including_an_unpartitioned_disk()
    {
        var p1 = Part(1, 1, 100, 4_000, 4_000, 1_000);
        var p2 = Part(1, 2, 5_000, 2_500);
        var snap = new StorageSnapshot([Disk(1), Disk(2, 4_000)], [p1, p2], DateTimeOffset.UtcNow);
        var rows = DiskMapRowViewModel.Build(snap);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Disk.Number);
        Assert.Equal(2, rows[1].Disk.Number);
        Assert.Equal(new long[] { 100, 4_000, 900, 2_500, 2_500 },
            rows[0].Segments.Select(x => x.Size));
        Assert.Equal(new long[] { 0, 100, 4_100, 5_000, 7_500 },
            rows[0].Segments.Select(x => x.Offset));
        var unallocated = Assert.Single(rows[1].Segments);
        Assert.True(unallocated.IsUnallocated);
        Assert.Equal(4_000, unallocated.Size);
        Assert.Equal(2, unallocated.DiskNumber);
        Assert.Equal("Não alocado", unallocated.Title);
        Assert.False(unallocated.HasVolumeUsage);
    }

    [Fact]
    public void File_usage_uses_only_verified_windows_volume_counters()
    {
        var p = Part(1, 1, 100, 4_000, 4_000, 1_000);
        var row = Assert.Single(DiskMapRowViewModel.Build(new StorageSnapshot(
            [Disk(1)], [p], DateTimeOffset.UtcNow)));
        var part = Assert.Single(row.Segments.Where(s => s.Partition is not null));
        Assert.True(part.HasVolumeUsage);
        Assert.Equal(75d, part.UsagePercent);
        Assert.Equal("C:", part.Title);
        Assert.StartsWith("NTFS · ", part.Subtitle);
        var free = row.Segments.Where(s => s.IsUnallocated).ToArray();
        Assert.All(free, s => Assert.False(s.HasVolumeUsage));

        var noCounters = p with { VolumeAvailableBytes = null };
        var row2 = Assert.Single(DiskMapRowViewModel.Build(new StorageSnapshot(
            [Disk(1)], [noCounters], DateTimeOffset.UtcNow)));
        var part2 = Assert.Single(row2.Segments.Where(s => s.Partition is not null));
        Assert.False(part2.HasVolumeUsage);
        Assert.Equal(0d, part2.UsagePercent);
    }

    [Fact]
    public void Row_and_partition_selection_follow_active_target_without_replacing_segments()
    {
        var one = Part(1, 1, 100, 4_000);
        var two = Part(2, 1, 100, 2_000);
        var rows = DiskMapRowViewModel.Build(new StorageSnapshot(
            [Disk(1), Disk(2)], [one, two], DateTimeOffset.UtcNow));
        var original = rows[0].Segments[1];
        foreach (var row in rows) row.UpdateSelection(1, one);
        Assert.True(rows[0].IsSelected);
        Assert.False(rows[1].IsSelected);
        Assert.True(original.IsSelected);
        foreach (var row in rows) row.UpdateSelection(2, two);
        Assert.False(original.IsSelected);
        Assert.False(rows[0].IsSelected);
        Assert.True(rows[1].IsSelected);
        Assert.Single(rows[1].Segments.Where(s => s.IsSelected));
        Assert.Same(original, rows[0].Segments[1]);
        foreach (var row in rows) row.UpdateSelection(null, null);
        Assert.All(rows, row => Assert.False(row.IsSelected));
        Assert.DoesNotContain(rows.SelectMany(row => row.Segments), seg => seg.IsSelected);
    }

    [Fact]
    public void Map_rejects_invalid_geometry_rather_than_painting_a_plausible_layout()
    {
        var invalid = new StorageSnapshot([Disk(1)],
            [Part(1, 1, 10, 8_000), Part(1, 2, 7_000, 2_000)],
            DateTimeOffset.UtcNow);
        Assert.Throws<InvalidDataException>(() => DiskMapRowViewModel.Build(invalid));
    }

    [Fact]
    public void Pinned_ambiguous_windows_disk_identity_remains_readonly_in_row()
    {
        var ambiguous = Disk(4) with { HasAmbiguousUniqueId = true, UniqueId = "" };
        var row = Assert.Single(DiskMapRowViewModel.Build(
            new StorageSnapshot([ambiguous], [], DateTimeOffset.UtcNow)));
        Assert.Contains("somente leitura", row.SafetyLabel);
        Assert.False(row.Disk.HasStrongIdentity);
    }

    [Theory]
    [InlineData(800d, new long[] { 1, 998, 1 })]
    [InlineData(250d, new long[] { 1, 1, 1, 1, 1 })]
    [InlineData(0d, new long[] { 1, 4, 8 })]
    public void Proportional_rows_preserve_width_and_minimum_click_target(double available, long[] bytes)
    {
        var widths = ProportionalPartitionPanel.CalculateWidths(bytes, available);
        Assert.Equal(bytes.Length, widths.Length);
        Assert.InRange(widths.Sum(), available - 0.001d, available + 0.001d);
        Assert.All(widths, width => Assert.True(width >= 0d && double.IsFinite(width)));
        if (available > 0)
            Assert.All(widths, width =>
                Assert.True(width >= Math.Min(38d, available / bytes.Length) - 0.001d));
    }

    [Fact]
    public void Long_partition_count_does_not_expand_beyond_panel_width()
    {
        var widths = ProportionalPartitionPanel.CalculateWidths(
            Enumerable.Repeat(1L, 40).ToArray(), 200d);
        Assert.Equal(40, widths.Length);
        Assert.All(widths, w => Assert.InRange(w, 4.999d, 5.001d));
    }

    [Fact]
    public void Invalid_width_and_negative_byte_extents_are_not_displayed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProportionalPartitionPanel.CalculateWidths([1, -1], 200d));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProportionalPartitionPanel.CalculateWidths([1], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProportionalPartitionPanel.CalculateWidths([1], -10d));
    }
}
