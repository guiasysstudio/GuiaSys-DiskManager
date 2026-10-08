using System.Text.Json;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

/// <summary>Runs a constant read-only script. No user-controlled value is interpolated into PowerShell.</summary>
public sealed class PowerShellDiskInventoryService(IAppLogger logger) : IDiskInventoryService
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $physicalDisks = @(Get-PhysicalDisk -ErrorAction SilentlyContinue)
        $disks = @(Get-Disk | ForEach-Object {
          $disk = $_
          $physical = $physicalDisks | Where-Object { ([string]$_.DeviceId -eq [string]$disk.Number) -or ($disk.SerialNumber -and ([string]$_.SerialNumber).Trim() -eq ([string]$disk.SerialNumber).Trim()) } | Select-Object -First 1
          $temperature = $null
          if ($physical) { try { $temperature = ($physical | Get-StorageReliabilityCounter -ErrorAction Stop).Temperature } catch { } }
          [pscustomobject]@{
            Number=[int]$disk.Number; UniqueId=[string]$disk.UniqueId; FriendlyName=[string]$disk.FriendlyName
            Manufacturer=[string]$disk.Manufacturer; SerialNumber=[string]$disk.SerialNumber
            PartitionStyle=[string]$disk.PartitionStyle; BusType=[string]$disk.BusType; MediaType=if ($physical) {[string]$physical.MediaType} else {'Unspecified'}
            HealthStatus=[string]$disk.HealthStatus; OperationalStatus=[string]($disk.OperationalStatus -join ', '); TemperatureC=$temperature
            Size=[int64]$disk.Size; IsBoot=[bool]$disk.IsBoot; IsSystem=[bool]$disk.IsSystem
            IsOffline=[bool]$disk.IsOffline; IsReadOnly=[bool]$disk.IsReadOnly
          }
        })
        $parts = @(Get-Partition | ForEach-Object {
          $partition = $_
          $volume = $null
          try { $volume = $partition | Get-Volume -ErrorAction Stop } catch { }
          [pscustomobject]@{
            DiskNumber=[int]$partition.DiskNumber; PartitionNumber=[int]$partition.PartitionNumber
            AccessPaths=[string]($partition.AccessPaths -join ';'); DriveLetter=if ($partition.DriveLetter) {[string]$partition.DriveLetter} else {''}
            Type=[string]$partition.Type; Offset=[int64]$partition.Offset; Size=[int64]$partition.Size
            IsBoot=[bool]$partition.IsBoot; IsSystem=[bool]$partition.IsSystem
            IsHidden=[bool]$partition.IsHidden; IsReadOnly=[bool]$partition.IsReadOnly; IsOffline=[bool]$partition.IsOffline
            FileSystem=if ($volume) {[string]$volume.FileSystem} else {''}
            Label=if ($volume) {[string]$volume.FileSystemLabel} else {''}
            VolumeSizeRemaining=if ($volume) {[int64]$volume.SizeRemaining} else {[int64]0}
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
            using var document = JsonDocument.Parse(output.StandardOutput);
            var root = document.RootElement;
            var disks = ReadRequiredElements(root, "Disks").Select(ParseDisk).OrderBy(x => x.Number).ToArray();
            var partitions = ReadRequiredElements(root, "Partitions").Select(ParsePartition).OrderBy(x => x.DiskNumber).ThenBy(x => x.Offset).ToArray();
            ValidateSnapshot(disks, partitions);
            logger.Information("storage.inventory.completed", new { disks = disks.Length, partitions = partitions.Length, durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds });
            return new StorageSnapshot(disks, partitions, DateTimeOffset.Now);
        }
        catch (JsonException exception) { throw new InvalidOperationException("O Windows retornou dados de armazenamento em formato inesperado. Consulte os logs.", exception); }
        catch (InvalidDataException exception) { throw new InvalidOperationException("O Windows retornou dados de armazenamento incompletos ou inconsistentes. Nenhuma alteração será permitida até o inventário ser atualizado. Consulte os logs.", exception); }
    }

    private static DiskInfo ParseDisk(JsonElement item) => new()
    {
        Number = ReadRequiredInt(item, "Number"), UniqueId = ReadString(item, "UniqueId"), FriendlyName = ReadString(item, "FriendlyName", "Dispositivo sem nome"),
        Manufacturer = ReadString(item, "Manufacturer"), SerialNumber = ReadString(item, "SerialNumber"), PartitionStyle = ReadRequiredString(item, "PartitionStyle"),
        BusType = ReadString(item, "BusType", "Unknown"), MediaType = ReadString(item, "MediaType", "Unspecified"), HealthStatus = ReadString(item, "HealthStatus", "Unknown"), OperationalStatus = ReadString(item, "OperationalStatus", "Unknown"), TemperatureC = ReadNullableInt(item, "TemperatureC"),
        Size = ReadRequiredLong(item, "Size"), IsBoot = ReadRequiredBool(item, "IsBoot"), IsSystem = ReadRequiredBool(item, "IsSystem"), IsOffline = ReadRequiredBool(item, "IsOffline"), IsReadOnly = ReadRequiredBool(item, "IsReadOnly")
    };

    private static PartitionInfo ParsePartition(JsonElement item) => new()
    {
        DiskNumber = ReadRequiredInt(item, "DiskNumber"), PartitionNumber = ReadRequiredInt(item, "PartitionNumber"), AccessPaths = ReadString(item, "AccessPaths"), DriveLetter = ReadString(item, "DriveLetter"),
        Type = ReadRequiredString(item, "Type"), Offset = ReadRequiredLong(item, "Offset"), Size = ReadRequiredLong(item, "Size"), IsBoot = ReadRequiredBool(item, "IsBoot"), IsSystem = ReadRequiredBool(item, "IsSystem"),
        IsHidden = ReadRequiredBool(item, "IsHidden"), IsReadOnly = ReadRequiredBool(item, "IsReadOnly"), IsOffline = ReadRequiredBool(item, "IsOffline"), FileSystem = ReadString(item, "FileSystem"),
        Label = ReadString(item, "Label"), VolumeSizeRemaining = ReadLongOrDefault(item, "VolumeSizeRemaining"), HealthStatus = ReadString(item, "HealthStatus", "Unknown")
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
        if (disks.Any(d => d.Number < 0 || d.Size <= 0))
            throw new InvalidDataException("Inventário contém número de disco negativo ou tamanho de disco não positivo.");
        if (disks.GroupBy(d => d.Number).Any(group => group.Count() != 1))
            throw new InvalidDataException("Inventário contém números de disco duplicados.");

        var diskNumbers = disks.Select(d => d.Number).ToHashSet();
        if (partitions.Any(p => p.DiskNumber < 0 || p.PartitionNumber <= 0 || p.Offset < 0 || p.Size <= 0))
            throw new InvalidDataException("Inventário contém número, offset ou tamanho de partição inválido.");
        if (partitions.Any(p => !diskNumbers.Contains(p.DiskNumber)))
            throw new InvalidDataException("Inventário contém partição órfã sem disco correspondente.");
        if (partitions.GroupBy(p => (p.DiskNumber, p.PartitionNumber)).Any(group => group.Count() != 1))
            throw new InvalidDataException("Inventário contém números de partição duplicados no mesmo disco.");
        if (partitions.Any(p => p.Offset > long.MaxValue - p.Size))
            throw new InvalidDataException("Inventário contém intervalo de partição que excede o limite numérico.");
        if (partitions.Any(p => disks.First(d => d.Number == p.DiskNumber).Size < p.Offset + p.Size))
            throw new InvalidDataException("Inventário contém partição fora dos limites do disco.");
    }

    private static InvalidDataException InvalidField(string key, string expected) => new($"Campo obrigatório '{key}' ausente ou inválido; esperado {expected}.");
    private static string ToFriendlyError(string error) => error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || error.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase)
        ? "Acesso negado ao consultar o armazenamento. Tente executar como administrador."
        : "Não foi possível consultar o armazenamento do Windows. Consulte os logs para detalhes.";
    private static string TrimTechnicalError(string error) => error.Trim().Length <= 4000 ? error.Trim() : error.Trim()[..4000];
}
