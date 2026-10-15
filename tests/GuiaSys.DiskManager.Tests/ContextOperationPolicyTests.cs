using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;
using GuiaSys.DiskManager.ViewModels;

namespace GuiaSys.DiskManager.Tests;

public sealed class ContextOperationPolicyTests
{
    private static readonly SafetyService Safety = new((char?)null);
    private static DiskInfo Disk(int number = 2, string style = "GPT") =>
        new() { Number = number, UniqueId = $"context-{number}", FriendlyName = "Disposable",
            PartitionStyle = style, Size = 10_000_000_000 };
    private static PartitionInfo Part(int number = 2, string type = "Basic") =>
        new() { DiskNumber = number, PartitionNumber = 1, PartitionGuid = "63b99233-ed94-4851-8e36-38bd0123abcd",
            GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7", AccessPaths = "",
            DriveLetter = "E", Type = type, Offset = 1_048_576, Size = 2_000_000_000, FileSystem = "NTFS" };
    private static StorageSnapshot Snapshot(DiskInfo disk, params PartitionInfo[] partitions) =>
        new([disk], partitions, DateTimeOffset.UtcNow);

    [Fact]
    public void No_selection_disables_all_actions_with_explanations()
    {
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.CreatePartition,
            "Criar", null, null, Snapshot(Disk()), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("Selecione", result.Reason);
    }

    [Fact]
    public void Disk_selection_disables_partition_operations_without_target()
    {
        var disk = Disk();
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.FormatPartition,
            "Formatar", disk, null, Snapshot(disk), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("partição", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Partition_selection_disables_entire_disk_actions()
    {
        var disk = Disk();
        var partition = Part();
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.InitializeGpt,
            "Inicializar", disk, partition, Snapshot(disk, partition), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("disco inteiro", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disk_zero_refuses_even_nondestructive_disk_actions()
    {
        var disk = Disk(0);
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.SetDiskOnline,
            "Online", disk, null, Snapshot(disk), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("Disco 0", result.Reason);
    }

    [Fact]
    public void Gpt_basic_data_partition_offers_metadata_operation_as_simulation()
    {
        var disk = Disk();
        var partition = Part();
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.SetVolumeLabel,
            "Alterar rótulo", disk, partition, Snapshot(disk, partition), Safety);
        Assert.True(result.IsAvailable);
        Assert.Contains("planejamento", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_partition_is_disabled_with_safety_reason()
    {
        var disk = Disk();
        var partition = Part(type: "Recovery");
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.FormatPartition,
            "Formatar", disk, partition, Snapshot(disk, partition), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("proteg", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gpt_data_disk_allows_creating_new_partition_in_plan_only()
    {
        var disk = Disk();
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.CreatePartition,
            "Criar", disk, null, Snapshot(disk), Safety);
        Assert.True(result.IsAvailable);
        Assert.Contains("gravação continua bloqueada", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disk_with_recovery_partition_refuses_new_layout_changes()
    {
        var disk = Disk();
        var partition = Part(type: "Recovery");
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.CreatePartition,
            "Criar", disk, null, Snapshot(disk, partition), Safety);
        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Missing_guid_disables_partition_operations_even_with_disk_identity()
    {
        var disk = Disk();
        var partition = Part() with { PartitionGuid = "" };
        var result = ContextOperationPolicy.Evaluate(StorageOperationType.ResizePartition,
            "Redimensionar", disk, partition, Snapshot(disk, partition), Safety);
        Assert.False(result.IsAvailable);
        Assert.Contains("identidade", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extent_segments_expose_selection_without_changing_storage_models()
    {
        var partition = Part();
        var segment = new PartitionSegmentViewModel(partition, "E:", "NTFS", "NTFS", 2_000_000_000, 170, true);
        Assert.True(segment.IsSelected);
        Assert.False((segment with { IsSelected = false }).IsSelected);
    }
}
