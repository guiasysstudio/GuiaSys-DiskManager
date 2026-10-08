#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$required = @('New-VHD','Mount-VHD','Dismount-VHD','Get-Disk','Initialize-Disk','New-Partition','Format-Volume','Resize-Partition','Remove-Partition')
foreach ($command in $required) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command '$command' is unavailable. Enable Hyper-V management tools." } }

$baseTestRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) 'GuiaSysDiskManager.Tests'))
$testRoot = Join-Path $baseTestRoot ([guid]::NewGuid().ToString('N'))
$vhdxPath = Join-Path $testRoot 'integration-test.vhdx'
$diskNumber = $null

function Assert-TestDisk([object]$disk, [string]$expectedPath) {
    if ($null -eq $disk) { throw 'The temporary VHDX did not resolve to a disk.' }
    if ([int]$disk.Number -eq 0) { throw 'Safety refusal: Disk 0 can never be an integration target.' }
    if ([bool]$disk.IsBoot -or [bool]$disk.IsSystem) { throw 'Safety refusal: boot/system disk detected.' }
    if ([int64]$disk.Size -lt 1900000000 -or [int64]$disk.Size -gt 2300000000) { throw 'Safety refusal: unexpected virtual disk size.' }
    if (-not (Test-Path -LiteralPath $expectedPath)) { throw 'Safety refusal: VHDX path disappeared.' }
    $allowedBus = @('File Backed Virtual','Virtual','Unknown')
    if ([string]$disk.BusType -notin $allowedBus) { throw "Safety refusal: unexpected bus type '$($disk.BusType)'." }
}

try {
    New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
    New-VHD -Path $vhdxPath -SizeBytes 2GB -Dynamic | Out-Null
    $mounted = Mount-VHD -Path $vhdxPath -PassThru
    $disk = $mounted | Get-Disk
    Assert-TestDisk $disk $vhdxPath
    $diskNumber = [int]$disk.Number

    Initialize-Disk -Number $diskNumber -PartitionStyle GPT
    $partition = New-Partition -DiskNumber $diskNumber -Size 1200MB -AssignDriveLetter
    Assert-TestDisk (Get-Disk -Number $diskNumber) $vhdxPath
    $volume = $partition | Format-Volume -FileSystem NTFS -NewFileSystemLabel 'GSDM_TEST' -Confirm:$false -Force
    if ($volume.FileSystem -ne 'NTFS' -or $volume.FileSystemLabel -ne 'GSDM_TEST') { throw 'NTFS format validation failed.' }

    Resize-Partition -DiskNumber $diskNumber -PartitionNumber $partition.PartitionNumber -Size 900MB
    $resized = Get-Partition -DiskNumber $diskNumber -PartitionNumber $partition.PartitionNumber
    if ([math]::Abs([int64]$resized.Size - 900MB) -gt 2MB) { throw 'Resize validation failed.' }

    Remove-Partition -DiskNumber $diskNumber -PartitionNumber $partition.PartitionNumber -Confirm:$false
    if (Get-Partition -DiskNumber $diskNumber -PartitionNumber $partition.PartitionNumber -ErrorAction SilentlyContinue) { throw 'Partition deletion validation failed.' }
    $replacement = New-Partition -DiskNumber $diskNumber -UseMaximumSize -AssignDriveLetter
    $replacementVolume = $replacement | Format-Volume -FileSystem exFAT -NewFileSystemLabel 'GSDM_EXFAT' -Confirm:$false -Force
    if ($replacementVolume.FileSystem -ne 'exFAT') { throw 'exFAT recreation validation failed.' }
    Write-Output "VHDX integration passed on temporary Disk $diskNumber."
}
finally {
    if (Test-Path -LiteralPath $vhdxPath) {
        Dismount-VHD -Path $vhdxPath -ErrorAction SilentlyContinue
    }
    $resolvedTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $safePrefix = $baseTestRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTestRoot.StartsWith($safePrefix, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Cleanup safety refusal: temporary path escaped the test root.' }
    if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue }
}
