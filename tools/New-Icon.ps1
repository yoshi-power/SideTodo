$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$images = @()
foreach ($size in @(16, 32, 48, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 256.0, $size / 256.0)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(8, 8, 116, 116, 180, 90)
    $path.AddArc(132, 8, 116, 116, 270, 90)
    $path.AddArc(132, 132, 116, 116, 0, 90)
    $path.AddArc(8, 132, 116, 116, 90, 90)
    $path.CloseFigure()
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(27,27,27))
    $graphics.FillPath($background, $path)
    $bar = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(233,215,200), 10)
    $white = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(241,241,241), 17)
    $line = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(133,133,133), 9)
    foreach ($pen in @($bar, $white, $line)) { $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round' }
    $graphics.DrawLine($bar, 50, 68, 50, 188)
    $graphics.DrawLines($white, [System.Drawing.PointF[]]@([System.Drawing.PointF]::new(87,130), [System.Drawing.PointF]::new(116,159), [System.Drawing.PointF]::new(177,92)))
    $graphics.DrawLine($line, 90, 191, 192, 191)
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,@{ Size=$size; Bytes=$stream.ToArray() }
    foreach ($item in @($stream,$bar,$white,$line,$background,$path,$graphics,$bitmap)) { $item.Dispose() }
}
$file = [System.IO.File]::Create((Join-Path $root 'assets\SideTodo.ico'))
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($icon in $images) {
        $dimension = if ($icon.Size -eq 256) { 0 } else { $icon.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$icon.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $icon.Bytes.Length
    }
    foreach ($icon in $images) { $writer.Write([byte[]]$icon.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
