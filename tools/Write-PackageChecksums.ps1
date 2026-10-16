[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw 'Invalid or missing stable application version.'
}
$packages = @(
    (Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe'),
    (Join-Path $root "artifacts\setup\GuiaSys-DiskManager-Setup-$version-x64.exe")
)
foreach ($file in $packages) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Required distribution file missing: $file"
    }
    if ((Get-Item -LiteralPath $file).Length -lt 1MB) {
        throw "Required distribution file is unexpectedly small: $file"
    }
}
$target = Join-Path $root 'artifacts\checksums'
New-Item -ItemType Directory -Path $target -Force | Out-Null
$manifest = Join-Path $target 'SHA256SUMS.txt'
$lines = @($packages | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $_)"
})
# Use UTF-8 without BOM for cross-platform sha256sum validation.
[System.IO.File]::WriteAllLines($manifest, [string[]]$lines,
    [System.Text.UTF8Encoding]::new($false))
if ((Get-Content -LiteralPath $manifest).Count -ne 2) {
    throw 'SHA256 manifest must contain exactly the portable exe and versioned installer.'
}
Write-Host "Created SHA256 manifest for $version : $manifest"
