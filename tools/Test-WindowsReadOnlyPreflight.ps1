[CmdletBinding()]
param(
    [string]$PortablePath,
    [string]$InstallerPath,
    [string]$ManifestPath,
    [string]$OutputPath,
    [switch]$RequireValidSignatures
)

# GDM-H01 -- NON-DESTRUCTIVE Windows package preflight.
# NEVER launches/installs executables, uses Get-Disk/partitions, elevates, downloads
# packages or modifies licensing. The only write is the optional JSON report.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw 'H01_INVALID_VERSION: Directory.Build.props must contain stable X.Y.Z package version.'
}
if (-not $PortablePath) {
    $PortablePath = Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe'
}
if (-not $InstallerPath) {
    $InstallerPath = Join-Path $root "artifacts\setup\GuiaSys-DiskManager-Setup-$version-x64.exe"
}
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $root 'artifacts\checksums\SHA256SUMS.txt'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $root 'artifacts\smoke\windows-readonly-preflight.json'
}

if (-not [Environment]::Is64BitOperatingSystem -or
    [Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'H01_WINDOWS_X64_REQUIRED: preflight only runs on 64-bit Windows.'
}

$portableName = 'GuiaSys.DiskManager.exe'
$installerName = "GuiaSys-DiskManager-Setup-$version-x64.exe"
if ((Split-Path -Leaf $PortablePath) -cne $portableName -or
    (Split-Path -Leaf $InstallerPath) -cne $installerName) {
    throw 'H01_PACKAGE_NAME_MISMATCH: expected canonical portable and versioned installer names.'
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw 'H01_MANIFEST_MISSING: SHA256SUMS.txt must be supplied.'
}
$lines = @(Get-Content -LiteralPath $ManifestPath -Encoding UTF8)
if ($lines.Count -ne 2) {
    throw 'H01_MANIFEST_INVALID: expected exactly two SHA256 entries.'
}
$known = @{}
foreach ($line in $lines) {
    if ($line -cnotmatch '\A([0-9a-f]{64})  ([A-Za-z0-9._-]+)\z') {
        throw 'H01_MANIFEST_INVALID: unrecognized SHA256 entry format.'
    }
    $hash = $Matches[1]
    $name = $Matches[2]
    if ($name -cne $portableName -and $name -cne $installerName) {
        throw 'H01_MANIFEST_INVALID: unexpected package name.'
    }
    if ($known.ContainsKey($name)) {
        throw 'H01_MANIFEST_INVALID: duplicate package name.'
    }
    $known[$name] = $hash
}
if (-not $known.ContainsKey($portableName) -or -not $known.ContainsKey($installerName)) {
    throw 'H01_MANIFEST_INVALID: required package missing.'
}

$packages = @(
    [pscustomobject]@{ Name = $portableName; Path = $PortablePath; Kind = 'portable' },
    [pscustomobject]@{ Name = $installerName; Path = $InstallerPath; Kind = 'installer' }
)
$results = @()
foreach ($item in $packages) {
    if (-not (Test-Path -LiteralPath $item.Path -PathType Leaf)) {
        throw "H01_PACKAGE_MISSING: $($item.Name)"
    }
    $file = Get-Item -LiteralPath $item.Path
    if ($file.Length -lt 1MB) {
        throw "H01_PACKAGE_TOO_SMALL: $($item.Name)"
    }
    $actual = (Get-FileHash -LiteralPath $item.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $known[$item.Name]) {
        throw "H01_PACKAGE_CHECKSUM_MISMATCH: $($item.Name)"
    }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($file.FullName)
    $components = $version.Split('.')
    if ($info.FileMajorPart -ne [int]$components[0] -or
        $info.FileMinorPart -ne [int]$components[1] -or
        $info.FileBuildPart -ne [int]$components[2]) {
        throw "H01_PACKAGE_VERSION_MISMATCH: $($item.Name) reports $($info.FileVersion), expected $version."
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
    $signatureValid = $signature.Status -eq 'Valid' -and
        $null -ne $signature.SignerCertificate
    $results += [pscustomobject]@{
        kind = $item.Kind
        name = $item.Name
        fileVersion = [string]$info.FileVersion
        sizeBytes = [long]$file.Length
        sha256 = $actual
        matchesProvidedManifest = $true
        authenticodeStatus = [string]$signature.Status
        hasValidSignature = [bool]$signatureValid
    }
}
$allSigned = @($results | Where-Object { -not $_.hasValidSignature }).Count -eq 0
$report = [ordered]@{
    schemaVersion = 1
    project = 'GuiaSys Disk Manager'
    buildVersion = $version
    workflowCommit = [string]$env:GITHUB_SHA
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    osBuild = [Environment]::OSVersion.Version.Build
    osArchitecture = 'x64'
    decision = 'NO-GO'
    automatedPackageIntegrityPassed = $true
    validSignatures = [bool]$allSigned
    provenanceConfirmedByIndependentTrustedChannel = $false
    installedOrExecutedApplication = $false
    touchedDiskOrPartitionConfiguration = $false
    manualWindows10And11SmokeCompleted = $false
    manualAccessibilitySmokeCompleted = $false
    packageResults = $results
    note = 'A matching checksum in a manifest from the same source does not prove publisher identity. No installation, device inventory, destructive operation, Windows 10/11 human smoke or commercial authorization was performed.'
}
$folder = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $folder)) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}
[IO.File]::WriteAllText($OutputPath,
    ($report | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
Write-Host "H01: package integrity and version PASS for $version; Authenticode valid on both: $allSigned."
Write-Host "H01: commercial NO-GO; no disk access or installers executed. Report: $OutputPath"
if ($RequireValidSignatures -and -not $allSigned) {
    throw 'H01_UNSIGNED_PACKAGES: Authenticode verification is required and failed.'
}
