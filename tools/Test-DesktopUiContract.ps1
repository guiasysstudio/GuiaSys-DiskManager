[CmdletBinding()]
param()

# GDM-H03-UI static desktop contract audit. No processes, disks or APIs.
# This does not replace a manual WPF visual/DPI/accessibility smoke test.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$windowPath = Join-Path $root 'src\GuiaSys.DiskManager\MainWindow.xaml'
$themePath = Join-Path $root 'src\GuiaSys.DiskManager\App.xaml'
$gatePath = Join-Path $root 'src\GuiaSys.DiskManager\Licensing\GslV1Models.cs'
if (@($windowPath, $themePath, $gatePath | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -gt 0) {
    throw 'H03_UI_MISSING_REQUIRED_SOURCE'
}
[xml]$windowXml = Get-Content -LiteralPath $windowPath -Raw -Encoding UTF8
[xml]$themeXml = Get-Content -LiteralPath $themePath -Raw -Encoding UTF8
$xml = Get-Content -LiteralPath $windowPath -Raw -Encoding UTF8
$theme = Get-Content -LiteralPath $themePath -Raw -Encoding UTF8
$gate = Get-Content -LiteralPath $gatePath -Raw -Encoding UTF8
if ($windowXml.DocumentElement.LocalName -ne 'Window' -or
    $themeXml.DocumentElement.LocalName -ne 'Application') {
    throw 'H03_UI_INVALID_XAML_ROOT'
}
$required = @(
    'x:Name="PartitionsSection"', 'x:Name="DiskMapSection"',
    'x:Name="OverviewSection"', 'x:Name="DetailsSection"',
    'x:Name="HealthSection"', 'x:Name="RecoverySection"',
    'x:Name="MaintenanceSection"', 'x:Name="OperationEditor"',
    'x:Name="OperationsSection"', 'x:Name="QueueSection"',
    'x:Name="PartitionsGrid"', 'x:Name="OperationTypeSelector"'
)
foreach ($anchor in $required) {
    if (-not $xml.Contains($anchor)) { throw "H03_UI_MISSING_ANCHOR: $anchor" }
}
if ($xml.IndexOf('x:Name="PartitionsSection"', [StringComparison]::Ordinal) -ge
    $xml.IndexOf('x:Name="DiskMapSection"', [StringComparison]::Ordinal) -or
    $xml.IndexOf('x:Name="DiskMapSection"', [StringComparison]::Ordinal) -ge
    $xml.IndexOf('x:Name="OverviewSection"', [StringComparison]::Ordinal)) {
    throw 'H03_UI_MAIN_PANE_ORDER_CHANGED'
}
$patterns = @(
    'Grid.Row="0" Background="#1268BA"',
    '<Style x:Key="RibbonButton"',
    '<Style x:Key="ToolbarButton"',
    'Command="{Binding RefreshCommand}"',
    'Command="{Binding ApplyCommand}"',
    'Command="{Binding UndoCommand}"',
    'Command="{Binding ClearCommand}"',
    'Click="License_Click"',
    'Click="About_Click"',
    'Click="PartitionSegment_Click"',
    'Click="DiskRow_Click"',
    'ItemsSource="{Binding DiskMapRows}"',
    'ItemsSource="{Binding Segments}"',
    '<controls:ProportionalPartitionPanel/>',
    'Value="{Binding UsagePercent, Mode=OneWay}"',
    'Value="{Binding SelectedAllocatedPercent, Mode=OneWay}"',
    'Value="{Binding Progress, Mode=OneWay}"',
    'IsReadOnly="True" CanUserAddRows="False" CanUserDeleteRows="False"'
)
foreach ($anchor in $patterns) {
    if (-not $xml.Contains($anchor)) { throw "H03_UI_COMMAND_OR_SAFETY_BINDING_MISSING: $anchor" }
}
if ($xml -match 'Value="\{Binding\s+(?:SelectedAllocatedPercent|SelectedPartition\.UsagePercent|UsagePercent|Progress)\}"') {
    throw 'H03_UI_READONLY_BINDING_NOT_ONEWAY'
}
if (-not $theme.Contains('Color="#14283F"') -or
    -not $theme.Contains('Color="#F4F7FC"')) {
    throw 'H03_UI_LIGHT_THEME_NOT_CONFIGURED'
}
if ($gate -cnotmatch 'ReleasedAfterSafetyAudit\s*=>\s*false\s*;') {
    throw 'H03_UI_WRITE_GATE_CHANGED'
}
Write-Host 'PASS: H03 desktop UI XML; all-disk proportional rows, read-only usage bindings, navigation and closed write gate.'
Write-Host 'NOTE: Window screenshot, contrast, responsive sizing and manual Windows tests are still required.'
