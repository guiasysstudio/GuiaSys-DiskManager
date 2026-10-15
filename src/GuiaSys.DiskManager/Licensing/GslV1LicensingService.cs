using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuiaSys.DiskManager.Licensing;

/// <summary>
/// GSL-v1 client facade. Only a pinned ES256 authorization can grant access.
/// Storage is intentionally not controlled here; storage safety remains independent.
/// </summary>
public sealed class GslV1LicensingService(
    GslV1ApiClient api, IGslV1Store store, GslV1Trust? trust,
    TimeProvider? timeProvider = null) : ILicenseEntitlements
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _serial = new(1, 1);

    private static readonly Regex LicenseKeyPattern = new(
        "^[A-Z0-9]{1,8}(?:-[A-Z0-9]{5}){4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private bool Configured => trust is { IsValid: true };
    private DateTimeOffset Now => _clock.GetUtcNow();

    private static GslLicenseDecision NoConfig() => GslLicenseDecision.Deny(
        GslLicenseStatus.NotConfigured,
        "Chave pública de confiança GSL-v1 não configurada. A ativação está bloqueada até instalar gsl-v1-trust.json validado.");

    private static GslLicenseDecision Failure(GslV1ApiException ex) => ex.Code switch
    {
        "expired" or "trial_expired" => GslLicenseDecision.Deny(GslLicenseStatus.Expired, "Licença ou avaliação expirada."),
        "revoked" => GslLicenseDecision.Deny(GslLicenseStatus.Revoked, "Licença revogada pelo servidor."),
        "suspended" => GslLicenseDecision.Deny(GslLicenseStatus.Suspended, "Licença suspensa pelo servidor."),
        "device_not_authorized" or "device_bound_to_other_license" or "device_limit" =>
            GslLicenseDecision.Deny(GslLicenseStatus.DeviceNotAuthorized, "Dispositivo não autorizado ou limite de dispositivos atingido."),
        "rate_limited" => GslLicenseDecision.Deny(GslLicenseStatus.RateLimited,
            $"Limite de requisições. Tente mais tarde{(ex.RetryAfterSeconds is > 0 ? $" (em {ex.RetryAfterSeconds}s)" : "")}."),
        "network_error" => GslLicenseDecision.Deny(GslLicenseStatus.NetworkUnavailable, "Não foi possível conectar ao GuiaSys Licensing."),
        "upstream_error" or "upstream_timeout" or "server_error" or "internal_error" =>
            GslLicenseDecision.Deny(GslLicenseStatus.ServiceUnavailable, "Serviço de licenciamento indisponível."),
        _ => GslLicenseDecision.Deny(GslLicenseStatus.Error, $"Licenciamento recusado: {ex.Code}.")
    };

    private static bool IsServerDenial(string code) => code is
        "expired" or "trial_expired" or "revoked" or "suspended" or
        "device_not_authorized" or "device_bound_to_other_license" or "device_limit" or
        "license_invalid" or "trial_converted" or "project_inactive" or "customer_inactive" or
        "pending" or "trial_not_started" or "trial_unavailable";

    private GslLicenseDecision Inspect(GslV1SavedState state, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(state.Entitlement) ||
            state.Type is not ("license" or "trial"))
            return GslLicenseDecision.Deny(GslLicenseStatus.NotActivated,
                "Nenhuma licença ou avaliação ativada neste dispositivo.");

        try
        {
            var verified = GslV1TokenVerifier.Verify(
                state.Entitlement, trust!, deviceId, state.Type, Now, state.LastObservedUtc);
            if (state.LastOnlineUtc is not null && state.LastOnlineUtc > Now.AddMinutes(5))
                throw new FormatException("Last online time exceeds local clock.");
            return new GslLicenseDecision(true,
                verified.Type == "license" ? GslLicenseStatus.ActiveLicense : GslLicenseStatus.ActiveTrial,
                verified.Type == "license" ? "Licença ativa (autorização ES256 verificada)." :
                    "Avaliação ativa (autorização ES256 verificada).",
                verified.ExpiresAt, verified.OfflineUntil, verified.PlanName, state.LastOnlineUtc);
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.InvalidEntitlement,
                "Autorização local inválida, expirada ou relógio alterado. Validação online necessária.");
        }
    }

    public Task<GslLicenseDecision> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Configured) return Task.FromResult(NoConfig());
        try
        {
            var deviceId = store.GetDeviceId();
            var state = store.Load();
            var decision = Inspect(state, deviceId);
            if (decision.Allowed && (state.LastObservedUtc is null || Now > state.LastObservedUtc))
                store.Save(state with { LastObservedUtc = Now });
            return Task.FromResult(decision);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or InvalidOperationException or JsonException)
        {
            return Task.FromResult(GslLicenseDecision.Deny(GslLicenseStatus.InvalidEntitlement,
                "O cache protegido ou a identidade local não estão disponíveis."));
        }
    }

    private async Task AssertProjectAsync(CancellationToken ct)
    {
        var response = await api.PostAsync(GslV1Protocol.ProjectConfig,
            new { integrationCode = GslV1Protocol.IntegrationCode }, ct);
        if (!response.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            project.GetProperty("projectId").GetString() != trust!.ProjectId ||
            project.GetProperty("integrationCode").GetString() != GslV1Protocol.IntegrationCode)
            throw new GslV1ApiException("untrusted_project_config");
        var signing = project.GetProperty("signing");
        var publicJwk = signing.GetProperty("publicJwk");
        if (signing.GetProperty("algorithm").GetString() != "ES256" ||
            signing.GetProperty("keyId").GetString() != trust.KeyId ||
            publicJwk.GetProperty("kty").GetString() != "EC" ||
            publicJwk.GetProperty("crv").GetString() != "P-256" ||
            publicJwk.GetProperty("x").GetString() != trust.X ||
            publicJwk.GetProperty("y").GetString() != trust.Y)
            throw new GslV1ApiException("untrusted_signing_key");
    }

    private GslLicenseDecision Accept(JsonElement response, string envelope,
        string expectedType, string? licenseKey, string deviceId)
    {
        try
        {
            var body = response.GetProperty(envelope);
            if (body.ValueKind != JsonValueKind.Object || body.GetProperty("valid").ValueKind != JsonValueKind.True ||
                body.GetProperty("type").GetString() != expectedType ||
                body.GetProperty("projectId").GetString() != trust!.ProjectId ||
                body.GetProperty("integrationCode").GetString() != GslV1Protocol.IntegrationCode ||
                body.GetProperty("status").GetString() != "active")
                throw new FormatException("Invalid online authorization.");
            var entitlement = body.GetProperty("entitlement");
            if (entitlement.GetProperty("format").GetString() != "JWS" ||
                entitlement.GetProperty("algorithm").GetString() != "ES256" ||
                entitlement.GetProperty("keyId").GetString() != trust.KeyId)
                throw new FormatException("Unexpected JWS envelope.");
            var token = entitlement.GetProperty("token").GetString() ?? "";
            var verified = GslV1TokenVerifier.Verify(token, trust, deviceId, expectedType, Now,
                store.Load().LastObservedUtc);
            if (expectedType == "license" &&
                body.GetProperty("licenseId").GetString() != verified.LicenseId)
                throw new FormatException("License ID mismatch.");
            if (body.GetProperty("offlineUntil").GetString() is not string offline ||
                !DateTimeOffset.TryParse(offline, out var until) || until != verified.OfflineUntil)
                throw new FormatException("Unsigned offline window mismatch.");

            store.Save(new GslV1SavedState(
                licenseKey, token, expectedType, Now, Now));
            return Inspect(store.Load(), deviceId);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or FormatException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.InvalidEntitlement,
                "O servidor não apresentou uma autorização ES256 válida e vinculada ao dispositivo.");
        }
    }

    private static object DeviceRequest(string deviceId, string version, string requestId) => new
    {
        integrationCode = GslV1Protocol.IntegrationCode,
        deviceId,
        deviceName = Environment.MachineName.Length <= 120 ? Environment.MachineName : Environment.MachineName[..120],
        platform = "Windows",
        appVersion = version,
        requestId
    };

    private static string Version =>
        typeof(GslV1LicensingService).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    public async Task<GslLicenseDecision> ActivateAsync(string licenseKey, CancellationToken ct)
    {
        if (!Configured) return NoConfig();
        var normalized = (licenseKey ?? "").Trim().ToUpperInvariant();
        if (!LicenseKeyPattern.IsMatch(normalized))
            return GslLicenseDecision.Deny(GslLicenseStatus.Error, "Formato de License Key inválido.");
        await _serial.WaitAsync(ct);
        try
        {
            var deviceId = store.GetDeviceId();
            await AssertProjectAsync(ct);
            var response = await api.PostAsync(GslV1Protocol.Activate, new
            {
                integrationCode = GslV1Protocol.IntegrationCode,
                licenseKey = normalized,
                deviceId,
                deviceName = Environment.MachineName.Length <= 120 ? Environment.MachineName : Environment.MachineName[..120],
                platform = "Windows",
                appVersion = Version,
                requestId = Guid.NewGuid().ToString("N")
            }, ct);
            return Accept(response, "license", "license", normalized, deviceId);
        }
        catch (GslV1ApiException ex) { return Failure(ex); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or FormatException or
            KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.Error,
                "Falha ao validar a configuração de confiança ou armazenamento protegido.");
        }
        finally { _serial.Release(); }
    }

    public async Task<GslLicenseDecision> StartTrialAsync(CancellationToken ct)
    {
        if (!Configured) return NoConfig();
        await _serial.WaitAsync(ct);
        try
        {
            var deviceId = store.GetDeviceId();
            await AssertProjectAsync(ct);
            var response = await api.PostAsync(GslV1Protocol.TrialStart,
                DeviceRequest(deviceId, Version, Guid.NewGuid().ToString("N")), ct);
            return Accept(response, "trial", "trial", null, deviceId);
        }
        catch (GslV1ApiException ex) { return Failure(ex); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or FormatException or
            KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.Error, "Falha ao iniciar avaliação.");
        }
        finally { _serial.Release(); }
    }

    public async Task<GslLicenseDecision> ValidateOnlineAsync(CancellationToken ct)
    {
        if (!Configured) return NoConfig();
        await _serial.WaitAsync(ct);
        try
        {
            var saved = store.Load();
            var deviceId = store.GetDeviceId();
            if (saved.Type is not ("license" or "trial") ||
                (saved.Type == "license" && string.IsNullOrWhiteSpace(saved.LicenseKey)))
                return GslLicenseDecision.Deny(GslLicenseStatus.NotActivated, "Ative uma licença ou avaliação primeiro.");
            await AssertProjectAsync(ct);
            var response = saved.Type == "license"
                ? await api.PostAsync(GslV1Protocol.Validate, new
                {
                    integrationCode = GslV1Protocol.IntegrationCode,
                    licenseKey = saved.LicenseKey, deviceId, appVersion = Version,
                    requestId = Guid.NewGuid().ToString("N")
                }, ct)
                : await api.PostAsync(GslV1Protocol.TrialValidate, new
                {
                    integrationCode = GslV1Protocol.IntegrationCode,
                    deviceId, appVersion = Version, requestId = Guid.NewGuid().ToString("N")
                }, ct);
            return Accept(response, saved.Type, saved.Type, saved.LicenseKey, deviceId);
        }
        catch (GslV1ApiException ex)
        {
            if (IsServerDenial(ex.Code))
            {
                try { var state = store.Load(); store.Save(state with { Entitlement = null }); }
                catch (Exception) { /* Deny regardless of local store failure. */ }
                return Failure(ex);
            }
            if (ex.Code is "network_error" or "upstream_error" or "upstream_timeout" or "server_error" or "internal_error")
            {
                var cached = await CheckAsync(ct);
                return cached.Allowed ? cached with { Message = "Sem conexão: autorização offline assinada ainda vigente." } : Failure(ex);
            }
            return Failure(ex);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or FormatException or
            KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.InvalidEntitlement,
                "Falha na validação do estado de licenciamento.");
        }
        finally { _serial.Release(); }
    }

    public async Task<GslLicenseDecision> DeactivateAsync(CancellationToken ct)
    {
        if (!Configured) return NoConfig();
        await _serial.WaitAsync(ct);
        try
        {
            var state = store.Load();
            if (state.Type != "license" || string.IsNullOrWhiteSpace(state.LicenseKey))
                return GslLicenseDecision.Deny(GslLicenseStatus.NotActivated, "Nenhuma licença paga para desativar.");
            var response = await api.PostAsync(GslV1Protocol.Deactivate, new
            {
                integrationCode = GslV1Protocol.IntegrationCode,
                licenseKey = state.LicenseKey, deviceId = store.GetDeviceId(),
                requestId = Guid.NewGuid().ToString("N")
            }, ct);
            var result = response.GetProperty("result");
            if (result.GetProperty("ok").ValueKind != JsonValueKind.True ||
                result.GetProperty("protocolVersion").GetString() != GslV1Protocol.Version ||
                !(result.GetProperty("deactivated").GetBoolean() || result.GetProperty("alreadyInactive").GetBoolean()))
                return GslLicenseDecision.Deny(GslLicenseStatus.InvalidEntitlement, "Desativação não confirmada pelo servidor.");
            store.Save(new GslV1SavedState(LastObservedUtc: Now));
            return GslLicenseDecision.Deny(GslLicenseStatus.NotActivated, "Licença desativada neste dispositivo.");
        }
        catch (GslV1ApiException ex) { return Failure(ex); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or FormatException or
            KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return GslLicenseDecision.Deny(GslLicenseStatus.Error, "Não foi possível confirmar a desativação.");
        }
        finally { _serial.Release(); }
    }
}
