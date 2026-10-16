using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>
/// Public ES256 key is trusted only when its identity and coordinates match
/// a fingerprint pinned in the *built application*, never merely a writable file.
/// </summary>
public static class GslV1TrustLoader
{
    // Populate this PUBLIC, non-secret SHA-256 digest only after verifying the project's
    // real Project ID, signing key ID and JWK via the official Licensing admin panel.
    // Empty intentionally blocks real activation in this unconfigured development build.
    private const string ApprovedPublicKeyFingerprintHex = "";

    // Non-secret read-only deployment status. A fingerprint is not valid merely
    // because a file is supplied by the machine running the application.
    public static bool HasPinnedTrustFingerprint =>
        ApprovedPublicKeyFingerprintHex.Length == 64 &&
        ApprovedPublicKeyFingerprintHex.All(Uri.IsHexDigit);

    public static GslV1Trust? Load(string? path = null)
    {
        if (ApprovedPublicKeyFingerprintHex.Length != 64) return null;
        var source = path ?? Path.Combine(AppContext.BaseDirectory, "gsl-v1-trust.json");
        if (!File.Exists(source)) return null;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(source));
            var root = json.RootElement;
            var jwk = root.GetProperty("publicJwk");
            var trust = new GslV1Trust(
                root.GetProperty("projectId").GetString() ?? "",
                root.GetProperty("keyId").GetString() ?? "",
                jwk.GetProperty("x").GetString() ?? "",
                jwk.GetProperty("y").GetString() ?? "");
            if (jwk.GetProperty("kty").GetString() != "EC" ||
                jwk.GetProperty("crv").GetString() != "P-256" || !trust.IsValid)
                return null;

            var canonical = string.Join("\n", "GSL-v1", trust.ProjectId,
                trust.KeyId, trust.X, trust.Y);
            var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            var expected = Convert.FromHexString(ApprovedPublicKeyFingerprintHex);
            return CryptographicOperations.FixedTimeEquals(fingerprint, expected) ? trust : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
