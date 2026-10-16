using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.ViewModels;
using System.Text.Json;
using System.IO;

namespace GuiaSys.DiskManager.Tests;

public sealed class ModelAndQueueTests
{
    [Theory]
    [InlineData("EFI System Partition", "", "EFI")]
    [InlineData("Reserved", "", "MSR")]
    [InlineData("Basic", "NTFS", "NTFS")]
    [InlineData("Basic", "FAT32", "FAT32")]
    [InlineData("Recovery", "NTFS", "Recovery")]
    [InlineData("Unknown", "RAW", "RAW")]
    public void Partition_classifier_maps_known_types(string type, string fileSystem, string expected) => Assert.Equal(expected, PartitionClassifier.Classify(type, fileSystem));

    [Theory]
    [InlineData("20 GB", 20_000_000_000L)]
    [InlineData("512MB", 512_000_000L)]
    [InlineData("1,5 TB", 1_500_000_000_000L)]
    [InlineData("4096", 4096L)]
    public void Size_parser_accepts_human_input(string text, long expected) { Assert.True(MainViewModel.TryParseSize(text, out var actual)); Assert.Equal(expected, actual); }

    [Theory]
    [InlineData("")]
    [InlineData("-1 GB")]
    [InlineData("many GB")]
    [InlineData("999999999999999999999 TB")]
    public void Size_parser_rejects_invalid_input(string text) => Assert.False(MainViewModel.TryParseSize(text, out _));

    [Fact]
    public void Queue_preserves_order_and_undoes_last()
    {
        var queue = new OperationQueue(); var first = Operation(StorageOperationType.SetDiskOnline); var second = Operation(StorageOperationType.SetDiskReadOnly);
        queue.Enqueue(first); queue.Enqueue(second);
        Assert.Equal([first, second], queue.Snapshot()); Assert.True(queue.UndoLast()); Assert.Equal([first], queue.Snapshot());
    }

    [Fact]
    public void Queue_clear_removes_everything()
    {
        var queue = new OperationQueue(); queue.Enqueue(Operation(StorageOperationType.SetDiskOnline)); queue.Clear(); Assert.Empty(queue.Pending); Assert.False(queue.UndoLast());
    }

    [Fact]
    public void Disk_fallback_identity_is_explicitly_weak()
    {
        var disk = new DiskInfo { Number = 4, UniqueId = "", FriendlyName = "USB", SerialNumber = "ABC", Size = 32_000_000_000 };
        Assert.False(disk.HasStrongIdentity);
        Assert.Equal("weak:number:4|serial:ABC|size:32000000000", disk.Identity);
    }

    [Fact]
    public void Disk_unique_id_is_normalized_as_strong_identity()
    {
        var disk = new DiskInfo { Number = 4, UniqueId = "  ABC-123  ", FriendlyName = "USB", Size = 32_000_000_000 };
        Assert.True(disk.HasStrongIdentity);
        Assert.Equal("uid:ABC-123", disk.Identity);
    }

    [Fact]
    public void Operation_description_identifies_target()
    {
        var operation = Operation(StorageOperationType.DeletePartition) with { PartitionNumber = 3 };
        Assert.Contains("Partição 3", operation.Description); Assert.Contains("Disco 2", operation.Description);
    }

    [Fact]
    public void Inventory_accepts_null_temperature()
    {
        using var document = JsonDocument.Parse("""{"TemperatureC":null}""");
        Assert.Null(PowerShellDiskInventoryService.ReadNullableInt(document.RootElement, "TemperatureC"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"IsBoot":null}""")]
    [InlineData("""{"IsBoot":0}""")]
    [InlineData("""{"IsBoot":"false"}""")]
    public void Inventory_rejects_missing_or_invalid_required_boolean(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ReadRequiredBool(document.RootElement, "IsBoot"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Number":null}""")]
    [InlineData("""{"Number":"0"}""")]
    [InlineData("""{"Number":1.5}""")]
    public void Inventory_rejects_missing_or_invalid_required_integer(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ReadRequiredInt(document.RootElement, "Number"));
    }

    [Fact]
    public void Inventory_rejects_wrong_typed_nullable_temperature()
    {
        using var document = JsonDocument.Parse("""{"TemperatureC":"35"}""");
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ReadNullableInt(document.RootElement, "TemperatureC"));
    }

    [Fact]
    public void Inventory_rejects_semantically_inconsistent_snapshot()
    {
        var disk = new DiskInfo { Number = 2, UniqueId = "disk-2", FriendlyName = "Disk", Size = 1_000, PartitionStyle = "GPT" };
        var orphan = new PartitionInfo { DiskNumber = 3, PartitionNumber = 1, AccessPaths = "", DriveLetter = "", Type = "Basic", Offset = 0, Size = 100 };
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ValidateSnapshot([disk], [orphan]));
    }

    [Fact]
    public void Inventory_rejects_partition_outside_disk_bounds()
    {
        var disk = new DiskInfo { Number = 2, UniqueId = "disk-2", FriendlyName = "Disk", Size = 1_000, PartitionStyle = "GPT" };
        var partition = new PartitionInfo { DiskNumber = 2, PartitionNumber = 1, AccessPaths = "", DriveLetter = "", Type = "Basic", Offset = 900, Size = 200 };
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ValidateSnapshot([disk], [partition]));
    }

    [Fact]
    public void Inventory_rejects_duplicate_strong_disk_identity()
    {
        var first = new DiskInfo { Number = 2, UniqueId = "SAME", FriendlyName = "A", Size = 1_000, PartitionStyle = "GPT" };
        var second = new DiskInfo { Number = 3, UniqueId = "SAME", FriendlyName = "B", Size = 1_000, PartitionStyle = "GPT" };
        Assert.Throws<InvalidDataException>(() => PowerShellDiskInventoryService.ValidateSnapshot([first, second], []));
    }

    [Fact]
    public void Inventory_accepts_consistent_snapshot()
    {
        var disk = new DiskInfo { Number = 2, UniqueId = "disk-2", FriendlyName = "Disk", Size = 1_000, PartitionStyle = "GPT" };
        var partition = new PartitionInfo { DiskNumber = 2, PartitionNumber = 1, AccessPaths = "", DriveLetter = "", Type = "Basic", Offset = 100, Size = 200 };
        PowerShellDiskInventoryService.ValidateSnapshot([disk], [partition]);
    }

    private static StorageOperation Operation(StorageOperationType type) => new() { Type = type, DiskNumber = 2, ExpectedDiskIdentity = "disk-2" };
}
