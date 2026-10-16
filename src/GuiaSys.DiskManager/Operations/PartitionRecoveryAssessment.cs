using System.IO;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

// GDM-C15: compare two GPT inventories. Never reads sectors or modifies a disk.
public enum RecoveryEntryState { Present, Changed, MissingFree, MissingOccupied, ProtectedMissing }
public sealed record RecoveryEntry(Guid Guid, long Offset, long Size,
    RecoveryEntryState State, string Status, string Detail);
public sealed record RecoveryOldPartition(Guid Guid, Guid Type, long Offset, long Size, bool Protected);
public sealed record RecoveryBaseline(string DiskIdentity, int DiskNumber, long Size,
    int SectorSize, DateTimeOffset CapturedAt, IReadOnlyList<RecoveryOldPartition> Partitions)
{
    public string Label => $"Referência: {CapturedAt:dd/MM/yyyy HH:mm:ss} · Disco {DiskNumber} · {Partitions.Count} partições";
}
public sealed record RecoveryReport(int? DiskNumber, DateTimeOffset ComparedAt,
    IReadOnlyList<RecoveryEntry> Entries, IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings, string Summary)
{
    public bool CanRecover => false;
    public bool Comparable => Blockers.Count == 0;
    public int MissingCount => Entries.Count(x => x.State is RecoveryEntryState.MissingFree
        or RecoveryEntryState.MissingOccupied or RecoveryEntryState.ProtectedMissing);
}

public static class PartitionRecoveryAssessment
{
    private static readonly Guid Basic = Guid.Parse("ebd0a0a2-b9e5-4433-87c0-68b6b72699c7");

    public static RecoveryBaseline Capture(StorageSnapshot snapshot, int number)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _ = StorageGeometry.Build(snapshot);
        var disk = snapshot.FindDisk(number)
            ?? throw new InvalidDataException("Disco ausente do inventário.");
        if (!disk.HasStrongIdentity || disk.IsOffline ||
            !disk.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exige disco GPT online com UniqueId forte.");
        if (disk.LogicalSectorSize is not (512 or 1024 or 2048 or 4096 or 8192 or 16384 or 32768 or 65536))
            throw new InvalidDataException("Setor lógico desconhecido ou inválido.");
        int sector = disk.LogicalSectorSize.Value;
        if (disk.Size % sector != 0 || snapshot.CapturedAt == DateTimeOffset.MinValue)
            throw new InvalidDataException("Capacidade desalinhada ou data de coleta inválida.");
        var parts = snapshot.Partitions.Where(p => p.DiskNumber == disk.Number)
            .OrderBy(p => p.Offset).ToArray();
        if (parts.Any(p => !p.HasStrongIdentity || !Guid.TryParse(p.GptType, out _) ||
            p.Offset % sector != 0 || p.Size % sector != 0))
            throw new InvalidDataException("Partição GPT sem identidade/tipo/alinhamento confiável.");
        var baseline = parts.Select(p => new RecoveryOldPartition(Guid.Parse(p.PartitionGuid),
            Guid.Parse(p.GptType), p.Offset, p.Size,
            Guid.Parse(p.GptType) != Basic || p.IsBoot || p.IsSystem ||
            p.IsHidden || p.IsReadOnly || p.IsOffline)).ToArray();
        return new(disk.Identity, disk.Number, disk.Size, sector, snapshot.CapturedAt,
            Array.AsReadOnly(baseline));
    }

    public static RecoveryReport Compare(RecoveryBaseline previous, StorageSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var blockers = new List<string>();
        var warnings = new List<string>();
        var entries = new List<RecoveryEntry>();
        int? diskNumber = null;
        try
        {
            var geometry = StorageGeometry.Build(current);
            var matches = current.Disks.Where(d => d.HasStrongIdentity &&
                string.Equals(previous.DiskIdentity, d.Identity, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                blockers.Add("Disco original não encontrado por UniqueId forte.");
            if (current.CapturedAt <= previous.CapturedAt)
                blockers.Add("Atualize o inventário depois da captura da referência.");
            if (matches.Length == 1)
            {
                var disk = matches[0];
                diskNumber = disk.Number;
                if (disk.IsOffline || !disk.PartitionStyle.Equals("GPT", StringComparison.OrdinalIgnoreCase) ||
                    disk.Size != previous.Size || disk.LogicalSectorSize != previous.SectorSize)
                    blockers.Add("Geometria/estilo/setor do disco diferente, ou disco offline.");
                var parts = current.Partitions.Where(p => p.DiskNumber == disk.Number).ToArray();
                if (parts.Any(p => !p.HasStrongIdentity))
                    blockers.Add("Partições atuais sem GUID: comparação recusada.");
                if (blockers.Count == 0)
                {
                    var now = parts.ToDictionary(p => Guid.Parse(p.PartitionGuid));
                    var layout = geometry.FindDisk(disk.Number)!;
                    foreach (var old in previous.Partitions)
                    {
                        if (now.TryGetValue(old.Guid, out var live))
                        {
                            var identical = live.Offset == old.Offset && live.Size == old.Size &&
                                Guid.TryParse(live.GptType, out var type) && type == old.Type;
                            entries.Add(new(old.Guid, old.Offset, old.Size,
                                identical ? RecoveryEntryState.Present : RecoveryEntryState.Changed,
                                identical ? "Presente" : "Metadados alterados",
                                identical ? "GUID e geometria presentes. Conteúdo não verificado." :
                                "GUID presente, mas posição/tamanho/tipo alterados. Não classificar como perdida."));
                        }
                        else
                        {
                            var free = layout.Extents.Any(e => e.IsUnallocated && e.Offset <= old.Offset &&
                                e.EndExclusive >= checked(old.Offset + old.Size));
                            var state = old.Protected ? RecoveryEntryState.ProtectedMissing :
                                free ? RecoveryEntryState.MissingFree : RecoveryEntryState.MissingOccupied;
                            entries.Add(new(old.Guid, old.Offset, old.Size, state,
                                old.Protected ? "Protegida ausente" : free ? "Entrada ausente em faixa livre" :
                                "Entrada ausente em área ocupada",
                                old.Protected ? "Partição especial/sistema: intervenção não autorizada." :
                                free ? "Intervalo anteriormente ocupado está livre no inventário; dados não verificados." :
                                "Área antiga conflita com partição atual. Não reescrever a tabela."));
                        }
                    }
                    if (parts.Any(p => previous.Partitions.All(x => x.Guid != Guid.Parse(p.PartitionGuid))))
                        warnings.Add("Novas entradas GPT detectadas; não associar automaticamente a entradas anteriores.");
                    if (disk.Number != previous.DiskNumber)
                        warnings.Add("Número do disco mudou: associação exclusivamente por UniqueId.");
                }
            }
        }
        catch (InvalidDataException)
        {
            blockers.Add("Inventário inválido ou geometria sobreposta; comparação interrompida.");
        }
        catch (OverflowException)
        {
            blockers.Add("Intervalo fora de limites numéricos; comparação interrompida.");
        }
        warnings.Add("Área não alocada não comprova dados recuperáveis. Sem leitura de setores, assinaturas ou filesystem.");
        warnings.Add("Não formate nem grave no disco suspeito; preserve cópia forense quando necessário.");
        var summary = blockers.Count > 0
            ? "COMPARAÇÃO INCONCLUSIVA: " + string.Join(" ", blockers)
            : $"{entries.Count} entrada(s) anteriores, " +
              $"{entries.Count(e => e.State == RecoveryEntryState.MissingFree)} ausente(s) em espaço livre, " +
              $"{entries.Count(e => e.State == RecoveryEntryState.MissingOccupied)} em conflito e " +
              $"{entries.Count(e => e.State == RecoveryEntryState.ProtectedMissing)} protegida(s) ausente(s).";
        return new(diskNumber, current.CapturedAt, entries.ToArray(), blockers.ToArray(),
            warnings.ToArray(), summary + " SEM VARREDURA OU RECUPERAÇÃO. GRAVAÇÕES BLOQUEADAS.");
    }
}
