using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Tests;

public sealed class SafetyServiceTests
{
    private readonly SafetyService _service = new('X');

    [Fact]
    public void Allows_non_destructive_change_on_external_disk() => Assert.True(_service.Evaluate(Operation(StorageOperationType.SetDiskOnline), Snapshot()).Allowed);

    [Fact]
    public void Blocks_when_disk_was_removed()
    {
        var snapshot = new StorageSnapshot([], [], DateTimeOffset.Now);
        Assert.Contains("não existe", _service.Evaluate(Operation(StorageOperationType.SetDiskOnline), snapshot).Reason);
    }

    [Fact]
    public void Blocks_when_identity_changed()
    {
        var operation = Operation(StorageOperationType.SetDiskOnline) with { ExpectedDiskIdentity = "different" };
        Assert.Contains("identidade", _service.Evaluate(operation, Snapshot()).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(AllOperationTypes))]
    public void Disk_zero_is_read_only_for_every_mutation(StorageOperationType type)
    {
        var disk = Disk(0) with { UniqueId = "disk0", PartitionStyle = "GPT" };
        var partition = Partition() with { DiskNumber = 0 };
        var snapshot = new StorageSnapshot([disk], [partition], DateTimeOffset.Now);
        var operation = Operation(type) with { DiskNumber = 0, ExpectedDiskIdentity = "disk0" };

        var decision = _service.Evaluate(operation, snapshot);

        Assert.False(decision.Allowed);
        Assert.Contains("somente leitura", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("C")]
    [InlineData("")]
    public void Blocks_system_or_boot_partition(string driveLetter)
    {
        var partition = Partition() with { DriveLetter = driveLetter, IsSystem = driveLetter.Length == 0 };
        Assert.False(_service.Evaluate(Operation(StorageOperationType.FormatPartition), Snapshot(partition)).Allowed);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("Reserved")]
    [InlineData("Recovery")]
    [InlineData("EFI System Partition")]
    public void Classifies_protected_partition_types(string type) => Assert.True(SafetyService.IsProtected(Partition() with { Type = type }));

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline)]
    [InlineData(StorageOperationType.SetDiskOffline)]
    [InlineData(StorageOperationType.SetDiskReadOnly)]
    [InlineData(StorageOperationType.ClearDiskReadOnly)]
    public void Blocks_every_whole_disk_state_change_when_recovery_exists(StorageOperationType type)
    {
        var decision = _service.Evaluate(Operation(type), Snapshot(Partition() with { Type = "Recovery" }));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Initialize_requires_raw_empty_disk()
    {
        var snapshot = Snapshot() with { Disks = [Disk(2) with { PartitionStyle = "GPT" }] };
        Assert.False(_service.Evaluate(Operation(StorageOperationType.InitializeGpt), snapshot).Allowed);
    }

    [Fact]
    public void Initialize_requires_online_writable_disk()
    {
        var offline = Snapshot() with { Disks = [Disk(2) with { IsOffline = true }] };
        var readOnly = Snapshot() with { Disks = [Disk(2) with { IsReadOnly = true }] };
        Assert.False(_service.Evaluate(Operation(StorageOperationType.InitializeGpt), offline).Allowed);
        Assert.False(_service.Evaluate(Operation(StorageOperationType.InitializeGpt), readOnly).Allowed);
        Assert.True(_service.Evaluate(Operation(StorageOperationType.InitializeGpt), Snapshot()).Allowed);
    }

    [Fact]
    public void Create_partition_requires_initialized_writable_disk()
    {
        Assert.False(_service.Evaluate(Operation(StorageOperationType.CreatePartition), Snapshot()).Allowed);
        var offline = Snapshot() with { Disks = [Disk(2) with { PartitionStyle = "GPT", IsOffline = true }] };
        Assert.False(_service.Evaluate(Operation(StorageOperationType.CreatePartition), offline).Allowed);
        var ready = Snapshot() with { Disks = [Disk(2) with { PartitionStyle = "GPT" }] };
        Assert.True(_service.Evaluate(Operation(StorageOperationType.CreatePartition), ready).Allowed);
    }

    [Fact]
    public void Partition_change_requires_online_writable_disk_and_partition()
    {
        var operation = Operation(StorageOperationType.FormatPartition);
        var diskReadOnly = Snapshot(Partition()) with { Disks = [Disk(2) with { IsReadOnly = true }] };
        var partitionOffline = Snapshot(Partition() with { IsOffline = true });
        var partitionReadOnly = Snapshot(Partition() with { IsReadOnly = true });
        Assert.False(_service.Evaluate(operation, diskReadOnly).Allowed);
        Assert.False(_service.Evaluate(operation, partitionOffline).Allowed);
        Assert.False(_service.Evaluate(operation, partitionReadOnly).Allowed);
        Assert.True(_service.Evaluate(operation, Snapshot(Partition())).Allowed);
    }

    [Theory]
    [InlineData("NTFS", true)]
    [InlineData("FAT32", true)]
    [InlineData("exFAT", true)]
    [InlineData("ReFS", false)]
    public void Format_file_system_is_allowlisted(string fileSystem, bool expected)
    {
        var operation = Operation(StorageOperationType.FormatPartition) with { FileSystem = fileSystem };
        Assert.Equal(expected, _service.Evaluate(operation, Snapshot(Partition())).Allowed);
    }

    [Theory]
    [InlineData('D', true)]
    [InlineData('Z', true)]
    [InlineData('C', false)]
    [InlineData('1', false)]
    public void Drive_letter_is_validated(char letter, bool expected)
    {
        var operation = Operation(StorageOperationType.SetDriveLetter) with { DriveLetter = letter };
        Assert.Equal(expected, _service.Evaluate(operation, Snapshot(Partition())).Allowed);
    }

    [Fact]
    public void Set_drive_letter_requires_a_letter()
    {
        var operation = Operation(StorageOperationType.SetDriveLetter) with { DriveLetter = null };
        Assert.False(_service.Evaluate(operation, Snapshot(Partition())).Allowed);
    }

    [Theory]
    [InlineData(StorageOperationType.InitializeGpt)]
    [InlineData(StorageOperationType.InitializeMbr)]
    [InlineData(StorageOperationType.CreatePartition)]
    [InlineData(StorageOperationType.DeletePartition)]
    [InlineData(StorageOperationType.FormatPartition)]
    [InlineData(StorageOperationType.ResizePartition)]
    [InlineData(StorageOperationType.SetDiskReadOnly)]
    [InlineData(StorageOperationType.ClearDiskReadOnly)]
    public void High_risk_operations_require_reinforced_confirmation(StorageOperationType type)
    {
        var snapshot = type switch
        {
            StorageOperationType.CreatePartition => Snapshot() with { Disks = [Disk(2) with { PartitionStyle = "GPT" }] },
            StorageOperationType.DeletePartition or StorageOperationType.FormatPartition or StorageOperationType.ResizePartition => Snapshot(Partition()),
            _ => Snapshot()
        };
        var decision = _service.Evaluate(Operation(type), snapshot);
        Assert.True(decision.Allowed);
        Assert.True(decision.RequiresReinforcedConfirmation);
    }

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline)]
    [InlineData(StorageOperationType.SetDiskOffline)]
    [InlineData(StorageOperationType.SetDriveLetter)]
    [InlineData(StorageOperationType.RemoveDriveLetter)]
    [InlineData(StorageOperationType.SetVolumeLabel)]
    public void Lower_risk_mutations_do_not_require_reinforced_confirmation(StorageOperationType type)
    {
        var snapshot = RequiresPartition(type) ? Snapshot(Partition()) : Snapshot();
        var decision = _service.Evaluate(Operation(type), snapshot);
        Assert.True(decision.Allowed);
        Assert.False(decision.RequiresReinforcedConfirmation);
    }

    [Fact]
    public void Blocks_partition_that_hosts_the_running_executable()
    {
        var service = new SafetyService('D');
        var decision = service.Evaluate(Operation(StorageOperationType.FormatPartition), Snapshot(Partition() with { DriveLetter = "D" }));
        Assert.False(decision.Allowed);
        Assert.Contains("aplicativo", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<StorageOperationType> AllOperationTypes
    {
        get
        {
            var data = new TheoryData<StorageOperationType>();
            foreach (var type in Enum.GetValues<StorageOperationType>()) data.Add(type);
            return data;
        }
    }

    private static StorageOperation Operation(StorageOperationType type) => new()
    {
        Type = type,
        DiskNumber = 2,
        PartitionNumber = RequiresPartition(type) ? 1 : null,
        ExpectedDiskIdentity = "disk-2",
        SizeBytes = type == StorageOperationType.ResizePartition ? 5_000_000_000 : null
    };

    private static bool RequiresPartition(StorageOperationType type) => type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
    private static DiskInfo Disk(int number) => new() { Number = number, UniqueId = $"disk-{number}", FriendlyName = "Test disk", Size = 100_000_000_000, PartitionStyle = "RAW" };
    private static PartitionInfo Partition() => new() { DiskNumber = 2, PartitionNumber = 1, AccessPaths = "", DriveLetter = "D", Type = "Basic", Offset = 1_000_000, Size = 10_000_000_000, FileSystem = "NTFS" };
    private static StorageSnapshot Snapshot(params PartitionInfo[] partitions) => new([Disk(2)], partitions, DateTimeOffset.Now);
}
