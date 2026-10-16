using System.IO;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>
/// Loads the publisher-pinned public ES256 key only. A user-writable JSON file
/// cannot appoint itself as a trust anchor or enable production licensing.
/// </summary>
public static class GslV1TrustLoader
{
    // Derived from the PUBLIC GSL-v1 signing JWK in the official project integration
    // contract supplied through the authenticated admin panel on 2026-10-16.
    // Key rotation requires an authenticated review and new reviewed binary;
    // publishing this public fingerprint does not approve licensing E2E or writes.
    private const string ApprovedPublicKeyFingerprintHex = "6714ec5a65788f4cc937e0f2abd3656362d70ccd6d3cc09aae7e460566ef8d97";

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
