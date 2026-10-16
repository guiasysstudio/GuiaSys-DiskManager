[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$RequireProductionTrust
)

# H02 -- read-only licensing deployment preflight. Does not contact the
# production API, issue activation/deactivation, read customer license keys,
# load DPAPI state, distribute keys or modify storage devices.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'artifacts\licensing\gsl-v1-readiness.json'
}

$source = Join-Path $root 'src\GuiaSys.DiskManager\Licensing\GslV1TrustLoader.cs'
$contract = Join-Path $root 'src\GuiaSys.DiskManager\Licensing\GslV1Models.cs'
if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
    -not (Test-Path -LiteralPath $contract -PathType Leaf)) {
    throw 'H02_PROJECT_FILES_MISSING'
}
$loader = Get-Content -LiteralPath $source -Raw
$protocol = Get-Content -LiteralPath $contract -Raw

$pinConfigured = $loader -cmatch 'ApprovedPublicKeyFingerprintHex\s*=\s*"[a-fA-F0-9]{64}"\s*;'
$pinIntentionallyEmpty = $loader -cmatch 'ApprovedPublicKeyFingerprintHex\s*=\s*""\s*;'
$writeGateClosed = $protocol -cmatch 'ReleasedAfterSafetyAudit\s*=>\s*false\s*;'
$publicEndpointConfigured = $protocol -cmatch 'ApiBase\s*=\s*"https://licencas\.guiasys\.com\.br/"'
$integrationCodePresent = $protocol -cmatch 'IntegrationCode\s*=\s*"GSLI-[A-Z0-9-]+"'
$publicTrustPath = Join-Path $root 'artifacts\portable\gsl-v1-trust.json'
$trustBundled = Test-Path -LiteralPath $publicTrustPath -PathType Leaf
$trustedBundleMatchesPinnedIdentity = $false
if ($trustBundled -and $pinConfigured) {
    # Only PUBLIC fields are read here. Validating the P-256 point is covered
    # by the shared C# trust parser unit tests and the H02.1 CLI inspector.
    try {
        $identity = Get-Content -LiteralPath $publicTrustPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
        $components = @('GSL-v1', $identity.projectId, $identity.keyId,
            $identity.publicJwk.x, $identity.publicJwk.y)
        $canonical = [string]::Join("`n", [string[]]$components)
        $fingerprint = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))).ToLowerInvariant()
        $matchedPin = [regex]::Match($loader, 'ApprovedPublicKeyFingerprintHex\s*=\s*"([a-fA-F0-9]{64})"\s*;')
        $trustedBundleMatchesPinnedIdentity = $matchedPin.Success -and
            $fingerprint -ceq $matchedPin.Groups[1].Value.ToLowerInvariant() -and
            $identity.projectId -ceq 'prj_b41909541f924fe5b837' -and
            $identity.keyId -ceq 'sig_3828131003da47bda2f7' -and
            $identity.publicJwk.kty -ceq 'EC' -and
            $identity.publicJwk.crv -ceq 'P-256' -and
            @($identity.PSObject.Properties).Count -eq 3 -and
            @($identity.publicJwk.PSObject.Properties).Count -eq 4
    } catch {
        $trustedBundleMatchesPinnedIdentity = $false
    }
}

# A file, its digest, or a CI flag cannot prove independent publisher approval.
# Real GSL E2E, JWK provenance and revoked-license behavior require supervised
# evidence from the official admin and a separate release decision.
$report = [ordered]@{
    schemaVersion = 1
    milestone = 'GDM-H02'
    revision = [string]$env:GITHUB_SHA
    capturedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    protocol = 'GSL-v1'
    publicEndpointConfigured = [bool]$publicEndpointConfigured
    publicIntegrationIdentifierPresent = [bool]$integrationCodePresent
    publicPinPresentInReviewedBuildSource = [bool]$pinConfigured
    publicPinIntentionallyEmpty = [bool]$pinIntentionallyEmpty
    publicTrustFileBundled = [bool]$trustBundled
    bundledPublicKeyMatchesReviewedFingerprint = [bool]$trustedBundleMatchesPinnedIdentity
    storageWriteGateKnownClosed = [bool]$writeGateClosed
    publicSigningKeyProvenanceIndependentlyVerified = $false
    trustedOfflineES256TokenTestedAgainstRealProject = $false
    productionActivationDeactivationRevocationTested = $false
    customerLicenseOrPrivateKeyAccessed = $false
    productionApiCalled = $false
    decision = 'NO-GO'
    reason = 'GSL-v1 publisher key provenance and production E2E are unverified; this audit never grants an authorization.'
}
$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
[IO.File]::WriteAllText($OutputPath,
    ($report | ConvertTo-Json -Depth 5),
    [Text.UTF8Encoding]::new($false))
Write-Host "H02: trust pin configured: $pinConfigured; GSL production readiness: NO-GO."
Write-Host "H02: no network, activation, secrets or disk operations. Evidence: $OutputPath"

if ($trustBundled -and -not $pinConfigured) {
    throw 'H02_UNTRUSTED_FILE_BUNDLED: remove unpinned trust config from packaged application.'
}
if ($pinConfigured -and -not $trustedBundleMatchesPinnedIdentity) {
    throw 'H02_PINNED_TRUST_MISMATCH: approved public signing key missing or altered in Windows package.'
}
if (-not $writeGateClosed) {
    throw 'H02_STORAGE_WRITE_GATE_CHANGED: human safety audit required.'
}
if (-not $publicEndpointConfigured -or -not $integrationCodePresent) {
    throw 'H02_PROTOCOL_CONTRACT_CHANGED: verify public API and integration identifier.'
}
if ($RequireProductionTrust) {
    throw 'H02_PRODUCTION_TRUST_NOT_VERIFIED: independent public-key approval and real E2E are required.'
}
