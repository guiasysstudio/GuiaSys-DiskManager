using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class StoragePlanningTests
{
    private const long MB = 1_000_000;
    private static readonly DateTimeOffset Captured = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DiskInfo Disk(int number = 2, long size = 1_000 * MB, string style = "GPT") =>
        new() { Number = number, UniqueId = "fixture-disk-" + number, FriendlyName = "Disposable",
            PartitionStyle = style, Size = size };

    private static PartitionInfo Part(int disk = 2, int number = 1, long offset = 100 * MB,
        long size = 300 * MB, string letter = "E") =>
        new() { DiskNumber = disk, PartitionNumber = number, Offset = offset, Size = size,
            AccessPaths = "", DriveLetter = letter, Type = "Basic", FileSystem = "NTFS",
            PartitionGuid = $"ecb7da44-3b19-4e0f-97a4-{disk:0000}{number:00000000}" };

    private static StorageSnapshot Snapshot(params PartitionInfo[] partitions) =>
        new([Disk()], partitions, Captured);

    private static StorageOperation Op(StorageOperationType type, int disk = 2, int? partition = null,
        long? size = null, char? letter = null) =>
        new() { Type = type, DiskNumber = disk, PartitionNumber = partition,
            ExpectedDiskIdentity = Disk(disk).Identity,
            ExpectedPartitionIdentity = partition is int n ? Part(disk: disk, number: n,
                offset: n == 1 ? 100 * MB : 500 * MB).Identity : null,
            SizeBytes = size,
            DriveLetter = letter };

    private static OperationPlanner Planner() => new(new SafetyService(null));

    [Fact]
    public void Geometry_produces_exact_half_open_ranges_and_contiguous_free_space()
    {
        var parts = new[] { Part(), Part(number: 2, offset: 500 * MB, size: 200 * MB, letter: "F") };
        var original = Snapshot(parts);
        var layout = StorageGeometry.Build(original).FindDisk(2)!;
        Assert.Equal(5, layout.Extents.Count);
        Assert.Equal([(0L, 100 * MB), (100 * MB, 400 * MB), (400 * MB, 500 * MB),
            (500 * MB, 700 * MB), (700 * MB, 1000 * MB)],
            layout.Extents.Select(x => (x.Offset, x.EndExclusive)).ToArray());
        Assert.Equal(500 * MB, layout.TotalFreeBytes);
        Assert.Equal(300 * MB, layout.LargestFreeExtentBytes);
        Assert.Equal([null, 1, null, 2, null],
            layout.Extents.Select(x => x.PartitionNumber).ToArray());
        Assert.Equal(2, original.Disks[0].Number);
    }

    [Fact]
    public void Empty_disk_is_entirely_unallocated()
    {
        var layout = StorageGeometry.Build(Snapshot()).FindDisk(2)!;
        Assert.Single(layout.Extents);
        Assert.Equal(1_000 * MB, layout.TotalFreeBytes);
    }

    [Fact]
    public void Adjacent_partitions_do_not_create_fake_free_ranges()
    {
        var layout = StorageGeometry.Build(Snapshot(
            Part(offset: 0, size: 200 * MB),
            Part(number: 2, offset: 200 * MB, size: 800 * MB, letter: "F"))).FindDisk(2)!;
        Assert.Equal(0, layout.TotalFreeBytes);
        Assert.Equal(2, layout.Extents.Count);
    }

    [Theory]
    [InlineData(100_000_000L, 200_000_000L)]
    [InlineData(-1L, 200_000_000L)]
    [InlineData(900_000_000L, 200_000_000L)]
    [InlineData(0L, -2L)]
    [InlineData(0L, 0L)]
    [InlineData(9223372036854775797L, 200L)]
    public void Geometry_rejects_overlaps_invalid_lengths_and_out_of_bounds(long offset, long size)
    {
        var snapshot = Snapshot(Part(offset: 0, size: 250 * MB),
            Part(number: 2, offset: offset, size: size, letter: "F"));
        Assert.Throws<InvalidDataException>(() => StorageGeometry.Build(snapshot));
    }

    [Fact]
    public void Geometry_rejects_orphans_duplicate_partition_ids_and_disk_identity_collision()
    {
        Assert.Throws<InvalidDataException>(() => StorageGeometry.Build(Snapshot(Part(disk: 3))));
        Assert.Throws<InvalidDataException>(() => StorageGeometry.Build(Snapshot(
            Part(), Part(number: 1, offset: 500 * MB, letter: "F"))));
        var first = Disk();
        var second = Disk(3) with { UniqueId = first.UniqueId };
        Assert.Throws<InvalidDataException>(() => StorageGeometry.Build(new StorageSnapshot(
            [first, second], [], Captured)));
    }

    [Fact]
    public void Geometry_does_not_mutate_original_snapshot()
    {
        var first = Part();
        var snapshot = Snapshot(first);
        var layout = StorageGeometry.Build(snapshot);
        Assert.Equal(first, snapshot.Partitions[0]);
        Assert.Equal(1, layout.FindDisk(2)!.Extents.Count(x => x.PartitionNumber is not null));
    }

    [Fact]
    public void Planner_applies_metadata_on_clone_and_keeps_original_immutable()
    {
        var snapshot = Snapshot(Part());
        var ops = new[] { Op(StorageOperationType.SetDriveLetter, partition: 1, letter: 'F'),
            Op(StorageOperationType.SetVolumeLabel, partition: 1) with { Label = "Preview" } };
        var plan = Planner().Plan(snapshot, ops);
        Assert.True(plan.CanApply);
        Assert.All(plan.Steps, step => Assert.Equal(PlanStepState.Ready, step.State));
        Assert.Equal("F", plan.ProjectedSnapshot.FindPartition(2, 1)!.DriveLetter);
        Assert.Equal("Preview", plan.ProjectedSnapshot.FindPartition(2, 1)!.Label);
        Assert.Equal("E", snapshot.FindPartition(2, 1)!.DriveLetter);
        Assert.Equal("", snapshot.FindPartition(2, 1)!.Label);
    }

    [Fact]
    public void Planner_rejects_duplicate_drive_letter_even_on_other_disk()
    {
        var snapshot = new StorageSnapshot([Disk(), Disk(3)],
            [Part(), Part(disk: 3, letter: "F")], Captured);
        var plan = Planner().Plan(snapshot, [Op(StorageOperationType.SetDriveLetter,
            partition: 1, letter: 'F')]);
        Assert.False(plan.CanApply);
        Assert.Contains("letra", plan.FirstConflict!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Planner_blocks_duplicate_drive_letter_introduced_earlier_in_queue()
    {
        var snapshot = Snapshot(Part(), Part(number: 2, offset: 500 * MB, letter: "G"));
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.SetDriveLetter, partition: 1, letter: 'F'),
            Op(StorageOperationType.SetDriveLetter, partition: 2, letter: 'F') ]);
        Assert.False(plan.CanApply);
        Assert.Equal(2, plan.FirstConflict!.Position);
    }

    [Fact]
    public void Planner_blocks_dependents_of_opaque_disk_layout_change()
    {
        var snapshot = Snapshot(Part());
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.DeletePartition, partition: 1),
            Op(StorageOperationType.SetDriveLetter, partition: 1, letter: 'G') ]);
        Assert.False(plan.CanApply);
        Assert.True(plan.Steps[0].RequiresRefreshAfterExecution);
        Assert.Equal(PlanStepState.Conflict, plan.Steps[1].State);
        Assert.Equal("E", plan.ProjectedSnapshot.FindPartition(2, 1)!.DriveLetter);
    }

    [Fact]
    public void Planner_blocks_format_followup_on_same_partition_but_not_other_partition()
    {
        var snapshot = Snapshot(Part(), Part(number: 2, offset: 500 * MB, letter: "F"));
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.FormatPartition, partition: 1),
            Op(StorageOperationType.SetVolumeLabel, partition: 2) with { Label = "Other" },
            Op(StorageOperationType.SetVolumeLabel, partition: 1) with { Label = "Unsafe" }]);
        Assert.True(plan.Steps[0].RequiresRefreshAfterExecution);
        Assert.Equal(PlanStepState.Ready, plan.Steps[1].State);
        Assert.Equal(PlanStepState.Conflict, plan.Steps[2].State);
    }

    [Fact]
    public void Planner_propagates_read_only_disk_state_to_later_mutations()
    {
        var snapshot = Snapshot();
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.SetDiskReadOnly),
            Op(StorageOperationType.CreatePartition, size: 50 * MB) ]);
        Assert.False(plan.CanApply);
        Assert.True(plan.ProjectedSnapshot.FindDisk(2)!.IsReadOnly);
        Assert.Equal(PlanStepState.Conflict, plan.Steps[1].State);
        Assert.False(snapshot.FindDisk(2)!.IsReadOnly);
    }

    [Fact]
    public void Planner_propagates_online_transition_and_protects_raw_disk_layout()
    {
        var snapshot = new StorageSnapshot([Disk(style: "RAW") with { IsOffline = true }], [], Captured);
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.SetDiskOnline),
            Op(StorageOperationType.InitializeGpt),
            Op(StorageOperationType.CreatePartition, size: 20 * MB) ]);
        Assert.Equal([PlanStepState.Ready, PlanStepState.Ready, PlanStepState.Conflict],
            plan.Steps.Select(x => x.State).ToArray());
    }

    [Fact]
    public void Planner_uses_largest_contiguous_free_range_not_total_free_bytes()
    {
        var snapshot = Snapshot(Part(), Part(number: 2, offset: 500 * MB, size: 200 * MB, letter: "F"));
        var tooBig = Planner().Plan(snapshot, [Op(StorageOperationType.CreatePartition, size: 400 * MB)]);
        var fitting = Planner().Plan(snapshot, [Op(StorageOperationType.CreatePartition, size: 200 * MB)]);
        Assert.False(tooBig.CanApply);
        Assert.True(fitting.CanApply);
    }

    [Fact]
    public void Planner_rejects_impossible_expand_and_does_not_assume_filesystem_can_shrink()
    {
        var snapshot = Snapshot(Part(), Part(number: 2, offset: 500 * MB, letter: "F"));
        Assert.False(Planner().Plan(snapshot, [Op(StorageOperationType.ResizePartition, partition: 1, size: 450 * MB)]).CanApply);
        var shrinking = Planner().Plan(snapshot, [Op(StorageOperationType.ResizePartition, partition: 1, size: 250 * MB)]);
        Assert.True(shrinking.CanApply);
        Assert.True(shrinking.Steps[0].RequiresRefreshAfterExecution);
        Assert.Equal(300 * MB, shrinking.ProjectedSnapshot.FindPartition(2, 1)!.Size);
    }

    [Fact]
    public void Planner_rejects_duplicate_operation_ids_and_changed_disk_identity()
    {
        var snapshot = Snapshot();
        var first = Op(StorageOperationType.SetDiskOffline);
        var plan = Planner().Plan(snapshot, [first, first]);
        Assert.False(plan.CanApply);
        Assert.Equal(2, plan.FirstConflict!.Position);
        var wrong = first with { Id = Guid.NewGuid(), ExpectedDiskIdentity = "uid:other" };
        Assert.False(Planner().Plan(snapshot, [wrong]).CanApply);
    }

    [Fact]
    public void Planner_can_plan_independent_disks_without_relying_on_future_disk_numbers()
    {
        var snapshot = new StorageSnapshot([Disk(), Disk(3)], [], Captured);
        var plan = Planner().Plan(snapshot, [
            Op(StorageOperationType.CreatePartition, size: 100 * MB),
            Op(StorageOperationType.SetDiskReadOnly, disk: 3) ]);
        Assert.True(plan.CanApply);
        Assert.Equal(2, plan.AffectedDisks);
        Assert.Equal([2, 3], plan.ProjectedDiskNumbers);
    }

    [Fact]
    public void Planning_is_pure_and_does_not_call_storage_executor()
    {
        var snapshot = Snapshot();
        var executor = new UncallableExecutor();
        var plan = Planner().Plan(snapshot, [Op(StorageOperationType.SetDiskOffline)]);
        Assert.True(plan.CanApply);
        Assert.False(executor.WasCalled);
    }

    private sealed class UncallableExecutor : IStorageOperationExecutor
    {
        public bool WasCalled { get; private set; }
        public Task<OperationResult> ExecuteAsync(StorageOperation operation, CancellationToken token)
        {
            WasCalled = true;
            throw new InvalidOperationException("Planner should never execute disk operations.");
        }
    }
}
