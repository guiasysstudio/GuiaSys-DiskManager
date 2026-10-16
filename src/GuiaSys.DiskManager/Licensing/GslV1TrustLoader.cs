using System.IO;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>
/// Loads the publisher-pinned public ES256 key only. A user-writable JSON file
/// cannot appoint itself as a trust anchor or enable production licensing.
/// </summary>
public static class GslV1TrustLoader
{
    // This is intentionally empty until out-of-band official project metadata,
    // publisher review and a new trusted build are available. Never populate
    // it with a digest copied solely from an untrusted network response.
    private const string ApprovedPublicKeyFingerprintHex = "";

    public static bool HasPinnedTrustFingerprint =>
        ApprovedPublicKeyFingerprintHex.Length == 64 &&
        ApprovedPublicKeyFingerprintHex.All(Uri.IsHexDigit);

    public static GslV1Trust? Load(string? path = null)
    {
        if (!HasPinnedTrustFingerprint) return null;
        var source = path ?? Path.Combine(AppContext.BaseDirectory, "gsl-v1-trust.json");
        try
        {
            var file = new FileInfo(source);
            if (!file.Exists || file.Length > GslV1TrustMaterial.MaxDocumentBytes)
                return null;
            var trust = GslV1TrustMaterial.ParseStrict(File.ReadAllText(file.FullName));
            return GslV1TrustMaterial.MatchesPinnedFingerprint(
                trust, ApprovedPublicKeyFingerprintHex) ? trust : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            InvalidDataException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
