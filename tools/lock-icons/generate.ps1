# Deterministic generator for the DropWheel Lock padlock badge.
# Run with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\lock-icons\generate.ps1
# Output: src/FileAction.App/Assets/Lock/padlock.ico (16/24/32/48/64/256 PNG frames) and padlock-256.png.
# Only the padlock is drawn here. The displayed icons are composed from the system's own file/folder icon.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $root 'src\FileAction.App\Assets\Lock'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sizes = @(16, 24, 32, 48, 64, 256)

$purple = [System.Drawing.Color]::FromArgb(255, 0x82, 0x24, 0xD4)
$lavender = [System.Drawing.Color]::FromArgb(255, 0xB4, 0x8A, 0xE0)
$deep = [System.Drawing.Color]::FromArgb(255, 0x5A, 0x0B, 0x8E)
$white = [System.Drawing.Color]::White

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc([single]$x, [single]$y, [single]$d, [single]$d, 180, 90)
    $path.AddArc([single]($x + $w - $d), [single]$y, [single]$d, [single]$d, 270, 90)
    $path.AddArc([single]($x + $w - $d), [single]($y + $h - $d), [single]$d, [single]$d, 0, 90)
    $path.AddArc([single]$x, [single]($y + $h - $d), [single]$d, [single]$d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Frame([int]$s) {
    $u = $s / 100.0
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded shackle.
    $archX = 33 * $u
    $archY = 15 * $u
    $archW = 34 * $u
    $archH = 34 * $u
    $archPen = New-Object System.Drawing.Pen($purple, [single][Math]::Max(2.0, 11.0 * $u))
    $archPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $archPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($archPen, [single]$archX, [single]$archY, [single]$archW, [single]$archH, 180, 180)
    $archPen.Dispose()

    # Solid #8224D4 body with a lavender highlight band and a thin deep-purple rim for both themes.
    $bodyX = 18 * $u
    $bodyY = 43 * $u
    $bodyW = 64 * $u
    $bodyH = 49 * $u
    $body = New-RoundedPath $bodyX $bodyY $bodyW $bodyH (9 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush($purple)), $body)
    $highlight = New-RoundedPath ($bodyX + 4 * $u) ($bodyY + 4 * $u) ($bodyW - 8 * $u) (8 * $u) (4 * $u)
    $g.FillPath((New-Object System.Drawing.SolidBrush($lavender)), $highlight)
    $highlight.Dispose()
    $rim = New-Object System.Drawing.Pen($deep, [single][Math]::Max(1.0, 1.6 * $u))
    $g.DrawPath($rim, $body)
    $rim.Dispose()
    $body.Dispose()

    # White keyhole: circle and stem.
    $keyR = 7.5 * $u
    $cx = 50 * $u
    $keyY = 61 * $u
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($white)),
        [single]($cx - $keyR), [single]($keyY - $keyR), [single]($keyR * 2), [single]($keyR * 2))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($white)),
        [single]($cx - $keyR * 0.34), [single]($keyY + $keyR * 0.9), [single]($keyR * 0.68), [single]($keyR * 1.8))

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return $bytes
}

$frames = @{}
foreach ($s in $sizes) { $frames[$s] = New-Frame $s }

$stream = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($stream)
$w.Write([UInt16]0)
$w.Write([UInt16]1)
$w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
    $bytes = $frames[$s]
    $w.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))
    $w.Write([byte]($(if ($s -eq 256) { 0 } else { $s })))
    $w.Write([byte]0)
    $w.Write([byte]0)
    $w.Write([UInt16]1)
    $w.Write([UInt16]32)
    $w.Write([UInt32]$bytes.Length)
    $w.Write([UInt32]$offset)
    $offset += $bytes.Length
}
foreach ($s in $sizes) { $bytes = [byte[]]$frames[$s]; $w.Write($bytes, 0, $bytes.Length) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $outDir 'padlock.ico'), $stream.ToArray())
$w.Dispose()
$stream.Dispose()
[IO.File]::WriteAllBytes((Join-Path $outDir 'padlock-256.png'), $frames[256])
Write-Host ("padlock.ico {0} bytes; padlock-256.png {1} bytes" -f (Get-Item (Join-Path $outDir 'padlock.ico')).Length, $frames[256].Length)
