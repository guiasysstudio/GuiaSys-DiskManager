using System.Text;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Immutable multi-target confirmation challenge. The expected text includes ALL disk
/// numbers; no first-item shorthand is sufficient to authorize a different target.
/// </summary>
public sealed record OperationConfirmation(string Summary, string ExpectedResponse,
    IReadOnlyList<int> DiskNumbers)
{
    public bool Matches(string? answer) => string.Equals(answer?.Trim(),
        ExpectedResponse, StringComparison.Ordinal);

    public static OperationConfirmation Create(IReadOnlyList<StorageOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count == 0 || operations.Any(x => x is null || x.DiskNumber < 0))
            throw new ArgumentException("A confirmação exige operações e discos válidos.", nameof(operations));

        var disks = operations.Select(x => x.DiskNumber).Distinct().OrderBy(x => x).ToArray();
        var code = (disks.Length == 1 ? "DISCO " : "DISCOS ") + string.Join(",", disks);
        var summary = new StringBuilder();
        summary.AppendLine("Alvos envolvidos: " + string.Join(", ", disks.Select(x => $"Disco {x}")));
        summary.AppendLine("As seguintes ações foram solicitadas:");
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            var target = op.PartitionNumber is int number ? $"Disco {op.DiskNumber}, Partição {number}" : $"Disco {op.DiskNumber}";
            summary.AppendLine($"{i + 1}. [{target}] {op.Description}");
        }
        summary.AppendLine();
        summary.AppendLine("Revise TODOS os discos envolvidos. Uma confirmação não substitui a revalidação de segurança e identidade.");
        return new OperationConfirmation(summary.ToString(), code, disks);
    }
}
