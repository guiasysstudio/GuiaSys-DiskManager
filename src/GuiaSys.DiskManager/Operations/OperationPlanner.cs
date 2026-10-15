using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Operations;

public enum PlanStepState { Ready, Conflict }

public sealed record PlanStep(
    Guid OperationId, int Position, PlanStepState State, string Message,
    bool RequiresRefreshAfterExecution);

public sealed record OperationPlan(
    IReadOnlyList<PlanStep> Steps, StorageSnapshot ProjectedSnapshot,
    StorageLayout InitialLayout, bool CanApply)
{
    public PlanStep? FirstConflict => Steps.FirstOrDefault(x => x.State == PlanStepState.Conflict);
    public int AffectedDisks => Steps.Count == 0 ? 0 : ProjectedDiskNumbers.Count;
    public IReadOnlyList<int> ProjectedDiskNumbers { get; init; } = [];
}

public interface IOperationPlanner
{
    OperationPlan Plan(StorageSnapshot snapshot, IReadOnlyList<StorageOperation> operations);
}

/// <summary>
/// Deterministic, never-executing operation planner. Projects *only* reliable metadata;
/// operations that might change disk geometry or partition identity are treated as barriers.
/// A preview is not authorization to write: the executor must re-evaluate live identity and policy.
/// </summary>
public sealed class OperationPlanner(ISafetyService safety) : IOperationPlanner
{
    public OperationPlan Plan(StorageSnapshot snapshot, IReadOnlyList<StorageOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(operations);
        var initialLayout = StorageGeometry.Build(snapshot);
        var disks = snapshot.Disks.ToDictionary(x => x.Number);
        var partitions = snapshot.Partitions.ToDictionary(x => (x.DiskNumber, x.PartitionNumber));
        var steps = new List<PlanStep>(operations.Count);
        var affected = new HashSet<int>();
        var seenIds = new HashSet<Guid>();
        var uncertainDisks = new HashSet<int>();
        var uncertainPartitions = new HashSet<(int Disk, int Partition)>();

        foreach (var operation in operations)
        {
            if (operation is null) throw new ArgumentException("Fila contém operação nula.", nameof(operations));
            affected.Add(operation.DiskNumber);
            var number = steps.Count + 1;
            var message = "";
            var barrier = false;
            var target = (operation.DiskNumber, operation.PartitionNumber ?? -1);

            var projected = new StorageSnapshot(disks.Values.OrderBy(x => x.Number).ToArray(),
                partitions.Values.OrderBy(x => x.DiskNumber).ThenBy(x => x.Offset).ToArray(),
                snapshot.CapturedAt);

            if (operation.Id == Guid.Empty || !seenIds.Add(operation.Id))
                message = "Operação duplicada ou sem identificador único.";
            else if (!Enum.IsDefined(operation.Type))
                message = "Tipo de operação não reconhecido.";
            else if (uncertainDisks.Contains(operation.DiskNumber))
                message = "O layout do disco será alterado por uma etapa anterior. Atualize o inventário e recrie esta operação.";
            else if (operation.PartitionNumber is not null && uncertainPartitions.Contains(target))
                message = "A partição sofrerá uma alteração que exige novo inventário antes desta etapa.";
            else if (!disks.TryGetValue(operation.DiskNumber, out var disk))
                message = "O disco da operação não consta do inventário.";
            else if (!string.Equals(disk.Identity, operation.ExpectedDiskIdentity, StringComparison.Ordinal))
                message = "Identidade do disco divergente do inventário.";
            else
            {
                var decision = safety.Evaluate(operation, projected);
                if (!decision.Allowed) message = decision.Reason;
                else if (operation.Type == StorageOperationType.SetDriveLetter &&
                    operation.DriveLetter is char requested &&
                    partitions.Values.Any(x => x.DriveLetter.Equals(requested.ToString(), StringComparison.OrdinalIgnoreCase)
                        && (x.DiskNumber, x.PartitionNumber) != target))
                    message = "A letra solicitada já pertence a outra partição no plano projetado.";
                else if (operation.Type == StorageOperationType.ResizePartition &&
                    operation.SizeBytes is long requestedSize &&
                    (!partitions.TryGetValue(target, out var oldPartition) ||
                     !CanResizeWithinAdjacentExtent(initialLayout, projected, oldPartition, requestedSize)))
                    message = "Redimensionamento ultrapassa o espaço contíguo disponível ou a geometria válida.";
                else if (operation.Type == StorageOperationType.CreatePartition &&
                    (initialLayout.FindDisk(operation.DiskNumber) is not { } layout ||
                     (operation.SizeBytes ?? 1) > layout.LargestFreeExtentBytes))
                    message = "Não existe uma extensão livre contígua suficiente no inventário.";
                else
                {
                    barrier = IsDiskGeometryBarrier(operation.Type) || IsPartitionBarrier(operation.Type);
                    ApplyProjection(operation, disks, partitions);
                    if (IsDiskGeometryBarrier(operation.Type)) uncertainDisks.Add(operation.DiskNumber);
                    if (IsPartitionBarrier(operation.Type) && operation.PartitionNumber is int partNumber)
                        uncertainPartitions.Add((operation.DiskNumber, partNumber));
                }
            }
            steps.Add(new PlanStep(operation.Id, number,
                message.Length == 0 ? PlanStepState.Ready : PlanStepState.Conflict,
                message.Length == 0
                    ? barrier ? "Planejada; exigir novo inventário após execução." : "Planejada sobre o estado projetado."
                    : message,
                barrier));
        }

        return new OperationPlan(steps.ToArray(),
            new StorageSnapshot(disks.Values.OrderBy(x => x.Number).ToArray(),
                partitions.Values.OrderBy(x => x.DiskNumber).ThenBy(x => x.Offset).ToArray(),
                snapshot.CapturedAt),
            initialLayout, steps.All(x => x.State == PlanStepState.Ready))
        {
            ProjectedDiskNumbers = affected.OrderBy(x => x).ToArray()
        };
    }

    private static bool IsDiskGeometryBarrier(StorageOperationType type) => type is
        StorageOperationType.InitializeGpt or StorageOperationType.InitializeMbr or
        StorageOperationType.CreatePartition or StorageOperationType.DeletePartition or
        StorageOperationType.ResizePartition;

    private static bool IsPartitionBarrier(StorageOperationType type) =>
        type == StorageOperationType.FormatPartition;

    private static bool CanResizeWithinAdjacentExtent(StorageLayout initial,
        StorageSnapshot projected, PartitionInfo part, long requested)
    {
        if (requested <= 0) return false;
        var disk = projected.FindDisk(part.DiskNumber);
        if (disk is null || part.Offset > disk.Size || requested > disk.Size - part.Offset)
            return false;
        if (requested <= part.Size) return true; // filesystem constraints are not known until Windows validation
        var layout = initial.FindDisk(part.DiskNumber);
        if (layout is null) return false;
        var next = layout.Extents.FirstOrDefault(x => x.IsUnallocated && x.Offset == part.Offset + part.Size);
        return next is not null && requested - part.Size <= next.Length;
    }

    private static void ApplyProjection(StorageOperation op,
        IDictionary<int, DiskInfo> disks, IDictionary<(int, int), PartitionInfo> partitions)
    {
        var disk = disks[op.DiskNumber];
        disks[op.DiskNumber] = op.Type switch
        {
            StorageOperationType.SetDiskOnline => disk with { IsOffline = false },
            StorageOperationType.SetDiskOffline => disk with { IsOffline = true },
            StorageOperationType.SetDiskReadOnly => disk with { IsReadOnly = true },
            StorageOperationType.ClearDiskReadOnly => disk with { IsReadOnly = false },
            _ => disk
        };
        if (op.PartitionNumber is not int pn || !partitions.TryGetValue((op.DiskNumber, pn), out var part))
            return;
        partitions[(op.DiskNumber, pn)] = op.Type switch
        {
            StorageOperationType.SetDriveLetter => part with { DriveLetter = op.DriveLetter?.ToString() ?? part.DriveLetter },
            StorageOperationType.RemoveDriveLetter => part with { DriveLetter = "" },
            StorageOperationType.SetVolumeLabel => part with { Label = op.Label },
            // Format is a barrier; retain unknown filesystem/space until Windows is re-read.
            _ => part
        };
    }
}
