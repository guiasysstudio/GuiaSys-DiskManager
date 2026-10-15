using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Independently compares Windows inventory BEFORE and AFTER a mutating command.
/// A successful process exit never implies success without a verified postcondition.
/// The verifier does not execute storage operations and never authorizes a retry.
/// </summary>
public static class OperationPostconditionVerifier
{
    public static PostconditionDecision Verify(StorageOperation operation, StorageSnapshot before, StorageSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var diskBefore = before.FindDisk(operation.DiskNumber);
        var diskAfter = after.FindDisk(operation.DiskNumber);
        if (diskBefore is null || diskAfter is null ||
            !diskBefore.HasStrongIdentity || !diskAfter.HasStrongIdentity ||
            !string.Equals(diskBefore.Identity, operation.ExpectedDiskIdentity, StringComparison.Ordinal) ||
            !string.Equals(diskAfter.Identity, diskBefore.Identity, StringComparison.Ordinal) ||
            diskBefore.Size != diskAfter.Size)
            return PostconditionDecision.Uncertain("A identidade do disco mudou ou não pôde ser verificada.");

        // Only InitializeGpt/Mbr may change the disk partition style.
        if (operation.Type is not (StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr) &&
            !diskBefore.PartitionStyle.Equals(diskAfter.PartitionStyle, StringComparison.OrdinalIgnoreCase))
            return PostconditionDecision.Uncertain("O estilo do disco mudou inesperadamente.");

        var prior = before.Partitions.Where(x => x.DiskNumber == operation.DiskNumber).ToArray();
        var current = after.Partitions.Where(x => x.DiskNumber == operation.DiskNumber).ToArray();
        PartitionInfo? targetBefore = operation.PartitionNumber is int pn ? before.FindPartition(operation.DiskNumber, pn) : null;
        PartitionInfo? targetAfter = operation.PartitionNumber is int partNumber ? after.FindPartition(operation.DiskNumber, partNumber) : null;

        if (targetBefore is not null &&
            (string.IsNullOrWhiteSpace(operation.ExpectedPartitionIdentity) ||
             !string.Equals(targetBefore.Identity, operation.ExpectedPartitionIdentity, StringComparison.Ordinal)))
            return PostconditionDecision.Uncertain("O alvo original da operação não é verificável.");

        if (operation.Type is StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr)
        {
            var style = operation.Type == StorageOperationType.InitializeGpt ? "GPT" : "MBR";
            return diskBefore.PartitionStyle.Equals("RAW", StringComparison.OrdinalIgnoreCase) &&
                prior.Length == 0 && current.Length == 0 &&
                diskAfter.PartitionStyle.Equals(style, StringComparison.OrdinalIgnoreCase)
                ? PostconditionDecision.Confirmed() : PostconditionDecision.Uncertain("A inicialização não foi confirmada integralmente.");
        }

        if (operation.Type == StorageOperationType.CreatePartition)
        {
            var unchanged = prior.All(x => current.Any(y => SamePartition(x, y)));
            var introduced = current.Where(x => !prior.Any(y => SamePartition(y, x))).ToArray();
            var validNew = introduced.Length == 1 && introduced[0].HasStrongIdentity &&
                (operation.SizeBytes is null || introduced[0].Size == operation.SizeBytes.Value);
            return unchanged && validNew && current.Length == prior.Length + 1
                ? PostconditionDecision.Confirmed()
                : PostconditionDecision.Uncertain("Não foi possível confirmar exatamente uma partição nova preservando as existentes.");
        }

        if (operation.Type is StorageOperationType.SetDiskOnline or StorageOperationType.SetDiskOffline or
            StorageOperationType.SetDiskReadOnly or StorageOperationType.ClearDiskReadOnly)
        {
            var expected = operation.Type switch
            {
                StorageOperationType.SetDiskOnline => !diskAfter.IsOffline,
                StorageOperationType.SetDiskOffline => diskAfter.IsOffline,
                StorageOperationType.SetDiskReadOnly => diskAfter.IsReadOnly,
                _ => !diskAfter.IsReadOnly
            };
            return expected && IdenticalTopology(prior, current)
                ? PostconditionDecision.Confirmed()
                : PostconditionDecision.Uncertain("O estado solicitado ou a topologia do disco não foi confirmado.");
        }

        if (targetBefore is null)
            return PostconditionDecision.Uncertain("Não foi possível localizar a partição anterior.");

        if (operation.Type == StorageOperationType.DeletePartition)
            return targetAfter is null && !current.Any(x => SamePartition(x, targetBefore)) &&
                current.Length == prior.Length - 1 &&
                prior.Where(x => x.PartitionNumber != targetBefore.PartitionNumber)
                    .All(x => current.Any(y => SamePartition(x, y)))
                ? PostconditionDecision.Confirmed()
                : PostconditionDecision.Uncertain("A exclusão ou preservação das demais partições não pôde ser confirmada.");

        if (targetAfter is null || !SameTargetGuidAndLocation(targetBefore, targetAfter))
            return PostconditionDecision.Uncertain("A partição de destino desapareceu ou foi substituída.");

        var othersUnchanged = prior.Where(x => x.PartitionNumber != targetBefore.PartitionNumber)
            .All(x => current.Any(y => SamePartition(x, y))) && current.Length == prior.Length;
        if (!othersUnchanged)
            return PostconditionDecision.Uncertain("Outra partição mudou durante a operação.");

        var targetValid = operation.Type switch
        {
            StorageOperationType.FormatPartition =>
                SamePartition(targetBefore, targetAfter) &&
                targetAfter.FileSystem.Equals(operation.FileSystem, StringComparison.OrdinalIgnoreCase) &&
                targetAfter.Label.Equals(operation.Label, StringComparison.Ordinal),
            StorageOperationType.SetDriveLetter =>
                SamePartition(targetBefore, targetAfter) &&
                targetAfter.DriveLetter.Equals(operation.DriveLetter?.ToString(), StringComparison.OrdinalIgnoreCase),
            StorageOperationType.RemoveDriveLetter =>
                SamePartition(targetBefore, targetAfter) && targetAfter.DriveLetter.Length == 0,
            StorageOperationType.SetVolumeLabel =>
                SamePartition(targetBefore, targetAfter) &&
                targetAfter.Label.Equals(operation.Label, StringComparison.Ordinal),
            StorageOperationType.ResizePartition =>
                operation.SizeBytes is > 0 && targetAfter.Size == operation.SizeBytes.Value,
            _ => false
        };
        return targetValid ? PostconditionDecision.Confirmed()
            : PostconditionDecision.Uncertain("O resultado solicitado não foi comprovado pelo inventário independente.");
    }

    private static bool IdenticalTopology(PartitionInfo[] before, PartitionInfo[] after) =>
        before.Length == after.Length && before.All(x => after.Any(y => SamePartition(x, y)));

    private static bool SamePartition(PartitionInfo a, PartitionInfo b) =>
        a.DiskNumber == b.DiskNumber &&
        a.PartitionNumber == b.PartitionNumber &&
        a.Offset == b.Offset && a.Size == b.Size &&
        a.Type.Equals(b.Type, StringComparison.OrdinalIgnoreCase) &&
        a.GptType.Equals(b.GptType, StringComparison.OrdinalIgnoreCase) &&
        a.PartitionGuid.Equals(b.PartitionGuid, StringComparison.OrdinalIgnoreCase);

    private static bool SameTargetGuidAndLocation(PartitionInfo a, PartitionInfo b) =>
        a.DiskNumber == b.DiskNumber && a.PartitionNumber == b.PartitionNumber &&
        a.Offset == b.Offset &&
        a.Type.Equals(b.Type, StringComparison.OrdinalIgnoreCase) &&
        a.GptType.Equals(b.GptType, StringComparison.OrdinalIgnoreCase) &&
        a.PartitionGuid.Equals(b.PartitionGuid, StringComparison.OrdinalIgnoreCase);
}

public sealed record PostconditionDecision(bool Verified, string Reason)
{
    public static PostconditionDecision Confirmed() => new(true, "Resultado confirmado por novo inventário.");
    public static PostconditionDecision Uncertain(string reason) => new(false, reason);
}
