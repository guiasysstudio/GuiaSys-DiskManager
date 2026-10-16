using System.IO;
using System.Text.Json;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

/// <summary>Runs a constant read-only script. No user-controlled value is interpolated into PowerShell.</summary>
public sealed class PowerShellDiskInventoryService(IAppLogger logger) : IDiskInventoryService
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        # Providers may return unsupported numbers/UInt64.MaxValue.
        function Safe-CounterLong($value) {
          if ($null -eq $value) { return $null }
          try {
            $raw = [string]$value
            if ($raw -notmatch '^[0-9]+$') { return $null }
            $parsed = [uint64]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture)
            if ($parsed -gt [long]::MaxValue) { return $null }
            return [long]$parsed
          } catch { return $null }
        }
        function Safe-CounterPercent($value) {
          $n = Safe-CounterLong $value
          if ($null -ne $n -and $n -le 100) { return [int]$n }
          return $null
        }
        function Safe-CounterTemperature($value) {
          if ($null -eq $value) { return $null }
          try {
            $n = [int]$value
            if ($n -ge -20 -and $n -le 125) { return $n }
          } catch {}
          return $null
        }
        $physicalDisks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue)
        $windowsDisks = @(Get-Disk -ErrorAction Stop)
        $disks = @($windowsDisks | ForEach-Object {
          $disk = $_
          # Get-PhysicalDisk.DeviceId is a subsystem identifier; it is not necessarily
          # Get-Disk.Number. Never infer a hardware match from the numeric IDs alone.
          $physical = $null
          if (-not [string]::IsNullOrWhiteSpace([string]$disk.SerialNumber)) {
            $diskCandidates = @($windowsDisks | Where-Object {
              ([string]$_.SerialNumber).Trim() -eq ([string]$disk.SerialNumber).Trim()
            })
            $physicalCandidates = @($physicalDisks | Where-Object {
              -not [string]::IsNullOrWhiteSpace([string]$_.SerialNumber) -and
              ([string]$_.SerialNumber).Trim() -eq ([string]$disk.SerialNumber).Trim()
            })
            # One-to-one serial match in both namespaces. Duplicate serials are ambiguous.
            if ($diskCandidates.Count -eq 1 -and $physicalCandidates.Count -eq 1) {
              $physical = $physicalCandidates[0]
            }
          }
          $reliability = $null
          if ($physical) {
            try { $reliability = $physical | Get-StorageReliabilityCounter -ErrorAction Stop } catch { }
          }
          [pscustomobject]@{
            Number=[int]$disk.Number; UniqueId=[string]$disk.UniqueId; FriendlyName=[string]$disk.FriendlyName
            Manufacturer=[string]$disk.Manufacturer; SerialNumber=[string]$disk.SerialNumber
            PartitionStyle=[string]$disk.PartitionStyle; BusType=[string]$disk.BusType; MediaType=if ($physical) {[string]$physical.MediaType} else {'Unspecified'}
            HealthStatus=[string]$disk.HealthStatus; OperationalStatus=[string]($disk.OperationalStatus -join ', ')
            TemperatureC=(Safe-CounterTemperature $(if ($reliability) {$reliability.Temperature} else {$null}))
            ReliabilityCountersAvailable=[bool]($null -ne $reliability)
            WearPercent=(Safe-CounterPercent $(if ($reliability) {$reliability.Wear} else {$null}))
            PowerOnHours=(Safe-CounterLong $(if ($reliability) {$reliability.PowerOnHours} else {$null}))
            ReadErrorsTotal=(Safe-CounterLong $(if ($reliability) {$reliability.ReadErrorsTotal} else {$null}))
            ReadErrorsUncorrected=(Safe-CounterLong $(if ($reliability) {$reliability.ReadErrorsUncorrected} else {$null}))
            WriteErrorsTotal=(Safe-CounterLong $(if ($reliability) {$reliability.WriteErrorsTotal} else {$null}))
            WriteErrorsUncorrected=(Safe-CounterLong $(if ($reliability) {$reliability.WriteErrorsUncorrected} else {$null}))
            LogicalSectorSize=if ($disk.LogicalSectorSize -gt 0) {[int]$disk.LogicalSectorSize} else {$null}
            PhysicalSectorSize=if ($disk.PhysicalSectorSize -gt 0) {[int]$disk.PhysicalSectorSize} else {$null}
            Size=[int64]$disk.Size; IsBoot=[bool]$disk.IsBoot; IsSystem=[bool]$disk.IsSystem
            IsOffline=[bool]$disk.IsOffline; IsReadOnly=[bool]$disk.IsReadOnly
          }
        })
        $parts = @(Get-Partition | ForEach-Object {
          $partition = $_
          $volume = $null
          try { $volume = $partition | Get-Volume -ErrorAction Stop } catch { }
          # Optional, read-only capability probe. A failed/unsupported query never
          # guesses a minimum or maximum. Limit cost to non-system GPT data volumes.
          $resizeMin = $null; $resizeMax = $null
          $ownerDisk = @($windowsDisks | Where-Object { $_.Number -eq $partition.DiskNumber } | Select-Object -First 1)
          $gptKind = ([string]$partition.GptType).Trim([char[]]@('{','}'))
          if ([int]$partition.DiskNumber -gt 0 -and $ownerDisk.Count -eq 1 -and
              [string]$ownerDisk[0].PartitionStyle -ieq 'GPT' -and
              -not $ownerDisk[0].IsOffline -and -not $ownerDisk[0].IsReadOnly -and
              -not $partition.IsBoot -and -not $partition.IsSystem -and
              -not $partition.IsHidden -and -not $partition.IsOffline -and -not $partition.IsReadOnly -and
              [string]$partition.DriveLetter -ne 'C' -and
              $gptKind -ieq 'ebd0a0a2-b9e5-4433-87c0-68b6b72699c7' -and
              -not [string]::IsNullOrWhiteSpace([string]$partition.Guid)) {
            try {
              $sizeBounds = Get-PartitionSupportedSize -DiskNumber ([int]$partition.DiskNumber) -PartitionNumber ([int]$partition.PartitionNumber) -ErrorAction Stop
              if ($sizeBounds -and $sizeBounds.SizeMin -gt 0 -and
                  $sizeBounds.SizeMax -ge $sizeBounds.SizeMin -and
                  $sizeBounds.SizeMax -le [long]::MaxValue -and
                  $sizeBounds.SizeMin -le [uint64]$partition.Size -and
                  $sizeBounds.SizeMax -ge [uint64]$partition.Size) {
                $resizeMin = [int64]$sizeBounds.SizeMin
                $resizeMax = [int64]$sizeBounds.SizeMax
              }
            } catch {
              # Unsupported filesystem, storage driver, encryption or access failure:
              # display 'unavailable', prohibit resize preparation.
            }
          }
          [pscustomobject]@{
            DiskNumber=[int]$partition.DiskNumber; PartitionNumber=[int]$partition.PartitionNumber
            PartitionGuid=if ($partition.Guid) {[string]$partition.Guid} else {''}
            GptType=if ($partition.GptType) {[string]$partition.GptType} else {''}
            AccessPaths=[string]($partition.AccessPaths -join ';'); DriveLetter=if ($partition.DriveLetter) {[string]$partition.DriveLetter} else {''}
            Type=[string]$partition.Type; Offset=[int64]$partition.Offset; Size=[int64]$partition.Size
            IsBoot=[bool]$partition.IsBoot; IsSystem=[bool]$partition.IsSystem
            IsHidden=[bool]$partition.IsHidden; IsReadOnly=[bool]$partition.IsReadOnly; IsOffline=[bool]$partition.IsOffline
            FileSystem=if ($volume) {[string]$volume.FileSystem} else {''}
            Label=if ($volume) {[string]$volume.FileSystemLabel} else {''}
            VolumeSizeRemaining=if ($volume) {[int64]$volume.SizeRemaining} else {[int64]0}
            VolumeCapacityBytes=if ($volume -and $volume.Size -gt 0 -and $volume.SizeRemaining -ge 0 -and $volume.SizeRemaining -le $volume.Size) {[int64]$volume.Size} else {$null}
            VolumeAvailableBytes=if ($volume -and $volume.Size -gt 0 -and $volume.SizeRemaining -ge 0 -and $volume.SizeRemaining -le $volume.Size) {[int64]$volume.SizeRemaining} else {$null}
            ResizeMinBytes=$resizeMin; ResizeMaxBytes=$resizeMax
            HealthStatus=if ($volume) {[string]$volume.HealthStatus} else {'Unknown'}
          }
        })
        [pscustomobject]@{ Disks=$disks; Partitions=$parts } | ConvertTo-Json -Depth 6 -Compress
        """;

    public async Task<StorageSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var output = await PowerShellProcessRunner.RunEncodedAsync(Script, null, TimeSpan.FromSeconds(30), cancellationToken);
        if (output.ExitCode != 0) throw new InvalidOperationException(ToFriendlyError(output.StandardError), new InvalidOperationException(TrimTechnicalError(output.StandardError)));
        try
        {
            var snapshot = ParseInventory(output.StandardOutput, DateTimeOffset.Now);
            logger.Information("storage.inventory.completed", new
            {
                disks = snapshot.Disks.Count, partitions = snapshot.Partitions.Count,
                durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds
            });
            return snapshot;
        }
        catch (JsonException exception) { throw new InvalidOperationException("O Windows retornou dados de armazenamento em formato inesperado. Consulte os logs.", exception); }
        catch (InvalidDataException exception) { throw new InvalidOperationException("O Windows retornou dados de armazenamento incompletos ou inconsistentes. Nenhuma alteração será permitida até o inventário ser atualizado. Consulte os logs.", exception); }
    }

    /// <summary>
    /// Pure parser used by production and tests. Missing optional volume/hardware metrics
    /// never fabricate zero free bytes or an SSD/HDD classification.
    /// </summary>
    internal static StorageSnapshot ParseInventory(string json, DateTimeOffset capturedAt)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var disks = ReadRequiredElements(root, "Disks").Select(ParseDisk).OrderBy(x => x.Number).ToArray();
        var partitions = ReadRequiredElements(root, "Partitions").Select(ParsePartition)
            .OrderBy(x => x.DiskNumber).ThenBy(x => x.Offset).ToArray();
        ValidateSnapshot(disks, partitions);
        return new StorageSnapshot(disks, partitions, capturedAt);
    }

    private static DiskInfo ParseDisk(JsonElement item) => new()
    {
        Number = ReadRequiredInt(item, "Number"), UniqueId = ReadString(item, "UniqueId"), FriendlyName = ReadString(item, "FriendlyName", "Dispositivo sem nome"),
        Manufacturer = ReadString(item, "Manufacturer"), SerialNumber = ReadString(item, "SerialNumber"), PartitionStyle = ReadRequiredString(item, "PartitionStyle"),
        BusType = ReadString(item, "BusType", "Unknown"), MediaType = ReadString(item, "MediaType", "Unspecified"), HealthStatus = ReadString(item, "HealthStatus", "Unknown"), OperationalStatus = ReadString(item, "OperationalStatus", "Unknown"), TemperatureC = ReadDiagnosticTemperature(item, "TemperatureC"), LogicalSectorSize = ReadNullableInt(item, "LogicalSectorSize"), PhysicalSectorSize = ReadNullableInt(item, "PhysicalSectorSize"),
        ReliabilityCountersAvailable = ReadOptionalBool(item, "ReliabilityCountersAvailable"),
        WearPercent = ReadDiagnosticPercent(item, "WearPercent"),
        PowerOnHours = ReadDiagnosticCounter(item, "PowerOnHours"),
        ReadErrorsTotal = ReadDiagnosticCounter(item, "ReadErrorsTotal"),
        ReadErrorsUncorrected = ReadDiagnosticCounter(item, "ReadErrorsUncorrected"),
        WriteErrorsTotal = ReadDiagnosticCounter(item, "WriteErrorsTotal"),
        WriteErrorsUncorrected = ReadDiagnosticCounter(item, "WriteErrorsUncorrected"),
        Size = ReadRequiredLong(item, "Size"), IsBoot = ReadRequiredBool(item, "IsBoot"), IsSystem = ReadRequiredBool(item, "IsSystem"), IsOffline = ReadRequiredBool(item, "IsOffline"), IsReadOnly = ReadRequiredBool(item, "IsReadOnly")
    };

    private static PartitionInfo ParsePartition(JsonElement item) => new()
    {
        DiskNumber = ReadRequiredInt(item, "DiskNumber"), PartitionNumber = ReadRequiredInt(item, "PartitionNumber"), PartitionGuid = ReadString(item, "PartitionGuid"), GptType = ReadString(item, "GptType"), AccessPaths = ReadString(item, "AccessPaths"), DriveLetter = ReadString(item, "DriveLetter"),
        Type = ReadRequiredString(item, "Type"), Offset = ReadRequiredLong(item, "Offset"), Size = ReadRequiredLong(item, "Size"), IsBoot = ReadRequiredBool(item, "IsBoot"), IsSystem = ReadRequiredBool(item, "IsSystem"),
        IsHidden = ReadRequiredBool(item, "IsHidden"), IsReadOnly = ReadRequiredBool(item, "IsReadOnly"), IsOffline = ReadRequiredBool(item, "IsOffline"), FileSystem = ReadString(item, "FileSystem"),
        Label = ReadString(item, "Label"), VolumeSizeRemaining = ReadLongOrDefault(item, "VolumeSizeRemaining"),
        VolumeCapacityBytes = ReadNullableLong(item, "VolumeCapacityBytes"), VolumeAvailableBytes = ReadNullableLong(item, "VolumeAvailableBytes"),
        ResizeMinBytes = ReadNullableLong(item, "ResizeMinBytes"), ResizeMaxBytes = ReadNullableLong(item, "ResizeMaxBytes"), HealthStatus = ReadString(item, "HealthStatus", "Unknown")
    };

    internal static IEnumerable<JsonElement> Elements(JsonElement element) => element.ValueKind switch { JsonValueKind.Array => element.EnumerateArray(), JsonValueKind.Object => [element], _ => [] };
    internal static IEnumerable<JsonElement> ReadRequiredElements(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
            throw InvalidField(key, "array ou objeto");
        return Elements(value);
    }

    private static string ReadString(JsonElement item, string key, string fallback = "") => item.TryGetProperty(key, out var value) && value.ValueKind is not JsonValueKind.Null ? value.ToString().Trim() : fallback;
    internal static string ReadRequiredString(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            throw InvalidField(key, "texto");
        var result = value.GetString()?.Trim() ?? "";
        if (result.Length == 0) throw InvalidField(key, "texto não vazio");
        return result;
    }

    internal static int ReadRequiredInt(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw InvalidField(key, "inteiro");
        return result;
    }

    internal static long ReadRequiredLong(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var result))
            throw InvalidField(key, "inteiro de 64 bits");
        return result;
    }

    internal static long? ReadNullableLong(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)) return result;
        throw InvalidField(key, "inteiro de 64 bits ou null");
    }

    private static bool ReadOptionalBool(JsonElement item, string key) =>
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    // Invalid optional hardware metrics are unknown, not zero or fatal inventory errors.
    internal static long? ReadDiagnosticCounter(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var count) || count < 0) return null;
        return count;
    }

    internal static int? ReadDiagnosticTemperature(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var temperature) || temperature is < -20 or > 125) return null;
        return temperature;
    }

    internal static int? ReadDiagnosticPercent(JsonElement item, string key)
    {
        var count = ReadDiagnosticCounter(item, key);
        return count is >= 0 and <= 100 ? (int)count.Value : null;
    }

    private static long ReadLongOrDefault(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : 0;

    internal static bool ReadRequiredBool(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw InvalidField(key, "booleano");
        return value.GetBoolean();
    }

    internal static int? ReadNullableInt(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        throw InvalidField(key, "inteiro ou null");
    }

    internal static void ValidateSnapshot(IReadOnlyList<DiskInfo> disks, IReadOnlyList<PartitionInfo> partitions)
    {
        // C02 uses one authoritative geometry invariant for Windows inventory and dry-run planner.
        _ = StorageGeometry.Build(new StorageSnapshot(disks, partitions, DateTimeOffset.MinValue));
        var strong = partitions.Where(p => p.HasStrongIdentity).ToArray();
        if (strong.GroupBy(p => (p.DiskNumber, Guid.Parse(p.PartitionGuid))).Any(g => g.Count() != 1))
            throw new InvalidDataException("Identidade GUID duplicada no mesmo disco.");
        if (partitions.Any(p => !string.IsNullOrWhiteSpace(p.PartitionGuid) && !p.HasStrongIdentity))
            throw new InvalidDataException("GUID de partição inválido.");
        if (partitions.Any(p => (p.ResizeMinBytes is null) != (p.ResizeMaxBytes is null) ||
            (p.ResizeMinBytes is not null && (!p.HasResizeLimits ||
             p.ResizeMinBytes.Value > p.Size || p.ResizeMaxBytes!.Value < p.Size))))
            throw new InvalidDataException("Limites de redimensionamento inconsistentes no inventário.");
    }

    private static InvalidDataException InvalidField(string key, string expected) => new($"Campo obrigatório '{key}' ausente ou inválido; esperado {expected}.");
    private static string ToFriendlyError(string error) => error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || error.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase)
        ? "Acesso negado ao consultar o armazenamento. Tente executar como administrador."
        : "Não foi possível consultar o armazenamento do Windows. Consulte os logs para detalhes.";
    private static string TrimTechnicalError(string error) => error.Trim().Length <= 4000 ? error.Trim() : error.Trim()[..4000];
}
