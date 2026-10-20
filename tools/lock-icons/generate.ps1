# Deterministic generator for the DropWheel Lock badge icons.
# Run from any directory with Windows PowerShell 5.1:  powershell -ExecutionPolicy Bypass -File tools\lock-icons\generate.ps1
# It needs only System.Drawing (part of Windows PowerShell); no packages, no network, no timestamps.
# Outputs the committed .ico assets and the white/dark preview PNGs used as design evidence.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$outDir = Join-Path $root 'src\FileAction.App\Assets\Lock'
$shotDir = Join-Path $root 'docs\screenshots\lock-badge'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
New-Item -ItemType Directory -Force -Path $shotDir | Out-Null

$sizes = @(16, 24, 32, 48, 64, 256)
$leftViolet = [System.Drawing.Color]::FromArgb(255, 0xA0, 0x12, 0xD8)
$rightViolet = [System.Drawing.Color]::FromArgb(255, 0x6A, 0x0D, 0xA8)
$brightViolet = [System.Drawing.Color]::FromArgb(255, 0xB4, 0x18, 0xE8)
$edgeViolet = [System.Drawing.Color]::FromArgb(255, 0x42, 0x08, 0x6C)
$white = [System.Drawing.Color]::White
$paper = [System.Drawing.Color]::FromArgb(255, 0xFB, 0xFB, 0xFB)
$paperEdge = [System.Drawing.Color]::FromArgb(255, 0x9A, 0x9A, 0x9A)
$paperFold = [System.Drawing.Color]::FromArgb(255, 0xE0, 0xE0, 0xE0)
$glyph = [System.Drawing.Color]::FromArgb(255, 0x6A, 0x2B, 0xB8)
$folderTop = [System.Drawing.Color]::FromArgb(255, 0xFF, 0xD2, 0x4A)
$folderBottom = [System.Drawing.Color]::FromArgb(255, 0xF0, 0xA4, 0x00)
$folderEdge = [System.Drawing.Color]::FromArgb(255, 0xC9, 0x86, 0x12)

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

function Fill-Rounded($g, $brush, [double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-RoundedPath $x $y $w $h $r
    $g.FillPath($brush, $p)
    $p.Dispose()
}

function Draw-Base($g, [int]$s, [string]$kind) {
    $u = $s / 100.0
    if ($kind -eq 'folder') {
        # The normal yellow Windows folder, with a light top edge and a subtle vertical gradient.
        $tab = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new([single](8*$u), [single](17*$u), [single](38*$u), [single](20*$u)),
            $folderTop, $folderBottom, [single]90)
        Fill-Rounded $g $tab (8*$u) (17*$u) (38*$u) (20*$u) (4*$u)
        $tab.Dispose()

        $bodyRect = [System.Drawing.RectangleF]::new([single](5*$u), [single](28*$u), [single](66*$u), [single](47*$u))
        $body = [System.Drawing.Drawing2D.LinearGradientBrush]::new($bodyRect, $folderTop, $folderBottom, [single]90)
        Fill-Rounded $g $body (5*$u) (28*$u) (66*$u) (47*$u) (5*$u)
        $body.Dispose()

        $edge = New-Object System.Drawing.Pen($folderEdge, [single][Math]::Max(1, 1.2*$u))
        $p = New-RoundedPath (5*$u) (28*$u) (66*$u) (47*$u) (5*$u)
        $g.DrawPath($edge, $p)
        $p.Dispose()
        $edge.Dispose()
        return
    }

    # Page with a folded top-right corner.
    $pageBrush = New-Object System.Drawing.SolidBrush($paper)
    Fill-Rounded $g $pageBrush (17*$u) (8*$u) (54*$u) (72*$u) (4*$u)
    $pagePen = New-Object System.Drawing.Pen($paperEdge, [single][Math]::Max(1, 1.1*$u))
    $p = New-RoundedPath (17*$u) (8*$u) (54*$u) (72*$u) (4*$u)
    $g.DrawPath($pagePen, $p)
    $p.Dispose()
    $fold = New-Object System.Drawing.Drawing2D.GraphicsPath
    $fold.AddPolygon(@(
        (New-Object System.Drawing.PointF([single](59*$u), [single](8*$u))),
        (New-Object System.Drawing.PointF([single](71*$u), [single](8*$u))),
        (New-Object System.Drawing.PointF([single](71*$u), [single](20*$u)))
    ))
    $g.FillPath((New-Object System.Drawing.SolidBrush($paperFold)), $fold)
    $g.DrawPath((New-Object System.Drawing.Pen($paperEdge, [single][Math]::Max(1, 0.9*$u))), $fold)
    $fold.Dispose()

    switch ($kind) {
        'image' {
            $g.FillEllipse((New-Object System.Drawing.SolidBrush($glyph)), [single](24*$u), [single](31*$u), [single](8*$u), [single](8*$u))
            $mount = New-Object System.Drawing.Drawing2D.GraphicsPath
            $mount.AddPolygon(@(
                (New-Object System.Drawing.PointF([single](22*$u), [single](62*$u))),
                (New-Object System.Drawing.PointF([single](37*$u), [single](44*$u))),
                (New-Object System.Drawing.PointF([single](49*$u), [single](58*$u))),
                (New-Object System.Drawing.PointF([single](56*$u), [single](51*$u))),
                (New-Object System.Drawing.PointF([single](66*$u), [single](62*$u)))
            ))
            $g.FillPath((New-Object System.Drawing.SolidBrush($glyph)), $mount)
            $mount.Dispose()
        }
        'video' {
            $play = New-Object System.Drawing.Drawing2D.GraphicsPath
            $play.AddPolygon(@(
                (New-Object System.Drawing.PointF([single](34*$u), [single](30*$u))),
                (New-Object System.Drawing.PointF([single](60*$u), [single](45*$u))),
                (New-Object System.Drawing.PointF([single](34*$u), [single](60*$u)))
            ))
            $g.FillPath((New-Object System.Drawing.SolidBrush($glyph)), $play)
            $play.Dispose()
        }
        'archive' {
            $band = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xB4, 0x8A, 0xE0))
            $g.FillRectangle($band, [single](24*$u), [single](22*$u), [single](38*$u), [single](6*$u))
            $g.FillRectangle($band, [single](24*$u), [single](63*$u), [single](38*$u), [single](6*$u))
            $zip = New-Object System.Drawing.SolidBrush($glyph)
            for ($i = 0; $i -lt 5; $i++) {
                $g.FillRectangle($zip, [single]((41 + ($i % 2) * 5) * $u), [single]((31 + $i * 6) * $u), [single](5*$u), [single](4*$u))
            }
        }
        'other' {
            $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xD8, 0xD8, 0xD8))),
                [single](27*$u), [single](36*$u), [single](34*$u), [single](3*$u))
            $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xD8, 0xD8, 0xD8))),
                [single](27*$u), [single](47*$u), [single](34*$u), [single](3*$u))
            $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0xD8, 0xD8, 0xD8))),
                [single](27*$u), [single](58*$u), [single](22*$u), [single](3*$u))
        }
        default {
            # A document: blank page, like the owner mockup.
        }
    }
    $pagePen.Dispose()
    $pageBrush.Dispose()
}

function Draw-Badge($g, [int]$s, [switch]$OnlyBadge) {
    $u = $s / 100.0
    if ($OnlyBadge) {
        # A badge-only icon: transparent, with the lock filling most of the frame.
        $cx = 50 * $u
        $bodyW = 62 * $u
        $bodyH = 54 * $u
        $bodyX = $cx - $bodyW / 2
        $bodyY = 40 * $u
        $archW = 40 * $u
        $archH = 40 * $u
        $archX = $cx - $archW / 2
        $archY = $bodyY - $archH * 0.52
        $archPen = [Math]::Max(2.2, 13 * $u)
        $keyR = 7 * $u
    } else {
        # Bottom-left overlay, ~44% of the icon height, slightly overlapping the base edge.
        $bodyW = 32 * $u
        $bodyH = 34 * $u
        $bodyX = -1 * $u
        $bodyY = 62 * $u
        $archW = 23 * $u
        $archH = 22 * $u
        $archX = $bodyX + ($bodyW - $archW) / 2
        $archY = $bodyY - $archH * 0.55
        $archPen = [Math]::Max(1.5, 7.0 * $u)
        $keyR = 3.4 * $u
    }

    # Thick rounded shackle, bright violet on the left shading to darker on the right.
    $archRect = [System.Drawing.RectangleF]::new([single]$archX, [single]$archY, [single]$archW, [single]$archH)
    $archBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new($archRect, $brightViolet, $rightViolet, [single]0)
    $arch = New-Object System.Drawing.Pen($archBrush, [single]$archPen)
    $arch.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $arch.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($arch, [single]$archX, [single]$archY, [single]$archW, [single]$archH, 180, 180)
    $arch.Dispose()
    $archBrush.Dispose()

    # Two-tone body: left half brighter, right half darker, split exactly down the middle.
    $bodyPath = New-RoundedPath $bodyX $bodyY $bodyW $bodyH (5 * $u)
    $g.SetClip($bodyPath)
    $leftRect = [System.Drawing.RectangleF]::new([single]$bodyX, [single]$bodyY, [single]$bodyW, [single]$bodyH)
    $left = [System.Drawing.Drawing2D.LinearGradientBrush]::new($leftRect, $leftViolet, $brightViolet, [single]90)
    $right = [System.Drawing.Drawing2D.LinearGradientBrush]::new($leftRect, $rightViolet, [System.Drawing.Color]::FromArgb(255, 0x80, 0x10, 0xC0), [single]90)
    $g.FillRectangle($left, [single]$bodyX, [single]$bodyY, [single]($bodyW / 2), [single]$bodyH)
    $g.FillRectangle($right, [single]($bodyX + $bodyW / 2), [single]$bodyY, [single]($bodyW / 2), [single]$bodyH)
    $g.ResetClip()
    $left.Dispose()
    $right.Dispose()

    # A thin dark rim keeps the badge readable on the light Explorer background; the bright fills
    # keep it readable on the dark one.
    $rim = New-Object System.Drawing.Pen($edgeViolet, [single][Math]::Max(0.8, 1.0 * $u))
    $g.DrawPath($rim, $bodyPath)
    $rim.Dispose()
    $bodyPath.Dispose()

    # White keyhole: circle plus stem, centred in the body.
    $kcx = $bodyX + $bodyW / 2
    $keyY = $bodyY + $bodyH * 0.30
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($white)),
        [single]($kcx - $keyR), [single]$keyY, [single]($keyR * 2), [single]($keyR * 2))
    $g.FillRectangle((New-Object System.Drawing.SolidBrush($white)),
        [single]($kcx - $keyR * 0.30), [single]($keyY + $keyR * 1.45), [single]($keyR * 0.60), [single]($keyR * 1.55))
}

function New-Frames([string]$kind, [switch]$OnlyBadge) {
    $frames = @{}
    foreach ($s in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        if (-not $OnlyBadge) { Draw-Base $g $s $kind }
        Draw-Badge $g $s -OnlyBadge:$OnlyBadge
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames[$s] = $ms.ToArray()
        $ms.Dispose()
        $g.Dispose()
        $bmp.Dispose()
    }
    return $frames
}

function Write-Ico($frames, [string]$path) {
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
    foreach ($s in $sizes) { $w.Write($frames[$s]) }
    $w.Flush()
    [IO.File]::WriteAllBytes($path, $stream.ToArray())
    $w.Dispose()
    $stream.Dispose()
}

$all = @{}
foreach ($kind in @('document', 'image', 'video', 'archive', 'other', 'folder')) {
    $frames = New-Frames $kind
    $all[$kind] = $frames
    $path = Join-Path $outDir ("locked-" + $kind + ".ico")
    Write-Ico $frames $path
    Write-Host "$path $((Get-Item $path).Length) bytes"
}
$badgeFrames = New-Frames 'other' -OnlyBadge
$all['badge'] = $badgeFrames
$badgePath = Join-Path $outDir 'locked-badge.ico'
Write-Ico $badgeFrames $badgePath
Write-Host "$badgePath $((Get-Item $badgePath).Length) bytes"

function Write-Preview([string]$kind, [int]$size, [System.Drawing.Color]$background, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear($background)
    $stream = [System.IO.MemoryStream]::new([byte[]]$all[$kind][$size])
    $icon = [System.Drawing.Image]::FromStream($stream)
    $g.DrawImage($icon, 0, 0, $size, $size)
    $icon.Dispose()
    $g.Dispose()
    $bmp.Save((Join-Path $shotDir $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $stream.Dispose()
}

$whiteBg = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$darkBg = [System.Drawing.Color]::FromArgb(255, 0x1F, 0x1F, 0x1F)
foreach ($kind in @('document', 'folder')) {
    foreach ($size in @(48, 256)) {
        Write-Preview $kind $size $whiteBg ("{0}-white-{1}.png" -f $kind, $size)
        Write-Preview $kind $size $darkBg ("{0}-dark-{1}.png" -f $kind, $size)
    }
}
$total = (Get-ChildItem $outDir -Filter *.ico | Measure-Object Length -Sum).Sum
Write-Host "TOTAL $total bytes"
