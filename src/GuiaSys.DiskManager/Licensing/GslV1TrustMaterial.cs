using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>
/// Strict, bounded public GSL-v1 trust document parser. Parsing or displaying a
/// fingerprint does NOT authorize licensing. Only the publisher-pinned digest
/// embedded in a reviewed application build is an acceptable trust anchor.
/// </summary>
internal static class GslV1TrustMaterial
{
    internal const int MaxDocumentBytes = 8192;
    private static readonly Regex FingerprintPattern =
        new(@"\A[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static GslV1Trust ParseStrict(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaxDocumentBytes)
            throw new InvalidDataException("GSL trust document exceeds 8192 bytes.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            var root = document.RootElement;
            RequireExactKeys(root, "projectId", "keyId", "publicJwk");
            var jwk = root.GetProperty("publicJwk");
            RequireExactKeys(jwk, "kty", "crv", "x", "y");
            if (ReadString(jwk, "kty") != "EC" || ReadString(jwk, "crv") != "P-256")
                throw new InvalidDataException("Unsupported JWK curve or type.");

            var trust = new GslV1Trust(ReadString(root, "projectId"),
                ReadString(root, "keyId"), ReadString(jwk, "x"), ReadString(jwk, "y"));
            if (!trust.IsValid) throw new InvalidDataException("Invalid public signing key metadata.");

            // Validate that the 32-byte canonical coordinates import as an EC point.
            // No private key is ever loaded, generated or accepted.
            using var publicKey = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = DecodeCoordinate(trust.X), Y = DecodeCoordinate(trust.Y) }
            });
            _ = publicKey.ExportParameters(false);
            return trust;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or
            CryptographicException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Untrusted GSL public-key document.", ex);
        }
    }

    internal static string FingerprintHex(GslV1Trust trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        if (!trust.IsValid) throw new InvalidDataException("Invalid GSL public key.");
        var canonical = string.Join("\n", "GSL-v1", trust.ProjectId,
            trust.KeyId, trust.X, trust.Y);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    internal static bool MatchesPinnedFingerprint(GslV1Trust trust, string? pinnedHex)
    {
        if (string.IsNullOrEmpty(pinnedHex) || !FingerprintPattern.IsMatch(pinnedHex))
            return false;
        var actual = Convert.FromHexString(FingerprintHex(trust));
        var expected = Convert.FromHexString(pinnedHex);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static void RequireExactKeys(JsonElement element, params string[] required)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("GSL document must be an object.");
        var found = element.EnumerateObject().Select(p => p.Name).ToArray();
        if (found.Length != required.Length ||
            found.Distinct(StringComparer.Ordinal).Count() != found.Length ||
            required.Any(name => !found.Contains(name, StringComparer.Ordinal)))
            throw new InvalidDataException("Unknown, missing or duplicate GSL trust field.");
    }

    private static string ReadString(JsonElement obj, string key)
    {
        var value = obj.GetProperty(key);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Invalid GSL field type.");
        return value.GetString() ?? throw new InvalidDataException("Missing GSL field value.");
    }

    private static byte[] DecodeCoordinate(string coordinate)
    {
        // IsValid already checked canonical URL-safe base64 and exact size.
        var normalized = coordinate.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(44, '='));
    }
}
