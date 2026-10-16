using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Licensing;

namespace GuiaSys.DiskManager.Tests;

/// <summary>H02 tests use ephemeral keys; none is a real customer or publisher key.</summary>
public sealed class GslV1TrustMaterialTests
{
    private const string Project = "prj_0123456789abcdefabcd";

    private static (string Document, GslV1Trust Trust) Synthetic()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(false).Q;
        static string Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var trust = new GslV1Trust(Project, "sig_synthetic_h02",
            Url(point.X!), Url(point.Y!));
        return (JsonSerializer.Serialize(new
        {
            projectId = trust.ProjectId, keyId = trust.KeyId,
            publicJwk = new { kty = "EC", crv = "P-256", x = trust.X, y = trust.Y }
        }), trust);
    }

    [Fact]
    public void Synthetic_p256_document_is_parsed_without_granting_production_trust()
    {
        var (document, expected) = Synthetic();
        var parsed = GslV1TrustMaterial.ParseStrict(document);
        Assert.Equal(expected, parsed);
        Assert.True(GslV1TrustLoader.HasPinnedTrustFingerprint);
        // A valid synthetic test key is not the publisher-pinned official key.
    }

    [Fact]
    public void Canonical_fingerprint_is_exact_utf8_lf_sha256_not_pretty_json_hash()
    {
        var (json, trust) = Synthetic();
        var actual = GslV1TrustMaterial.FingerprintHex(
            GslV1TrustMaterial.ParseStrict(json));
        var canonical = string.Join("\n", "GSL-v1", trust.ProjectId,
            trust.KeyId, trust.X, trust.Y);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
        Assert.Equal(expected, actual);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), actual);
        Assert.Matches("^[a-f0-9]{64}$", actual);
    }

    [Fact]
    public void Synthetic_expected_pin_matches_only_exact_material()
    {
        var (_, trust) = Synthetic();
        var fingerprint = GslV1TrustMaterial.FingerprintHex(trust);
        Assert.True(GslV1TrustMaterial.MatchesPinnedFingerprint(trust, fingerprint));
        Assert.True(GslV1TrustMaterial.MatchesPinnedFingerprint(trust, fingerprint.ToUpperInvariant()));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(trust, ""));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(trust, "0"));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(trust, new string('g', 64)));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(trust,
            (fingerprint[0] == 'a' ? "b" : "a") + fingerprint[1..]));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(
            trust with { KeyId = "sig_rotated" }, fingerprint));
        Assert.False(GslV1TrustMaterial.MatchesPinnedFingerprint(
            trust with { ProjectId = "prj_ffffffffffffffffffff" }, fingerprint));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"projectId\":\"a\"}")]
    [InlineData("{\"projectId\":null,\"keyId\":null,\"publicJwk\":null}")]
    [InlineData("{}")]
    public void Invalid_json_objects_are_rejected(string input) =>
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(input));

    [Fact]
    public void Duplicate_project_id_is_rejected_even_when_last_value_matches()
    {
        var (json, _) = Synthetic();
        var manipulated = json.Replace("{\"projectId\":",
            "{\"projectId\":\"prj_ffffffffffffffffffff\",\"projectId\":",
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(manipulated));
    }

    [Fact]
    public void Duplicate_jwk_coordinate_is_rejected()
    {
        var (json, trust) = Synthetic();
        var manipulated = json.Replace("\"x\":\"" + trust.X + "\"",
            "\"x\":\"" + trust.X + "\",\"x\":\"" + trust.X + "\"",
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(manipulated));
    }

    [Fact]
    public void Additional_top_level_and_jwk_properties_are_rejected()
    {
        var (json, _) = Synthetic();
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace("\"keyId\":", "\"private\":\"not allowed\",\"keyId\":", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace("\"crv\":", "\"d\":\"fake private coordinate\",\"crv\":", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("P-384", "EC")]
    [InlineData("P-256", "RSA")]
    [InlineData("P-521", "OKP")]
    public void Only_elliptic_curve_p256_is_accepted(string curve, string type)
    {
        var (json, _) = Synthetic();
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace("\"crv\":\"P-256\"", "\"crv\":\"" + curve + "\"",
                StringComparison.Ordinal).Replace("\"kty\":\"EC\"",
                "\"kty\":\"" + type + "\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Oversized_document_is_rejected_before_json_parsing()
    {
        var (json, _) = Synthetic();
        var oversized = json + new string(' ', GslV1TrustMaterial.MaxDocumentBytes + 1);
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(oversized));
    }

    [Fact]
    public void Invalid_project_id_key_id_and_noncanonical_base64url_are_rejected()
    {
        var (json, trust) = Synthetic();
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace(Project, "prj_NOTVALID", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace("sig_synthetic_h02", "invalid key id!", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace(trust.X, trust.X + "=", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(
            json.Replace(trust.Y, "AAA", StringComparison.Ordinal)));
    }

    [Fact]
    public void No_user_writable_file_could_promote_a_synthetic_key()
    {
        var (json, _) = Synthetic();
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Null(GslV1TrustLoader.Load(path));
            File.WriteAllText(path, "{\"invalid\":true}");
            Assert.Null(GslV1TrustLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Known_protocol_id_is_public_but_never_an_override_for_physical_writes()
    {
        Assert.Equal("GSL-v1", GslV1Protocol.Version);
        Assert.StartsWith("GSLI-", GslV1Protocol.IntegrationCode);
        var allowed = new GslLicenseDecision(true, GslLicenseStatus.ActiveLicense, "synthetic");
        Assert.False(StorageWriteGate.Allows(allowed));
    }
}
