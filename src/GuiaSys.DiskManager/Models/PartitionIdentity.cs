using System.Globalization;
namespace GuiaSys.DiskManager.Models;
public static class PartitionIdentity
{
    public static bool HasStrongIdentity(PartitionInfo p) =>
        Guid.TryParse(p.PartitionGuid, out var id) && id != Guid.Empty;
    public static string? TryGetFingerprint(PartitionInfo p)
    {
        if (!Guid.TryParse(p.PartitionGuid, out var id) || id == Guid.Empty ||
            p.DiskNumber < 0 || p.PartitionNumber <= 0 || p.Offset < 0 ||
            p.Size <= 0 || p.Offset > long.MaxValue - p.Size ||
            string.IsNullOrWhiteSpace(p.Type)) return null;
        return string.Create(CultureInfo.InvariantCulture,
            $"gpt:{id:D}|disk:{p.DiskNumber}|part:{p.PartitionNumber}|offset:{p.Offset}|size:{p.Size}|type:{p.Type.Trim().ToUpperInvariant()}");
    }
}
