[CmdletBinding()]
param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
if ($version -cnotmatch '\A(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') {
    throw "Version must be a stable three-component SemVer (e.g. 0.2.0): $version"
}
if ([int]$Matches[1] -gt 65535 -or [int]$Matches[2] -gt 65535 -or [int]$Matches[3] -gt 65535) {
    throw 'Inno Setup Windows version components must not exceed 65535.'
}


if (-not $IsccPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) { throw 'Inno Setup 6 (ISCC.exe) was not found.' }

$portable = Join-Path $root 'artifacts\portable\GuiaSys.DiskManager.exe'
if (-not (Test-Path -LiteralPath $portable)) { throw 'Portable publish not found. Run dotnet publish first.' }
$exe = Get-Item -LiteralPath $portable
if ($exe.Length -lt 1MB) { throw 'Portable application is unexpectedly small.' }
$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($portable)
if ($info.FileMajorPart -ne [int]($version.Split('.')[0]) -or
    $info.FileMinorPart -ne [int]($version.Split('.')[1]) -or
    $info.FileBuildPart -ne [int]($version.Split('.')[2])) {
    throw "Portable version $($info.FileVersion) does not match Directory.Build.props version $version."
}


& $IsccPath "/DAppVersion=$version" (Join-Path $root 'installer\GuiaSys-DiskManager.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
$setup = Join-Path $root "artifacts\setup\GuiaSys-DiskManager-Setup-$version-x64.exe"
if (-not (Test-Path -LiteralPath $setup) -or (Get-Item -LiteralPath $setup).Length -lt 1MB) {
    throw "Expected versioned x64 installer missing or unexpectedly small: $setup"
}
Write-Host "Validated installer version $version : $setup"

