using System.Text.Json;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

/// <summary>Runs a constant read-only script. No user-controlled value is interpolated into PowerShell.</summary>
public sealed class PowerShellDiskInventoryService(IAppLogger logger) : IDiskInventoryService
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $disks = @(Get-Disk | ForEach-Object {
          [pscustomobject]@{
            Number=[int]$_.Number; UniqueId=[string]$_.UniqueId; FriendlyName=[string]$_.FriendlyName
            Manufacturer=[string]$_.Manufacturer; SerialNumber=[string]$_.SerialNumber
            PartitionStyle=[string]$_.PartitionStyle; BusType=[string]$_.BusType
            HealthStatus=[string]$_.HealthStatus; OperationalStatus=[string]($_.OperationalStatus -join ', ')
            Size=[int64]$_.Size; IsBoot=[bool]$_.IsBoot; IsSystem=[bool]$_.IsSystem
            IsOffline=[bool]$_.IsOffline; IsReadOnly=[bool]$_.IsReadOnly
          }
        })
        $parts = @(Get-Partition | ForEach-Object {
          $partition = $_
          $volume = $null
          try { $volume = $partition | Get-Volume -ErrorAction Stop } catch { }
          [pscustomobject]@{
            DiskNumber=[int]$partition.DiskNumber; PartitionNumber=[int]$partition.PartitionNumber
            AccessPaths=[string]($partition.AccessPaths -join ';'); DriveLetter=[string]$partition.DriveLetter
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
        if (output.ExitCode != 0) throw new InvalidOperationException(ToFriendlyError(output.StandardError));
        try
        {
            using var document = JsonDocument.Parse(output.StandardOutput);
            var root = document.RootElement;
            var disks = Elements(root.GetProperty("Disks")).Select(ParseDisk).OrderBy(x => x.Number).ToArray();
            var partitions = Elements(root.GetProperty("Partitions")).Select(ParsePartition).OrderBy(x => x.DiskNumber).ThenBy(x => x.Offset).ToArray();
            logger.Information("storage.inventory.completed", new { disks = disks.Length, partitions = partitions.Length, durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds });
            return new StorageSnapshot(disks, partitions, DateTimeOffset.Now);
        }
        catch (JsonException exception) { throw new InvalidOperationException("O Windows retornou dados de armazenamento em formato inesperado. Consulte os logs.", exception); }
    }

    private static DiskInfo ParseDisk(JsonElement item) => new()
    {
        Number = ReadInt(item, "Number"), UniqueId = ReadString(item, "UniqueId"), FriendlyName = ReadString(item, "FriendlyName", "Dispositivo sem nome"),
        Manufacturer = ReadString(item, "Manufacturer"), SerialNumber = ReadString(item, "SerialNumber"), PartitionStyle = ReadString(item, "PartitionStyle", "RAW"),
        BusType = ReadString(item, "BusType", "Unknown"), HealthStatus = ReadString(item, "HealthStatus", "Unknown"), OperationalStatus = ReadString(item, "OperationalStatus", "Unknown"),
        Size = ReadLong(item, "Size"), IsBoot = ReadBool(item, "IsBoot"), IsSystem = ReadBool(item, "IsSystem"), IsOffline = ReadBool(item, "IsOffline"), IsReadOnly = ReadBool(item, "IsReadOnly")
    };

    private static PartitionInfo ParsePartition(JsonElement item) => new()
    {
        DiskNumber = ReadInt(item, "DiskNumber"), PartitionNumber = ReadInt(item, "PartitionNumber"), AccessPaths = ReadString(item, "AccessPaths"), DriveLetter = ReadString(item, "DriveLetter"),
        Type = ReadString(item, "Type", "Unknown"), Offset = ReadLong(item, "Offset"), Size = ReadLong(item, "Size"), IsBoot = ReadBool(item, "IsBoot"), IsSystem = ReadBool(item, "IsSystem"),
        IsHidden = ReadBool(item, "IsHidden"), IsReadOnly = ReadBool(item, "IsReadOnly"), IsOffline = ReadBool(item, "IsOffline"), FileSystem = ReadString(item, "FileSystem"),
        Label = ReadString(item, "Label"), VolumeSizeRemaining = ReadLong(item, "VolumeSizeRemaining"), HealthStatus = ReadString(item, "HealthStatus", "Unknown")
    };

    internal static IEnumerable<JsonElement> Elements(JsonElement element) => element.ValueKind switch { JsonValueKind.Array => element.EnumerateArray(), JsonValueKind.Object => [element], _ => [] };
    private static string ReadString(JsonElement item, string key, string fallback = "") => item.TryGetProperty(key, out var value) && value.ValueKind is not JsonValueKind.Null ? value.ToString().Trim() : fallback;
    private static int ReadInt(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.TryGetInt32(out var result) ? result : 0;
    private static long ReadLong(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.TryGetInt64(out var result) ? result : 0;
    private static bool ReadBool(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    private static string ToFriendlyError(string error) => error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || error.Contains("Acesso negado", StringComparison.OrdinalIgnoreCase)
        ? "Acesso negado ao consultar o armazenamento. Tente executar como administrador."
        : "Não foi possível consultar o armazenamento do Windows. Consulte os logs para detalhes.";
}
