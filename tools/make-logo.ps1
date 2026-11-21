# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Jouko Broström
# Ritar Cyberdromes logotyp (samma geometri som assets\logo.svg, 512x512) med System.Drawing och skapar:
#   assets\logo-<storlek>.png (16-512), src\Cyberdrome.App\Assets\cyberdrome.ico + cyberdrome-256.png,
#   Android-ikoner (adaptiv förgrund på #1E6BFF) för Cyberdrome Launcher och Cyberdrome Radio,
#   assets\social-preview.png (1280x640, GitHub).
# Användning: powershell -ExecutionPolicy Bypass -File tools\make-logo.ps1
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function C([string]$hex, [int]$a = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($hex); [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B) }
function RoundPen($color, [float]$w) { $p = New-Object System.Drawing.Pen($color, $w); $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p }

# Ritar logotypen i ett 512x512-koordinatsystem. $background = false ger bara mätaren (Android-förgrund).
function Draw-Logo([System.Drawing.Graphics]$g, [bool]$background) {
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    if ($background) {
        $r = 112; $d = 2 * $r; $x = 16; $y = 16; $s = 480
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($x, $y, $d, $d, 180, 90); $path.AddArc($x + $s - $d, $y, $d, $d, 270, 90)
        $path.AddArc($x + $s - $d, $y + $s - $d, $d, $d, 0, 90); $path.AddArc($x, $y + $s - $d, $d, $d, 90, 90); $path.CloseFigure()
        $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(16, 16)), (New-Object System.Drawing.PointF(496, 496)), (C "#1E6BFF"), (C "#0A2A6B"))
        $g.FillPath($bg, $path)
    }
    # C-bågen: cirkel r144 kring 256,256 från -45° moturs 270° (öppen åt höger), gradient vänster→höger.
    $arcBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(90, 0)), (New-Object System.Drawing.PointF(380, 0)), (C "#7FE3FF"), (C "#FFFFFF"))
    $arcPen = New-Object System.Drawing.Pen($arcBrush, 44); $arcPen.StartCap = 'Round'; $arcPen.EndCap = 'Round'
    $g.DrawArc($arcPen, 112, 112, 288, 288, -45, -270)
    # Skalstreck
    $tick = RoundPen (C "#BFEFFF" 217) 10
    foreach ($l in @(@(160, 256, 182, 256), @(188, 188, 204, 204), @(256, 160, 256, 182), @(188, 324, 204, 308), @(256, 352, 256, 330))) { $g.DrawLine($tick, $l[0], $l[1], $l[2], $l[3]) }
    # Visare och nav
    $g.DrawLine((RoundPen (C "#FF7A45") 16), 256, 256, 338, 196)
    $g.FillEllipse([System.Drawing.Brushes]::White, 234, 234, 44, 44)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C "#FF7A45")), 247, 247, 18, 18)
    # Signalvågor i C:ets öppning (bågar medurs, centrum till vänster om ändpunkterna)
    $h1 = [Math]::Sqrt(40 * 40 - 30 * 30); $c1 = 392 - $h1; $a1 = [Math]::Atan2(30, $h1) * 180 / [Math]::PI
    $g.DrawArc((RoundPen (C "#7FE3FF") 12), [float]($c1 - 40), 216, 80, 80, [float](-$a1), [float](2 * $a1))
    $h2 = [Math]::Sqrt(68 * 68 - 50 * 50); $c2 = 420 - $h2; $a2 = [Math]::Atan2(50, $h2) * 180 / [Math]::PI
    $g.DrawArc((RoundPen (C "#7FE3FF" 179) 12), [float]($c2 - 68), 188, 136, 136, [float](-$a2), [float](2 * $a2))
}

function New-LogoBitmap([int]$size, [bool]$background = $true, [float]$scale = 1.0, [System.Drawing.Color]$fill = [System.Drawing.Color]::Transparent) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($fill)
    $f = $size / 512.0 * $scale
    $off = $size * (1 - $scale) / 2
    $g.TranslateTransform($off, $off); $g.ScaleTransform($f, $f)
    Draw-Logo $g $background
    $g.Dispose()
    return $bmp
}

# PNG i alla storlekar
$sizes = 16, 24, 32, 48, 64, 128, 256, 512
$pngBytes = @{}
foreach ($s in $sizes) {
    $b = New-LogoBitmap $s
    $file = Join-Path $assets "logo-$s.png"; $b.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $ms = New-Object System.IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $pngBytes[$s] = $ms.ToArray(); $b.Dispose()
}

# .ico (16–256) för programmet, fönstret, genvägar och installationen
$appAssets = Join-Path $root "src\Cyberdrome.App\Assets"
$icoSizes = 16, 24, 32, 48, 64, 128, 256
$fs = [System.IO.File]::Create((Join-Path $appAssets "cyberdrome.ico"))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$icoSizes.Count)
$offset = 6 + 16 * $icoSizes.Count
foreach ($s in $icoSizes) { $b = if ($s -ge 256) { 0 } else { $s }; $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngBytes[$s].Length); $w.Write([uint32]$offset); $offset += $pngBytes[$s].Length }
foreach ($s in $icoSizes) { $w.Write($pngBytes[$s]) }
$w.Close()
[IO.File]::WriteAllBytes((Join-Path $appAssets "cyberdrome-256.png"), $pngBytes[256])

# Android: adaptiv ikon (förgrund = mätaren inom säker zon, bakgrund #1E6BFF) + vanlig ikon för äldre Android.
$dens = @{ "mdpi" = 1.0; "hdpi" = 1.5; "xhdpi" = 2.0; "xxhdpi" = 3.0; "xxxhdpi" = 4.0 }
foreach ($app in "CyberdromeLauncher", "CyberdromeRadio") {
    $res = Join-Path $root "android\$app\res"
    foreach ($d in $dens.Keys) {
        $dir = Join-Path $res "mipmap-$d"; New-Item -ItemType Directory -Force $dir | Out-Null
        $fg = New-LogoBitmap ([int](108 * $dens[$d])) $false 0.62
        $fg.Save((Join-Path $dir "ic_launcher_foreground.png"), [System.Drawing.Imaging.ImageFormat]::Png); $fg.Dispose()
        $legacy = New-LogoBitmap ([int](48 * $dens[$d]))
        $legacy.Save((Join-Path $dir "ic_launcher.png"), [System.Drawing.Imaging.ImageFormat]::Png); $legacy.Dispose()
    }
    $any = Join-Path $res "mipmap-anydpi-v26"; New-Item -ItemType Directory -Force $any | Out-Null
    $xml = "<?xml version=""1.0"" encoding=""utf-8""?>`n<adaptive-icon xmlns:android=""http://schemas.android.com/apk/res/android"">`n    <background android:drawable=""@color/ic_launcher_background"" />`n    <foreground android:drawable=""@mipmap/ic_launcher_foreground"" />`n</adaptive-icon>`n"
    [IO.File]::WriteAllText((Join-Path $any "ic_launcher.xml"), $xml, (New-Object Text.UTF8Encoding($false)))
    $vals = Join-Path $res "values"; New-Item -ItemType Directory -Force $vals | Out-Null
    [IO.File]::WriteAllText((Join-Path $vals "ic_launcher_background.xml"), "<?xml version=""1.0"" encoding=""utf-8""?>`n<resources>`n    <color name=""ic_launcher_background"">#1E6BFF</color>`n</resources>`n", (New-Object Text.UTF8Encoding($false)))
}

# GitHub social preview 1280x640
$sp = New-Object System.Drawing.Bitmap 1280, 640
$g = [System.Drawing.Graphics]::FromImage($sp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(1280, 640)), (C "#0A2A6B"), (C "#1E6BFF"))
$g.FillRectangle($grad, 0, 0, 1280, 640)
$logo = New-LogoBitmap 360
$g.DrawImage($logo, 110, 140, 360, 360); $logo.Dispose()
$g.DrawString("Cyberdrome Cockpit", (New-Object System.Drawing.Font("Segoe UI", 70, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)), [System.Drawing.Brushes]::White, 520, 190)
$g.DrawString("Android head unit and virtual OBD simulator", (New-Object System.Drawing.Font("Segoe UI", 34, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)), (New-Object System.Drawing.SolidBrush (C "#BFEFFF")), 526, 310)
$g.DrawString("Open source, GPL-3.0", (New-Object System.Drawing.Font("Segoe UI", 30, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)), (New-Object System.Drawing.SolidBrush (C "#FF7A45")), 526, 372)
$g.Dispose()
$sp.Save((Join-Path $assets "social-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png); $sp.Dispose()
Write-Host "Logotyp klar: assets\, src\Cyberdrome.App\Assets\, android\*\res\"
