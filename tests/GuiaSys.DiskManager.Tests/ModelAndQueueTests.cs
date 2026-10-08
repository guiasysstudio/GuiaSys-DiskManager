using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Services;
using GuiaSys.DiskManager.ViewModels;
using System.Text.Json;

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
    public void Disk_fallback_identity_is_stable()
    {
        var disk = new DiskInfo { Number = 4, UniqueId = "", FriendlyName = "USB", SerialNumber = "ABC", Size = 32_000_000_000 };
        Assert.Equal("number:4|serial:ABC|size:32000000000", disk.Identity);
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

    private static StorageOperation Operation(StorageOperationType type) => new() { Type = type, DiskNumber = 2, ExpectedDiskIdentity = "disk-2" };
}
