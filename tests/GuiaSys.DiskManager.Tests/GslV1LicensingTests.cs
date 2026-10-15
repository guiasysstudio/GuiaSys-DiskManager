using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GuiaSys.DiskManager.Licensing;

namespace GuiaSys.DiskManager.Tests;

public sealed class GslV1LicensingTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 12, 0, 0, TimeSpan.Zero);
    private const string DeviceId = "GDM-INTEGRATION-TEST-001";
    private const string LicenseKey = "GDM-ABCDE-ABCDE-ABCDE-ABCDE";
    private const string ProjectId = "prj_0123456789abcdefabcd";

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Store : IGslV1Store
    {
        public GslV1SavedState State = new();
        public string GetDeviceId() => DeviceId;
        public GslV1SavedState Load() => State;
        public void Save(GslV1SavedState state) => State = state;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(callback(request));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        public GslV1Trust Trust { get; }
        public Fixture()
        {
            var point = _key.ExportParameters(false).Q;
            Trust = new GslV1Trust(ProjectId, "sig_gdm_test",
                Url(point.X!), Url(point.Y!));
        }

        private static string Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Url(string text) => Url(Encoding.UTF8.GetBytes(text));

        public string Signed(string type = "license", DateTimeOffset? expiry = null,
            DateTimeOffset? offline = null, string? device = null, bool lifetime = false,
            string? status = null, string? projectId = null, string? integrationCode = null,
            string? kid = null, bool crit = false, string? algorithm = null,
            DateTimeOffset? serverTime = null)
        {
            var header = new Dictionary<string, object?>
            {
                ["alg"] = algorithm ?? "ES256", ["typ"] = "GSL-ENT",
                ["kid"] = kid ?? Trust.KeyId
            };
            if (crit) header["crit"] = new[] { "b64" };
            var payload = new
            {
                protocolVersion = GslV1Protocol.Version,
                type, projectId = projectId ?? Trust.ProjectId,
                integrationCode = integrationCode ?? GslV1Protocol.IntegrationCode,
                deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(device ?? DeviceId))).ToLowerInvariant(),
                status = status ?? "active",
                licenseId = type == "license" ? "lic_0123456789abcdefabcd" : null,
                planName = type == "license" ? "Plano Teste" : null,
                lifetime, serverTime = (serverTime ?? Now.AddHours(-1)).ToString("O"),
                offlineUntil = (offline ?? Now.AddDays(2)).ToString("O"),
                expiresAt = lifetime ? null : (expiry ?? Now.AddDays(10)).ToString("O"),
                startedAt = type == "trial" ? Now.AddDays(-2).ToString("O") : null
            };
            var input = Url(JsonSerializer.Serialize(header)) + "." + Url(JsonSerializer.Serialize(payload));
            var signature = _key.SignData(Encoding.ASCII.GetBytes(input),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return input + "." + Url(signature);
        }

        public object Config() => new
        {
            ok = true,
            project = new
            {
                projectId = Trust.ProjectId,
                integrationCode = GslV1Protocol.IntegrationCode,
                signing = new
                {
                    algorithm = "ES256",
                    keyId = Trust.KeyId,
                    publicJwk = new { kty = "EC", crv = "P-256", x = Trust.X, y = Trust.Y }
                }
            }
        };

        public object Activation(string token) => new
        {
            ok = true,
            license = new
            {
                valid = true, type = "license", projectId = Trust.ProjectId,
                integrationCode = GslV1Protocol.IntegrationCode,
                status = "active", licenseId = "lic_0123456789abcdefabcd",
                offlineUntil = Now.AddDays(2).ToString("O"),
                entitlement = new { format = "JWS", algorithm = "ES256", keyId = Trust.KeyId, token }
            }
        };
        public object Trial(string token) => new
        {
            ok = true,
            trial = new
            {
                valid = true, type = "trial", projectId = Trust.ProjectId,
                integrationCode = GslV1Protocol.IntegrationCode,
                status = "active", offlineUntil = Now.AddDays(2).ToString("O"),
                entitlement = new { format = "JWS", algorithm = "ES256", keyId = Trust.KeyId, token }
            }
        };

        public void Dispose() => _key.Dispose();
    }

    private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private static GslV1ApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> handle) =>
        new(new HttpClient(new Handler(handle)));

    [Fact]
    public void Valid_ES256_license_allows_only_matching_project_device_and_lifetime()
    {
        using var f = new Fixture();
        var verified = GslV1TokenVerifier.Verify(f.Signed(), f.Trust, DeviceId, "license", Now);
        Assert.Equal("license", verified.Type);
        Assert.Equal(Now.AddDays(2), verified.OfflineUntil);
        Assert.Equal(Now.AddDays(10), verified.ExpiresAt);
    }

    [Theory]
    [InlineData("device")]
    [InlineData("project")]
    [InlineData("code")]
    [InlineData("kid")]
    [InlineData("alg")]
    [InlineData("crit")]
    [InlineData("status")]
    public void Signed_but_untrusted_claims_are_rejected(string mutation)
    {
        using var f = new Fixture();
        var token = mutation switch
        {
            "device" => f.Signed(device: "OTHER"),
            "project" => f.Signed(projectId: "prj_aaaaaaaaaaaaaaaaaaaa"),
            "code" => f.Signed(integrationCode: "GSLI-XXXX-XXXX-XXXX"),
            "kid" => f.Signed(kid: "sig_other"),
            "alg" => f.Signed(algorithm: "HS256"),
            "crit" => f.Signed(crit: true),
            "status" => f.Signed(status: "revoked"),
            _ => throw new InvalidOperationException()
        };
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(
            token, f.Trust, DeviceId, "license", Now));
    }

    [Fact]
    public void Tampered_signature_and_wrong_signing_key_fail()
    {
        using var f = new Fixture();
        using var other = new Fixture();
        var token = f.Signed();
        var parts = token.Split('.');
        var signature = parts[2];
        var tampered = parts[0] + "." + parts[1] + "." +
            (signature[0] == 'A' ? 'B' : 'A') + signature[1..];
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(tampered, f.Trust, DeviceId, "license", Now));
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(token, other.Trust, DeviceId, "license", Now));
    }

    [Fact]
    public void Offline_and_license_expirations_use_exclusive_UTC_boundary()
    {
        using var f = new Fixture();
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(
            f.Signed(offline: Now), f.Trust, DeviceId, "license", Now));
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(
            f.Signed(expiry: Now, offline: Now), f.Trust, DeviceId, "license", Now));
    }

    [Fact]
    public void Clock_rollback_is_rejected()
    {
        using var f = new Fixture();
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(
            f.Signed(), f.Trust, DeviceId, "license", Now, Now.AddHours(1)));
    }

    [Fact]
    public void A_valid_trial_is_not_a_paid_license_and_lifetime_has_offline_limit()
    {
        using var f = new Fixture();
        Assert.Equal("trial", GslV1TokenVerifier.Verify(f.Signed("trial"), f.Trust, DeviceId, "trial", Now).Type);
        Assert.Throws<FormatException>(() => GslV1TokenVerifier.Verify(f.Signed("trial"), f.Trust, DeviceId, "license", Now));
        Assert.Equal("license", GslV1TokenVerifier.Verify(f.Signed(lifetime: true), f.Trust, DeviceId, "license", Now).Type);
    }

    [Fact]
    public async Task Missing_trust_never_grants_access_or_opens_disk_write_gate()
    {
        using var f = new Fixture();
        var store = new Store { State = new GslV1SavedState(LicenseKey, f.Signed(), "license") };
        var service = new GslV1LicensingService(Client(_ => throw new Exception("No network expected")),
            store, null, new Clock());
        var decision = await service.CheckAsync(CancellationToken.None);
        Assert.False(decision.Allowed);
        Assert.Equal(GslLicenseStatus.NotConfigured, decision.Status);
        Assert.False(StorageWriteGate.Allows(new GslLicenseDecision(true, GslLicenseStatus.ActiveLicense, "test")));
    }

    [Fact]
    public async Task Active_signed_cache_is_usable_but_clock_rollback_invalidates_it()
    {
        using var f = new Fixture();
        var store = new Store { State = new GslV1SavedState(LicenseKey, f.Signed(), "license") };
        var service = new GslV1LicensingService(Client(_ => throw new Exception("No network expected")),
            store, f.Trust, new Clock());
        Assert.True((await service.CheckAsync(CancellationToken.None)).Allowed);
        store.State = store.State with { LastObservedUtc = Now.AddDays(1) };
        Assert.False((await service.CheckAsync(CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Activation_uses_real_GSL_paths_and_validates_signed_response()
    {
        using var f = new Fixture();
        var store = new Store();
        var seen = new List<string>();
        var api = Client(request =>
        {
            Assert.Equal("licencas.guiasys.online", request.RequestUri?.Host);
            Assert.Equal(HttpMethod.Post, request.Method);
            seen.Add(request.RequestUri!.AbsolutePath);
            return Json(request.RequestUri.AbsolutePath == GslV1Protocol.ProjectConfig
                ? f.Config() : f.Activation(f.Signed()));
        });
        var service = new GslV1LicensingService(api, store, f.Trust, new Clock());
        var d = await service.ActivateAsync(LicenseKey, CancellationToken.None);
        Assert.True(d.Allowed);
        Assert.Equal([GslV1Protocol.ProjectConfig, GslV1Protocol.Activate], seen);
        Assert.Equal(LicenseKey, store.State.LicenseKey);
        Assert.NotNull(store.State.Entitlement);
    }

    [Fact]
    public async Task Activation_refuses_unpinned_project_signing_key()
    {
        using var f = new Fixture();
        var store = new Store();
        var api = Client(_ => Json(new
        {
            ok = true, project = new
            {
                projectId = ProjectId,
                integrationCode = GslV1Protocol.IntegrationCode,
                signing = new
                {
                    algorithm = "ES256", keyId = "sig_untrusted",
                    publicJwk = new { kty = "EC", crv = "P-256", x = f.Trust.X, y = f.Trust.Y }
                }
            }
        }));
        var result = await new GslV1LicensingService(api, store, f.Trust, new Clock())
            .ActivateAsync(LicenseKey, CancellationToken.None);
        Assert.False(result.Allowed);
        Assert.Null(store.State.Entitlement);
    }

    [Fact]
    public async Task Revocation_response_clears_old_offline_authorization()
    {
        using var f = new Fixture();
        var store = new Store { State = new GslV1SavedState(LicenseKey, f.Signed(), "license") };
        var api = Client(request => request.RequestUri!.AbsolutePath == GslV1Protocol.ProjectConfig
            ? Json(f.Config()) : Json(new { ok = false, error = "revoked", message = "untrusted" }, HttpStatusCode.Forbidden));
        var service = new GslV1LicensingService(api, store, f.Trust, new Clock());
        var result = await service.ValidateOnlineAsync(CancellationToken.None);
        Assert.Equal(GslLicenseStatus.Revoked, result.Status);
        Assert.False((await service.CheckAsync(CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Network_outage_only_uses_unexpired_signed_cache()
    {
        using var f = new Fixture();
        var store = new Store { State = new GslV1SavedState(LicenseKey, f.Signed(), "license") };
        var api = Client(_ => throw new HttpRequestException("offline"));
        var service = new GslV1LicensingService(api, store, f.Trust, new Clock());
        Assert.True((await service.ValidateOnlineAsync(CancellationToken.None)).Allowed);
        store.State = store.State with { Entitlement = f.Signed(offline: Now) };
        Assert.False((await service.ValidateOnlineAsync(CancellationToken.None)).Allowed);
    }

    [Fact]
    public async Task Trial_start_and_deactivation_use_correct_envelopes()
    {
        using var f = new Fixture();
        var store = new Store();
        var paths = new List<string>();
        var api = Client(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            paths.Add(path);
            return Json(path switch
            {
                GslV1Protocol.ProjectConfig => f.Config(),
                GslV1Protocol.TrialStart => f.Trial(f.Signed("trial")),
                GslV1Protocol.Deactivate => new
                {
                    ok = true,
                    result = new { ok = true, protocolVersion = GslV1Protocol.Version, deactivated = true, alreadyInactive = false }
                },
                _ => f.Activation(f.Signed())
            });
        });
        var service = new GslV1LicensingService(api, store, f.Trust, new Clock());
        Assert.True((await service.StartTrialAsync(CancellationToken.None)).Allowed);
        Assert.Equal("trial", store.State.Type);
        Assert.True((await service.ActivateAsync(LicenseKey, CancellationToken.None)).Allowed);
        Assert.Equal(GslLicenseStatus.NotActivated,
            (await service.DeactivateAsync(CancellationToken.None)).Status);
        Assert.Null(store.State.Entitlement);
        Assert.Contains(GslV1Protocol.TrialStart, paths);
        Assert.Contains(GslV1Protocol.Deactivate, paths);
    }

    [Fact]
    public async Task Strict_http_envelope_rejects_success_without_ok()
    {
        var api = Client(_ => Json(new { data = "untrusted" }));
        await Assert.ThrowsAsync<GslV1ApiException>(() =>
            api.PostAsync(GslV1Protocol.ProjectConfig, new { integrationCode = GslV1Protocol.IntegrationCode },
                CancellationToken.None));
    }
}
