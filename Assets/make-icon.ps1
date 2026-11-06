# Draws the app icon and writes a multi-size .ico. Usage: make-icon.ps1 <output.ico> [preview.png]
param([string]$Output, [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Pt($x, $y) { New-Object Drawing.PointF([single]$x, [single]$y) }

function Draw-Icon([int]$size) {
  $bmp = New-Object Drawing.Bitmap $size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'
  $g.PixelOffsetMode = 'HighQuality'
  $g.ScaleTransform($size / 256.0, $size / 256.0)

  # Rounded dark tile
  $r = 52
  $tile = New-Object Drawing.Drawing2D.GraphicsPath
  $tile.AddArc(4, 4, $r, $r, 180, 90)
  $tile.AddArc(252 - $r, 4, $r, $r, 270, 90)
  $tile.AddArc(252 - $r, 252 - $r, $r, $r, 0, 90)
  $tile.AddArc(4, 252 - $r, $r, $r, 90, 90)
  $tile.CloseFigure()
  $g.FillPath((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 24, 25, 31))), $tile)

  # Crimson sun behind the blade
  $g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 190, 38, 48))), 56, 42, 158, 158)

  # Blade, two-toned along its spine, tip at the top right
  $light = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 244, 245, 248))
  $shade = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(255, 176, 182, 196))
  $g.FillPolygon($light, [Drawing.PointF[]]@((Pt 228 28), (Pt 68 170), (Pt 86 188)))
  $g.FillPolygon($shade, [Drawing.PointF[]]@((Pt 228 28), (Pt 86 188), (Pt 100 202)))

  # Guard and grip
  $guard = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 244, 245, 248)), 20
  $guard.StartCap = 'Round'; $guard.EndCap = 'Round'
  $g.DrawLine($guard, 54, 158, 112, 216)
  $grip = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(255, 190, 38, 48)), 24
  $grip.StartCap = 'Round'; $grip.EndCap = 'Round'
  $g.DrawLine($grip, 74, 196, 42, 228)

  $g.Dispose()
  $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
  $bmp = Draw-Icon $s
  $ms = New-Object IO.MemoryStream
  $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  , $ms.ToArray()
}

$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$frames[$i].Length); $w.Write([uint32]$offset)
  $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f) }
[IO.File]::WriteAllBytes($Output, $out.ToArray())

if ($Preview) {
  $sheet = New-Object Drawing.Bitmap 720, 280
  $g = [Drawing.Graphics]::FromImage($sheet)
  $g.Clear([Drawing.Color]::FromArgb(255, 60, 62, 70))
  $x = 12
  foreach ($s in $sizes) {
    $bmp = Draw-Icon $s
    $g.DrawImageUnscaled($bmp, $x, 12)
    $x += $s + 12
    $bmp.Dispose()
  }
  $g.Dispose()
  $sheet.Save($Preview, [Drawing.Imaging.ImageFormat]::Png)
  $sheet.Dispose()
}
