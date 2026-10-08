#Requires -RunAsAdministrator
[CmdletBinding()]
param([string]$ReportPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'tests\GuiaSys.DiskManager.IntegrationRunner\GuiaSys.DiskManager.IntegrationRunner.csproj'
if (-not $ReportPath) {
    $reportDirectory = Join-Path $root 'artifacts\integration'
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $ReportPath = Join-Path $reportDirectory ("vhdx-integration-{0}.json" -f ([guid]::NewGuid().ToString('N')))
}

dotnet run --project $project -c Release -- $ReportPath
if ($LASTEXITCODE -ne 0) { throw "VHDX integration failed. See $ReportPath" }
Write-Output "VHDX integration passed. Report: $ReportPath"
