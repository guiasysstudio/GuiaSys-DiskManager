[CmdletBinding()]
param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Version not found in Directory.Build.props.' }

if (-not $IsccPath) {
    $candidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }

$portable = Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe'
if (-not (Test-Path -LiteralPath $portable)) { throw 'Portable publish not found. Run dotnet publish first.' }

& $IsccPath "/DAppVersion=$version" (Join-Path $root 'installer\GuiaSys-DiskManager.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
