using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Tests;

/// <summary>Only synthetic disk inventories and pure decisions: NEVER writes to disk.</summary>
public sealed class EssentialOperationsTests
{
    private static readonly SafetyService Safety = new('X');
    private const long Mb = 1_000_000;
    private const string DataGuid = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";
    private static DiskInfo Disk(string style = "GPT") => new()
    {
        Number = 6, UniqueId = "c07-synthetic-vhdx", FriendlyName = "Fictitious",
        Size = 1000 * Mb, PartitionStyle = style
    };
    private static PartitionInfo Part(int number = 1, long offset = 100 * Mb, long size = 300 * Mb,
        string letter = "F") => new()
    {
        DiskNumber = 6, PartitionNumber = number, Offset = offset, Size = size,
        PartitionGuid = number == 1 ? "a8ca5e21-04b4-42e9-838c-b12c0177a101"
            : "a8ca5e21-04b4-42e9-838c-b12c0177a102",
        GptType = DataGuid, DriveLetter = letter, Type = "Basic", FileSystem = "NTFS", AccessPaths = ""
    };
    private static StorageSnapshot State(string style = "GPT", params PartitionInfo[] partitions) =>
        new([Disk(style)], partitions, DateTimeOffset.UtcNow);

    private static StorageOperation Op(StorageOperationType type, PartitionInfo? part = null) =>
        new()
        {
            Type = type, DiskNumber = 6, ExpectedDiskIdentity = Disk().Identity,
            PartitionNumber = EssentialOperationValidator.RequiresPartition(type) ? part?.PartitionNumber : null,
            ExpectedPartitionIdentity = EssentialOperationValidator.RequiresPartition(type) ? part?.Identity : null,
            SizeBytes = type is StorageOperationType.CreatePartition or StorageOperationType.ResizePartition ? 100 * Mb : null,
            DriveLetter = type == StorageOperationType.SetDriveLetter ? 'G' : null
        };

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline)]
    [InlineData(StorageOperationType.SetDiskOffline)]
    [InlineData(StorageOperationType.SetDiskReadOnly)]
    [InlineData(StorageOperationType.ClearDiskReadOnly)]
    [InlineData(StorageOperationType.InitializeGpt)]
    [InlineData(StorageOperationType.InitializeMbr)]
    [InlineData(StorageOperationType.CreatePartition)]
    [InlineData(StorageOperationType.DeletePartition)]
    [InlineData(StorageOperationType.FormatPartition)]
    [InlineData(StorageOperationType.SetDriveLetter)]
    [InlineData(StorageOperationType.RemoveDriveLetter)]
    [InlineData(StorageOperationType.ResizePartition)]
    [InlineData(StorageOperationType.SetVolumeLabel)]
    public void Each_known_mutation_has_a_canonical_draft_schema(StorageOperationType type)
    {
        Assert.True(EssentialOperationValidator.Validate(Op(type, Part())).Allowed);
    }

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline)]
    [InlineData(StorageOperationType.InitializeGpt)]
    [InlineData(StorageOperationType.CreatePartition)]
    public void Whole_disk_commands_reject_an_accidental_partition_target(StorageOperationType type)
    {
        var op = Op(type) with { PartitionNumber = 1, ExpectedPartitionIdentity = Part().Identity };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Theory]
    [InlineData(StorageOperationType.FormatPartition)]
    [InlineData(StorageOperationType.SetDriveLetter)]
    [InlineData(StorageOperationType.RemoveDriveLetter)]
    [InlineData(StorageOperationType.DeletePartition)]
    [InlineData(StorageOperationType.SetVolumeLabel)]
    public void Partition_commands_reject_missing_strong_target_fingerprint(StorageOperationType type)
    {
        var op = Op(type, Part()) with { ExpectedPartitionIdentity = "" };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Theory]
    [InlineData(StorageOperationType.SetDiskOffline)]
    [InlineData(StorageOperationType.FormatPartition)]
    [InlineData(StorageOperationType.SetDriveLetter)]
    public void Unrelated_size_is_rejected_instead_of_silently_ignored(StorageOperationType type)
    {
        var op = Op(type, Part()) with { SizeBytes = 200 * Mb };
        Assert.Contains("tamanho", EssentialOperationValidator.Validate(op).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline)]
    [InlineData(StorageOperationType.DeletePartition)]
    [InlineData(StorageOperationType.SetVolumeLabel)]
    public void Unrelated_drive_letter_is_rejected(StorageOperationType type)
    {
        var op = Op(type, Part()) with { DriveLetter = 'G' };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Theory]
    [InlineData("InvalidFS")]
    [InlineData("")]
    [InlineData("ntfs")]
    public void Formatting_rejects_unrecognized_or_noncanonical_file_system(string fs)
    {
        var op = Op(StorageOperationType.FormatPartition, Part()) with { FileSystem = fs };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(700)]
    [InlineData(2_097_153)]
    public void Formatting_rejects_invalid_allocation_unit_sizes(int allocation)
    {
        var op = Op(StorageOperationType.FormatPartition, Part()) with { AllocationUnitSize = allocation };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Fact]
    public void Formatting_accepts_valid_cluster_size_but_other_operations_refuse_it()
    {
        var format = Op(StorageOperationType.FormatPartition, Part()) with { AllocationUnitSize = 4096 };
        Assert.True(EssentialOperationValidator.Validate(format).Allowed);
        Assert.False(EssentialOperationValidator.Validate(format with
        {
            Type = StorageOperationType.SetVolumeLabel
        }).Allowed);
    }

    [Theory]
    [InlineData('C')]
    [InlineData('a')]
    [InlineData('1')]
    [InlineData('[')]
    public void Drive_letter_is_an_explicit_single_uppercase_safe_letter(char letter)
    {
        var op = Op(StorageOperationType.SetDriveLetter, Part()) with { DriveLetter = letter };
        Assert.False(EssentialOperationValidator.Validate(op).Allowed);
    }

    [Fact]
    public void Missing_resize_size_fails_but_maximum_create_partition_is_valid()
    {
        Assert.False(EssentialOperationValidator.Validate(
            Op(StorageOperationType.ResizePartition, Part()) with { SizeBytes = null }).Allowed);
        Assert.True(EssentialOperationValidator.Validate(
            Op(StorageOperationType.CreatePartition) with { SizeBytes = null }).Allowed);
    }

    [Theory]
    [InlineData(StorageOperationType.FormatPartition)]
    [InlineData(StorageOperationType.SetVolumeLabel)]
    public void Relevant_volume_label_rejects_controls_and_long_values(StorageOperationType type)
    {
        Assert.False(EssentialOperationValidator.Validate(
            Op(type, Part()) with { Label = "name\nnew" }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(
            Op(type, Part()) with { Label = new string('a', 33) }).Allowed);
        Assert.True(EssentialOperationValidator.Validate(
            Op(type, Part()) with { Label = "Minha unidade" }).Allowed);
    }

    [Fact]
    public void Extraneous_label_and_format_options_are_not_permitted()
    {
        var original = Op(StorageOperationType.SetDiskOnline);
        Assert.False(EssentialOperationValidator.Validate(original with { Label = "Hidden stale label" }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(original with { AllocationUnitSize = 4096 }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(original with { QuickFormat = false }).Allowed);
        Assert.False(EssentialOperationValidator.Validate(original with { FileSystem = "exFAT" }).Allowed);
    }

    [Fact]
    public void Removal_of_nonexistent_drive_letter_is_rejected_before_dispatch()
    {
        var part = Part(letter: "");
        var op = Op(StorageOperationType.RemoveDriveLetter, part);
        var decision = Safety.Evaluate(op, State("GPT", part));
        Assert.False(decision.Allowed);
        Assert.Contains("não possui letra", decision.Reason);
    }

    [Fact]
    public void Mbr_partition_creation_is_disabled_until_postcondition_can_identify_it()
    {
        var disk = Disk("MBR");
        var op = Op(StorageOperationType.CreatePartition);
        var decision = Safety.Evaluate(op, new StorageSnapshot([disk], [], DateTimeOffset.UtcNow));
        Assert.False(decision.Allowed);
        Assert.Contains("GPT", decision.Reason);
    }

    [Fact]
    public void Gpt_creation_accepts_size_within_largest_contiguous_free_extent()
    {
        var part = Part();
        var op = Op(StorageOperationType.CreatePartition) with { SizeBytes = 590 * Mb };
        var decision = Safety.Evaluate(op, State("GPT", part));
        Assert.True(decision.Allowed); // largest free region is 600 MB
    }

    [Fact]
    public void Gpt_creation_fails_closed_when_size_exceeds_available_contiguous_space()
    {
        var part = Part();
        var op = Op(StorageOperationType.CreatePartition) with { SizeBytes = 601 * Mb };
        Assert.False(Safety.Evaluate(op, State("GPT", part)).Allowed);
        Assert.True(Safety.Evaluate(op with { SizeBytes = 600 * Mb }, State("GPT", part)).Allowed);
    }

    [Fact]
    public void Fully_partitioned_gpt_disk_rejects_even_maximum_size_creation()
    {
        var all = Part(offset: 0, size: 1000 * Mb);
        var op = Op(StorageOperationType.CreatePartition) with { SizeBytes = null };
        Assert.False(Safety.Evaluate(op, State("GPT", all)).Allowed);
    }

    [Fact]
    public void Partition_expansion_cannot_cross_occupied_extent_even_on_direct_safety_call()
    {
        var first = Part();
        var next = Part(2, offset: 500 * Mb, size: 300 * Mb, letter: "G");
        var operation = Op(StorageOperationType.ResizePartition, first);
        Assert.False(Safety.Evaluate(operation with { SizeBytes = 401 * Mb }, State("GPT", first, next)).Allowed);
        Assert.True(Safety.Evaluate(operation with { SizeBytes = 400 * Mb }, State("GPT", first, next)).Allowed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void Unknown_partition_identity_refuses_other_layout_mutations(string guid)
    {
        var partition = Part() with { PartitionGuid = guid };
        var operation = Op(StorageOperationType.CreatePartition);
        Assert.False(Safety.Evaluate(operation, State("GPT", partition)).Allowed);
    }

    [Fact]
    public void Missing_gpt_type_refuses_layout_mutation_even_if_existing_partition_has_guid()
    {
        var partition = Part() with { GptType = "" };
        Assert.False(Safety.Evaluate(Op(StorageOperationType.CreatePartition), State("GPT", partition)).Allowed);
    }

    [Fact]
    public void Null_label_from_external_caller_is_denied_not_crashed()
    {
        var partition = Part();
        var operation = Op(StorageOperationType.SetVolumeLabel, partition) with { Label = null! };
        var decision = Safety.Evaluate(operation, State("GPT", partition));
        Assert.False(decision.Allowed);
    }

    [Fact]
    public void Malformed_geometry_is_refused_in_security_preflight()
    {
        var first = Part();
        var overlap = Part(2, offset: 200 * Mb, size: 500 * Mb);
        var op = Op(StorageOperationType.CreatePartition);
        Assert.False(Safety.Evaluate(op, State("GPT", first, overlap)).Allowed);
    }

    [Fact]
    public void Write_gate_stays_closed_after_adding_essential_operation_validation()
    {
        Assert.False(GuiaSys.DiskManager.Licensing.StorageWriteGate.ReleasedAfterSafetyAudit);
    }
}
