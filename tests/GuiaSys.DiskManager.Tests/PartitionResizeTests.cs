using System.IO;
using System.Text.Json.Nodes;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class PartitionResizeTests
{
    private const long MiB = 1_048_576;
    private static readonly SafetyService Safety = new('X');

    private static DiskInfo Disk(int number = 3, int? sector = 512) => new()
    {
        Number = number, UniqueId = $"resize-fixture-{number}", FriendlyName = "VHDX simulated",
        PartitionStyle = "GPT", Size = 1_000_000_000, LogicalSectorSize = sector
    };

    private static PartitionInfo Part(int disk = 3, int number = 1,
        long offset = 100 * MiB, long size = 300 * MiB) => new()
    {
        DiskNumber = disk, PartitionNumber = number, Offset = offset, Size = size,
        Type = "Basic", FileSystem = "NTFS", DriveLetter = "F", AccessPaths = "",
        PartitionGuid = $"29ab261d-9d49-47af-9e88-000000000{number:000}",
        GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        ResizeMinBytes = 200 * MiB, ResizeMaxBytes = 750 * MiB
    };

    private static StorageSnapshot State(PartitionInfo p, params PartitionInfo[] others) =>
        new([Disk(p.DiskNumber)], [p, .. others], DateTimeOffset.UtcNow);

    private static StorageOperation Resize(PartitionInfo p, long bytes) => new()
    {
        Type = StorageOperationType.ResizePartition, DiskNumber = p.DiskNumber,
        PartitionNumber = p.PartitionNumber, ExpectedDiskIdentity = Disk(p.DiskNumber).Identity,
        ExpectedPartitionIdentity = p.Identity, SizeBytes = bytes
    };

    [Fact]
    public void Windows_limits_do_not_override_occupied_geometry()
    {
        var first = Part();
        var second = Part(number: 2, offset: 600 * MiB, size: 200 * MiB)
            with { ResizeMinBytes = null, ResizeMaxBytes = null };
        Assert.True(PartitionResizePolicy.TryResolve(State(first, second), first,
            out var range, out _));
        Assert.NotNull(range);
        Assert.Equal(200 * MiB, range.MinimumBytes);
        Assert.Equal(500 * MiB, range.MaximumBytes);
        Assert.Equal(300 * MiB, range.CurrentBytes);
        Assert.True(range.CanExpand);
        Assert.True(range.CanShrink);
    }

    [Fact]
    public void Requests_are_accepted_only_within_both_windows_and_disk_limits()
    {
        var first = Part();
        var second = Part(number: 2, offset: 600 * MiB, size: 200 * MiB);
        var state = State(first, second);
        Assert.True(PartitionResizePolicy.Evaluate(state, first, 500 * MiB).Allowed);
        Assert.False(PartitionResizePolicy.Evaluate(state, first, 501 * MiB).Allowed);
        Assert.True(PartitionResizePolicy.Evaluate(state, first, 200 * MiB).Allowed);
        Assert.False(PartitionResizePolicy.Evaluate(state, first, 199 * MiB).Allowed);
    }

    [Fact]
    public void Noop_resize_is_rejected_and_is_not_an_available_action()
    {
        var p = Part();
        var state = State(p);
        Assert.False(PartitionResizePolicy.Evaluate(state, p, p.Size).Allowed);
        Assert.Contains("igual", PartitionResizePolicy.Evaluate(state, p, p.Size).Reason);
    }

    [Fact]
    public void Unaligned_requested_size_is_rejected_even_inside_window()
    {
        var p = Part();
        var decision = PartitionResizePolicy.Evaluate(State(p), p, 250 * MiB + 1);
        Assert.False(decision.Allowed);
        Assert.Contains("alinhad", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nonaligned_reported_limits_are_clamped_inwards_not_rounded_outwards()
    {
        var p = Part() with
        {
            ResizeMinBytes = 200 * MiB + 1,
            ResizeMaxBytes = 750 * MiB - 1
        };
        Assert.True(PartitionResizePolicy.TryResolve(State(p), p, out var bounds, out _));
        Assert.Equal(200 * MiB + 512, bounds!.MinimumBytes);
        Assert.Equal(750 * MiB - 512, bounds.MaximumBytes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(256)]
    [InlineData(1000)]
    [InlineData(131072)]
    public void Missing_or_unreasonable_sector_size_disables_resize(int? sector)
    {
        var disk = Disk(sector: sector);
        var p = Part();
        var snapshot = new StorageSnapshot([disk], [p], DateTimeOffset.UtcNow);
        Assert.False(PartitionResizePolicy.TryResolve(snapshot, p, out _, out var reason));
        Assert.Contains("setor", reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(Safety.Evaluate(Resize(p, 250 * MiB), snapshot).Allowed);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0L, 700_000_000L)]
    [InlineData(500_000_000L, 400_000_000L)]
    [InlineData(200_000_000L, null)]
    public void Incomplete_or_incoherent_windows_limits_refuse_resize(long? minimum, long? maximum)
    {
        var p = Part() with { ResizeMinBytes = minimum, ResizeMaxBytes = maximum };
        Assert.False(PartitionResizePolicy.TryResolve(State(p), p, out _, out _));
        Assert.False(Safety.Evaluate(Resize(p, 250 * MiB), State(p)).Allowed);
    }

    [Fact]
    public void Windows_limits_must_contain_original_partition_size()
    {
        var p = Part() with { ResizeMinBytes = 400 * MiB, ResizeMaxBytes = 700 * MiB };
        Assert.False(PartitionResizePolicy.TryResolve(State(p), p, out _, out _));
    }

    [Fact]
    public void Identical_bounds_report_resize_unavailable()
    {
        var p = Part() with { ResizeMinBytes = 300 * MiB, ResizeMaxBytes = 300 * MiB };
        Assert.True(PartitionResizePolicy.TryResolve(State(p), p, out var bounds, out _));
        Assert.False(bounds!.CanResize);
        Assert.Null(bounds.SuggestedSizeBytes);
        Assert.False(PartitionResizePolicy.Evaluate(State(p), p, 300 * MiB).Allowed);
    }

    [Fact]
    public void Shrink_only_suggests_minimum_size()
    {
        var p = Part() with { ResizeMinBytes = 200 * MiB, ResizeMaxBytes = 300 * MiB };
        Assert.True(PartitionResizePolicy.TryResolve(State(p), p, out var bounds, out _));
        Assert.True(bounds!.CanShrink);
        Assert.False(bounds.CanExpand);
        Assert.Equal(200 * MiB, bounds.SuggestedSizeBytes);
    }

    [Fact]
    public void Expand_only_suggests_maximum_size()
    {
        var p = Part() with { ResizeMinBytes = 300 * MiB, ResizeMaxBytes = 700 * MiB };
        Assert.True(PartitionResizePolicy.TryResolve(State(p), p, out var bounds, out _));
        Assert.False(bounds!.CanShrink);
        Assert.True(bounds.CanExpand);
        Assert.Equal(700 * MiB, bounds.SuggestedSizeBytes);
    }

    [Fact]
    public void Incomplete_or_overlapping_inventory_fails_closed()
    {
        var p = Part();
        var overlapping = Part(number: 2, offset: 200 * MiB, size: 300 * MiB);
        Assert.False(PartitionResizePolicy.TryResolve(State(p, overlapping), p, out _, out _));
    }

    [Fact]
    public void Protected_partition_is_rejected_even_with_published_windows_bounds()
    {
        var p = Part() with { IsSystem = true };
        Assert.False(Safety.Evaluate(Resize(p, 250 * MiB), State(p)).Allowed);
    }

    [Fact]
    public void Replacing_partition_fingerprint_is_detected_before_resize_preparation()
    {
        var original = Part();
        var newPart = original with { PartitionGuid = "bee96970-1dd7-4fe2-9e54-708995d4b610" };
        Assert.False(Safety.Evaluate(Resize(original, 250 * MiB), State(newPart)).Allowed);
    }

    [Fact]
    public void Planning_uses_windows_bounds_and_disallows_two_resize_steps_without_refresh()
    {
        var p = Part();
        var planner = new OperationPlanner(Safety);
        var first = Resize(p, 250 * MiB);
        var second = Resize(p, 400 * MiB);
        var plan = planner.Plan(State(p), [first, second]);
        Assert.False(plan.CanApply);
        Assert.Equal(PlanStepState.Ready, plan.Steps[0].State);
        Assert.True(plan.Steps[0].RequiresRefreshAfterExecution);
        Assert.Equal(PlanStepState.Conflict, plan.Steps[1].State);
        Assert.Equal(p.Size, plan.ProjectedSnapshot.FindPartition(p.DiskNumber, p.PartitionNumber)!.Size);
    }

    [Fact]
    public void Resize_is_still_a_reinforced_confirmed_operation_not_an_immediate_write()
    {
        var p = Part();
        var decision = Safety.Evaluate(Resize(p, 250 * MiB), State(p));
        Assert.True(decision.Allowed);
        Assert.True(decision.RequiresReinforcedConfirmation);
        Assert.False(GuiaSys.DiskManager.Licensing.StorageWriteGate.ReleasedAfterSafetyAudit);
    }

    private const string JsonSample = """
        {
          "Disks":[{
            "Number":3, "UniqueId":"resize-fixture-3", "FriendlyName":"Windows example",
            "PartitionStyle":"GPT", "Size":1000000000,
            "IsBoot":false, "IsSystem":false, "IsOffline":false, "IsReadOnly":false,
            "LogicalSectorSize":512
          }],
          "Partitions":[{
            "DiskNumber":3, "PartitionNumber":1,
            "PartitionGuid":"29ab261d-9d49-47af-9e88-000000000001",
            "GptType":"ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
            "AccessPaths":"", "DriveLetter":"F", "Type":"Basic",
            "Offset":104857600, "Size":314572800,
            "IsBoot":false, "IsSystem":false, "IsHidden":false,
            "IsReadOnly":false, "IsOffline":false,
            "ResizeMinBytes":209715200, "ResizeMaxBytes":786432000
          }]
        }
        """;

    [Fact]
    public void Windows_inventory_json_keeps_optional_valid_resize_metadata()
    {
        var snapshot = PowerShellDiskInventoryService.ParseInventory(
            JsonSample, DateTimeOffset.UtcNow);
        var partition = Assert.Single(snapshot.Partitions);
        Assert.True(partition.HasResizeLimits);
        Assert.Equal(200 * MiB, partition.ResizeMinBytes);
        Assert.Equal(750 * MiB, partition.ResizeMaxBytes);
        Assert.True(PartitionResizePolicy.TryResolve(snapshot, partition, out _, out _));
    }

    [Fact]
    public void Old_windows_json_without_resize_data_does_not_invent_capabilities()
    {
        var node = JsonNode.Parse(JsonSample)!.AsObject();
        var partition = node["Partitions"]![0]!.AsObject();
        Assert.True(partition.Remove("ResizeMinBytes"));
        Assert.True(partition.Remove("ResizeMaxBytes"));
        var old = node.ToJsonString();
        var snapshot = PowerShellDiskInventoryService.ParseInventory(old, DateTimeOffset.UtcNow);
        Assert.False(snapshot.Partitions[0].HasResizeLimits);
        Assert.False(PartitionResizePolicy.TryResolve(snapshot, snapshot.Partitions[0], out _, out _));
    }

    [Theory]
    [InlineData("\"ResizeMinBytes\":null, \"ResizeMaxBytes\":786432000")]
    [InlineData("\"ResizeMinBytes\":-5, \"ResizeMaxBytes\":786432000")]
    [InlineData("\"ResizeMinBytes\":524288000, \"ResizeMaxBytes\":786432000")]
    [InlineData("\"ResizeMinBytes\":209715200, \"ResizeMaxBytes\":-7")]
    public void Windows_inventory_rejects_partial_or_invalid_resize_ranges(string invalidFields)
    {
        var json = JsonSample.Replace("\"ResizeMinBytes\":209715200, \"ResizeMaxBytes\":786432000",
            invalidFields);
        Assert.Throws<InvalidDataException>(() =>
            PowerShellDiskInventoryService.ParseInventory(json, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Parser_rejects_wrong_typed_supported_size()
    {
        var bad = JsonSample.Replace("\"ResizeMinBytes\":209715200",
            "\"ResizeMinBytes\":\"not-a-number\"");
        Assert.Throws<InvalidDataException>(() =>
            PowerShellDiskInventoryService.ParseInventory(bad, DateTimeOffset.UtcNow));
    }
}
