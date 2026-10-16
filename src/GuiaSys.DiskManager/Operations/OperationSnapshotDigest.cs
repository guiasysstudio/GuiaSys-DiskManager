using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GuiaSys.DiskManager.Models;

namespace GuiaSys.DiskManager.Operations;

/// <summary>
/// Redacted fingerprint of relevant topology and volume metadata; no raw volume labels,
/// serial numbers, license data or access paths are written into the audit journal.
/// </summary>
public static class OperationSnapshotDigest
{
    public static string Compute(StorageSnapshot snapshot, int diskNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var disk = snapshot.FindDisk(diskNumber)
            ?? throw new InvalidOperationException("Disco ausente para fingerprint de journal.");
        var lines = new List<string>
        {
            string.Join("|", "DISK", disk.Number.ToString(CultureInfo.InvariantCulture),
                disk.Identity, disk.Size.ToString(CultureInfo.InvariantCulture), disk.PartitionStyle,
                disk.IsOffline.ToString(), disk.IsReadOnly.ToString())
        };
        foreach (var part in snapshot.Partitions.Where(x => x.DiskNumber == diskNumber)
            .OrderBy(x => x.PartitionNumber))
        {
            lines.Add(string.Join("|", "PART",
                part.PartitionNumber.ToString(CultureInfo.InvariantCulture), part.PartitionGuid,
                part.Offset.ToString(CultureInfo.InvariantCulture),
                part.Size.ToString(CultureInfo.InvariantCulture), part.Type, part.GptType,
                part.IsOffline.ToString(), part.IsReadOnly.ToString(), part.FileSystem,
                part.DriveLetter, part.Label));
        }
        var canonical = string.Join("\n", lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
