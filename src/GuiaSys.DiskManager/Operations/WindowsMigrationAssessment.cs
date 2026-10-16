using System.IO;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// C12: Windows migration READ-ONLY topology and destination-capacity inspection.
/// A "compatible" assessment means only that a conservative subset of the
/// storage inventory is plausible. It does NOT certify boot, BitLocker, BCD,
/// firmware, VSS, data transfer or any execution path.
/// </summary>
public static class WindowsMigrationAssessment
{
    private const string EfiType = "c12a7328-f81f-11d2-ba4b-00a0c93ec93b";
    private const string MsrType = "e3c9e316-0b5c-4db8-817d-f92df00215ae";
    private const string RecoveryType = "de94bba4-06d1-4d40-a16a-bfd50179d6ac";
    private const string BasicDataType = "ebd0a0a2-b9e5-4433-87c0-68b6b72699c7";

    public static WindowsMigrationReport Analyze(StorageSnapshot snapshot, int sourceDiskNumber,
        int targetDiskNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var blockers = new List<string>();
        var warnings = new List<string>();
        var checks = new List<WindowsMigrationCheck>();
        var source = snapshot.FindDisk(sourceDiskNumber);
        var target = snapshot.FindDisk(targetDiskNumber);
        if (source is null || target is null)
            return WindowsMigrationReport.Block(sourceDiskNumber, targetDiskNumber,
                "O disco de origem ou destino não consta do inventário.");

        if (sourceDiskNumber == targetDiskNumber ||
            string.Equals(source.Identity, target.Identity, StringComparison.Ordinal))
            blockers.Add("Origem e destino devem ser discos distintos com identidade diferente.");
        if (targetDiskNumber == 0)
            blockers.Add("Disco 0 não pode ser utilizado como destino.");
        // Source Disk 0 is legitimate for read-only diagnosis of a running Windows.
        if (!source.HasStrongIdentity || !target.HasStrongIdentity)
            blockers.Add("Identidades únicas de ambos os discos não são confiáveis.");
        if (source.IsOffline)
            blockers.Add("Disco de origem está offline.");
        if (target.IsOffline || target.IsReadOnly || target.IsBoot || target.IsSystem)
            blockers.Add("Destino offline, somente leitura ou marcado como Windows/boot/sistema.");
        if (source.Size <= 0 || target.Size <= 0)
            blockers.Add("Tamanhos físicos de discos indisponíveis.");
        else if (target.Size < source.Size)
            blockers.Add("Destino menor que a capacidade física integral da origem: redução automática não é suportada.");

        if (!ValidSector(source.LogicalSectorSize) || !ValidSector(target.LogicalSectorSize))
            blockers.Add("Setor lógico de origem/destino desconhecido ou inválido.");
        else if (source.LogicalSectorSize != target.LogicalSectorSize)
            blockers.Add("Tamanhos de setor lógico diferentes: clonagem setorial não é equivalente.");
        if (source.LogicalSectorSize is > 0 && source.Size % source.LogicalSectorSize.Value != 0 ||
            target.LogicalSectorSize is > 0 && target.Size % target.LogicalSectorSize.Value != 0)
            blockers.Add("Capacidade não alinhada ao setor lógico informado.");

        if (source.PhysicalSectorSize is null || target.PhysicalSectorSize is null)
            warnings.Add("Setor físico não verificado em ambos os discos.");
        else if (source.PhysicalSectorSize != target.PhysicalSectorSize)
            warnings.Add("Setores físicos distintos exigem validação adicional de desempenho/alinhamento.");

        // A destination with existing data is NEVER an eligible migration target.
        var sourceParts = snapshot.Partitions.Where(p => p.DiskNumber == sourceDiskNumber).ToArray();
        var targetParts = snapshot.Partitions.Where(p => p.DiskNumber == targetDiskNumber).ToArray();
        if (!string.Equals(target.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase) ||
            targetParts.Length != 0)
            blockers.Add("O destino deve estar RAW e sem partições. Sobrescrita não é oferecida.");
        if (string.Equals(source.PartitionStyle, "RAW", StringComparison.OrdinalIgnoreCase) ||
            sourceParts.Length == 0)
            blockers.Add("Origem sem partições de Windows verificáveis.");

        // A Windows installation may have its ESP/BCD on ANOTHER disk. Do not
        // interpret a lone C: as a self-contained, bootable migration.
        var foreignSystem = snapshot.Partitions.Any(p =>
            p.DiskNumber != sourceDiskNumber && p.IsSystem);
        if (foreignSystem)
            blockers.Add("Há partição de sistema em outro disco: cadeia de boot pode depender de outro dispositivo.");

        try
        {
            StorageGeometry.Build(snapshot);
        }
        catch (InvalidDataException)
        {
            blockers.Add("Inventário de discos/partições inconsistente ou sobreposto.");
        }

        checks.Add(new WindowsMigrationCheck("Disco de destino",
            target.PartitionStyle.Equals("RAW", StringComparison.OrdinalIgnoreCase) &&
            targetParts.Length == 0 ? WindowsMigrationEvidence.Present : WindowsMigrationEvidence.Missing,
            "Destino RAW vazio; nenhum dado será substituído."));

        var os = sourceParts.Where(p => p.IsBoot).ToArray();
        checks.Add(new WindowsMigrationCheck("Partição Windows",
            os.Length == 1 ? WindowsMigrationEvidence.Present : WindowsMigrationEvidence.Missing,
            os.Length == 1 ? $"Partição {os[0].PartitionNumber}, {ByteSize.Format(os[0].Size)}." :
            $"Esperada uma partição de Windows; encontradas {os.Length}."));
        if (os.Length != 1)
            blockers.Add("Não foi possível identificar uma única partição de Windows (IsBoot).");
        else if (os[0].IsOffline || os[0].IsHidden || os[0].Size <= 0 ||
            string.IsNullOrWhiteSpace(os[0].FileSystem) ||
            !os[0].FileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
            blockers.Add("A partição do Windows não é um volume NTFS online e identificável.");

        if (!source.IsBoot && !sourceParts.Any(p => p.IsBoot))
            blockers.Add("Disco sem sinal de Windows em execução no inventário.");

        var isGpt = source.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase);
        if (!isGpt)
        {
            blockers.Add("Migração Legacy/MBR não é homologada: ausência de dados confiáveis de Active/BCD.");
            checks.Add(new WindowsMigrationCheck("Modo de inicialização",
                WindowsMigrationEvidence.Unknown,
                "Somente GPT/UEFI é analisado em detalhe. Legacy BIOS/MBR exige inventário e fluxo próprios."));
        }
        else
        {
            // GPT GUIDs are authoritative for classification; a drive-letter/label
            // must not be used as a substitute for an EFI or MSR type GUID.
            var missingIdentity = sourceParts.Any(p => !p.HasStrongIdentity ||
                !Guid.TryParse(p.GptType?.Trim().Trim('{', '}'), out _));
            if (missingIdentity)
                blockers.Add("Partições GPT sem GUID de identidade ou GUID de tipo verificável.");

            var efi = sourceParts.Where(p => IsType(p, EfiType)).ToArray();
            var msr = sourceParts.Where(p => IsType(p, MsrType)).ToArray();
            var recovery = sourceParts.Where(p => IsType(p, RecoveryType)).ToArray();
            checks.Add(new WindowsMigrationCheck("Partição EFI (ESP)",
                efi.Length == 1 ? WindowsMigrationEvidence.Present : WindowsMigrationEvidence.Missing,
                $"Esperada uma ESP; encontradas {efi.Length}."));
            checks.Add(new WindowsMigrationCheck("Microsoft Reserved (MSR)",
                msr.Length == 1 ? WindowsMigrationEvidence.Present : WindowsMigrationEvidence.Missing,
                $"Esperada uma MSR; encontradas {msr.Length}."));
            checks.Add(new WindowsMigrationCheck("Partição Recovery / WinRE",
                recovery.Length == 1 ? WindowsMigrationEvidence.Present : WindowsMigrationEvidence.Unknown,
                recovery.Length == 1 ? "Tipo de partição Recovery presente; vínculo com WinRE não comprovado." :
                    $"Encontradas {recovery.Length}; status real do WinRE não comprovado."));

            if (efi.Length != 1)
                blockers.Add("Partição de inicialização EFI ausente ou ambígua no disco de origem.");
            else if (efi[0].IsOffline || efi[0].Size <= 0 ||
                !efi[0].FileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase))
                blockers.Add("Partição EFI sem sistema FAT32 verificável ou indisponível.");
            if (msr.Length != 1 || msr[0].Size <= 0)
                blockers.Add("Partição MSR ausente ou ambígua no disco de origem.");
            if (recovery.Length != 1)
                warnings.Add("Recovery/WinRE ausente ou múltipla: restauração do ambiente de recuperação não verificada.");
            if (os.Length == 1 && !IsType(os[0], BasicDataType))
                blockers.Add("A partição Windows não possui tipo GPT de dados básicos.");
        }

        checks.Add(new WindowsMigrationCheck("BitLocker / criptografia",
            WindowsMigrationEvidence.Unknown,
            "Estado, protetores, chave de recuperação e exigência de desbloqueio não constam do inventário."));
        checks.Add(new WindowsMigrationCheck("BCD / firmware UEFI / Secure Boot",
            WindowsMigrationEvidence.Unknown,
            "Não foram auditados BCD, NVRAM, TPM, drivers e compatibilidade de firmware."));
        checks.Add(new WindowsMigrationCheck("Consistência dos arquivos / VSS",
            WindowsMigrationEvidence.Unknown,
            "Nenhum snapshot VSS, bloqueio de volume, leitura de arquivo ou cópia foi executado."));

        warnings.Add("Capacidade livre do NTFS não é prova de que o Windows possa migrar para um disco menor.");
        warnings.Add("Migração exige cópia de todas as partições necessárias, BCD, firmware, GUIDs, BitLocker e WinRE.");
        warnings.Add("Esta é uma inspeção de metadados: não valida boot nem preservação dos arquivos.");
        return new WindowsMigrationReport(sourceDiskNumber, targetDiskNumber,
            source.PartitionStyle.ToUpperInvariant(), source.Size, target.Size,
            source.Identity, target.Identity, snapshot.CapturedAt,
            blockers.ToArray(), warnings.ToArray(), checks.ToArray());
    }

    private static bool IsType(PartitionInfo partition, string type) =>
        string.Equals(partition.GptType?.Trim().Trim('{', '}'), type, StringComparison.OrdinalIgnoreCase);

    private static bool ValidSector(int? sector) =>
        sector is >= 512 and <= 65_536 && (sector.Value & (sector.Value - 1)) == 0;
}

public enum WindowsMigrationEvidence { Present, Missing, Unknown }

public sealed record WindowsMigrationCheck(string Component,
    WindowsMigrationEvidence Evidence, string Detail)
{
    public string Status => Evidence switch
    {
        WindowsMigrationEvidence.Present => "Presente",
        WindowsMigrationEvidence.Missing => "Ausente / ambíguo",
        _ => "Não verificado"
    };
}

/// <summary>No executable migration can be approved by a read-only report.</summary>
public sealed record WindowsMigrationReport(int SourceDiskNumber, int TargetDiskNumber,
    string SourcePartitionStyle, long SourceBytes, long TargetBytes,
    string SourceIdentity, string TargetIdentity, DateTimeOffset CapturedAt,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings,
    IReadOnlyList<WindowsMigrationCheck> Checks)
{
    public bool TopologyLooksPlausible => Blockers.Count == 0;
    public bool CanExecute => false;
    public string Summary =>
        $"C12 · MIGRAÇÃO WINDOWS | Disco {SourceDiskNumber} ({SourcePartitionStyle}) " +
        $"→ Disco {TargetDiskNumber}. Fonte: {ByteSize.Format(SourceBytes)}; " +
        $"destino: {ByteSize.Format(TargetBytes)}. " +
        (TopologyLooksPlausible ? "Topologia GPT/UEFI preliminarmente compatível. " :
            "MIGRAÇÃO RESTRITA: requisitos de topologia não atendidos. ") +
        "Verificações: " + string.Join(" | ", Checks.Select(c =>
            $"{c.Component}: {c.Status} ({c.Detail})")) + ". " +
        (Blockers.Count == 0 ? "" : "Impedimentos: " + string.Join(" | ", Blockers) + ". ") +
        "Avisos: " + string.Join(" | ", Warnings) +
        ". SEM MIGRAÇÃO EXECUTÁVEL. Nenhuma cópia ou escrita foi realizada.";
    public static WindowsMigrationReport Block(int source, int target, string reason) =>
        new(source, target, "Desconhecido", 0, 0, "", "",
            DateTimeOffset.MinValue, [reason],
            ["Inventário incompleto; nenhuma operação está autorizada."], []);
}
