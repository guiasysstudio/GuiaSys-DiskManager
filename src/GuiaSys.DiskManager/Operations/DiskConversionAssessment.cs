using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// C10 NON-EXECUTING layout inspection. A geometric fit is not evidence that
/// filesystem, GPT/MBR metadata, boot firmware or installed Windows can migrate.
/// No in-place conversion engine, partition rewrites or copying is provided.
/// </summary>
public static class DiskConversionAssessment
{
    public static DiskConversionReport Analyze(StorageSnapshot snapshot, int diskNumber,
        DiskPartitionScheme desiredScheme)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(desiredScheme))
            return DiskConversionReport.Block(diskNumber, desiredScheme, "Esquema solicitado desconhecido.");

        var disk = snapshot.FindDisk(diskNumber);
        if (disk is null)
            return DiskConversionReport.Block(diskNumber, desiredScheme, "O disco não consta do inventário.");

        var warnings = new List<string>();
        var blockers = new List<string>();
        var parts = snapshot.Partitions.Where(p => p.DiskNumber == diskNumber).ToArray();
        var actual = (disk.PartitionStyle ?? "").Trim().ToUpperInvariant();
        if (actual is not ("GPT" or "MBR" or "RAW"))
            blockers.Add("O Windows não identificou um esquema RAW, GPT ou MBR confiável.");

        if (!disk.HasStrongIdentity || disk.Number == 0)
            blockers.Add("Disco 0 ou identidade de disco insuficiente: conversão bloqueada.");
        if (disk.IsBoot || disk.IsSystem || disk.IsOffline || disk.IsReadOnly ||
            parts.Any(SafetyService.IsProtected))
            blockers.Add("Disco de sistema, boot, offline, somente leitura ou com partição protegida.");

        var sector = disk.LogicalSectorSize;
        if (sector is not (>= 512 and <= 65_536) ||
            (sector.Value & (sector.Value - 1)) != 0)
            blockers.Add("Setor lógico não informado, inválido ou não é potência de dois.");
        if (disk.Size <= 0 || sector is null || sector <= 0 || disk.Size % sector.Value != 0)
            blockers.Add("Capacidade física ou alinhamento do disco não verificável.");

        DiskLayout? diskLayout = null;
        try { diskLayout = StorageGeometry.Build(snapshot).FindDisk(diskNumber); }
        catch (InvalidDataException)
        {
            blockers.Add("Inventário com geometria inválida ou sobreposição de partições.");
        }
        if (diskLayout is null)
            blockers.Add("O mapa de armazenamento não está disponível.");

        if (actual == desiredScheme.ToString().ToUpperInvariant())
            blockers.Add("O disco já utiliza o esquema de destino. Nenhuma conversão é necessária.");

        if (actual == "RAW" && parts.Length != 0)
            blockers.Add("Disco RAW com partições: inventário incoerente. Não inicializar.");
        if (actual == "RAW" && parts.Length == 0)
            warnings.Add("Inicializar um disco RAW vazio não é converter um disco que contém dados.");

        if (desiredScheme == DiskPartitionScheme.Mbr && sector is > 0)
        {
            // Classic MBR uses 32-bit LBA in logical sectors, NOT a fixed 2TB
            // regardless of 512e/4Kn. This is a representability check only.
            var maxAddressable = checked((ulong)sector.Value * (ulong)uint.MaxValue + (ulong)sector.Value);
            if ((ulong)Math.Max(0, disk.Size) > maxAddressable)
                blockers.Add($"MBR não endereça toda a capacidade do disco com setores lógicos de {sector} bytes.");
            if (parts.Length > 4)
                blockers.Add("MBR clássico suporta no máximo quatro entradas de partição primária.");
            if (parts.Any(p => p.Size > 0 && sector.Value > 0 &&
                (p.Offset % sector.Value != 0 || p.Size % sector.Value != 0)))
                blockers.Add("Partição existente fora do alinhamento do setor lógico para MBR.");
            if (actual == "GPT" && parts.Length > 0)
                warnings.Add("Identificadores GUID/tipos GPT não podem ser preservados literalmente em uma tabela MBR.");
        }
        if (desiredScheme == DiskPartitionScheme.Gpt)
        {
            warnings.Add("A compatibilidade de firmware UEFI/Legacy e boot do Windows não é comprovada por esta análise.");
            if (actual == "MBR" && parts.Length > 0)
                warnings.Add("Partições MBR/estendidas e metadados de boot exigem migração especializada, não inicialização.");
        }

        var existingData = parts.Length > 0;
        if (existingData)
        {
            // C10 does not attempt to infer whether unknown partitions contain files:
            // assume loss is possible even when filesystem or volume metrics are missing.
            warnings.Add("Partições existentes são tratadas como contendo dados: risco de perda sem motor de migração.");
        }

        var rawInitializationCandidate = actual == "RAW" && parts.Length == 0 &&
            blockers.Count == 0;
        if (existingData)
            warnings.Add("Conversão com preservação de dados NÃO IMPLEMENTADA: requer backup, motor e recuperação homologados.");
        else if (!rawInitializationCandidate)
            warnings.Add("Nenhuma conversão executável foi aprovada por esta análise.");

        return new DiskConversionReport(diskNumber, actual, desiredScheme, disk.Size,
            sector, parts.Length, blockers.Count == 0, rawInitializationCandidate,
            blockers.ToArray(), warnings.ToArray());
    }
}

public enum DiskPartitionScheme { Gpt, Mbr }

/// <summary>Never grants authority to change partition tables or existing data.</summary>
public sealed record DiskConversionReport(int DiskNumber, string CurrentScheme,
    DiskPartitionScheme DesiredScheme, long DiskSizeBytes, int? LogicalSectorBytes,
    int PartitionCount, bool GeometryLooksCompatible, bool IsEmptyRawInitializationCandidate,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings)
{
    public bool DataPreservingConversionSupported => false;
    public string DestinationLabel => DesiredScheme == DiskPartitionScheme.Gpt ? "GPT" : "MBR";
    public string StatusLabel => Blockers.Count != 0 ? "INCOMPATÍVEL / RESTRITO" :
        IsEmptyRawInitializationCandidate ? "RAW VAZIO · APENAS INICIALIZAÇÃO (BLOQUEADA)" :
        "PRÉVIA GEOMÉTRICA · CONVERSÃO INDISPONÍVEL";
    public string Summary =>
        $"DISCO {DiskNumber}: {CurrentScheme} → {DestinationLabel}. {StatusLabel}. " +
        $"Partições: {PartitionCount}. Setor lógico: " +
        (LogicalSectorBytes is int bytes ? $"{bytes:N0} B. " : "indisponível. ") +
        (Blockers.Count > 0 ? "Impedimentos: " + string.Join(" | ", Blockers) + ". " : "") +
        (Warnings.Count > 0 ? "Observações: " + string.Join(" | ", Warnings) + ". " : "") +
        "Sem conversão com dados, sem escrita e sem autorização de execução.";
    public static DiskConversionReport Block(int disk, DiskPartitionScheme scheme, string reason) =>
        new(disk, "Desconhecido", scheme, 0, null, 0, false, false,
            [reason], ["Inspeção somente leitura; não autoriza converter."]);
}
