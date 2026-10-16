using System.IO;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Safety;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// C14: review maintenance requests using *only* an inventory snapshot.
/// No process runner, maintenance command, file deletion, sanitization or writer is present.
/// This assessment is never authority to execute a mutation.
/// </summary>
public enum MaintenanceTask
{
    InspectVolume,
    OptimizeVolume,
    ReviewTemporaryFiles,
    WipeVolumeFreeSpace,
    EraseEntireDisk
}

public enum MaintenanceRisk { MetadataOnly, ChangesData, Destructive }

public sealed record MaintenanceCheck(string Component, string Status, string Detail);

public sealed record MaintenanceReport(
    MaintenanceTask Task, int DiskNumber, int? PartitionNumber,
    DateTimeOffset CapturedAt, MaintenanceRisk Risk, IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings, IReadOnlyList<MaintenanceCheck> Checks, string Summary)
{
    /// <summary>Hard-coded off: even "metadata plausible" is not a maintenance authorization.</summary>
    public bool CanExecute => false;
    public bool IsMetadataPlausible => Blockers.Count == 0;
    public string RiskLabel => Risk switch
    {
        MaintenanceRisk.MetadataOnly => "Consulta de metadados",
        MaintenanceRisk.ChangesData => "Pode modificar arquivos / metadados",
        MaintenanceRisk.Destructive => "Destrutiva · pode causar perda de dados",
        _ => "Risco não classificado"
    };
}

public static class MaintenanceAssessment
{
    private static readonly HashSet<string> KnownFileSystems =
        new(StringComparer.OrdinalIgnoreCase) { "NTFS", "FAT32", "exFAT" };

    public static MaintenanceReport Analyze(StorageSnapshot snapshot, int diskNumber,
        int? partitionNumber, MaintenanceTask task)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var blockers = new List<string>();
        var warnings = new List<string>();
        var checks = new List<MaintenanceCheck>();
        var risk = task switch
        {
            MaintenanceTask.InspectVolume => MaintenanceRisk.MetadataOnly,
            MaintenanceTask.EraseEntireDisk or MaintenanceTask.WipeVolumeFreeSpace =>
                MaintenanceRisk.Destructive,
            _ => MaintenanceRisk.ChangesData
        };
        if (!Enum.IsDefined(task))
        {
            blockers.Add("Tipo de manutenção desconhecido; não há procedimento autorizado.");
            return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
        }

        var disk = snapshot.FindDisk(diskNumber);
        if (disk is null)
        {
            blockers.Add("Disco não localizado no inventário atual.");
            return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
        }

        try { _ = StorageGeometry.Build(snapshot); }
        catch (InvalidDataException)
        {
            blockers.Add("Geometria/identidades do inventário inconsistentes. Releia o dispositivo.");
        }

        checks.Add(new("Origem", "Metadados Windows",
            $"Disco {disk.Number} · {disk.FriendlyName} · {disk.PartitionStyle} · {disk.SizeLabel}. " +
            $"Captura: {snapshot.CapturedAt:dd/MM/yyyy HH:mm:ss zzz}. Não foi acessada a superfície."));
        if (!disk.HasStrongIdentity)
            blockers.Add("Disco sem identidade forte: não é possível vincular um futuro comando ao dispositivo.");
        if (disk.IsOffline)
            blockers.Add("Disco offline: capacidade real dos volumes não verificável.");
        if (disk.IsReadOnly && task != MaintenanceTask.InspectVolume)
            blockers.Add("Dispositivo somente leitura: nenhuma manutenção modificadora pode ser recomendada.");

        var wholeDisk = task == MaintenanceTask.EraseEntireDisk;
        if (wholeDisk)
        {
            if (partitionNumber.HasValue)
                blockers.Add("Apagamento integral requer seleção explícita do disco, não uma partição.");
            if (disk.Number == 0 || disk.IsBoot || disk.IsSystem)
                blockers.Add("Disco 0 / Windows / sistema protegido: apagamento integral proibido.");
            var all = snapshot.Partitions.Where(p => p.DiskNumber == diskNumber).ToArray();
            if (all.Any(SafetyService.IsProtected))
                blockers.Add("Disco com partições de sistema, EFI, MSR, Recovery, ocultas ou protegidas.");
            checks.Add(new("Escopo", "Disco inteiro",
                $"{all.Length} partição(ões) detectada(s). A sanitização real poderia destruir arquivos, boot e espaço não alocado."));
            checks.Add(new("Sanitização confiável", "Não verificada",
                "Secure Erase/PSID/NVMe Sanitize, caches, HPA/DCO, wear-leveling, criptografia e firmware não foram verificados."));
            warnings.Add("Dispositivos SSD/USB/RAID podem impedir a confirmação de sobrescrita física de todos os blocos.");
            warnings.Add("Não há motor homologado de apagamento, confirmação reforçada, comprovação criptográfica ou recuperação pós-falha.");
            return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
        }

        if (partitionNumber is null)
        {
            blockers.Add("Selecione uma partição específica: este procedimento é de volume.");
            return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
        }

        var partition = snapshot.FindPartition(diskNumber, partitionNumber.Value);
        if (partition is null)
        {
            blockers.Add("A partição selecionada não consta do inventário.");
            return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
        }

        if (!partition.HasStrongIdentity)
            blockers.Add("Partição sem GUID GPT verificável: execução futura não poderia usar identidade estável.");
        if (partition.IsOffline)
            blockers.Add("Partição offline: volume não acessível.");
        if (partition.IsReadOnly && task != MaintenanceTask.InspectVolume)
            blockers.Add("Partição somente leitura: manutenção modificadora incompatível.");

        var protectedPart = SafetyService.IsProtected(partition);
        if (protectedPart && task != MaintenanceTask.InspectVolume)
            blockers.Add("Partição de Windows, boot, EFI/MSR/Recovery, oculta ou protegida: alterações recusadas.");

        var fileSystem = partition.FileSystem.Trim();
        checks.Add(new("Partição", "Inventário Windows",
            $"Partição {partition.PartitionNumber}, {partition.Kind}, {partition.SizeLabel}, " +
            $"filesystem: {(fileSystem.Length == 0 ? "não informado" : fileSystem)}, " +
            $"letra: {(partition.DriveLetter.Length == 0 ? "sem letra" : partition.DriveLetter + ":")}."));
        if (partition.HasVolumeUsage)
        {
            checks.Add(new("Ocupação", partition.UsagePercent is >= 95d ? "Atenção" : "Informada",
                $"{partition.UsageLabel} · {partition.FreeLabel} livres dentro do volume. " +
                "Não se confunde com espaço não alocado nem comprova arquivos removíveis."));
            if (partition.UsagePercent is >= 95d)
                warnings.Add("Volume com ocupação >= 95%: avalie espaço e cópia de segurança antes de manutenção.");
        }
        else
            checks.Add(new("Ocupação", "Não disponível",
                "O Windows não forneceu capacidade/livre confiáveis; não há estimativa de limpeza."));

        if (!KnownFileSystems.Contains(fileSystem))
            blockers.Add("Sistema de arquivos não suportado ou desconhecido para esta avaliação de manutenção.");

        switch (task)
        {
            case MaintenanceTask.InspectVolume:
                checks.Add(new("Saúde Windows", "Somente consulta",
                    $"HealthStatus: {(string.IsNullOrWhiteSpace(partition.HealthStatus) ? "não informado" : partition.HealthStatus)}. " +
                    "Dirty bit, erros NTFS detalhados, superfície e integridade real NÃO foram verificados."));
                checks.Add(new("Verificação de integridade", "Não executada",
                    "Nenhum CHKDSK /scan, Repair-Volume, correção offline ou teste de superfície foi iniciado."));
                if (protectedPart) warnings.Add("Volume protegido: somente leitura de metadados, nunca reparo automático.");
                break;

            case MaintenanceTask.OptimizeVolume:
                if (!fileSystem.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                    blockers.Add("Prévia de otimização restrita a NTFS até validação de outros sistemas de arquivos.");
                var media = disk.MediaType.Trim();
                var method = media.Equals("SSD", StringComparison.OrdinalIgnoreCase)
                    ? "Possível ReTrim SSD; suporte real/TRIM não verificado."
                    : media.Equals("HDD", StringComparison.OrdinalIgnoreCase)
                    ? "Possível desfragmentação HDD; necessidade real não verificada."
                    : "Tecnologia indeterminada: não escolher TRIM nem desfragmentação por suposição.";
                checks.Add(new("Tipo de manutenção", "Não executada", method));
                if (media is not ("SSD" or "HDD"))
                    warnings.Add("Tecnologia física não confirmada; NVMe/SATA/USB isoladamente não provam SSD/HDD.");
                warnings.Add("Otimização pode escrever no volume e causar atividade intensa; não foi avaliada fragmentação.");
                break;

            case MaintenanceTask.ReviewTemporaryFiles:
                if (partition.DriveLetter.Length != 1 || partition.DriveLetter[0] is < 'A' or > 'Z')
                    blockers.Add("Letra de unidade confiável ausente: não é possível relacionar caminhos.");
                checks.Add(new("Arquivos temporários", "Não enumerados",
                    "Nenhuma pasta do usuário, Downloads, cache de aplicativo, Lixeira ou caminho de sistema foi varrido."));
                checks.Add(new("Economia estimada", "Desconhecida",
                    "Espaço disponível do volume NÃO equivale a espaço recuperável. Nenhum arquivo será excluído."));
                warnings.Add("Limpeza futura deve respeitar consentimento do usuário, ACLs, arquivos em uso e preservação de dados.");
                break;

            case MaintenanceTask.WipeVolumeFreeSpace:
                checks.Add(new("Apagamento de espaço livre", "Não executado",
                    "Nenhum bloco, arquivo ou metadado foi modificado. Espaço livre não significa dados recuperáveis.");
                checks.Add(new("Garantias de eliminação", "Não verificadas",
                    "TRIM, remapeamento SSD, snapshots, cópias, criptografia e blocos reservados impedem garantia por sobrescrita."));
                warnings.Add("Não existe rotina homologada de sobrescrita segura de espaço livre. O sistema NÃO apaga dados.");
                break;
        }

        if (task != MaintenanceTask.InspectVolume)
            warnings.Add("Não há executor de manutenção homologado; não será criada operação na fila.");

        return Build(task, diskNumber, partitionNumber, snapshot.CapturedAt, risk, blockers, warnings, checks);
    }

    private static MaintenanceReport Build(MaintenanceTask task, int diskNumber, int? partitionNumber,
        DateTimeOffset capturedAt, MaintenanceRisk risk, IReadOnlyList<string> blockers,
        IReadOnlyList<string> warnings, IReadOnlyList<MaintenanceCheck> checks)
    {
        const string noExecution = " EXECUÇÃO BLOQUEADA: nenhum check, reparo, TRIM, limpeza ou apagamento foi realizado.";
        var summary = blockers.Count == 0
            ? "Metadados preliminares inspecionados, SEM homologação de execução."
            : $"Prévia com {blockers.Count} impedimento(s): " + string.Join(" ", blockers);
        if (warnings.Count > 0) summary += " Avisos: " + string.Join(" ", warnings);
        return new(task, diskNumber, partitionNumber, capturedAt, risk,
            blockers.ToArray(), warnings.ToArray(), checks.ToArray(), summary + noExecution);
    }
}
