using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.ViewModels;

/// <summary>
/// Read-only UI decision for planning shortcuts. It never authorizes execution.
/// Only SafetyService and the executor decide whether a queued mutation may run.
/// </summary>
public static class ContextOperationPolicy
{
    public static ContextOperationChoice Evaluate(
        StorageOperationType type, string label, DiskInfo? disk, PartitionInfo? partition,
        StorageSnapshot snapshot, ISafetyService safety)
    {
        if (disk is null)
            return new(type, label, false, "Selecione um disco para preparar esta operação.");

        var partitionTarget = RequiresPartition(type);
        if (partitionTarget && partition is null)
            return new(type, label, false, "Selecione uma partição no mapa ou na tabela.");
        if (!partitionTarget && partition is not null)
            return new(type, label, false, "Esta ação utiliza o disco inteiro. Desmarque a partição.");

        // Synthetic, non-destructive parameters are used ONLY for evaluating UI availability.
        // Real user inputs are checked again when enqueued and independently in the executor.
        var freeLetter = Enumerable.Range('D', 'Z' - 'D' + 1)
            .Select(number => (char)number)
            .FirstOrDefault(letter => snapshot.Partitions.All(p =>
                !string.Equals(p.DriveLetter, letter.ToString(), StringComparison.OrdinalIgnoreCase)));

        if (type == StorageOperationType.SetDriveLetter && freeLetter == default)
            return new(type, label, false, "Não há letras de unidade livres.");

        var probe = new StorageOperation
        {
            Type = type,
            DiskNumber = disk.Number,
            PartitionNumber = partitionTarget ? partition?.PartitionNumber : null,
            ExpectedDiskIdentity = disk.Identity,
            ExpectedPartitionIdentity = partitionTarget ? partition?.Identity : null,
            SizeBytes = type == StorageOperationType.ResizePartition ? partition?.Size : null,
            FileSystem = "NTFS",
            Label = "",
            DriveLetter = type == StorageOperationType.SetDriveLetter ? freeLetter : null
        };
        var result = safety.Evaluate(probe, snapshot);
        return new(type, label, result.Allowed,
            result.Allowed ? "Disponível para planejamento; a gravação continua bloqueada."
                : result.Reason);
    }

    public static bool RequiresPartition(StorageOperationType type) =>
        type is StorageOperationType.DeletePartition or StorageOperationType.FormatPartition
            or StorageOperationType.SetDriveLetter or StorageOperationType.RemoveDriveLetter
            or StorageOperationType.ResizePartition or StorageOperationType.SetVolumeLabel;
}

public sealed record ContextOperationChoice(
    StorageOperationType Type, string Display, bool IsAvailable, string Reason);
