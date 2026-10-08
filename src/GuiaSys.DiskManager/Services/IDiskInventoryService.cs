using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

public interface IDiskInventoryService
{
    Task<StorageSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public interface IStorageOperationExecutor
{
    Task<OperationResult> ExecuteAsync(StorageOperation operation, CancellationToken cancellationToken);
}

public interface ISafetyService
{
    SafetyDecision Evaluate(StorageOperation operation, StorageSnapshot currentSnapshot);
}

public interface IAppLogger
{
    string LogDirectory { get; }
    void Information(string eventName, object? data = null);
    void Error(string eventName, Exception exception, object? data = null);
}
