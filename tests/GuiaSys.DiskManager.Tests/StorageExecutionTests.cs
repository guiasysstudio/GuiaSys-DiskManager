using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Operations;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Tests;

public sealed class StorageExecutionTests
{
    private static DiskInfo Disk(string style = "GPT") => new()
    {
        Number = 3, UniqueId = "test-vhdx-3", FriendlyName = "Synthetic only",
        PartitionStyle = style, Size = 8_000_000_000
    };

    private static PartitionInfo Part(int number = 1, long size = 2_000_000_000,
        string filesystem = "NTFS", string letter = "F", string label = "") => new()
    {
        DiskNumber = 3, PartitionNumber = number, Offset = number == 1 ? 1_048_576 : 3_000_000_000,
        Size = size, PartitionGuid = number == 1 ? "775d97c3-5876-4ed7-8d7c-21a28903c001"
            : "775d97c3-5876-4ed7-8d7c-21a28903c002",
        GptType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7",
        Type = "Basic", FileSystem = filesystem, DriveLetter = letter, Label = label, AccessPaths = ""
    };

    private static StorageSnapshot State(DiskInfo? disk = null, params PartitionInfo[] partitions) =>
        new([disk ?? Disk()], partitions, DateTimeOffset.UtcNow);

    private static StorageOperation Op(StorageOperationType type, PartitionInfo? part = null,
        long? size = null, string filesystem = "NTFS", string label = "", char? letter = null) => new()
    {
        Type = type, DiskNumber = 3, PartitionNumber = part?.PartitionNumber,
        ExpectedDiskIdentity = Disk().Identity, ExpectedPartitionIdentity = part?.Identity,
        SizeBytes = size, FileSystem = filesystem, Label = label, DriveLetter = letter
    };

    [Theory]
    [InlineData(StorageOperationType.SetDiskOnline, true, false)]
    [InlineData(StorageOperationType.SetDiskOffline, false, true)]
    [InlineData(StorageOperationType.SetDiskReadOnly, false, true)]
    [InlineData(StorageOperationType.ClearDiskReadOnly, true, false)]
    public void Disk_state_transitions_need_independently_observed_effect(
        StorageOperationType type, bool was, bool expected)
    {
        var before = Disk() with { IsOffline = type is StorageOperationType.SetDiskOnline or StorageOperationType.SetDiskOffline && was,
            IsReadOnly = type is StorageOperationType.SetDiskReadOnly or StorageOperationType.ClearDiskReadOnly && was };
        var after = before with
        {
            IsOffline = type is StorageOperationType.SetDiskOnline or StorageOperationType.SetDiskOffline ? expected : before.IsOffline,
            IsReadOnly = type is StorageOperationType.SetDiskReadOnly or StorageOperationType.ClearDiskReadOnly ? expected : before.IsReadOnly
        };
        var op = Op(type);
        Assert.True(OperationPostconditionVerifier.Verify(op, State(before), State(after)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(op, State(before), State(before)).Verified);
    }

    [Theory]
    [InlineData(StorageOperationType.InitializeGpt, "GPT")]
    [InlineData(StorageOperationType.InitializeMbr, "MBR")]
    public void Initialization_requires_exact_style_change_on_empty_raw_disk(StorageOperationType type, string style)
    {
        var operation = Op(type);
        var before = State(Disk("RAW"));
        Assert.True(OperationPostconditionVerifier.Verify(operation, before, State(Disk(style))).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, before, before).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, before, State(Disk(style), Part())).Verified);
    }

    [Fact]
    public void Create_partition_requires_exactly_one_new_identity_and_preserves_existing()
    {
        var first = Part();
        var added = Part(number: 2, size: 500_000_000);
        var operation = Op(StorageOperationType.CreatePartition, size: 500_000_000);
        Assert.True(OperationPostconditionVerifier.Verify(operation, State(null, first), State(null, first, added)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, first), State(null, first)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, first), State(null, first,
            added with { Size = 600_000_000 })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, first),
            State(null, first with { Size = 1_900_000_000 }, added)).Verified);
    }

    [Fact]
    public void Delete_partition_requires_removal_and_preservation_of_other_partitions()
    {
        var first = Part();
        var second = Part(number: 2, size: 500_000_000);
        var op = Op(StorageOperationType.DeletePartition, first);
        Assert.True(OperationPostconditionVerifier.Verify(op, State(null, first, second), State(null, second)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, first, second), State(null, first, second)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, first, second),
            State(null, second with { Offset = 4_000_000_000 })).Verified);
    }

    [Fact]
    public void Format_needs_filesystem_and_label_confirmed_without_geometry_change()
    {
        var first = Part(label: "Previous");
        var operation = Op(StorageOperationType.FormatPartition, first, filesystem: "exFAT", label: "New");
        Assert.True(OperationPostconditionVerifier.Verify(operation, State(null, first),
            State(null, first with { FileSystem = "exFAT", Label = "New" })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, first),
            State(null, first with { FileSystem = "NTFS", Label = "New" })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, first),
            State(null, first with { FileSystem = "exFAT", Size = first.Size + 1, Label = "New" })).Verified);
    }

    [Fact]
    public void Drive_letter_changes_and_removal_have_independent_postconditions()
    {
        var p = Part(letter: "F");
        var changed = p with { DriveLetter = "G" };
        Assert.True(OperationPostconditionVerifier.Verify(
            Op(StorageOperationType.SetDriveLetter, p, letter: 'G'), State(null, p), State(null, changed)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(
            Op(StorageOperationType.SetDriveLetter, p, letter: 'G'), State(null, p), State(null, p)).Verified);
        Assert.True(OperationPostconditionVerifier.Verify(
            Op(StorageOperationType.RemoveDriveLetter, p), State(null, p),
            State(null, p with { DriveLetter = "" })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(
            Op(StorageOperationType.RemoveDriveLetter, p), State(null, p), State(null, p)).Verified);
    }

    [Fact]
    public void Resize_requires_requested_size_and_same_partition_guid_and_offset()
    {
        var p = Part() with
        {
            VolumeCapacityBytes = 1_900_000_000,
            VolumeAvailableBytes = 500_000_000
        };
        var after = p with
        {
            Size = 1_500_000_000,
            VolumeCapacityBytes = 1_400_000_000,
            VolumeAvailableBytes = 400_000_000
        };
        var op = Op(StorageOperationType.ResizePartition, p, size: 1_500_000_000);
        Assert.True(OperationPostconditionVerifier.Verify(op, State(null, p),
            State(null, after)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, p),
            State(null, after with { Size = 1_500_000_001 })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, p),
            State(null, after with { Offset = p.Offset + 512 })).Verified);
    }

    [Fact]
    public void Resize_postcondition_rejects_successful_geometry_without_filesystem_capacity_proof()
    {
        var original = Part() with
        {
            VolumeCapacityBytes = 1_900_000_000,
            VolumeAvailableBytes = 800_000_000
        };
        var operation = Op(StorageOperationType.ResizePartition, original, size: 2_500_000_000);
        var expanded = original with
        {
            Size = 2_500_000_000,
            VolumeCapacityBytes = 2_400_000_000,
            VolumeAvailableBytes = 1_300_000_000
        };
        Assert.True(OperationPostconditionVerifier.Verify(operation,
            State(null, original), State(null, expanded)).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation,
            State(null, original), State(null, expanded with { VolumeCapacityBytes = 1_900_000_000 })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation,
            State(null, original), State(null, expanded with { VolumeCapacityBytes = null })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation,
            State(null, original), State(null, expanded with { FileSystem = "exFAT" })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation,
            State(null, original), State(null, expanded with { VolumeCapacityBytes = 1_500_000_000 })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation,
            State(null, original with { VolumeCapacityBytes = null }),
            State(null, expanded)).Verified);
    }

    [Fact]
    public void Label_change_requires_exact_new_label()
    {
        var p = Part(label: "Old");
        var operation = Op(StorageOperationType.SetVolumeLabel, p, label: "New");
        Assert.True(OperationPostconditionVerifier.Verify(operation, State(null, p),
            State(null, p with { Label = "New" })).Verified);
        Assert.False(OperationPostconditionVerifier.Verify(operation, State(null, p), State(null, p)).Verified);
    }

    [Fact]
    public void Changed_disk_identity_always_makes_result_indeterminate()
    {
        var p = Part();
        var op = Op(StorageOperationType.SetVolumeLabel, p, label: "New");
        var changed = Disk() with { UniqueId = "replaced-disk" };
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, p),
            State(changed, p with { Label = "New" })).Verified);
    }

    [Fact]
    public void Changing_unrelated_partition_invalidates_confirmation()
    {
        var first = Part(); var second = Part(number: 2, size: 500_000_000);
        var op = Op(StorageOperationType.SetVolumeLabel, first, label: "New");
        Assert.False(OperationPostconditionVerifier.Verify(op, State(null, first, second),
            State(null, first with { Label = "New" }, second with { Size = second.Size + 1 })).Verified);
    }

    [Fact]
    public void Journal_persists_prepared_dispatch_and_success_without_unresolved_attempt()
    {
        var path = TempPath();
        try
        {
            var journal = new OperationJournal(path);
            var attempt = Guid.NewGuid();
            var op = Op(StorageOperationType.CreatePartition);
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Prepared, "PREPARED"));
            Assert.Contains(attempt, journal.FindUnresolvedAttempts());
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Dispatching, "DISPATCHING"));
            Assert.Contains(attempt, new OperationJournal(path).FindUnresolvedAttempts());
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Reconciled,
                "POSTCONDITION_VERIFIED", StorageExecutionOutcome.Verified));
            Assert.Empty(new OperationJournal(path).FindUnresolvedAttempts());
            Assert.Equal(3, File.ReadAllLines(path).Length);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Indeterminate_attempt_is_never_silently_cleared_after_restart()
    {
        var path = TempPath();
        try
        {
            var journal = new OperationJournal(path);
            var attempt = Guid.NewGuid();
            var op = Op(StorageOperationType.DeletePartition, Part());
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Dispatching, "DISPATCHING"));
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Reconciled,
                "EXECUTION_UNKNOWN", StorageExecutionOutcome.Indeterminate));
            Assert.Equal([attempt], new OperationJournal(path).FindUnresolvedAttempts());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Truncated_journal_refuses_automatic_recovery()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, """{"AttemptId":"truncated" """);
            Assert.ThrowsAny<Exception>(() => new OperationJournal(path).FindUnresolvedAttempts());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Journal_rejects_malformed_codes_and_mismatched_attempt_identity()
    {
        var path = TempPath();
        try
        {
            var op = Op(StorageOperationType.InitializeGpt);
            var journal = new OperationJournal(path);
            Assert.Throws<ArgumentException>(() => journal.Append(
                OperationJournal.Record(Guid.NewGuid(), op, OperationPhase.Prepared, "secret=123")));
            var attempt = Guid.NewGuid();
            journal.Append(OperationJournal.Record(attempt, op, OperationPhase.Prepared, "PREPARED"));
            journal.Append(OperationJournal.Record(attempt, op with { Id = Guid.NewGuid() },
                OperationPhase.Reconciled, "REJECTED", StorageExecutionOutcome.Rejected));
            Assert.Throws<InvalidDataException>(() => journal.FindUnresolvedAttempts());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Journal_never_contains_freeform_fields_or_sensitive_operation_content()
    {
        var path = TempPath();
        try
        {
            var journal = new OperationJournal(path);
            var op = Op(StorageOperationType.SetVolumeLabel, Part(), label: "PRIVATE_LABEL") with
            {
                ExpectedDiskIdentity = "uid:PRIVATE_SERIAL"
            };
            journal.Append(OperationJournal.Record(Guid.NewGuid(), op, OperationPhase.Prepared, "PREPARED"));
            var contents = File.ReadAllText(path);
            Assert.DoesNotContain("PRIVATE_LABEL", contents);
            Assert.DoesNotContain("PRIVATE_SERIAL", contents);
            Assert.Contains("Prepared", contents);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Snapshot_digests_are_redacted_and_sensitive_to_change()
    {
        var a = OperationSnapshotDigest.Compute(State(null, Part(label: "PRIVATE")), 3);
        var b = OperationSnapshotDigest.Compute(State(null, Part(label: "PUBLIC")), 3);
        Assert.Equal(64, a.Length);
        Assert.NotEqual(a, b);
        var entry = OperationJournal.Record(Guid.NewGuid(), Op(StorageOperationType.SetVolumeLabel, Part()),
            OperationPhase.Prepared, "PREPARED", snapshotSha256: a);
        Assert.Equal(a, entry.SnapshotSha256);
    }

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"gdm-c04-journal-{Guid.NewGuid():N}.jsonl");
}
