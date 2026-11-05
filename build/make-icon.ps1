# Generates src/StandUp.App/Assets/StandUp.ico (orange circle with a white "站"),
# matching the tray icon drawn in AppController.CreateTrayIcon.
# Only needed when the icon design changes; the .ico is committed to the repo.
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\StandUp.App\Assets\StandUp.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()

foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size / 32))
    $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0xE0, 0x8A, 0x2E))
    $g.FillEllipse($fill, $pad, $pad, $size - 2 * $pad, $size - 2 * $pad)

    $font = New-Object System.Drawing.Font 'Microsoft JhengHei UI', ($size * 17.0 / 32.0), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, ($size / 32.0), $size, $size
    $g.DrawString([string][char]0x7AD9, $font, [System.Drawing.Brushes]::White, $rect, $format)

    $g.Dispose(); $fill.Dispose(); $font.Dispose()

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
