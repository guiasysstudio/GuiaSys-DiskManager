using System.IO;
using System.Text.Json;
using GuiaSys.DiskManager.Licensing;

namespace GuiaSys.DiskManager.Tests;

/// <summary>
/// H02.1 public contract tests. Identifiers and JWK coordinates below are PUBLIC
/// and supplied by the official GSL-v1 product integration contract.
/// NO customer licenses, ES256 private keys or live network calls are used.
/// </summary>
public sealed class GslV1OfficialIntegrationTests
{
    private const string ProjectId = "prj_b41909541f924fe5b837";
    private const string KeyId = "sig_3828131003da47bda2f7";
    private const string X = "Rb_biptRYMM1wlGZ9ed0HQ5ElFli0pwxcD5RwCBKd7Y";
    private const string Y = "ZrGce5UqJ5V7skkZISQwqtSaZesJiJcT69ltG9rv-s0";
    private const string Fingerprint = "6714ec5a65788f4cc937e0f2abd3656362d70ccd6d3cc09aae7e460566ef8d97";

    private static string OfficialPublicDocument(string? keyId = null)
        => JsonSerializer.Serialize(new
        {
            projectId = ProjectId,
            keyId = keyId ?? KeyId,
            publicJwk = new { kty = "EC", crv = "P-256", x = X, y = Y }
        });

    [Fact]
    public void Official_endpoint_and_public_integration_code_match_admin_contract()
    {
        Assert.Equal("https://licencas.guiasys.com.br/", GslV1Protocol.ApiBase);
        Assert.Equal("GSLI-9AFS-LE4Q-3PHZ", GslV1Protocol.IntegrationCode);
        Assert.False(StorageWriteGate.ReleasedAfterSafetyAudit);
    }

    [Fact]
    public void Official_public_p256_key_is_valid_and_has_exact_canonical_fingerprint()
    {
        var parsed = GslV1TrustMaterial.ParseStrict(OfficialPublicDocument());
        Assert.Equal(new GslV1Trust(ProjectId, KeyId, X, Y), parsed);
        Assert.Equal(Fingerprint, GslV1TrustMaterial.FingerprintHex(parsed));
        Assert.True(GslV1TrustLoader.HasPinnedTrustFingerprint);
        Assert.True(GslV1TrustMaterial.MatchesPinnedFingerprint(parsed, Fingerprint));
    }

    [Fact]
    public void Approved_public_key_loads_from_a_local_public_configuration_without_accessing_network()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, OfficialPublicDocument());
            var actual = GslV1TrustLoader.Load(file);
            Assert.NotNull(actual);
            Assert.Equal(ProjectId, actual.ProjectId);
            Assert.Equal(KeyId, actual.KeyId);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Same_public_coordinates_with_unapproved_rotated_key_id_are_denied()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, OfficialPublicDocument("sig_unapproved_rotation"));
            Assert.Null(GslV1TrustLoader.Load(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Full_server_jwk_extensions_require_explicit_public_only_normalization()
    {
        var json = OfficialPublicDocument().Replace(
            "\"crv\":\"P-256\"", "\"crv\":\"P-256\",\"ext\":true",
            StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => GslV1TrustMaterial.ParseStrict(json));
    }
}
