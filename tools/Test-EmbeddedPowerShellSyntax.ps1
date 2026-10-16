# Parse embedded constant PowerShell scripts without executing ANY command.
# Uses the Windows PowerShell AST parser available on GitHub Actions Windows runners.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sources = @(
    'src/GuiaSys.DiskManager/Services/PowerShellDiskInventoryService.cs',
    'src/GuiaSys.DiskManager/Operations/PowerShellStorageOperationExecutor.cs'
)
$checked = 0
foreach ($relativePath in $sources) {
    $file = Join-Path $projectRoot $relativePath
    $source = [System.IO.File]::ReadAllText($file)
    $pattern = 'private\s+const\s+string\s+Script\s*=\s*"""(?<script>[\s\S]*?)""";'
    $matches = [regex]::Matches($source, $pattern)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one constant script in $relativePath, found $($matches.Count)."
    }
    $tokens = $null
    $errors = $null
    $script = $matches[0].Groups['script'].Value
    [void][System.Management.Automation.Language.Parser]::ParseInput(
        $script, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) {
        $detail = ($errors | ForEach-Object {
            "line $($_.Extent.StartLineNumber): $($_.Message)"
        }) -join '; '
        throw "PowerShell syntax error in $relativePath : $detail"
    }
    $checked++
    Write-Host "PASS: parsed embedded PowerShell ($relativePath)"
}
if ($checked -ne $sources.Count) { throw 'Embedded PowerShell audit incomplete.' }
Write-Host "PASS: $checked scripts parsed without running storage commands."
