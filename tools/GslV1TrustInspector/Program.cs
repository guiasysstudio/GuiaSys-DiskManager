using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Licensing;

// GDM-H02.1: local, offline read-only public metadata examination.
// No API requests, license activation, key provisioning, disk operations, or source changes.
return Run(args);

static int Run(string[] args)
{
    if (args.Length == 1 && args[0] == "--self-test")
    {
        try
        {
            SelfTest();
            Console.WriteLine("H02.1 SELF-TEST PASS (synthetic ephemeral P-256 only; no production trust).");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or JsonException)
        {
            Console.Error.WriteLine("H02.1 SELF-TEST FAILED: " + ex.GetType().Name);
            return 1;
        }
    }

    if (args.Length != 6 || args[0] != "--file" ||
        args[2] != "--expected-project-id" || args[4] != "--expected-key-id")
    {
        Console.Error.WriteLine("Usage: dotnet run --project tools/GslV1TrustInspector -- --file <official-public-json-path> --expected-project-id <independently-verified-project-id> --expected-key-id <independently-verified-key-id>");
        Console.Error.WriteLine("Only PUBLIC project/signing metadata is accepted; this utility NEVER approves a release.");
        return 2;
    }

    try
    {
        var file = new FileInfo(args[1]);
        if (!file.Exists || file.Length > GslV1TrustMaterial.MaxDocumentBytes)
            throw new InvalidDataException("Missing or oversized public trust document.");
        var text = File.ReadAllText(file.FullName, new UTF8Encoding(false, true));
        var trust = Inspect(text, args[3], args[5]);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            milestone = "GDM-H02.1",
            decision = "NO-GO",
            provenanceApproved = false,
            officialSigningKeyVerifiedOutsideThisTool = false,
            projectId = trust.ProjectId,
            keyId = trust.KeyId,
            algorithm = "ES256",
            curve = "P-256",
            candidateCanonicalFingerprintSha256 = GslV1TrustMaterial.FingerprintHex(trust),
            note = "PUBLIC metadata candidate only. Verify independently with the authenticated GuiaSys Licensing administrator. This tool never pins a key or enables production licensing."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
    catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or
        DecoderFallbackException or System.Security.SecurityException)
    {
        // Do not print raw input documents, key fields or local filesystem paths.
        Console.Error.WriteLine("H02.1 INVALID OR UNTRUSTED PUBLIC METADATA: " + ex.GetType().Name);
        return 1;
    }
}

static GslV1Trust Inspect(string text, string expectedProject, string expectedKey)
{
    if (string.IsNullOrWhiteSpace(expectedProject) || string.IsNullOrWhiteSpace(expectedKey))
        throw new InvalidDataException("Expected public identifiers are required.");
    var trust = GslV1TrustMaterial.ParseStrict(text);
    if (!string.Equals(trust.ProjectId, expectedProject, StringComparison.Ordinal) ||
        !string.Equals(trust.KeyId, expectedKey, StringComparison.Ordinal))
        throw new InvalidDataException("Public signing identity mismatch.");
    return trust;
}

static void SelfTest()
{
    using var ephemeral = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var publicPoint = ephemeral.ExportParameters(false).Q;
    static string Base64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var sample = new GslV1Trust("prj_0123456789abcdefabcd", "sig_h021_synthetic",
        Base64Url(publicPoint.X!), Base64Url(publicPoint.Y!));
    var valid = JsonSerializer.Serialize(new
    {
        projectId = sample.ProjectId,
        keyId = sample.KeyId,
        publicJwk = new { kty = "EC", crv = "P-256", x = sample.X, y = sample.Y }
    });
    if (Inspect(valid, sample.ProjectId, sample.KeyId) != sample)
        throw new InvalidDataException("Synthetic public material did not round-trip.");
    var digest = GslV1TrustMaterial.FingerprintHex(sample);
    if (!GslV1TrustMaterial.MatchesPinnedFingerprint(sample, digest) ||
        GslV1TrustMaterial.MatchesPinnedFingerprint(sample, new string('0', 64) == digest
            ? new string('1', 64) : new string('0', 64)))
        throw new InvalidDataException("Pinned fingerprint mismatch was not detected.");

    static void MustReject(Action attempt)
    {
        try { attempt(); }
        catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid synthetic document unexpectedly accepted.");
    }
    MustReject(() => Inspect(valid, "prj_ffffffffffffffffffff", sample.KeyId));
    MustReject(() => Inspect(valid, sample.ProjectId, "sig_other"));
    MustReject(() => GslV1TrustMaterial.ParseStrict(
        valid.Replace("\"keyId\":", "\"keyId\":\"sig_extra\",\"keyId\":",
            StringComparison.Ordinal)));
    MustReject(() => GslV1TrustMaterial.ParseStrict(
        valid.Replace("\"crv\":", "\"d\":\"private-field\",\"crv\":",
            StringComparison.Ordinal)));
    MustReject(() => GslV1TrustMaterial.ParseStrict(
        valid.Replace("\"P-256\"", "\"P-384\"", StringComparison.Ordinal)));
}
