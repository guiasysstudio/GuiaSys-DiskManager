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
    ILicenseEntitlements? licensing = null, IOperationJournal? journal = null) : IStorageOperationExecutor
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
          if ([string]$disk.PartitionStyle -ieq 'RAW') {
            if ($null -eq $disk.NumberOfPartitions -or [int]$disk.NumberOfPartitions -ne 0) {
              throw 'RAW_PARTITION_COUNT_UNVERIFIED'
            }
            $otherParts = @()
          } else {
            # Query failures must never be treated as an empty partition table.
            $otherParts = @(Get-Partition -DiskNumber $disk.Number -ErrorAction Stop)
          }
          foreach ($other in $otherParts) {
            if ([string]$disk.PartitionStyle -ieq 'GPT' -and
                ([string]::IsNullOrWhiteSpace([string]$other.Guid) -or
                 [string]::IsNullOrWhiteSpace([string]$other.GptType))) {
              throw 'GPT_PARTITION_METADATA_UNVERIFIED'
            }
            $otherType = ([string]$other.Type).ToUpperInvariant()
            $otherGptType = ([string]$other.GptType).Trim([char[]]@('{','}'))
            $otherNonBasic = $otherGptType.Length -gt 0 -and $otherGptType -ine 'ebd0a0a2-b9e5-4433-87c0-68b6b72699c7'
            if ($other.IsBoot -or $other.IsSystem -or $other.IsHidden -or $otherNonBasic -or
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
          $partGptType = ([string]$partition.GptType).Trim([char[]]@('{','}'))
          $partNonBasic = $partGptType.Length -gt 0 -and $partGptType -ine 'ebd0a0a2-b9e5-4433-87c0-68b6b72699c7'
          $partIdentity = "gpt:$($partitionGuid.ToString('D'))|disk:$([int]$disk.Number)|part:$([int]$partition.PartitionNumber)|offset:$([int64]$partition.Offset)|size:$([int64]$partition.Size)|type:$partType"
          if ($partIdentity -cne [string]$request.ExpectedPartitionIdentity) { throw 'PARTITION_IDENTITY_CHANGED' }
          if ($partition.IsBoot -or $partition.IsSystem -or $partition.IsHidden -or $partNonBasic -or
              [string]$partition.DriveLetter -eq 'C' -or $partType.Contains('EFI') -or
              $partType.Contains('SYSTEM') -or $partType.Contains('RESERVED') -or
              $partType.Contains('RECOVERY')) { throw 'PROTECTED_PARTITION' }
          if ($disk.IsOffline -or $disk.IsReadOnly -or $partition.IsOffline -or $partition.IsReadOnly) { throw 'TARGET_NOT_WRITABLE' }
        }
        if ([string]$request.Type -in @('InitializeGpt','InitializeMbr')) {
          if ([string]$disk.PartitionStyle -ine 'RAW' -or @($otherParts).Count -ne 0 -or
              $disk.IsOffline -or $disk.IsReadOnly) { throw 'DISK_NOT_EMPTY_RAW' }
        }
        if ([string]$request.Type -in @('InitializeGpt','InitializeMbr')) {
          # Independent C10 check against the LIVE physical device before initialization.
          # 32-bit classic MBR addresses 2^32 logical sectors, not a fixed 2 TB.
          $sectorSize = [uint64]$disk.LogicalSectorSize
          if ($sectorSize -lt 512 -or $sectorSize -gt 65536 -or
              ($sectorSize -band ($sectorSize - 1)) -ne 0 -or
              [uint64]$disk.Size % $sectorSize -ne 0) {
            throw 'RAW_INITIALIZATION_SECTOR_UNVERIFIED'
          }
          if ([string]$request.Type -eq 'InitializeMbr' -and
              [uint64]$disk.Size -gt (([uint64]4294967296) * $sectorSize)) {
            throw 'MBR_CLASSIC_CAPACITY_EXCEEDED'
          }
        }
        if ([string]$request.Type -eq 'CreatePartition') {
          if ([string]$disk.PartitionStyle -ine 'GPT' -or $disk.IsOffline -or $disk.IsReadOnly) {
            throw 'GPT_DISK_REQUIRED'
          }
        }
        if ([string]$request.Type -eq 'ResizePartition') {
          # Defense in depth: query LIVE Windows-supported bounds immediately before
          # Resize-Partition, after identity and protected-layout verification.
          if ($null -eq $partition -or $null -eq $request.SizeBytes) {
            throw 'RESIZE_TARGET_OR_SIZE_MISSING'
          }
          $newSize = [uint64]$request.SizeBytes
          $sector = [uint64]$disk.LogicalSectorSize
          if ($sector -lt 512 -or $sector -gt 65536 -or
              ($sector -band ($sector - 1)) -ne 0) { throw 'RESIZE_SECTOR_UNVERIFIED' }
          if ($newSize -le 0 -or $newSize -eq [uint64]$partition.Size -or
              $newSize % $sector -ne 0) { throw 'RESIZE_SIZE_NOT_ALIGNED_OR_UNCHANGED' }
          $actualBounds = Get-PartitionSupportedSize -DiskNumber $disk.Number -PartitionNumber $partition.PartitionNumber -ErrorAction Stop
          if ($null -eq $actualBounds -or $null -eq $actualBounds.SizeMin -or $null -eq $actualBounds.SizeMax -or
              $actualBounds.SizeMin -le 0 -or $actualBounds.SizeMin -gt $actualBounds.SizeMax -or
              $newSize -lt [uint64]$actualBounds.SizeMin -or $newSize -gt [uint64]$actualBounds.SizeMax) {
            throw 'RESIZE_WINDOWS_SUPPORTED_RANGE_CHANGED'
          }
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

    public Task<OperationResult> ExecuteAsync(StorageOperation operation, CancellationToken cancellationToken)
    {
        if (operation.Type == StorageOperationType.MovePartition)
            return Task.FromResult(new OperationResult(operation.Id, false,
                "Prévia C09 sem mecanismo de movimentação; nenhuma escrita iniciada.",
                TimeSpan.Zero, StorageExecutionOutcome.Rejected));
        var coordinator = new SafeExecutionCoordinator(logger, inventory, safety, licensing,
            journal ?? new OperationJournal(),
            (target, token) => PowerShellProcessRunner.RunEncodedAsync(Script,
                JsonSerializer.Serialize(target, JsonOptions), TimeSpan.FromMinutes(30), token));
        return coordinator.ExecuteAsync(operation, cancellationToken);
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
