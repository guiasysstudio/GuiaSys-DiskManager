using System.IO;
using System.Text.Json;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class GdmH03InventoryIdentityTests
{
    private static DiskInfo Disk(int number, string uniqueId) => new()
    {
        Number = number, UniqueId = uniqueId, FriendlyName = "Dispositivo de teste",
        Size = 8_000_000_000, PartitionStyle = "GPT"
    };

    [Fact]
    public void Repeated_windows_unique_ids_remain_visible_but_have_no_strong_write_identity()
    {
        var result = PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(
            [Disk(1, "DUPLICATE"), Disk(2, "duplicate "), Disk(3, "UNIQUE")]);
        Assert.Equal(3, result.Length);
        Assert.All(result.Take(2), disk =>
        {
            Assert.False(disk.HasStrongIdentity);
            Assert.True(disk.HasAmbiguousUniqueId);
            Assert.Empty(disk.UniqueId);
            Assert.Contains("somente leitura", disk.IdentityStrengthLabel);
            Assert.StartsWith("weak:number:", disk.Identity);
        });
        Assert.True(result[2].HasStrongIdentity);
        Assert.False(result[2].HasAmbiguousUniqueId);
        Assert.Equal("UNIQUE", result[2].UniqueId);
        var snapshot = new StorageSnapshot(result, [], DateTimeOffset.UtcNow);
        Assert.Equal(3, StorageGeometry.Build(snapshot).Disks.Count);
        Assert.Equal(2, InventoryInsights.Build(snapshot).Overview.UnknownIdentityCount);
    }

    [Fact]
    public void Repeated_disk_identifiers_cannot_become_write_targets()
    {
        var disks = PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(
            [Disk(2, "DUP"), Disk(3, " DUP ")]);
        var snapshot = new StorageSnapshot(disks, [], DateTimeOffset.UtcNow);
        var operation = new StorageOperation
        {
            Type = StorageOperationType.CreatePartition, DiskNumber = 2,
            ExpectedDiskIdentity = disks[0].Identity, SizeBytes = 100_000_000
        };
        Assert.False(new SafetyService('X').Evaluate(operation, snapshot).Allowed);
    }

    [Fact]
    public void Nonduplicated_ids_and_missing_ids_do_not_change_to_strong()
    {
        var result = PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(
            [Disk(0, ""), Disk(1, "ONE"), Disk(2, "TWO")]);
        Assert.False(result[0].HasStrongIdentity);
        Assert.True(result[1].HasStrongIdentity);
        Assert.True(result[2].HasStrongIdentity);
        Assert.All(result, disk => Assert.False(disk.HasAmbiguousUniqueId));
    }

    [Fact]
    public void Ambiguity_flag_never_becomes_strong_even_if_unique_id_is_repopulated()
    {
        var original = PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(
            [Disk(1, "SAME"), Disk(2, "SAME")])[0];
        var tampered = original with { UniqueId = "NOT-UNIQUE" };
        Assert.False(tampered.HasStrongIdentity);
        Assert.StartsWith("weak:number:", tampered.Identity);
    }

    [Fact]
    public void Normal_inventory_json_with_duplicate_ids_parses_for_readonly_inventory()
    {
        object Entry(int number, string id) => new
        {
            Number = number, UniqueId = id, FriendlyName = "Disco virtual",
            PartitionStyle = "GPT", Size = 8_000_000_000L,
            IsBoot = false, IsSystem = false, IsOffline = false, IsReadOnly = false
        };
        var json = JsonSerializer.Serialize(new
        {
            Disks = new [] { Entry(0, "CLONED-UID"), Entry(2, "cloned-uid") },
            Partitions = Array.Empty<object>()
        });
        var parsed = PowerShellDiskInventoryService.ParseInventory(json, DateTimeOffset.UtcNow);
        Assert.Equal(2, parsed.Disks.Count);
        Assert.All(parsed.Disks, d => Assert.True(d.HasAmbiguousUniqueId));
        Assert.Equal(2, StorageGeometry.Build(parsed).Disks.Count);
    }

    [Fact]
    public void Duplicate_disk_numbers_still_fail_geometry_even_when_id_collision_downgraded()
    {
        var duplicates = PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(
            [Disk(2, "SAME"), Disk(2, "SAME")]);
        Assert.Throws<InvalidDataException>(() =>
            PowerShellDiskInventoryService.ValidateSnapshot(duplicates, []));
    }

    [Fact]
    public void Direct_untrusted_snapshots_with_duplicate_strong_ids_still_fail_closed()
    {
        Assert.Throws<InvalidDataException>(() =>
            PowerShellDiskInventoryService.ValidateSnapshot(
                [Disk(1, "DUP"), Disk(2, "DUP")], []));
    }

    [Fact]
    public void Null_disk_collection_cannot_be_silently_converted_to_empty_inventory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PowerShellDiskInventoryService.NormalizeAmbiguousDiskIdentities(null!));
    }
}
