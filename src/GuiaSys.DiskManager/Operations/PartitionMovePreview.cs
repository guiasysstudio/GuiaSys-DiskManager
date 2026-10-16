using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Geometric feasibility STUDY only. Moving a populated partition requires a verified
/// block-copy engine, exclusive access, journaling and recovery; none exists here.
/// A positive preview must never be used as an execution authorization.
/// </summary>
public static class PartitionMovePreview
{
    public const long AlignmentBytes = 1_048_576; // conservative 1 MiB planning grid

    public static MovePreviewDecision Analyze(StorageSnapshot snapshot, int diskNumber,
        int partitionNumber, long requestedOffset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (requestedOffset < 0)
            return MovePreviewDecision.Reject("O deslocamento de destino precisa ser positivo ou zero.");
        var disk = snapshot.FindDisk(diskNumber);
        var part = snapshot.FindPartition(diskNumber, partitionNumber);
        if (disk is null || part is null)
            return MovePreviewDecision.Reject("Disco ou partição não está presente no inventário.");
        if (diskNumber == 0 || !disk.HasStrongIdentity ||
            !string.Equals(disk.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
            return MovePreviewDecision.Reject("Prévia restrita a discos GPT com identidade forte, exceto Disco 0.");
        if (disk.IsSystem || disk.IsBoot || disk.IsReadOnly || disk.IsOffline ||
            snapshot.Partitions.Any(p => p.DiskNumber == diskNumber && SafetyService.IsProtected(p)))
            return MovePreviewDecision.Reject("Disco com Windows, inicialização, recuperação, proteção ou estado não gravável.");
        if (!part.HasStrongIdentity || string.IsNullOrWhiteSpace(part.GptType) ||
            SafetyService.IsProtected(part) || part.IsOffline || part.IsReadOnly)
            return MovePreviewDecision.Reject("Partição protegida ou sem identidade GPT verificável.");
        if (snapshot.Partitions.Where(p => p.DiskNumber == diskNumber)
            .Any(p => !p.HasStrongIdentity || string.IsNullOrWhiteSpace(p.GptType)))
            return MovePreviewDecision.Reject("Existem partições GPT sem identidade ou tipo verificável.");

        var sector = disk.LogicalSectorSize;
        if (sector is not (>= 512 and <= 65_536) ||
            (sector.Value & (sector.Value - 1)) != 0 ||
            AlignmentBytes % sector.Value != 0)
            return MovePreviewDecision.Reject("Tamanho do setor lógico ausente ou inconsistente.");
        if (part.Offset < 0 || part.Size <= 0 || part.Offset > disk.Size ||
            part.Size > disk.Size - part.Offset)
            return MovePreviewDecision.Reject("Origem com geometria inválida; atualize o inventário.");
        if (requestedOffset % AlignmentBytes != 0)
            return MovePreviewDecision.Reject("O destino precisa estar alinhado a 1 MiB.");
        if (requestedOffset == part.Offset)
            return MovePreviewDecision.Reject("O deslocamento solicitado é idêntico à posição atual.");
        if (part.Size <= 0 || requestedOffset > disk.Size || part.Size > disk.Size - requestedOffset)
            return MovePreviewDecision.Reject("A partição não cabe dentro do destino solicitado.");
        if (IntervalsOverlap(part.Offset, part.Size, requestedOffset, part.Size))
            return MovePreviewDecision.Reject(
                "Destino sobrepõe a origem: movimentação com sobreposição exige cópia em blocos específica.");

        DiskLayout? layout;
        try { layout = StorageGeometry.Build(snapshot).FindDisk(diskNumber); }
        catch (InvalidDataException)
        {
            return MovePreviewDecision.Reject("Geometria inconsistente; atualize o inventário.");
        }
        if (layout is null)
            return MovePreviewDecision.Reject("Mapa do disco não disponível.");
        if (!layout.Extents.Any(e => e.PartitionNumber == partitionNumber &&
                                     e.Offset == part.Offset && e.Length == part.Size))
            return MovePreviewDecision.Reject("Posição ou tamanho original não conferem com o mapa.");

        // Destination must fit ENTIRELY inside ONE already-unallocated extent.
        var area = layout.Extents.FirstOrDefault(e => e.IsUnallocated &&
            requestedOffset >= e.Offset && requestedOffset < e.EndExclusive &&
            part.Size <= e.EndExclusive - requestedOffset);
        if (area is null)
            return MovePreviewDecision.Reject("O destino não está inteiramente em uma única faixa não alocada.");

        return new MovePreviewDecision(true,
            "Prévia geométrica compatível. Nenhuma cópia ou alteração será executada.",
            part.Offset, requestedOffset, part.Size, area.Offset, area.Length,
            requestedOffset > part.Offset ? "Direita" : "Esquerda");
    }

    public static long? SuggestDestination(StorageSnapshot snapshot, int diskNumber, int partitionNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var part = snapshot.FindPartition(diskNumber, partitionNumber);
        if (part is null) return null;
        DiskLayout? layout;
        try { layout = StorageGeometry.Build(snapshot).FindDisk(diskNumber); }
        catch (InvalidDataException) { return null; }
        if (layout is null) return null;
        // Select the nearest beginning of an adequately sized unallocated extent,
        // not the first visually large bar on a non-linear partition map.
        var options = new List<long>();
        foreach (var extent in layout.Extents.Where(e => e.IsUnallocated))
        {
            var aligned = AlignUp(extent.Offset);
            if (aligned is long target && target != part.Offset &&
                target <= extent.EndExclusive && part.Size <= extent.EndExclusive - target &&
                Analyze(snapshot, diskNumber, partitionNumber, target).CanPreview)
                options.Add(target);
        }
        return options.OrderBy(target => AbsoluteDistance(target, part.Offset))
            .ThenBy(target => target).Select(target => (long?)target).FirstOrDefault();
    }

    public static bool IntervalsOverlap(long a, long lenA, long b, long lenB) =>
        a < b + lenB && b < a + lenA; // caller guarantees nonnegative bounded ranges

    private static long? AlignUp(long x)
    {
        if (x < 0) return null;
        var remainder = x % AlignmentBytes;
        var delta = remainder == 0 ? 0 : AlignmentBytes - remainder;
        return x > long.MaxValue - delta ? null : x + delta;
    }

    private static ulong AbsoluteDistance(long x, long y) =>
        x >= y ? (ulong)(x - y) : (ulong)(y - x);
}

public sealed record MovePreviewDecision(bool CanPreview, string Reason,
    long SourceOffsetBytes = 0, long TargetOffsetBytes = 0, long BytesToCopy = 0,
    long UnallocatedExtentOffset = 0, long UnallocatedExtentLength = 0, string Direction = "")
{
    public static MovePreviewDecision Reject(string reason) => new(false, reason);
    public string Summary => !CanPreview ? $"Prévia recusada: {Reason}" :
        $"SIMULAÇÃO — {Direction}. Origem: {ByteSize.Format(SourceOffsetBytes)}; " +
        $"destino: {ByteSize.Format(TargetOffsetBytes)}; " +
        $"dados a preservar: {ByteSize.Format(BytesToCopy)}. " +
        "Sem mecanismo de cópia, rollback ou confirmação de integridade: execução indisponível.";
}
