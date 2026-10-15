using System.Diagnostics;
using System.Text.Json;
using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Fail-closed execution lifecycle. The production storage-write gate is always consulted
/// before a command delegate can be invoked. A process result is not a postcondition.
/// </summary>
public sealed class SafeExecutionCoordinator(
    IAppLogger logger,
    IDiskInventoryService? inventory,
    ISafetyService? safety,
    ILicenseEntitlements? licensing,
    IOperationJournal journal,
    Func<StorageOperation, CancellationToken, Task<ProcessOutput>> command)
{
    private static readonly SemaphoreSlim GlobalProcessGate = new(1, 1);

    public async Task<OperationResult> ExecuteAsync(StorageOperation op, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(op);
        var timer = Stopwatch.StartNew();
        if (!StorageWriteGate.ReleasedAfterSafetyAudit)
            return new OperationResult(op.Id, false, "Executor bloqueado por segurança.",
                timer.Elapsed, StorageExecutionOutcome.Rejected);

        var locked = false;
        var prepared = false;
        var mayHaveDispatched = false;
        var attemptId = Guid.NewGuid();
        try
        {
            await GlobalProcessGate.WaitAsync(cancellationToken);
            locked = true;
            if (journal.FindUnresolvedAttempts().Count > 0)
                throw new InvalidOperationException("Existe operação com estado indeterminado. Reconciliação humana obrigatória.");
            if (inventory is null || safety is null || licensing is null)
                throw new InvalidOperationException("Executor sem dependências de segurança.");
            if (!AdministratorService.IsAdministrator())
                throw new InvalidOperationException("Execução exige privilégios administrativos.");
            var entitlement = await licensing.CheckAsync(cancellationToken);
            if (!StorageWriteGate.Allows(entitlement))
                throw new InvalidOperationException("Licenciamento insuficiente para autorizar gravação.");
            var before = await inventory.ReadAsync(cancellationToken);
            _ = StorageGeometry.Build(before);
            var decision = safety.Evaluate(op, before);
            if (!decision.Allowed)
                throw new InvalidOperationException("Segurança recusou a operação: " + decision.Reason);

            journal.Append(OperationJournal.Record(attemptId, op, OperationPhase.Prepared, "PREPARED"));
            prepared = true;
            journal.Append(OperationJournal.Record(attemptId, op, OperationPhase.Dispatching, "DISPATCHING"));
            mayHaveDispatched = true;

            var result = await command(op, cancellationToken);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("O comando Windows terminou com erro.");
            using var response = JsonDocument.Parse(result.StandardOutput);
            if (!response.RootElement.TryGetProperty("Success", out var success) ||
                success.ValueKind != JsonValueKind.True)
                throw new InvalidOperationException("A saída do comando não comprova sucesso.");

            // Read the state even if the original caller canceled after command dispatch.
            var after = await inventory.ReadAsync(CancellationToken.None);
            _ = StorageGeometry.Build(after);
            var decisionAfter = OperationPostconditionVerifier.Verify(op, before, after);
            var status = decisionAfter.Verified ? StorageExecutionOutcome.Verified
                : StorageExecutionOutcome.Indeterminate;
            journal.Append(OperationJournal.Record(attemptId, op, OperationPhase.Reconciled,
                decisionAfter.Verified ? "POSTCONDITION_VERIFIED" : "POSTCONDITION_UNKNOWN", status));
            return new OperationResult(op.Id, decisionAfter.Verified, decisionAfter.Reason,
                timer.Elapsed, status);
        }
        catch (Exception ex)
        {
            var status = mayHaveDispatched ? StorageExecutionOutcome.Indeterminate
                : StorageExecutionOutcome.Rejected;
            if (prepared)
            {
                try
                {
                    journal.Append(OperationJournal.Record(attemptId, op, OperationPhase.Reconciled,
                        mayHaveDispatched ? "EXECUTION_UNKNOWN" : "REJECTED", status));
                }
                catch (Exception journalError)
                {
                    logger.Error("storage.execution.journal_failure", journalError,
                        new { op.Id, attemptId });
                    status = StorageExecutionOutcome.Indeterminate;
                }
            }
            logger.Error("storage.execution.failed", ex,
                new { op.Id, op.Type, op.DiskNumber, attemptId, status });
            return new OperationResult(op.Id, false,
                status == StorageExecutionOutcome.Indeterminate
                    ? "Resultado indeterminado: não repita a operação; consulte o journal e reavalie o disco."
                    : "Operação recusada antes do início: " + ex.Message,
                timer.Elapsed, status);
        }
        finally
        {
            if (locked) GlobalProcessGate.Release();
        }
    }
}
