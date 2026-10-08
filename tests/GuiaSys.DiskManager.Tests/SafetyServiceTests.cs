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

    [Fact]
    public void Blocks_destructive_operation_on_disk_zero()
    {
        var disk = Disk(0) with { UniqueId = "disk0" };
        var snapshot = new StorageSnapshot([disk], [], DateTimeOffset.Now);
        var operation = Operation(StorageOperationType.InitializeGpt) with { DiskNumber = 0, ExpectedDiskIdentity = "disk0" };
        Assert.False(_service.Evaluate(operation, snapshot).Allowed);
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

    [Fact]
    public void Blocks_whole_disk_change_when_recovery_exists()
    {
        var decision = _service.Evaluate(Operation(StorageOperationType.SetDiskOffline), Snapshot(Partition() with { Type = "Recovery" }));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Initialize_requires_raw_empty_disk()
    {
        var snapshot = Snapshot() with { Disks = [Disk(2) with { PartitionStyle = "GPT" }] };
        Assert.False(_service.Evaluate(Operation(StorageOperationType.InitializeGpt), snapshot).Allowed);
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

    [Fact]
    public void Destructive_operation_requires_reinforced_confirmation()
    {
        var decision = _service.Evaluate(Operation(StorageOperationType.DeletePartition), Snapshot(Partition()));
        Assert.True(decision.Allowed); Assert.True(decision.RequiresReinforcedConfirmation);
    }

    [Fact]
    public void Blocks_partition_that_hosts_the_running_executable()
    {
        var service = new SafetyService('D');
        var decision = service.Evaluate(Operation(StorageOperationType.FormatPartition), Snapshot(Partition() with { DriveLetter = "D" }));
        Assert.False(decision.Allowed);
        Assert.Contains("aplicativo", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static StorageOperation Operation(StorageOperationType type) => new() { Type = type, DiskNumber = 2, PartitionNumber = RequiresPartition(type) ? 1 : null, ExpectedDiskIdentity = "disk-2" };
    private static bool RequiresPartition(StorageOperationType type) => type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
    private static DiskInfo Disk(int number) => new() { Number = number, UniqueId = $"disk-{number}", FriendlyName = "Test disk", Size = 100_000_000_000, PartitionStyle = "RAW" };
    private static PartitionInfo Partition() => new() { DiskNumber = 2, PartitionNumber = 1, AccessPaths = "", DriveLetter = "D", Type = "Basic", Offset = 1_000_000, Size = 10_000_000_000, FileSystem = "NTFS" };
    private static StorageSnapshot Snapshot(params PartitionInfo[] partitions) => new([Disk(2)], partitions, DateTimeOffset.Now);
}
