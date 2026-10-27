param(
    [Parameter(Mandatory = $true)][string]$In,
    [Parameter(Mandatory = $true)][string]$Out
)
# Draws frames, numbered badges and arrows on the guide screenshots captured by GuideCapture.cs.
# Input : <In>/<name>.png + <name>.json (marks in pixels)
# Output: <Out>/<name>.png (and <name>-plain.png when requested), scaled to 1 pixel per point.

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$accent = [Drawing.Color]::FromArgb(255, 255, 70, 90)
$shadow = [Drawing.Color]::FromArgb(110, 0, 0, 0)
$white = [Drawing.Color]::White

function New-RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Get-EdgePoint($rect, [float]$dx, [float]$dy) {
    # point on the rectangle edge seen from its center in direction (dx, dy)
    $cx = $rect.X + $rect.Width / 2; $cy = $rect.Y + $rect.Height / 2
    $tx = if ([Math]::Abs($dx) -gt 1e-4) { ($rect.Width / 2) / [Math]::Abs($dx) } else { 1e9 }
    $ty = if ([Math]::Abs($dy) -gt 1e-4) { ($rect.Height / 2) / [Math]::Abs($dy) } else { 1e9 }
    $t = [Math]::Min($tx, $ty)
    return @(($cx + $dx * $t), ($cy + $dy * $t))
}

function Test-Inside($rects, [float]$x, [float]$y, [float]$pad) {
    foreach ($r in $rects) {
        if ($x -gt $r.X - $pad -and $x -lt $r.Right + $pad -and $y -gt $r.Y - $pad -and $y -lt $r.Bottom + $pad) { return $true }
    }
    return $false
}

function Draw-Badge($g, [float]$cx, [float]$cy, [int]$n, [float]$s) {
    $rad = 14 * $s
    $sb = New-Object Drawing.SolidBrush($shadow)
    $g.FillEllipse($sb, $cx - $rad - 1, $cy - $rad + 2, $rad * 2 + 2, $rad * 2 + 2)
    $b = New-Object Drawing.SolidBrush($accent)
    $g.FillEllipse($b, $cx - $rad, $cy - $rad, $rad * 2, $rad * 2)
    $pen = New-Object Drawing.Pen($white, 2.5 * $s)
    $g.DrawEllipse($pen, $cx - $rad, $cy - $rad, $rad * 2, $rad * 2)
    $font = New-Object Drawing.Font('Segoe UI', (13 * $s), [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object Drawing.StringFormat
    $fmt.Alignment = [Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [Drawing.StringAlignment]::Center
    $g.DrawString([string]$n, $font, (New-Object Drawing.SolidBrush($white)), (New-Object Drawing.RectangleF(($cx - $rad), ($cy - $rad + 0.5), ($rad * 2), ($rad * 2))), $fmt)
}

$done = @()
foreach ($jsonFile in Get-ChildItem -Path $In -Filter '*.json') {
    $spec = Get-Content -Raw -Encoding UTF8 $jsonFile.FullName | ConvertFrom-Json
    $src = Join-Path $In ($spec.name + '.png')
    if (-not (Test-Path $src)) { continue }
    $raw = [Drawing.Image]::FromFile($src)
    $ppp = [float]$spec.ppp; if ($ppp -le 0) { $ppp = 1 }
    $W = [int][Math]::Round($raw.Width / $ppp); $H = [int][Math]::Round($raw.Height / $ppp)
    $bmp = New-Object Drawing.Bitmap($W, $H, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($raw, 0, 0, $W, $H)
    $raw.Dispose()
    if ($spec.plain) { $bmp.Save((Join-Path $Out ($spec.name + '-plain.png')), [Drawing.Imaging.ImageFormat]::Png) }

    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $s = 1.0
    $pad = 4 * $s

    # frames
    $rects = @()
    foreach ($m in $spec.marks) {
        $r = New-Object Drawing.RectangleF(([float]$m.x / $ppp - $pad), ([float]$m.y / $ppp - $pad), ([float]$m.w / $ppp + $pad * 2), ([float]$m.h / $ppp + $pad * 2))
        # keep the frame inside the image
        $x0 = [Math]::Max(2, $r.X); $y0 = [Math]::Max(2, $r.Y)
        $x1 = [Math]::Min($W - 2, $r.Right); $y1 = [Math]::Min($H - 2, $r.Bottom)
        if ($x1 - $x0 -lt 6 -or $y1 - $y0 -lt 6) { $rects += $null; continue }
        $r = New-Object Drawing.RectangleF($x0, $y0, ($x1 - $x0), ($y1 - $y0))
        $rects += $r
        $path = New-RoundRect $r.X $r.Y $r.Width $r.Height (7 * $s)
        $g.DrawPath((New-Object Drawing.Pen($shadow, 6 * $s)), $path)
        $g.DrawPath((New-Object Drawing.Pen($accent, 3 * $s)), $path)
    }

    # arrows and badges
    $badges = @()
    for ($i = 0; $i -lt $spec.marks.Count; $i++) {
        $m = $spec.marks[$i]; $r = $rects[$i]
        if ($null -eq $r) { continue }
        $cx = $r.X + $r.Width / 2; $cy = $r.Y + $r.Height / 2
        if ($m.arrow) {
            # prefer a vertical arrow (from below for the upper half, from above for the lower half),
            # then sweep around until the tail lands on a free spot
            $base = if ($cy -lt $H * 0.5) { [Math]::PI / 2 } else { - [Math]::PI / 2 }
            $len = 64 * $s
            $chosen = $null
            foreach ($deg in 0, 25, -25, 50, -50, 75, -75, 100, -100, 125, -125, 150, -150, 180) {
                $a = $base + $deg * [Math]::PI / 180
                $dx = [Math]::Cos($a); $dy = [Math]::Sin($a)
                $edge = Get-EdgePoint $r $dx $dy
                $tx = $edge[0] + $dx * $len; $ty = $edge[1] + $dy * $len
                $margin = 20 * $s
                if ($tx -lt $margin -or $ty -lt $margin -or $tx -gt $W - $margin -or $ty -gt $H - $margin) { continue }
                $others = @(); for ($k = 0; $k -lt $rects.Count; $k++) { if ($k -ne $i -and $null -ne $rects[$k]) { $others += $rects[$k] } }
                if (Test-Inside $others $tx $ty (16 * $s)) { continue }
                $clash = $false; foreach ($bp in $badges) { if ([Math]::Abs($bp[0] - $tx) -lt 34 * $s -and [Math]::Abs($bp[1] - $ty) -lt 34 * $s) { $clash = $true } }
                if ($clash) { continue }
                $chosen = @($dx, $dy, $edge[0], $edge[1], $tx, $ty); break
            }
            if ($chosen) {
                $dx = $chosen[0]; $dy = $chosen[1]
                $hx = $chosen[2] + $dx * 5 * $s; $hy = $chosen[3] + $dy * 5 * $s
                $tx = $chosen[4]; $ty = $chosen[5]
                $sp = New-Object Drawing.Pen($shadow, 8 * $s); $sp.StartCap = 'Round'; $sp.EndCap = 'Round'
                $g.DrawLine($sp, [float]$tx, [float]$ty, [float]($hx + $dx * 10 * $s), [float]($hy + $dy * 10 * $s))
                $lp = New-Object Drawing.Pen($accent, 4 * $s); $lp.StartCap = 'Round'; $lp.EndCap = 'Round'
                $g.DrawLine($lp, [float]$tx, [float]$ty, [float]($hx + $dx * 10 * $s), [float]($hy + $dy * 10 * $s))
                # arrow head pointing at the frame
                $hl = 16 * $s; $hw = 9 * $s
                $px = -$dy; $py = $dx
                $pts = [Drawing.PointF[]]@(
                    (New-Object Drawing.PointF([float]$hx, [float]$hy)),
                    (New-Object Drawing.PointF([float]($hx + $dx * $hl + $px * $hw), [float]($hy + $dy * $hl + $py * $hw))),
                    (New-Object Drawing.PointF([float]($hx + $dx * $hl - $px * $hw), [float]($hy + $dy * $hl - $py * $hw)))
                )
                $g.DrawPolygon((New-Object Drawing.Pen($shadow, 3 * $s)), $pts)
                $g.FillPolygon((New-Object Drawing.SolidBrush($accent)), $pts)
                Draw-Badge $g $tx $ty $m.n $s
                $badges += , @($tx, $ty)
                continue
            }
        }
        # badge on a corner of the frame (top-left first; another corner if a badge is already there)
        $bx = 0; $by = 0
        foreach ($corner in @(@($r.X, $r.Y), @($r.Right, $r.Y), @($r.X, $r.Bottom), @($r.Right, $r.Bottom))) {
            $bx = [Math]::Min([Math]::Max($corner[0], 16 * $s), $W - 16 * $s)
            $by = [Math]::Min([Math]::Max($corner[1], 16 * $s), $H - 16 * $s)
            $clash = $false; foreach ($bp in $badges) { if ([Math]::Abs($bp[0] - $bx) -lt 32 * $s -and [Math]::Abs($bp[1] - $by) -lt 32 * $s) { $clash = $true } }
            if (-not $clash) { break }
        }
        Draw-Badge $g $bx $by $m.n $s
        $badges += , @($bx, $by)
    }

    $g.Dispose()
    $bmp.Save((Join-Path $Out ($spec.name + '.png')), [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $done += $spec.name
}
"annotated $($done.Count): $($done -join ', ')"
