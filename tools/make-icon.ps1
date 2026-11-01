# Génère src\split.ico (deux moitiés d'écran, bleu et orange) en plusieurs tailles.
Add-Type -AssemblyName System.Drawing

function New-RoundedRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Max(0.5, $r * 2)
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

$images = @()
foreach ($s in 256, 64, 48, 32, 24, 16) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 32.0
    $left = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(47, 128, 237))
    $right = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(242, 153, 74))
    $g.FillPath($left, (New-RoundedRect (1 * $u) (7 * $u) (14 * $u) (18 * $u) (2 * $u)))
    $g.FillPath($right, (New-RoundedRect (17 * $u) (7 * $u) (14 * $u) (18 * $u) (2 * $u)))
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += , @($s, $ms.ToArray())
    $left.Dispose(); $right.Dispose(); $g.Dispose(); $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($im in $images) {
    [byte[]]$data = $im[1]
    $w.Write([byte]($im[0] % 256)); $w.Write([byte]($im[0] % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($im in $images) { [byte[]]$data = $im[1]; $w.Write($data) }
$w.Flush()

$target = Join-Path (Split-Path $PSScriptRoot -Parent) "src\split.ico"
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
"Icône écrite : $target ($($out.Length) octets)"
