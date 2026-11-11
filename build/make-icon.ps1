# Generates src/Time-to-stand-up.App/Assets/Time-to-stand-up.ico (transparent background, full-color figure)
# from build/icon-source.png; icon by juicy_fish on Flaticon: https://www.flaticon.com/free-icon/person-standing_5557271
# Used as the exe, installer and tray icon.
# Only needed when the icon design changes; the .ico is committed to the repo.
param(
    [string]$OutFile = ''
)
if (-not $OutFile) {
    $OutFile = Join-Path $PSScriptRoot '..\src\Time-to-stand-up.App\Assets\Time-to-stand-up.ico'
}

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
$src = [System.Drawing.Image]::FromFile((Resolve-Path (Join-Path $PSScriptRoot 'icon-source.png')).Path)

function Resize-Smooth([System.Drawing.Image]$img, [int]$w, [int]$h) {
    # Premultiplied-alpha bitmap + high-quality bicubic + edge-clamped wrap: no dark halos or jagged edges.
    $dst = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $gr = [System.Drawing.Graphics]::FromImage($dst)
    $gr.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $gr.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gr.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gr.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $ia = New-Object System.Drawing.Imaging.ImageAttributes
    $ia.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
    $gr.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), 0, 0, $img.Width, $img.Height, [System.Drawing.GraphicsUnit]::Pixel, $ia)
    $gr.Dispose(); $ia.Dispose()
    return $dst
}

foreach ($size in $sizes) {
    # Fit the figure into the canvas (transparent background), shrinking in halving steps.
    # Small frames (tray) fill the full height and get a white halo so they stay visible on dark taskbars.
    $small = $size -le 48
    $h = if ($small) { $size } else { [int][Math]::Round($size * 0.94) }
    $w = [int][Math]::Round($h * $src.Width / $src.Height)
    $cur = $src
    while ($cur.Height / 2 -gt $h) {
        $next = Resize-Smooth $cur ([int]($cur.Width / 2)) ([int]($cur.Height / 2))
        if ($cur -ne $src) { $cur.Dispose() }
        $cur = $next
    }
    $fig = Resize-Smooth $cur $w $h
    if ($cur -ne $src) { $cur.Dispose() }

    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
    $tint = New-Object System.Drawing.Imaging.ImageAttributes
    $destRect = New-Object System.Drawing.Rectangle ([int][Math]::Floor(($size - $w) / 2)), ([int][Math]::Floor(($size - $h) / 2)), $w, $h
    if ($small) {
        $white = New-Object System.Drawing.Imaging.ImageAttributes
        $cm = New-Object System.Drawing.Imaging.ColorMatrix
        $cm.Matrix00 = 0; $cm.Matrix11 = 0; $cm.Matrix22 = 0; $cm.Matrix33 = 1
        $cm.Matrix40 = 1; $cm.Matrix41 = 1; $cm.Matrix42 = 1
        $white.SetColorMatrix($cm)
        $r = if ($size -le 32) { 1 } else { 2 }
        foreach ($dx in -$r..$r) { foreach ($dy in -$r..$r) {
            if ($dx -eq 0 -and $dy -eq 0) { continue }
            $rc = New-Object System.Drawing.Rectangle ($destRect.X + $dx), ($destRect.Y + $dy), $w, $h
            $g.DrawImage($fig, $rc, 0, 0, $fig.Width, $fig.Height, [System.Drawing.GraphicsUnit]::Pixel, $white)
        } }
        $white.Dispose()
    }
    $g.DrawImage($fig, $destRect, 0, 0, $fig.Width, $fig.Height, [System.Drawing.GraphicsUnit]::Pixel, $tint)
    $tint.Dispose()
    $g.Dispose(); $fig.Dispose()

    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        # 256 px frame stored as PNG (standard; keeps the file small).
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Smaller frames stored as 32-bit DIB (BITMAPINFOHEADER + bottom-up BGRA + AND mask)
        # for maximum compatibility with older readers such as System.Drawing.Icon.
        $bw = New-Object System.IO.BinaryWriter $ms
        $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4 + $maskStride * $size))
        $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    $frames += , @{ Size = $size; Bytes = $ms.ToArray() }
    $ms.Dispose()
}

# ICO container: DIB entries for small sizes, PNG for 256 px.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$f.Bytes.Length); $w.Write([UInt32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes((Resolve-Path -LiteralPath (Split-Path $OutFile)).Path + '\' + (Split-Path $OutFile -Leaf), $out.ToArray())
Write-Host "Wrote $OutFile"
