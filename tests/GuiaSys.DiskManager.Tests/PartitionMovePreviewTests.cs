using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Tests;

/// <summary>All inputs are fictional; no storage, mounted VHDX or PowerShell command is invoked.</summary>
public sealed class PartitionMovePreviewTests
{
    private const long MiB = 1_048_576;
    private const long GiB = 1_073_741_824;
    private static readonly SafetyService Safety = new('X');

    private static DiskInfo Disk(int id = 4) => new()
    {
        Number = id, UniqueId = $"fake-c09-disk-{id}", FriendlyName = "Synthetic",
        PartitionStyle = "GPT", Size = 8 * GiB, LogicalSectorSize = 512
    };

    private static PartitionInfo Part(int id = 4, int number = 1, long offset = GiB,
        long size = 2 * GiB, string gptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7") => new()
    {
        DiskNumber = id, PartitionNumber = number, Offset = offset, Size = size,
        Type = "Basic", FileSystem = "NTFS", DriveLetter = "F", AccessPaths = "",
        PartitionGuid = number == 1 ? "775d97c3-5876-4ed7-8d7c-21a28903c009" :
            "775d97c3-5876-4ed7-8d7c-21a28903c00a",
        GptType = gptType
    };

    private static StorageSnapshot State(PartitionInfo source, params PartitionInfo[] others) =>
        new([Disk(source.DiskNumber)], [source, .. others], DateTimeOffset.UtcNow);

    private static StorageOperation Intent(PartitionInfo source, long destination) => new()
    {
        Type = StorageOperationType.MovePartition, DiskNumber = source.DiskNumber,
        PartitionNumber = source.PartitionNumber,
        ExpectedDiskIdentity = Disk(source.DiskNumber).Identity,
        ExpectedPartitionIdentity = source.Identity, TargetOffsetBytes = destination
    };

    [Fact]
    public void Preview_identifies_nonoverlapping_free_extent_without_writing()
    {
        var p = Part();
        var snapshot = State(p);
        var preview = PartitionMovePreview.Analyze(snapshot, 4, 1, 4 * GiB);
        Assert.True(preview.CanPreview);
        Assert.Equal(GiB, preview.SourceOffsetBytes);
        Assert.Equal(4 * GiB, preview.TargetOffsetBytes);
        Assert.Equal(2 * GiB, preview.BytesToCopy);
        Assert.Equal("Direita", preview.Direction);
        Assert.Contains("SIMULAÇÃO", preview.Summary);
        Assert.Contains("Sem mecanismo de cópia", preview.Summary);
        Assert.Equal(GiB, snapshot.FindPartition(4, 1)!.Offset);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(2_147_483_648L, false)]
    [InlineData(1_073_741_824L, false)]
    [InlineData(5_368_709_120L, true)]
    public void Preview_checks_destination_wholly_unallocated(long offset, bool accepted)
    {
        var preview = PartitionMovePreview.Analyze(State(Part()), 4, 1, offset);
        Assert.Equal(accepted, preview.CanPreview);
    }

    [Fact]
    public void Preview_rejects_overlap_even_when_destination_sector_aligned()
    {
        var result = PartitionMovePreview.Analyze(State(Part()), 4, 1, 2 * GiB);
        Assert.False(result.CanPreview);
        Assert.Contains("sobrepõe", result.Reason);
    }

    [Fact]
    public void Preview_rejects_collisions_with_another_partition()
    {
        var first = Part();
        var second = Part(number: 2, offset: 4 * GiB, size: 1 * GiB);
        Assert.False(PartitionMovePreview.Analyze(State(first, second), 4, 1, 4 * GiB).CanPreview);
        Assert.False(PartitionMovePreview.Analyze(State(first, second), 4, 1, 3 * GiB).CanPreview);
        Assert.True(PartitionMovePreview.Analyze(State(first, second), 4, 1, 5 * GiB).CanPreview);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1000L)]
    [InlineData(1_073_741_825L)]
    [InlineData(7_516_192_768L)]
    public void Negative_unaligned_and_out_of_bounds_destinations_are_denied(long offset)
    {
        Assert.False(PartitionMovePreview.Analyze(State(Part()), 4, 1, offset).CanPreview);
    }

    [Fact]
    public void Adjacent_free_extent_with_no_gap_is_allowed_as_geometric_preview_only()
    {
        var result = PartitionMovePreview.Analyze(State(Part()), 4, 1, 3 * GiB);
        Assert.True(result.CanPreview);
        Assert.False(Safety.Evaluate(Intent(Part(), 3 * GiB), State(Part())).Allowed);
    }

    [Fact]
    public void Nearest_suggested_destination_is_deterministic_and_aligned()
    {
        var source = Part();
        var snapshot = State(source);
        var suggested = PartitionMovePreview.SuggestDestination(snapshot, 4, 1);
        Assert.Equal(3 * GiB, suggested);
        Assert.Equal(0, suggested!.Value % PartitionMovePreview.AlignmentBytes);
        Assert.True(PartitionMovePreview.Analyze(snapshot, 4, 1, suggested.Value).CanPreview);
    }

    [Fact]
    public void No_suggestion_when_empty_extent_is_too_small()
    {
        var source = Part(size: 6 * GiB);
        var snapshot = State(source);
        Assert.Null(PartitionMovePreview.SuggestDestination(snapshot, 4, 1));
    }

    [Fact]
    public void Preview_denies_disks_with_any_recovery_partition()
    {
        var source = Part();
        var recovery = Part(number: 2, offset: 4 * GiB, size: 1 * MiB,
            gptType: "de94bba4-06d1-4d40-a16a-bfd50179d6ac");
        Assert.False(PartitionMovePreview.Analyze(State(source, recovery), 4, 1, 5 * GiB).CanPreview);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Protected_disks_are_not_exempt_from_write_refusal(int disk)
    {
        var p = Part(id: disk);
        var result = PartitionMovePreview.Analyze(State(p), disk, 1, 4 * GiB);
        Assert.Equal(disk != 0, result.CanPreview);
        Assert.False(Safety.Evaluate(Intent(p, 4 * GiB), State(p)).Allowed);
    }

    [Fact]
    public void Preview_rejects_missing_sector_information_and_weak_gpt_identity()
    {
        var source = Part();
        var sectorMissing = new StorageSnapshot([Disk() with { LogicalSectorSize = null }],
            [source], DateTimeOffset.UtcNow);
        Assert.False(PartitionMovePreview.Analyze(sectorMissing, 4, 1, 4 * GiB).CanPreview);
        var bad = source with { PartitionGuid = "" };
        Assert.False(PartitionMovePreview.Analyze(State(bad), 4, 1, 4 * GiB).CanPreview);
    }

    [Fact]
    public void Move_intent_is_high_risk_and_never_a_valid_executable_planner_step()
    {
        var source = Part();
        var intent = Intent(source, 4 * GiB);
        Assert.True(intent.Risk.HasFlag(StorageOperationRisk.Destructive));
        Assert.True(intent.RequiresReinforcedConfirmation);
        Assert.True(EssentialOperationValidator.Validate(intent).Allowed);
        var denied = Safety.Evaluate(intent, State(source));
        Assert.False(denied.Allowed);
        Assert.Contains("simulação", denied.Reason, StringComparison.OrdinalIgnoreCase);
        var plan = new OperationPlanner(Safety).Plan(State(source), [intent]);
        Assert.False(plan.CanApply);
        Assert.Equal(PlanStepState.Conflict, Assert.Single(plan.Steps).State);
        Assert.Equal(GiB, plan.ProjectedSnapshot.FindPartition(4, 1)!.Offset);
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
    }

    [Fact]
    public void Move_intent_rejects_missing_destination_and_extra_operation_fields()
    {
        var source = Part();
        var op = Intent(source, 4 * GiB);
        Assert.False(EssentialOperationValidator.Validate(op with { TargetOffsetBytes = null }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(op with { TargetOffsetBytes = -2 }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(op with { PartitionNumber = null }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(op with { SizeBytes = GiB }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(op with { DriveLetter = 'G' }).Allowed);
        var other = op with { Type = StorageOperationType.ResizePartition, SizeBytes = GiB };
        Assert.False(EssentialOperationValidator.Validate(other).Allowed);
    }

    [Fact]
    public void Layout_errors_do_not_create_misleading_suggested_destinations()
    {
        var first = Part();
        var overlap = Part(number: 2, offset: 2 * GiB, size: GiB);
        Assert.Null(PartitionMovePreview.SuggestDestination(State(first, overlap), 4, 1));
    }
}
