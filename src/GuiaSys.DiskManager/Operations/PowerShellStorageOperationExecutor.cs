using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GuiaSys.DiskManager.Models;
using GuiaSys.DiskManager.Licensing;
using GuiaSys.DiskManager.Services;

namespace GuiaSys.DiskManager.Operations;

/// <summary>Executes only allow-listed operations through a constant script and strongly validated JSON input.</summary>
public sealed class PowerShellStorageOperationExecutor(IAppLogger logger,
    IDiskInventoryService? inventory = null, ISafetyService? safety = null,
    ILicenseEntitlements? licensing = null) : IStorageOperationExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
        $disk = Get-Disk -Number ([int]$request.DiskNumber) -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace([string]$disk.UniqueId)) { throw 'DEVICE_STRONG_IDENTITY_UNAVAILABLE' }
        $actualIdentity = "uid:$(([string]$disk.UniqueId).Trim())"
        if ($actualIdentity -cne [string]$request.ExpectedDiskIdentity) { throw 'DEVICE_IDENTITY_CHANGED' }
        if ($disk.Number -eq 0 -or $disk.IsBoot -or $disk.IsSystem) { throw 'PROTECTED_DISK' }
        $layoutMutations = @('SetDiskOnline','SetDiskOffline','SetDiskReadOnly','ClearDiskReadOnly',
          'InitializeGpt','InitializeMbr','CreatePartition','DeletePartition','ResizePartition')
        if ($layoutMutations -contains [string]$request.Type) {
          $otherParts = @(Get-Partition -DiskNumber $disk.Number -ErrorAction SilentlyContinue)
          foreach ($other in $otherParts) {
            $otherType = ([string]$other.Type).ToUpperInvariant()
            if ($other.IsBoot -or $other.IsSystem -or $other.IsHidden -or
                [string]$other.DriveLetter -eq 'C' -or $otherType.Contains('EFI') -or
                $otherType.Contains('SYSTEM') -or $otherType.Contains('RESERVED') -or
                $otherType.Contains('RECOVERY')) { throw 'PROTECTED_LAYOUT' }
          }
        }
        $partition = $null
        if ($null -ne $request.PartitionNumber) {
          $partition = Get-Partition -DiskNumber $disk.Number -PartitionNumber ([int]$request.PartitionNumber) -ErrorAction Stop
          $partitionGuid = [guid]::Empty
          if (-not [guid]::TryParse([string]$partition.Guid, [ref]$partitionGuid) -or $partitionGuid -eq [guid]::Empty) { throw 'PARTITION_IDENTITY_UNAVAILABLE' }
          $partType = ([string]$partition.Type).Trim().ToUpperInvariant()
          $partIdentity = "gpt:$($partitionGuid.ToString('D'))|disk:$([int]$disk.Number)|part:$([int]$partition.PartitionNumber)|offset:$([int64]$partition.Offset)|size:$([int64]$partition.Size)|type:$partType"
          if ($partIdentity -cne [string]$request.ExpectedPartitionIdentity) { throw 'PARTITION_IDENTITY_CHANGED' }
          if ($partition.IsBoot -or $partition.IsSystem -or $partition.IsHidden -or
              [string]$partition.DriveLetter -eq 'C' -or $partType.Contains('EFI') -or
              $partType.Contains('SYSTEM') -or $partType.Contains('RESERVED') -or
              $partType.Contains('RECOVERY')) { throw 'PROTECTED_PARTITION' }
          if ($disk.IsOffline -or $disk.IsReadOnly -or $partition.IsOffline -or $partition.IsReadOnly) { throw 'TARGET_NOT_WRITABLE' }
        }
        switch ([string]$request.Type) {
          'SetDiskOnline' { Set-Disk -Number $disk.Number -IsOffline $false -ErrorAction Stop }
          'SetDiskOffline' { Set-Disk -Number $disk.Number -IsOffline $true -ErrorAction Stop }
          'SetDiskReadOnly' { Set-Disk -Number $disk.Number -IsReadOnly $true -ErrorAction Stop }
          'ClearDiskReadOnly' { Set-Disk -Number $disk.Number -IsReadOnly $false -ErrorAction Stop }
          'InitializeGpt' { Initialize-Disk -Number $disk.Number -PartitionStyle GPT -ErrorAction Stop }
          'InitializeMbr' { Initialize-Disk -Number $disk.Number -PartitionStyle MBR -ErrorAction Stop }
          'CreatePartition' {
            if ($null -eq $request.SizeBytes) { New-Partition -DiskNumber $disk.Number -UseMaximumSize -ErrorAction Stop | Out-Null }
            else { New-Partition -DiskNumber $disk.Number -Size ([uint64]$request.SizeBytes) -ErrorAction Stop | Out-Null }
          }
          'DeletePartition' { $partition | Remove-Partition -Confirm:$false -ErrorAction Stop }
          'FormatPartition' {
            $format = @{ FileSystem=[string]$request.FileSystem; NewFileSystemLabel=[string]$request.Label; Confirm=$false; Force=$true; ErrorAction='Stop' }
            if ([bool]$request.QuickFormat) { $format.Full = $false } else { $format.Full = $true }
            if ($null -ne $request.AllocationUnitSize) { $format.AllocationUnitSize = [int]$request.AllocationUnitSize }
            $partition | Format-Volume @format | Out-Null
          }
          'SetDriveLetter' { Set-Partition -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -NewDriveLetter ([char]([string]$request.DriveLetter)[0]) -ErrorAction Stop }
          'RemoveDriveLetter' {
            if (-not $partition.DriveLetter) { throw 'PARTITION_HAS_NO_DRIVE_LETTER' }
            $path = ([string]$partition.DriveLetter) + ':\'
            Remove-PartitionAccessPath -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -AccessPath $path -ErrorAction Stop
          }
          'ResizePartition' { Resize-Partition -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -Size ([uint64]$request.SizeBytes) -ErrorAction Stop }
          'SetVolumeLabel' { $partition | Get-Volume -ErrorAction Stop | Set-Volume -NewFileSystemLabel ([string]$request.Label) -ErrorAction Stop }
          default { throw 'OPERATION_NOT_ALLOWLISTED' }
        }
        [pscustomobject]@{ Success=$true } | ConvertTo-Json -Compress
        """;

    public async Task<OperationResult> ExecuteAsync(StorageOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var timer = Stopwatch.StartNew();
        logger.Information("storage.operation.started", new { operation.Id, operation.Type, operation.DiskNumber, operation.PartitionNumber });
        try
        {
            // Authorization must be enforced at the executor boundary as well as in the UI.
            if (!StorageWriteGate.ReleasedAfterSafetyAudit)
                throw new InvalidOperationException("Executor bloqueado até a liberação formal de gravações.");
            if (inventory is null || safety is null || licensing is null)
                throw new InvalidOperationException("Executor sem dependências de segurança obrigatórias.");
            if (!AdministratorService.IsAdministrator())
                throw new InvalidOperationException("É necessário executar como administrador.");
            var license = await licensing.CheckAsync(cancellationToken);
            if (!StorageWriteGate.Allows(license))
                throw new InvalidOperationException("Licenciamento não autoriza a gravação.");
            var snapshot = await inventory.ReadAsync(cancellationToken);
            _ = StorageGeometry.Build(snapshot);
            var decision = safety.Evaluate(operation, snapshot);
            if (!decision.Allowed)
                throw new InvalidOperationException("Operação recusada pela política do executor: " + decision.Reason);
            var json = JsonSerializer.Serialize(operation, JsonOptions);
            var output = await PowerShellProcessRunner.RunEncodedAsync(Script, json, TimeSpan.FromMinutes(30), cancellationToken);
            timer.Stop();
            if (output.ExitCode != 0) throw new InvalidOperationException(FriendlyError(output.StandardError), new InvalidOperationException(TrimTechnicalError(output.StandardError)));
            logger.Information("storage.operation.completed", new { operation.Id, durationMs = timer.Elapsed.TotalMilliseconds });
            return new OperationResult(operation.Id, true, "Operação concluída.", timer.Elapsed);
        }
        catch (Exception exception)
        {
            timer.Stop();
            logger.Error("storage.operation.failed", exception, new { operation.Id, operation.Type, operation.DiskNumber, durationMs = timer.Elapsed.TotalMilliseconds });
            return new OperationResult(operation.Id, false, exception.Message, timer.Elapsed);
        }
    }

    private static string FriendlyError(string error)
    {
        if (error.Contains("PARTITION_IDENTITY_CHANGED", StringComparison.Ordinal)) return "A identidade da partição mudou antes da execução.";
        if (error.Contains("PROTECTED_", StringComparison.Ordinal) || error.Contains("PARTITION_IDENTITY_UNAVAILABLE", StringComparison.Ordinal)) return "Alvo protegido ou sem identidade forte; operação recusada.";
        if (error.Contains("DEVICE_IDENTITY_CHANGED", StringComparison.Ordinal)) return "A identidade do disco mudou. A operação foi cancelada.";
        if (error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || error.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase)) return "Acesso negado. Execute o aplicativo como administrador.";
        if (error.Contains("access path is already in use", StringComparison.OrdinalIgnoreCase) || error.Contains("caminho de acesso solicitado já está em uso", StringComparison.OrdinalIgnoreCase))
            return "A letra ou o caminho de acesso selecionado já está em uso.";
        if (error.Contains("in use", StringComparison.OrdinalIgnoreCase) || error.Contains("em uso", StringComparison.OrdinalIgnoreCase)) return "O volume está em uso e o Windows recusou a operação.";
        if (error.Contains("not supported", StringComparison.OrdinalIgnoreCase)) return "A operação ou sistema de arquivos não é suportado neste dispositivo.";
        return "O Windows recusou a operação. Consulte o log técnico para detalhes.";
    }
    private static string TrimTechnicalError(string error) => error.Trim().Length <= 4000 ? error.Trim() : error.Trim()[..4000];
}
