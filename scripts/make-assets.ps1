# Generates branding assets for IPTV Player:
#   assets\icon.ico          - multi-size app/installer icon (16..256, PNG-encoded)
#   assets\wizard-large.bmp  - 164x314 installer welcome/finish banner
#   assets\wizard-small.bmp  - 55x58 installer header logo
# Run with Windows PowerShell:  powershell -File scripts\make-assets.ps1

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$assets = Join-Path $PSScriptRoot '..\assets'
$assets = [System.IO.Path]::GetFullPath($assets)
New-Item -ItemType Directory -Force -Path $assets | Out-Null

# Brand colours.
$cTop    = [System.Drawing.Color]::FromArgb(255, 0x2E, 0x6B, 0xD6)   # blue
$cBottom = [System.Drawing.Color]::FromArgb(255, 0x13, 0x1A, 0x2E)   # near-black navy

function New-LogoBitmap([int]$size, [bool]$rounded) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cTop, $cBottom, 45.0)

    if ($rounded) {
        $r = [int]($size * 0.22)
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $path.AddArc(0, 0, $d, $d, 180, 90)
        $path.AddArc($size - $d, 0, $d, $d, 270, 90)
        $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
        $path.AddArc(0, $size - $d, $d, $d, 90, 90)
        $path.CloseFigure()
        $g.FillPath($brush, $path)
    } else {
        $g.FillRectangle($brush, $rect)
    }

    # White play triangle, centered.
    $pts = @(
        (New-Object System.Drawing.PointF([single]($size*0.40), [single]($size*0.30))),
        (New-Object System.Drawing.PointF([single]($size*0.40), [single]($size*0.70))),
        (New-Object System.Drawing.PointF([single]($size*0.70), [single]($size*0.50)))
    )
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPolygon($white, $pts)

    $g.Dispose(); $brush.Dispose(); $white.Dispose()
    return $bmp
}

# ---- icon.ico (multi-size, PNG-encoded entries) ----
$sizes = 16, 24, 32, 48, 64, 128, 256
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-LogoBitmap $s $true
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += [pscustomobject]@{ Size = $s; Bytes = $ms.ToArray() }
    $ms.Dispose(); $bmp.Dispose()
}

$icoPath = Join-Path $assets 'icon.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$entries.Count)
$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $wb = if ($e.Size -ge 256) { 0 } else { $e.Size }
    $bw.Write([byte]$wb); $bw.Write([byte]$wb); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$e.Bytes.Length); $bw.Write([uint32]$offset)
    $offset += $e.Bytes.Length
}
foreach ($e in $entries) { $bw.Write($e.Bytes) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()
Write-Output "wrote $icoPath"

# ---- wizard images (BMP, fully opaque) ----
function New-Banner([int]$w, [int]$h, [bool]$withText) {
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $cTop, $cBottom, 90.0)
    $g.FillRectangle($brush, $rect)

    # Logo mark (rounded tile with play) in the upper area.
    $mark = [int]([Math]::Min($w, $h) * 0.42)
    $logo = New-LogoBitmap $mark $true
    $lx = [int](($w - $mark) / 2)
    $ly = [int]($h * 0.14)
    $g.DrawImage($logo, $lx, $ly, $mark, $mark)
    $logo.Dispose()

    if ($withText) {
        $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
        $sf = New-Object System.Drawing.StringFormat
        $sf.Alignment = [System.Drawing.StringAlignment]::Center
        $f1 = New-Object System.Drawing.Font('Segoe UI', 18, [System.Drawing.FontStyle]::Bold)
        $f2 = New-Object System.Drawing.Font('Segoe UI', 10, [System.Drawing.FontStyle]::Regular)
        $cx = [single]($w / 2.0)
        $ty = [single]($ly + $mark + 14)
        $g.DrawString('IPTV', $f1, $white, $cx, $ty, $sf)
        $g.DrawString('PLAYER', $f1, $white, $cx, [single]($ty + 28), $sf)
        $dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(190, 255, 255, 255))
        $g.DrawString('4K . HD . LIVE', $f2, $dim, $cx, [single]($ty + 64), $sf)
        $f1.Dispose(); $f2.Dispose(); $white.Dispose(); $dim.Dispose(); $sf.Dispose()
    }

    $g.Dispose(); $brush.Dispose()
    return $bmp
}

$large = New-Banner 164 314 $true
$large.Save((Join-Path $assets 'wizard-large.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$large.Dispose()
Write-Output "wrote wizard-large.bmp"

$small = New-Banner 55 58 $false
$small.Save((Join-Path $assets 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$small.Dispose()
Write-Output "wrote wizard-small.bmp"

Write-Output "assets done."
