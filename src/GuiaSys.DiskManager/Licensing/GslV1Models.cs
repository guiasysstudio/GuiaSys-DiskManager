using System.Text.RegularExpressions;

namespace GuiaSys.DiskManager.Licensing;

public static class GslV1Protocol
{
    public const string Version = "GSL-v1";
    public const string IntegrationCode = "GSLI-9AFS-LE4Q-3PHZ";
    public const string ApiBase = "https://licencas.guiasys.online/";
    public const string ProjectConfig = "/api/v1/project/config";
    public const string TrialStart = "/api/v1/trial/start";
    public const string TrialValidate = "/api/v1/trial/validate";
    public const string Activate = "/api/v1/license/activate";
    public const string Validate = "/api/v1/license/validate";
    public const string Deactivate = "/api/v1/license/deactivate";
}

public sealed record GslV1Trust(string ProjectId, string KeyId, string X, string Y)
{
    public bool IsValid =>
        Regex.IsMatch(ProjectId, "^prj_[a-f0-9]{20}$", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(KeyId, "^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant) &&
        GslV1TokenVerifier.IsCanonicalCoordinate(X) && GslV1TokenVerifier.IsCanonicalCoordinate(Y);
}

public sealed record GslV1SavedState(
    string? LicenseKey = null,
    string? Entitlement = null,
    string? Type = null,
    DateTimeOffset? LastObservedUtc = null,
    DateTimeOffset? LastOnlineUtc = null,
    int ValidationHours = 24,
    string? PendingAction = null,
    string? PendingRequestId = null,
    string? PendingLicenseKey = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? ActivatedAt = null,
    int? MaxDevices = null);

public interface IGslV1Store
{
    string GetDeviceId();
    GslV1SavedState Load();
    void Save(GslV1SavedState state);
}

public enum GslLicenseStatus
{
    NotConfigured, NotActivated, ActiveLicense, ActiveTrial, Expired, Revoked,
    Suspended, DeviceNotAuthorized, RateLimited, NetworkUnavailable,
    InvalidEntitlement, ServiceUnavailable, Error
}

public sealed record GslLicenseDecision(
    bool Allowed,
    GslLicenseStatus Status,
    string Message,
    DateTimeOffset? ExpiresAt = null,
    DateTimeOffset? OfflineUntil = null,
    string? PlanName = null,
    DateTimeOffset? LastOnlineUtc = null,
    string? CustomerName = null,
    string? CustomerEmail = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? ActivatedAt = null,
    int? MaxDevices = null)
{
    public static GslLicenseDecision Deny(GslLicenseStatus status, string message) => new(false, status, message);
}

public interface ILicenseEntitlements
{
    Task<GslLicenseDecision> CheckAsync(CancellationToken cancellationToken);
    Task<GslLicenseDecision> ActivateAsync(string licenseKey, CancellationToken cancellationToken);
    Task<GslLicenseDecision> StartTrialAsync(CancellationToken cancellationToken);
    Task<GslLicenseDecision> ValidateOnlineAsync(CancellationToken cancellationToken);
    Task<GslLicenseDecision> DeactivateAsync(CancellationToken cancellationToken);
}

public sealed record GslVerifiedEntitlement(
    string Type, string? LicenseId, DateTimeOffset ServerTime,
    DateTimeOffset OfflineUntil, DateTimeOffset? ExpiresAt, bool Lifetime,
    string? PlanName);

public sealed class GslV1ApiException : Exception
{
    public string Code { get; }
    public int HttpStatus { get; }
    public int? RetryAfterSeconds { get; }
    public GslV1ApiException(string code, int httpStatus = 0, int? retryAfterSeconds = null)
        : base($"GSL-v1: {code}")
    {
        Code = code;
        HttpStatus = httpStatus;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

// C01 is a licensing milestone, not approval of the legacy storage executor.
// The physical write path remains locked until C02-C04 safety gates pass.
public static class StorageWriteGate
{
    public static bool ReleasedAfterSafetyAudit => false;
    public static bool Allows(GslLicenseDecision license) => ReleasedAfterSafetyAudit && license.Allowed;
}
