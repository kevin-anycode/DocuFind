# Build a Windows .ico from a 1024x1024 app-icon PNG.
#   powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1 `
#     -In assets\icon\icon-1024.png -Ico assets\icon\app.ico -Preview assets\icon\icon-256.png
param(
  [Parameter(Mandatory = $true)][string]$In,
  [Parameter(Mandatory = $true)][string]$Ico,
  [string]$Preview = "",
  [double]$RadiusRatio = 0.225
)

Add-Type -AssemblyName System.Drawing

$source = [System.Drawing.Bitmap]::FromFile((Resolve-Path $In))

# 1) 找到图标的实际范围（生成图外面常有一圈纯黑边），裁成正方形
$minX = $source.Width; $minY = $source.Height; $maxX = -1; $maxY = -1
for ($y = 0; $y -lt $source.Height; $y += 2) {
  for ($x = 0; $x -lt $source.Width; $x += 2) {
    $c = $source.GetPixel($x, $y)
    if (($c.R + $c.G + $c.B) -gt 40) {
      if ($x -lt $minX) { $minX = $x }
      if ($x -gt $maxX) { $maxX = $x }
      if ($y -lt $minY) { $minY = $y }
      if ($y -gt $maxY) { $maxY = $y }
    }
  }
}
if ($maxX -lt 0) { throw "icon looks empty: $In" }

$w = $maxX - $minX + 1
$h = $maxY - $minY + 1
$side = [Math]::Max($w, $h)
$cx = [int](($minX + $maxX) / 2)
$cy = [int](($minY + $maxY) / 2)
$x0 = [Math]::Max(0, $cx - [int]($side / 2))
$y0 = [Math]::Max(0, $cy - [int]($side / 2))
if ($x0 + $side -gt $source.Width) { $x0 = $source.Width - $side }
if ($y0 + $side -gt $source.Height) { $y0 = $source.Height - $side }

$square = New-Object System.Drawing.Bitmap 1024, 1024
$g = [System.Drawing.Graphics]::FromImage($square)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$dest = New-Object System.Drawing.Rectangle 0, 0, 1024, 1024
$srcRect = New-Object System.Drawing.Rectangle $x0, $y0, $side, $side
$g.DrawImage($source, $dest, $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$source.Dispose()

# 2) 用圆角矩形裁掉四角（TextureBrush + FillPath 自带抗锯齿）
$radius = [int](1024 * $RadiusRatio)
$d = $radius * 2
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(0, 0, $d, $d, 180, 90)
$path.AddArc(1024 - $d - 1, 0, $d, $d, 270, 90)
$path.AddArc(1024 - $d - 1, 1024 - $d - 1, $d, $d, 0, 90)
$path.AddArc(0, 1024 - $d - 1, $d, $d, 90, 90)
$path.CloseFigure()

$masked = New-Object System.Drawing.Bitmap 1024, 1024
$mg = [System.Drawing.Graphics]::FromImage($masked)
$mg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$mg.Clear([System.Drawing.Color]::Transparent)
$texture = New-Object System.Drawing.TextureBrush $square
$mg.FillPath($texture, $path)
$mg.Dispose()
$texture.Dispose()
$square.Dispose()

# 3) 各尺寸写进 .ico（PNG 帧，Vista+ 支持）
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $bg = [System.Drawing.Graphics]::FromImage($bmp)
  $bg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $bg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $bg.Clear([System.Drawing.Color]::Transparent)
  $bg.DrawImage($masked, 0, 0, $s, $s)
  $bg.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
  $frames += , $ms.ToArray()
  $ms.Dispose()

  if ($Preview -ne "" -and $s -eq 256) { $bmp.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png) }
  $bmp.Dispose()
}

$icoPath = [System.IO.Path]::GetFullPath($Ico)
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]
  $data = $frames[$i]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $bw.Write([byte]$dim)
  $bw.Write([byte]$dim)
  $bw.Write([byte]0)
  $bw.Write([byte]0)
  $bw.Write([UInt16]1)
  $bw.Write([UInt16]32)
  $bw.Write([UInt32]$data.Length)
  $bw.Write([UInt32]$offset)
  $offset += $data.Length
}
foreach ($data in $frames) { $bw.Write($data) }
$bw.Flush()
$bw.Dispose()
$fs.Dispose()
$masked.Dispose()

Write-Output ("icon written: {0} ({1} bytes)" -f $icoPath, (Get-Item $icoPath).Length)
