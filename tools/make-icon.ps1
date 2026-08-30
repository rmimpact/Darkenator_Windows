# Generates src/Darkenator/Assets/app.ico  (multi-size PNG-packed .ico)
Add-Type -AssemblyName System.Drawing

$sizes  = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$outDir = Join-Path $PSScriptRoot '..\src\Darkenator\Assets'
$outDir = [System.IO.Path]::GetFullPath($outDir)
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$out = Join-Path $outDir 'app.ico'

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1.0, $s * 0.06)
    $d   = $s - (2 * $pad)
    $rect = New-Object System.Drawing.RectangleF($pad, $pad, $d, $d)

    # dark half (full disc first)
    $dark = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 24, 24, 30))
    $g.FillEllipse($dark, $rect)

    # light half (left semicircle)
    $light = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 248, 249, 252))
    $g.FillPie($light, [float]$rect.X, [float]$rect.Y, [float]$rect.Width, [float]$rect.Height, [float]90, [float]180)

    # accent ring
    $ringW = [Math]::Max(1.0, $s * 0.075)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 96, 132, 255)), $ringW
    $inset = $ringW / 2.0
    $ringRect = New-Object System.Drawing.RectangleF(($pad + $inset), ($pad + $inset), ($d - $ringW), ($d - $ringW))
    $g.DrawEllipse($pen, $ringRect)

    $g.Dispose(); $dark.Dispose(); $light.Dispose(); $pen.Dispose()
    return $bmp
}

$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms  = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
    $ms.Dispose(); $bmp.Dispose()
}

$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)   # ICONDIR
$offset = 6 + (16 * $frames.Count)
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim)
    $bw.Write([byte]0);    $bw.Write([byte]0)
    $bw.Write([uint16]1);  $bw.Write([uint16]32)
    $bw.Write([uint32]$f.Data.Length)
    $bw.Write([uint32]$offset)
    $offset += $f.Data.Length
}
foreach ($f in $frames) { $bw.Write($f.Data) }
$bw.Flush(); $bw.Dispose(); $fs.Dispose()

Write-Host "Wrote $out ($((Get-Item $out).Length) bytes, $($frames.Count) frames)"
