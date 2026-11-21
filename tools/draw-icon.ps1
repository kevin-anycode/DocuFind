# 画出 DocuFind 的应用图标（深蓝圆角底 + 白色文件夹 + 蓝色放大镜），输出 1024x1024 PNG。
#   powershell -ExecutionPolicy Bypass -File tools\draw-icon.ps1 -Out assets\icon\icon-1024.png
param(
  [Parameter(Mandatory = $true)][string]$Out,
  [int]$Size = 1024
)

Add-Type -AssemblyName System.Drawing

function New-RoundedPath([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

$bmp = New-Object System.Drawing.Bitmap $Size, $Size
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

$u = $Size / 1024.0

# ── 底板 ──
$tile = New-RoundedPath 0 0 $Size $Size (230 * $u)
$tileRect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $tileRect,
  ([System.Drawing.Color]::FromArgb(255, 16, 26, 49)),
  ([System.Drawing.Color]::FromArgb(255, 31, 51, 90)),
  55.0
$g.FillPath($grad, $tile)

# ── 文件夹 ──
$backBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 219, 228, 240))
$tab = New-RoundedPath (266 * $u) (268 * $u) (214 * $u) (86 * $u) (28 * $u)
$g.FillPath($backBrush, $tab)

$folderBack = New-RoundedPath (246 * $u) (312 * $u) (532 * $u) (352 * $u) (38 * $u)
$g.FillPath($backBrush, $folderBack)

$frontRect = New-Object System.Drawing.RectangleF (246 * $u), (356 * $u), (532 * $u), (330 * $u)
$front = New-RoundedPath (246 * $u) (356 * $u) (532 * $u) (330 * $u) (34 * $u)
$frontGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $frontRect,
  ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)),
  ([System.Drawing.Color]::FromArgb(255, 230, 237, 246)),
  90.0
$g.FillPath($frontGrad, $front)

# ── 放大镜 ──
$cx = 610 * $u
$cy = 560 * $u
$ringR = 152 * $u
$lensR = 112 * $u

# 手柄（先画，被镜圈压住）
$dirX = 0.7071
$dirY = 0.7071
$x1 = $cx + $dirX * ($ringR * 0.62)
$y1 = $cy + $dirY * ($ringR * 0.62)
$x2 = $cx + $dirX * ($ringR + 168 * $u)
$y2 = $cy + $dirY * ($ringR + 168 * $u)
$penRect = New-Object System.Drawing.RectangleF ($cx + $ringR * 0.4), ($cy + $ringR * 0.4), (220 * $u), (220 * $u)
$penGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $penRect,
  ([System.Drawing.Color]::FromArgb(255, 246, 250, 254)),
  ([System.Drawing.Color]::FromArgb(255, 160, 178, 198)),
  45.0
$pen = New-Object System.Drawing.Pen $penGrad, (58 * $u)
$pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
$pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLine($pen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)

# 镜圈
$ringRect = New-Object System.Drawing.RectangleF ($cx - $ringR), ($cy - $ringR), ($ringR * 2), ($ringR * 2)
$ringGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $ringRect,
  ([System.Drawing.Color]::FromArgb(255, 252, 253, 255)),
  ([System.Drawing.Color]::FromArgb(255, 197, 211, 227)),
  60.0
$g.FillEllipse($ringGrad, $ringRect)

# 镜片
$lensRect = New-Object System.Drawing.RectangleF ($cx - $lensR), ($cy - $lensR), ($lensR * 2), ($lensR * 2)
$lensPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$lensPath.AddEllipse($lensRect)
$lensBrush = New-Object System.Drawing.Drawing2D.PathGradientBrush $lensPath
$lensBrush.CenterColor = [System.Drawing.Color]::FromArgb(255, 190, 246, 255)
$lensBrush.SurroundColors = @([System.Drawing.Color]::FromArgb(255, 12, 110, 205))
$lensBrush.FocusScales = New-Object System.Drawing.PointF 0.35, 0.35
$g.FillPath($lensBrush, $lensPath)

# 镜面高光
$hiRect = New-Object System.Drawing.RectangleF ($cx - 72 * $u), ($cy - 78 * $u), (88 * $u), (56 * $u)
$hiBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(140, 255, 255, 255))
$g.FillEllipse($hiBrush, $hiRect)

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output ("drawn: {0}" -f (Resolve-Path $Out))
