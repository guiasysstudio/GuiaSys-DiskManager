using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuiaSys.DiskManager.Licensing;

public static class GslV1TokenVerifier
{
    private static readonly Regex StrictIso = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?(?:Z|[+-]\d{2}:\d{2})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);

    internal static bool IsCanonicalCoordinate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { return Decode(text, 32).Length == 32; }
        catch (FormatException) { return false; }
    }

    private static byte[] Decode(string value, int maxBytes)
    {
        if (value.Length == 0 || value.Length > (maxBytes * 4 / 3 + 5) ||
            !Regex.IsMatch(value, "^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant) ||
            value.Length % 4 == 1) throw new FormatException("Invalid base64url.");
        var base64 = value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '=');
        var bytes = Convert.FromBase64String(base64);
        var canonical = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (bytes.Length > maxBytes || !string.Equals(value, canonical, StringComparison.Ordinal))
            throw new FormatException("Noncanonical base64url.");
        return bytes;
    }

    private static string RequiredString(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString())) throw new FormatException("Invalid JWS claim.");
        return value.GetString()!;
    }

    private static DateTimeOffset ParseTimestamp(string? timestamp)
    {
        if (timestamp is null || !StrictIso.IsMatch(timestamp) ||
            !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new FormatException("Invalid JWS timestamp.");
        return value.ToUniversalTime();
    }

    private static DateTimeOffset Date(JsonElement obj, string name) =>
        ParseTimestamp(RequiredString(obj, name));

    private static DateTimeOffset? OptionalDate(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new FormatException("Invalid JWS timestamp.");
        return ParseTimestamp(value.GetString());
    }

    /// <summary>Verify the exact ES256/P1363 signature and *all* bound claims before trusting access.</summary>
    public static GslVerifiedEntitlement Verify(
        string token, GslV1Trust trust, string deviceId, string expectedType,
        DateTimeOffset now, DateTimeOffset? lastObservedUtc = null)
    {
        if (trust is null || !trust.IsValid || !new[] { "license", "trial" }.Contains(expectedType) ||
            string.IsNullOrWhiteSpace(deviceId) || token is null || token.Length > 16_384)
            throw new FormatException("Untrusted entitlement configuration.");
        var parts = token.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new FormatException("Invalid JWS structure.");
        try
        {
            var headerBytes = Decode(parts[0], 4096);
            var payloadBytes = Decode(parts[1], 8192);
            var signature = Decode(parts[2], 64);
            if (signature.Length != 64) throw new FormatException("Invalid ES256 signature length.");
            using var headerJson = JsonDocument.Parse(headerBytes);
            using var payloadJson = JsonDocument.Parse(payloadBytes);
            var header = headerJson.RootElement;
            var claims = payloadJson.RootElement;
            if (header.ValueKind != JsonValueKind.Object || claims.ValueKind != JsonValueKind.Object ||
                header.TryGetProperty("crit", out _) || header.TryGetProperty("b64", out _) ||
                RequiredString(header, "alg") != "ES256" || RequiredString(header, "typ") != "GSL-ENT" ||
                RequiredString(header, "kid") != trust.KeyId)
                throw new FormatException("Untrusted JWS header.");

            using var key = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = Decode(trust.X, 32), Y = Decode(trust.Y, 32) }
            });
            if (!key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]),
                signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw new FormatException("Invalid JWS signature.");

            var deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId))).ToLowerInvariant();
            if (RequiredString(claims, "protocolVersion") != GslV1Protocol.Version ||
                RequiredString(claims, "type") != expectedType ||
                RequiredString(claims, "projectId") != trust.ProjectId ||
                RequiredString(claims, "integrationCode") != GslV1Protocol.IntegrationCode ||
                RequiredString(claims, "deviceHash") != deviceHash ||
                RequiredString(claims, "status") != "active")
                throw new FormatException("Entitlement context mismatch.");

            var lifetimeClaim = claims.GetProperty("lifetime");
            if (lifetimeClaim.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new FormatException("Invalid lifetime flag.");
            var lifetime = lifetimeClaim.GetBoolean();
            var serverTime = Date(claims, "serverTime");
            var offlineUntil = Date(claims, "offlineUntil");
            var expiresAt = OptionalDate(claims, "expiresAt");
            var current = now.ToUniversalTime();
            if (serverTime > current + ClockTolerance || offlineUntil < serverTime ||
                current >= offlineUntil || (expiresAt is not null &&
                (serverTime >= expiresAt || offlineUntil > expiresAt || current >= expiresAt)) ||
                (lastObservedUtc is not null && current + ClockTolerance < lastObservedUtc.Value.ToUniversalTime()))
                throw new FormatException("Expired entitlement or suspicious clock.");

            var licenseId = expectedType == "license" ? RequiredString(claims, "licenseId") : null;
            if (expectedType == "trial")
            {
                var startedAt = Date(claims, "startedAt");
                if (lifetime || expiresAt is null || startedAt > serverTime || startedAt >= expiresAt)
                    throw new FormatException("Invalid trial entitlement.");
            }
            else if ((lifetime && expiresAt is not null) || (!lifetime && expiresAt is null))
                throw new FormatException("Invalid license term.");

            string? plan = null;
            if (claims.TryGetProperty("planName", out var planValue) && planValue.ValueKind == JsonValueKind.String)
                plan = planValue.GetString();
            return new GslVerifiedEntitlement(expectedType, licenseId, serverTime, offlineUntil, expiresAt, lifetime, plan);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or CryptographicException or ArgumentException or OverflowException)
        {
            throw new FormatException("Entitlement is not valid or not trusted.", ex);
        }
    }
}
