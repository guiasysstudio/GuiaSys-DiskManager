using System.IO;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Read-only, fail-closed intersection of Windows filesystem limits, sector alignment
/// and authoritative adjacent disk geometry. No assumption about movable files, NTFS,
/// encryption, or unreported supported sizes is made.
/// </summary>
public static class PartitionResizePolicy
{
    public static bool TryResolve(
        StorageSnapshot snapshot, PartitionInfo partition,
        out SupportedResizeRange? range, out string reason)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(partition);
        range = null;
        var disk = snapshot.FindDisk(partition.DiskNumber);
        if (disk is null || !string.Equals(disk.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "O redimensionamento requer um disco GPT presente no inventário.";
            return false;
        }

        var sector = disk.LogicalSectorSize;
        if (sector is not (>= 512 and <= 65_536) || (sector.Value & (sector.Value - 1)) != 0)
        {
            reason = "O Windows não forneceu um tamanho confiável de setor lógico.";
            return false;
        }

        if (partition.ResizeMinBytes is not long min || partition.ResizeMaxBytes is not long max ||
            min <= 0 || max < min || min > partition.Size || max < partition.Size)
        {
            reason = "Limites de redimensionamento não disponíveis ou inconsistentes no Windows.";
            return false;
        }

        if (partition.Offset < 0 || partition.Size <= 0 || partition.Size % sector.Value != 0 ||
            partition.Offset > disk.Size || partition.Size > disk.Size - partition.Offset ||
            max > disk.Size - partition.Offset)
        {
            reason = "Geometria de partição e limites informados são inconsistentes.";
            return false;
        }

        DiskLayout? layout;
        try { layout = StorageGeometry.Build(snapshot).FindDisk(disk.Number); }
        catch (InvalidDataException)
        {
            reason = "Geometria de armazenamento inconsistente; atualize o inventário.";
            return false;
        }
        if (layout is null ||
            !layout.Extents.Any(e => e.PartitionNumber == partition.PartitionNumber &&
                e.Offset == partition.Offset && e.Length == partition.Size))
        {
            reason = "A partição mudou ou não consta do mapa de armazenamento.";
            return false;
        }

        var next = layout.Extents.FirstOrDefault(e =>
            e.IsUnallocated && e.Offset == partition.Offset + partition.Size);
        var physicalMax = next is null ? partition.Size : checked(partition.Size + next.Length);
        var high = Math.Min(max, physicalMax);
        var low = AlignUp(min, sector.Value);
        high -= high % sector.Value;
        if (low <= 0 || high < low || partition.Size < low || partition.Size > high)
        {
            reason = "Não existe intervalo alinhado válido entre o limite do Windows e o layout físico.";
            return false;
        }
        range = new SupportedResizeRange(low, high, partition.Size, sector.Value);
        reason = range.CanResize
            ? "Intervalo confirmado para planejamento; execução e espaço real devem ser revalidados."
            : "O Windows não oferece tamanho diferente do atual para esta partição.";
        return true;
    }

    public static SafetyDecision Evaluate(StorageSnapshot snapshot, PartitionInfo partition, long requestedBytes)
    {
        if (!TryResolve(snapshot, partition, out var limits, out var reason))
            return SafetyDecision.Deny(reason);
        if (limits is null || !limits.CanResize)
            return SafetyDecision.Deny("O Windows não permite aumentar nem reduzir esta partição no estado atual.");
        if (requestedBytes <= 0 || requestedBytes % limits.LogicalSectorBytes != 0)
            return SafetyDecision.Deny("O tamanho solicitado não está alinhado ao setor lógico do disco.");
        if (requestedBytes == limits.CurrentBytes)
            return SafetyDecision.Deny("O tamanho solicitado é igual ao tamanho atual da partição.");
        if (requestedBytes < limits.MinimumBytes || requestedBytes > limits.MaximumBytes)
            return SafetyDecision.Deny(
                $"Tamanho fora do intervalo suportado: {ByteSize.Format(limits.MinimumBytes)} a {ByteSize.Format(limits.MaximumBytes)}.");
        return SafetyDecision.Permit(reinforced: true);
    }

    private static long AlignUp(long value, int sector)
    {
        var remainder = value % sector;
        if (remainder == 0) return value;
        return value > long.MaxValue - (sector - remainder)
            ? long.MaxValue : value + sector - remainder;
    }
}

public sealed record SupportedResizeRange(
    long MinimumBytes, long MaximumBytes, long CurrentBytes, int LogicalSectorBytes)
{
    public bool CanShrink => MinimumBytes < CurrentBytes;
    public bool CanExpand => MaximumBytes > CurrentBytes;
    public bool CanResize => CanShrink || CanExpand;
    public long? SuggestedSizeBytes => CanExpand ? MaximumBytes
        : CanShrink ? MinimumBytes : null;
    public string MinimumLabel => ByteSize.Format(MinimumBytes);
    public string MaximumLabel => ByteSize.Format(MaximumBytes);
    public string CurrentLabel => ByteSize.Format(CurrentBytes);
    public string DirectionLabel => CanShrink && CanExpand ? "Reduzir ou expandir" :
        CanShrink ? "Somente reduzir" : CanExpand ? "Somente expandir" : "Sem redimensionamento disponível";
}
