# StoreHelper icin "SH" harfli ikonu (src\StoreHelper.App\app.ico) uretir.
# Excel simgesi gibi: yuvarlatilmis yesil karo, solda koyu seritli "sekme", beyaz kalin harfler.
# Cok boyutlu (16-256 px), PNG sikistirmali ICO. Kullanim: .\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $m = [Math]::Max(1, [int]($size * 0.04))
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($size - 2 * $m), ($size - 2 * $m)
    $r = $size * 0.2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Y, $r * 2, $r * 2, 270, 90)
    $path.AddArc($rect.Right - $r * 2, $rect.Bottom - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()

    # Govde: yesil gradyan
    $body = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(38, 168, 104)), ([System.Drawing.Color]::FromArgb(14, 108, 62)), 60
    $g.FillPath($body, $path)

    # Sol "sekme" seridi (Excel'in arka plani gibi): karoya kirpilmis koyu serit
    if ($size -ge 32) {
        $g.SetClip($path)
        $band = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 0, 40, 20))
        $g.FillRectangle($band, $rect.X, $rect.Y, $rect.Width * 0.2, $rect.Height)
        $g.ResetClip()
        $band.Dispose()
    }

    $ratio = if ($size -le 24) { 0.5 } elseif ($size -le 48) { 0.52 } else { 0.5 }
    $font = New-Object System.Drawing.Font 'Segoe UI', ([single]($size * $ratio)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF ($size * 0.04), ($size * 0.02), $size, $size
    $g.DrawString('SH', $font, [System.Drawing.Brushes]::White, $textRect, $fmt)

    $g.Dispose(); $body.Dispose(); $font.Dispose(); $path.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()

$target = Join-Path $PSScriptRoot 'src\StoreHelper.App\app.ico'
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Yazildi: $target ($([math]::Round((Get-Item $target).Length / 1KB)) KB)" -ForegroundColor Green
