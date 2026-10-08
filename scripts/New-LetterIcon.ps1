[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [Parameter(Mandatory)][string]$Letter,
    [string]$Color = '#3769A8'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sizes = @(16,20,24,32,40,48,64,256)
$frames = @()
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size,$size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $graphics.Clear([Drawing.Color]::Transparent)
    $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($Color))
    $inset = [single]($size / 32.0)
    $graphics.FillEllipse($brush,$inset,$inset,($size - 2*$inset),($size - 2*$inset))
    $font = [Drawing.Font]::new('Segoe UI',[single]($size * 0.53),[Drawing.FontStyle]::Bold,[Drawing.GraphicsUnit]::Pixel)
    $format = [Drawing.StringFormat]::new()
    $format.Alignment = [Drawing.StringAlignment]::Center
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $graphics.DrawString($Letter,$font,[Drawing.Brushes]::White,[Drawing.RectangleF]::new(0,0,$size,$size),$format)
    $memory = [IO.MemoryStream]::new()
    try {
        $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png)
        $frames += @{Size=$size;Bytes=$memory.ToArray()}
    } finally {
        $memory.Dispose();$format.Dispose();$font.Dispose();$brush.Dispose();$graphics.Dispose();$bitmap.Dispose()
    }
}
$stream = [IO.File]::Create([IO.Path]::GetFullPath($OutputPath))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
    $offset = [uint32](6 + 16*$frames.Count)
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
        $writer.Write($dimension);$writer.Write($dimension);$writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write($offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose();$stream.Dispose() }
