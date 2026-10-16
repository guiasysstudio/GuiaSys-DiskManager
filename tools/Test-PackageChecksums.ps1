[CmdletBinding()]
param([string]$ManifestPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw 'Invalid application version.'
}
if (-not $ManifestPath) {
    $ManifestPath = Join-Path $root 'artifacts\checksums\SHA256SUMS.txt'
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "SHA256 manifest does not exist: $ManifestPath"
}
$lines = @(Get-Content -LiteralPath $ManifestPath -Encoding UTF8)
if ($lines.Count -ne 2) { throw 'SHA256 manifest must contain exactly two entries.' }
$packages = [ordered]@{
    'GuiaSys.DiskManager.exe' = (Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe')
    "GuiaSys-DiskManager-Setup-$version-x64.exe" =
        (Join-Path $root "artifacts\setup\GuiaSys-DiskManager-Setup-$version-x64.exe")
}
$known = @{}
foreach ($line in $lines) {
    if ($line -cmatch '\A([a-f0-9]{64})  ([a-zA-Z0-9._-]+)\z') {
        $checksum = $Matches[1]
        $name = $Matches[2]
    } else {
        throw 'Invalid or unsafe SHA256 manifest line.'
    }
    if (-not $packages.Contains($name) -or $known.ContainsKey($name)) {
        throw "Duplicate or unexpected package name: $name"
    }
    $known[$name] = $checksum
}
foreach ($name in $packages.Keys) {
    if (-not $known.ContainsKey($name)) { throw "Missing required checksum for $name" }
    $path = $packages[$name]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Package missing: $path"
    }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $known[$name]) {
        throw "SHA256 checksum mismatch: $name"
    }
}
Write-Host 'Both packaged application and installer match the SHA256 manifest.'
