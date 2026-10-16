using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// C11: read-only eligibility study for full-disk and partition cloning.
/// A preview is never an authorization. No block-copying/formatting/partition-table writes
/// or cloning operations are registered in the storage command allowlist.
/// </summary>
public static class ClonePreviewAssessment
{
    private const long PartitionAlignment = 1_048_576; // conservative preview-only 1 MiB margin
    private const string BasicDataGpt = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";

    public static ClonePreviewReport Analyze(StorageSnapshot snapshot, int sourceDiskNumber,
        int targetDiskNumber, int? sourcePartitionNumber = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var scope = sourcePartitionNumber is null ? ClonePreviewScope.Disk : ClonePreviewScope.Partition;
        var blockers = new List<string>();
        var warnings = new List<string>();
        var source = snapshot.FindDisk(sourceDiskNumber);
        var target = snapshot.FindDisk(targetDiskNumber);
        long bytes = 0;
        long? destinationOffset = null;
        long? destinationExtent = null;

        if (source is null || target is null)
            return ClonePreviewReport.Block(scope, sourceDiskNumber, sourcePartitionNumber,
                targetDiskNumber, "Origem ou destino não encontrado no inventário.");

        if (sourceDiskNumber == targetDiskNumber ||
            string.Equals(source.Identity, target.Identity, StringComparison.Ordinal))
            blockers.Add("Origem e destino devem ser discos físicos/lógicos distintos com identidades diferentes.");
        if (sourceDiskNumber == 0 || targetDiskNumber == 0)
            blockers.Add("Disco 0 é protegido e não pode participar de uma prévia de clonagem.");
        if (!source.HasStrongIdentity || !target.HasStrongIdentity)
            blockers.Add("A identidade única de origem e destino deve ser forte e verificável.");
        if (source.IsSystem || source.IsBoot || target.IsSystem || target.IsBoot)
            blockers.Add("Volumes Windows/boot/sistema não são elegíveis para clonagem neste módulo.");
        if (source.IsOffline || target.IsOffline)
            blockers.Add("Origem e destino devem estar online para avaliação.");
        if (target.IsReadOnly)
            blockers.Add("Destino em somente leitura: clonagem futura não seria permitida.");
        if (source.Size <= 0 || target.Size <= 0)
            blockers.Add("Capacidade física inválida ou não informada.");

        var sourceSector = source.LogicalSectorSize;
        var targetSector = target.LogicalSectorSize;
        if (!ValidSector(sourceSector) || !ValidSector(targetSector))
            blockers.Add("Setores lógicos desconhecidos ou inválidos; clonagem não pode ser planejada.");
        else if (sourceSector != targetSector)
            blockers.Add("Setores lógicos diferentes: cópia setorial direta não seria equivalente.");

        if (source.PhysicalSectorSize is int sourcePhysical &&
            target.PhysicalSectorSize is int targetPhysical && sourcePhysical != targetPhysical)
            warnings.Add("Setores físicos diferentes: alinhamento, performance e integridade exigem homologação específica.");
        else if (source.PhysicalSectorSize is null || target.PhysicalSectorSize is null)
            warnings.Add("Tamanho físico de setor não confirmado para ambos os discos.");

        if (sourceSector is > 0 && source.Size > 0 && source.Size % sourceSector.Value != 0 ||
            targetSector is > 0 && target.Size > 0 && target.Size % targetSector.Value != 0)
            blockers.Add("Tamanho da unidade não está alinhado ao respectivo setor lógico.");

        var sourceParts = snapshot.Partitions.Where(p => p.DiskNumber == sourceDiskNumber).ToArray();
        var targetParts = snapshot.Partitions.Where(p => p.DiskNumber == targetDiskNumber).ToArray();
        if (sourceParts.Any(SafetyService.IsProtected) || targetParts.Any(SafetyService.IsProtected))
            blockers.Add("Partição EFI/MSR/Recovery, Windows, inicialização, oculta ou tipo especial detectada.");

        DiskLayout? sourceLayout = null;
        DiskLayout? targetLayout = null;
        try
        {
            var geometry = StorageGeometry.Build(snapshot);
            sourceLayout = geometry.FindDisk(sourceDiskNumber);
            targetLayout = geometry.FindDisk(targetDiskNumber);
        }
        catch (InvalidDataException)
        {
            blockers.Add("Inventário inconsistente, com discos duplicados ou partições sobrepostas.");
        }
        if (sourceLayout is null || targetLayout is null)
            blockers.Add("Geometria confiável de origem e destino indisponível.");

        if (scope == ClonePreviewScope.Disk)
        {
            bytes = source.Size;
            if (source.PartitionStyle is not ("GPT" or "MBR") || sourceParts.Length == 0)
                blockers.Add("Clonagem integral exige origem particionada em GPT/MBR com dados presumidos.");
            if (!string.Equals(target.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase) ||
                targetParts.Length != 0)
                blockers.Add("Para análise de disco integral, o destino precisa estar RAW e sem partições.");
            if (target.Size < source.Size)
                blockers.Add("O disco de destino é menor que a origem; espaço livre dentro dos volumes não basta.");
            if (sourceParts.Any(p => p.IsOffline))
                blockers.Add("Partição de origem indisponível.");
            if (source.PartitionStyle == "GPT" && sourceParts.Any(p =>
                !p.HasStrongIdentity || string.IsNullOrWhiteSpace(p.GptType)))
                blockers.Add("Partições GPT de origem sem GUID/tipo verificáveis.");
            warnings.Add("A cópia integral incluiria todos os setores da origem, inclusive espaços não alocados.");
            warnings.Add("Recriar IDs de disco/partição, metadados GPT de backup e resolver colisões de assinatura exigiria motor auditado.");
        }
        else
        {
            var part = snapshot.FindPartition(sourceDiskNumber, sourcePartitionNumber!.Value);
            if (part is null)
                blockers.Add("Partição de origem não encontrada.");
            else
            {
                bytes = part.Size;
                if (!string.Equals(source.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase) ||
                    !part.HasStrongIdentity ||
                    !string.Equals(part.GptType.Trim().Trim('{', '}'), BasicDataGpt,
                        StringComparison.OrdinalIgnoreCase))
                    blockers.Add("Clonagem de partição só é simulada para dados GPT básicos com GUID forte.");
                if (part.IsOffline)
                    blockers.Add("Partição de origem está offline.");
                if (part.Size <= 0 || sourceSector is > 0 &&
                    (part.Offset % sourceSector.Value != 0 || part.Size % sourceSector.Value != 0))
                    blockers.Add("A origem não respeita limites/alinhamento setorial.");
                if (part.FileSystem is not ("NTFS" or "exFAT" or "FAT32"))
                    blockers.Add("Sistema de arquivos da origem não reconhecido para este estudo conservador.");
            }
            if (!string.Equals(target.PartitionStyle, "GPT", StringComparison.OrdinalIgnoreCase))
                blockers.Add("Para uma partição, o destino deve estar inicializado como GPT.");
            if (targetParts.Any(p => !p.HasStrongIdentity || string.IsNullOrWhiteSpace(p.GptType)))
                blockers.Add("Partições GPT existentes no destino sem identidade confiável.");
            if (targetSector is > 0 && targetLayout is not null && bytes > 0)
            {
                // Reserve conservative 1 MiB at BOTH ends for GPT metadata, irrespective
                // of current free extents. We do not invent a target partition.
                var minStart = PartitionAlignment;
                var maxEnd = target.Size > PartitionAlignment ? target.Size - PartitionAlignment : 0;
                foreach (var extent in targetLayout.Extents.Where(e => e.IsUnallocated))
                {
                    var lower = Math.Max(extent.Offset, minStart);
                    var aligned = AlignUp(lower, PartitionAlignment);
                    var upper = Math.Min(extent.EndExclusive, maxEnd);
                    if (aligned is long offset && offset <= upper && bytes <= upper - offset)
                    {
                        if (destinationOffset is null || offset < destinationOffset)
                        {
                            destinationOffset = offset;
                            destinationExtent = upper - offset;
                        }
                    }
                }
            }
            if (destinationOffset is null)
                blockers.Add("Não existe uma faixa não alocada contígua, alinhada e suficiente no destino GPT.");
            warnings.Add("A prévia não cria partição nem copia sistema de arquivos, ACLs, GUIDs ou dados.");
        }

        // A positive geometry-only report does NOT grant the right to queue an operation.
        warnings.Add("Os dados reais, o acesso exclusivo, a consistência VSS e a recuperação pós-falha não foram inspecionados.");
        return new ClonePreviewReport(scope, sourceDiskNumber, sourcePartitionNumber,
            targetDiskNumber, bytes, destinationOffset, destinationExtent,
            source.Identity, target.Identity, snapshot.CapturedAt, blockers.ToArray(),
            warnings.ToArray());
    }

    private static bool ValidSector(int? bytes) => bytes is >= 512 and <= 65_536 &&
        (bytes.Value & (bytes.Value - 1)) == 0;

    private static long? AlignUp(long offset, long alignment)
    {
        if (offset < 0) return null;
        var delta = (alignment - offset % alignment) % alignment;
        return offset > long.MaxValue - delta ? null : offset + delta;
    }
}

public enum ClonePreviewScope { Disk, Partition }

/// <summary>Informational record. Clone execution is intentionally impossible.</summary>
public sealed record ClonePreviewReport(ClonePreviewScope Scope, int SourceDiskNumber,
    int? SourcePartitionNumber, int TargetDiskNumber, long BytesToRead,
    long? ProposedTargetOffsetBytes, long? RemainingFreeSpanBytes,
    string SourceIdentity, string TargetIdentity, DateTimeOffset CapturedAt,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings)
{
    public bool GeometryLooksCompatible => Blockers.Count == 0;
    public bool CanExecute => false;
    public string ScopeLabel => Scope == ClonePreviewScope.Disk ? "Disco inteiro" : "Partição";
    public string Summary =>
        $"C11 · PRÉVIA DE {ScopeLabel.ToUpperInvariant()} | Disco {SourceDiskNumber}" +
        (SourcePartitionNumber is int number ? $"/Partição {number}" : "") +
        $" → Disco {TargetDiskNumber}. " +
        (GeometryLooksCompatible ? "Geometria preliminar compatível. " :
            "PRÉVIA RESTRITA / INCOMPATÍVEL. ") +
        $"Leitura setorial estimada: {ByteSize.Format(BytesToRead)}. " +
        (ProposedTargetOffsetBytes is long offset
            ? $"Destino sugerido: deslocamento {ByteSize.Format(offset)} (bytes exatos: {offset}). "
            : "") +
        (Blockers.Count > 0 ? "Impedimentos: " + string.Join(" | ", Blockers) + ". " : "") +
        "Avisos: " + string.Join(" | ", Warnings) +
        ". SEM CÓPIA OU GRAVAÇÃO: clonagem não implementada e não executável.";
    public static ClonePreviewReport Block(ClonePreviewScope scope, int source,
        int? partition, int target, string message) =>
        new(scope, source, partition, target, 0, null, null, "", "",
            DateTimeOffset.MinValue, [message], ["Sem execução."]);
}
