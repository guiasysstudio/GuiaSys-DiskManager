using System.IO;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

public enum OperationPhase { Prepared, Dispatching, Reconciled }
public enum OperationOutcome { Rejected, Verified, Indeterminate }

/// <summary>
/// Minimal, privacy-preserving immutable audit entry. No license keys, filesystem paths,
/// volume labels, serials, script output or user-supplied exception messages are persisted.
/// </summary>
public sealed record OperationJournalEntry(
    Guid AttemptId, Guid OperationId, DateTimeOffset AtUtc,
    int DiskNumber, int? PartitionNumber, StorageOperationType Type,
    OperationPhase Phase, OperationOutcome? Outcome, string Code);

public interface IOperationJournal
{
    void Append(OperationJournalEntry entry);
    IReadOnlyList<Guid> FindUnresolvedAttempts();
}

/// <summary>
/// Durable append-only JSONL. Sync Flush(true) precedes dispatch; a write failure
/// must abort the operation rather than running a command without an audit trail.
/// </summary>
public sealed class OperationJournal : IOperationJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private readonly object _sync = new();
    private readonly string _path;
    public string JournalPath => _path;

    public OperationJournal(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GuiaSys", "DiskManager", "Journal", "operations.jsonl");
    }

    public void Append(OperationJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.AttemptId == Guid.Empty || entry.OperationId == Guid.Empty ||
            entry.DiskNumber < 0 || !Enum.IsDefined(entry.Type) ||
            !Enum.IsDefined(entry.Phase) || entry.Code.Length is < 1 or > 64 ||
            entry.Code.Any(x => !char.IsAsciiLetterUpper(x) && x != '_'))
            throw new ArgumentException("Registro de journal inválido.", nameof(entry));
        var row = JsonSerializer.Serialize(entry, JsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(row);
        lock (_sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    public IReadOnlyList<Guid> FindUnresolvedAttempts()
    {
        lock (_sync)
        {
            if (!File.Exists(_path)) return [];
            var latest = new Dictionary<Guid, OperationJournalEntry>();
            foreach (var row in File.ReadLines(_path, Encoding.UTF8))
            {
                if (row.Length == 0) continue;
                var entry = JsonSerializer.Deserialize<OperationJournalEntry>(row, JsonOptions)
                    ?? throw new InvalidDataException("Entrada de journal incompleta.");
                if (entry.AttemptId == Guid.Empty || entry.OperationId == Guid.Empty)
                    throw new InvalidDataException("ID inválido no journal.");
                if (latest.TryGetValue(entry.AttemptId, out var previous) &&
                    (previous.OperationId != entry.OperationId || previous.DiskNumber != entry.DiskNumber))
                    throw new InvalidDataException("Journal inconsistente. Revisão manual obrigatória.");
                latest[entry.AttemptId] = entry;
            }
            return latest.Values.Where(x => x.Phase != OperationPhase.Reconciled ||
                x.Outcome == OperationOutcome.Indeterminate)
                .Select(x => x.AttemptId).OrderBy(x => x).ToArray();
        }
    }

    public static OperationJournalEntry Record(Guid attemptId, StorageOperation operation,
        OperationPhase phase, string code, OperationOutcome? outcome = null) =>
        new(attemptId, operation.Id, DateTimeOffset.UtcNow, operation.DiskNumber,
            operation.PartitionNumber, operation.Type, phase, outcome, code);
}
