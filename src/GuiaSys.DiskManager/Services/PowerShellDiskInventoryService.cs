using System.Diagnostics;
using System.Text.Json;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Services;

/// <summary>Executes fixed, read-only Windows Storage cmdlets; no user input is interpolated into scripts.</summary>
public sealed class PowerShellDiskInventoryService : IDiskInventoryService
{
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        $disks = @(Get-Disk | Select-Object Number,FriendlyName,SerialNumber,PartitionStyle,Size,IsBoot,IsSystem,IsOffline,IsReadOnly)
        $parts = @(Get-Partition | Select-Object DiskNumber,PartitionNumber,DriveLetter,Type,Size,IsBoot,IsSystem)
        [pscustomobject]@{ Disks = $disks; Partitions = $parts } | ConvertTo-Json -Depth 5 -Compress
        """;

    public async Task<(IReadOnlyList<DiskRecord> Disks, IReadOnlyList<PartitionRecord> Partitions)> ReadAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("O inventário requer Windows.");

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(Script)));

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var json = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Falha ao consultar os discos: {error}");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (ParseDisks(root.GetProperty("Disks")), ParsePartitions(root.GetProperty("Partitions")));
    }

    private static IReadOnlyList<DiskRecord> ParseDisks(JsonElement value) =>
        Elements(value).Select(item => new DiskRecord(
            ReadInt(item, "Number"), ReadString(item, "FriendlyName"), ReadString(item, "SerialNumber"),
            ReadString(item, "PartitionStyle"), ReadLong(item, "Size"), ReadBool(item, "IsBoot"),
            ReadBool(item, "IsSystem"), ReadBool(item, "IsOffline"), ReadBool(item, "IsReadOnly"))).ToArray();

    private static IReadOnlyList<PartitionRecord> ParsePartitions(JsonElement value) =>
        Elements(value).Select(item => new PartitionRecord(
            ReadInt(item, "DiskNumber"), ReadInt(item, "PartitionNumber"), ReadString(item, "DriveLetter"),
            ReadString(item, "Type"), ReadLong(item, "Size"), ReadBool(item, "IsBoot"),
            ReadBool(item, "IsSystem"))).ToArray();

    private static IEnumerable<JsonElement> Elements(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Array => element.EnumerateArray(),
        JsonValueKind.Object => new[] { element },
        _ => Array.Empty<JsonElement>()
    };

    private static string ReadString(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
    private static int ReadInt(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.TryGetInt32(out var x) ? x : 0;
    private static long ReadLong(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.TryGetInt64(out var x) ? x : 0;
    private static bool ReadBool(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
