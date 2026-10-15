using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class PartitionSecurityTests
{
    private const string ValidGuid = "4d483e30-201b-4caa-9699-bfabc5532430";
    private static readonly SafetyService Policy = new('X');
    private static DiskInfo Disk(int number = 4) => new()
    {
        Number = number, UniqueId = $"safe-test-{number}", FriendlyName = "Test",
        Size = 8_000_000_000, PartitionStyle = "GPT"
    };
    private static PartitionInfo Part(int number = 4, int partition = 1) => new()
    {
        DiskNumber = number, PartitionNumber = partition, PartitionGuid = ValidGuid,
        Type = "Basic", GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", Offset = 1_048_576, Size = 500_000_000,
        DriveLetter = "F", AccessPaths = "", FileSystem = "NTFS"
    };
    private static StorageSnapshot Snapshot(PartitionInfo partition) =>
        new([Disk(partition.DiskNumber)], [partition], DateTimeOffset.UtcNow);
    private static StorageOperation Op(PartitionInfo partition) => new()
    {
        Type = StorageOperationType.FormatPartition, DiskNumber = partition.DiskNumber,
        PartitionNumber = partition.PartitionNumber,
        ExpectedDiskIdentity = Disk(partition.DiskNumber).Identity,
        ExpectedPartitionIdentity = partition.Identity
    };

    [Fact]
    public void Valid_gpt_identity_is_invariant_to_guid_case_and_metadata()
    {
        var part = Part();
        var same = part with { PartitionGuid = ValidGuid.ToUpperInvariant(), DriveLetter = "G", Label = "Other" };
        Assert.True(part.HasStrongIdentity);
        Assert.Equal(part.Identity, same.Identity);
        Assert.True(Policy.Evaluate(Op(part), Snapshot(same)).Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Missing_or_invalid_partition_guid_disallows_mutation(string guid)
    {
        var part = Part() with { PartitionGuid = guid };
        Assert.False(part.HasStrongIdentity);
        Assert.Null(part.Identity);
        Assert.False(Policy.Evaluate(Op(part), Snapshot(part)).Allowed);
    }

    [Fact]
    public void Missing_expected_partition_identity_is_never_implicitly_trusted()
    {
        var part = Part();
        Assert.False(Policy.Evaluate(Op(part) with { ExpectedPartitionIdentity = null }, Snapshot(part)).Allowed);
        Assert.False(Policy.Evaluate(Op(part) with { ExpectedPartitionIdentity = "" }, Snapshot(part)).Allowed);
    }

    [Fact]
    public void Replacing_a_partition_at_same_number_is_detected()
    {
        var before = Part();
        var after = before with { PartitionGuid = "ead59f9b-4f6a-4f07-a21b-788192cab189" };
        Assert.False(Policy.Evaluate(Op(before), Snapshot(after)).Allowed);
    }

    [Theory]
    [InlineData(1_048_577L, 500_000_000L, "Basic")]
    [InlineData(1_048_576L, 500_000_001L, "Basic")]
    [InlineData(1_048_576L, 500_000_000L, "Other")]
    public void Any_change_of_extent_or_type_invalidates_identity(long offset, long size, string type)
    {
        var before = Part();
        var after = before with { Offset = offset, Size = size, Type = type };
        Assert.False(Policy.Evaluate(Op(before), Snapshot(after)).Allowed);
    }

    [Fact]
    public void Moving_target_partition_to_another_number_is_not_authorized()
    {
        var before = Part();
        var after = before with { PartitionNumber = 2 };
        Assert.False(Policy.Evaluate(Op(before), Snapshot(after)).Allowed);
    }

    [Fact]
    public void Layout_mutation_is_rejected_if_any_other_partition_is_protected()
    {
        var boot = Part(partition: 2) with { PartitionGuid = "f592a2c9-f0b6-4d7a-920d-c96467a7fb6a",
            Offset = 700_000_000, Type = "Recovery", DriveLetter = "" };
        var snapshot = new StorageSnapshot([Disk()], [Part(), boot], DateTimeOffset.UtcNow);
        var create = new StorageOperation
        {
            Type = StorageOperationType.CreatePartition, DiskNumber = 4,
            ExpectedDiskIdentity = Disk().Identity, SizeBytes = 100_000_000
        };
        Assert.False(Policy.Evaluate(create, snapshot).Allowed);
        Assert.False(Policy.Evaluate(Op(Part()) with { Type = StorageOperationType.DeletePartition }, snapshot).Allowed);
    }

    [Theory]
    [InlineData("c12a7328-f81f-11d2-ba4b-00a0c93ec93b")]
    [InlineData("e3c9e316-0b5c-4db8-817d-f92df00215ae")]
    [InlineData("de94bba4-06d1-4d40-a16a-bfd50179d6ac")]
    [InlineData("d1cb7b7c-0000-0000-0000-000000000000")]
    public void Unknown_or_system_gpt_types_are_protected_even_if_basic_label(string gptType)
    {
        var current = Part() with { GptType = gptType };
        Assert.True(SafetyService.IsProtected(current));
        Assert.False(Policy.Evaluate(Op(current), Snapshot(current)).Allowed);
    }

    [Fact]
    public void Known_basic_data_gpt_type_is_allowed_for_policy_evaluation()
    {
        var current = Part() with { GptType = "{ebd0a0a2-b9e5-4433-87c0-68b6b72699c7}" };
        Assert.False(SafetyService.IsProtected(current));
        Assert.True(Policy.Evaluate(Op(current), Snapshot(current)).Allowed);
    }

    [Fact]
    public void Mounted_windows_system_path_is_protected_even_without_drive_letter()
    {
        var current = Part() with { DriveLetter = "", AccessPaths = @"C:\SystemVolume\;E:\" };
        Assert.True(SafetyService.IsProtected(current));
    }

    [Fact]
    public void Inventory_rejects_duplicate_gpt_guid_even_when_formats_differ()
    {
        var first = Part();
        var second = first with { PartitionNumber = 2, Offset = 700_000_000,
            PartitionGuid = "{" + ValidGuid.ToUpperInvariant() + "}" };
        Assert.Throws<System.IO.InvalidDataException>(() =>
            PowerShellDiskInventoryService.ValidateSnapshot([Disk()], [first, second]));
    }

    [Fact]
    public void No_operation_may_mutate_disk_zero()
    {
        var target = new StorageOperation { Type = StorageOperationType.CreatePartition,
            DiskNumber = 0, ExpectedDiskIdentity = Disk(0).Identity };
        var snapshot = new StorageSnapshot([Disk(0)], [], DateTimeOffset.UtcNow);
        Assert.False(Policy.Evaluate(target, snapshot).Allowed);
    }

    [Fact]
    public void Explicit_confirmation_names_every_disk_and_cannot_accept_only_first_one()
    {
        var op1 = new StorageOperation { Type = StorageOperationType.CreatePartition,
            DiskNumber = 4, ExpectedDiskIdentity = Disk().Identity };
        var op2 = op1 with { Id = Guid.NewGuid(), DiskNumber = 7, ExpectedDiskIdentity = Disk(7).Identity };
        var op3 = op1 with { Id = Guid.NewGuid() };
        var challenge = OperationConfirmation.Create([op2, op1, op3]);
        Assert.Equal("DISCOS 4,7", challenge.ExpectedResponse);
        Assert.Equal([4, 7], challenge.DiskNumbers);
        Assert.False(challenge.Matches("DISCO 4"));
        Assert.False(challenge.Matches("DISCOS 7,4"));
        Assert.False(challenge.Matches("4"));
        Assert.True(challenge.Matches("DISCOS 4,7"));
        Assert.Contains("Disco 7", challenge.Summary);
    }

    [Fact]
    public void Single_disk_confirmation_cannot_authorize_a_different_disk()
    {
        var single = OperationConfirmation.Create([new StorageOperation {
            Type = StorageOperationType.InitializeGpt, DiskNumber = 4,
            ExpectedDiskIdentity = Disk().Identity }]);
        Assert.Equal("DISCO 4", single.ExpectedResponse);
        Assert.False(single.Matches("DISCO 5"));
        Assert.False(single.Matches("DISCOS 4"));
    }

    [Fact]
    public async Task Direct_executor_call_is_denied_without_invoking_inventory_or_powershell()
    {
        var partition = Part();
        var logger = new TestLogger();
        var inventory = new ExplodingInventory();
        var executor = new PowerShellStorageOperationExecutor(logger, inventory, Policy);
        var result = await executor.ExecuteAsync(Op(partition), CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("bloqueado", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(inventory.Called);
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
    }

    private sealed class ExplodingInventory : IDiskInventoryService
    {
        public bool Called { get; private set; }
        public Task<StorageSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            Called = true;
            throw new InvalidOperationException("Inventory must never run when write gate is closed.");
        }
    }

    private sealed class TestLogger : IAppLogger
    {
        public string LogDirectory => "";
        public void Information(string eventName, object? data = null) { }
        public void Error(string eventName, Exception exception, object? data = null) { }
    }
}
