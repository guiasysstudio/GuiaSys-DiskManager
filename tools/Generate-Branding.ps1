[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'GuiaSys-DiskManager-Logo.png'
$outputDirectory = Join-Path $root 'src\GuiaSys.DiskManager\Assets\Branding'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $outputDirectory 'GuiaSys-DiskManager-Logo.png') -Force

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
$crop = [System.Drawing.Rectangle]::new(155, 20, 945, 835)
$symbol = [System.Drawing.Bitmap]::new(945, 945, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($symbol)
$graphics.Clear([System.Drawing.Color]::Transparent)
$graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$graphics.DrawImage($source, [System.Drawing.Rectangle]::new(0, 55, 945, 835), $crop, [System.Drawing.GraphicsUnit]::Pixel)
$graphics.Dispose()

$sizes = @(16,20,24,32,40,48,64,128,256,512)
$pngPayloads = @()
foreach ($size in $sizes) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($symbol, 0, 0, $size, $size)
    $g.Dispose()
    $pngPath = Join-Path $outputDirectory "logo-$size.png"
    $bitmap.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngPayloads += ,$stream.ToArray()
    $stream.Dispose()
    $bitmap.Dispose()
}

$icoPath = Join-Path $outputDirectory 'GuiaSys-DiskManager.ico'
$file = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($file)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + (16 * $sizes.Count)
for ($index = 0; $index -lt $sizes.Count; $index++) {
    $size = $sizes[$index]
    $dimension = if ($size -ge 256) { 0 } else { $size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$pngPayloads[$index].Length); $writer.Write([uint32]$offset)
    $offset += $pngPayloads[$index].Length
}
foreach ($payload in $pngPayloads) { $writer.Write($payload) }
$writer.Dispose(); $file.Dispose(); $symbol.Dispose(); $source.Dispose()
Write-Output "Branding generated in $outputDirectory"
