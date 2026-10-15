# Regenerates every Pharma Remit icon and logo from the 2048x2048 master artwork.
# Usage: .\tools\branding\make-brand-assets.ps1 -Source PharmaBill.App\Resources\logo_3d.png -App (Resolve-Path PharmaBill.App)
param([Parameter(Mandatory)] [string]$Source, [Parameter(Mandatory)] [string]$App, [string]$Preview)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Image]::FromFile($Source)

# Crop boxes in the 2048x2048 artwork: the inner panel (full logo with wordmark) and the emblem only.
$Full = @{ X = 250; Y = 216; W = 1548 }
$Mark = @{ X = 500; Y = 360; W = 1040 }

function New-RoundedPath([int]$size, [int]$r) {
	$p = New-Object System.Drawing.Drawing2D.GraphicsPath
	$d = 2 * $r
	$p.AddArc(0, 0, $d, $d, 180, 90); $p.AddArc($size - $d - 1, 0, $d, $d, 270, 90)
	$p.AddArc($size - $d - 1, $size - $d - 1, $d, $d, 0, 90); $p.AddArc(0, $size - $d - 1, $d, $d, 90, 90)
	$p.CloseFigure(); return $p
}

# Square tile with transparent rounded corners.
function New-Tile($box, [int]$size, [double]$radius) {
	$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.Clear([System.Drawing.Color]::Transparent)
	$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
	if ($radius -gt 0) { $g.SetClip((New-RoundedPath $size ([int][Math]::Max(1, $size * $radius)))) }
	$g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), (New-Object System.Drawing.Rectangle $box.X, $box.Y, $box.W, $box.W), 'Pixel')
	$g.Dispose(); return $bmp
}

# Tile centred on a solid canvas (Store wide tile, splash screen).
function New-Canvas([int]$w, [int]$h, $box, [int]$logo, [double]$radius, [string]$bg) {
	$bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
	$g = [System.Drawing.Graphics]::FromImage($bmp)
	$g.Clear([System.Drawing.ColorTranslator]::FromHtml($bg))
	$tile = New-Tile $box $logo $radius
	$g.DrawImage($tile, [int](($w - $logo) / 2), [int](($h - $logo) / 2), $logo, $logo)
	$tile.Dispose(); $g.Dispose(); return $bmp
}

function Save-Png($bmp, [string]$path) {
	New-Item -ItemType Directory -Force (Split-Path $path) | Out-Null
	$bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
	Write-Output ("{0,-60} {1,8} bytes" -f $path.Replace($App + '\', ''), (Get-Item $path).Length)
}

function Get-PngBytes($bmp) { $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b = $ms.ToArray(); $ms.Dispose(); return ,$b }

# 32-bit DIB entry (BITMAPINFOHEADER + bottom-up BGRA + AND mask) for sizes below 256; PNG for 256.
function Get-DibBytes($bmp) {
	$s = $bmp.Width
	$ms = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ms
	$maskStride = [int]([Math]::Ceiling($s / 32.0) * 4)
	$w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
	$w.Write([int]0); $w.Write([int]($s * $s * 4 + $maskStride * $s)); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
	for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A) } }
	for ($y = $s - 1; $y -ge 0; $y--) {
		$row = New-Object byte[] $maskStride
		for ($x = 0; $x -lt $s; $x++) { if ($bmp.GetPixel($x, $y).A -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
		$w.Write($row)
	}
	$w.Flush(); $b = $ms.ToArray(); $w.Dispose(); return ,$b
}

function Save-Ico([string]$path) {
	# Small sizes use the emblem (the wordmark is unreadable below ~96px); 128 and 256 show the full logo.
	$frames = @(16, 20, 24, 32, 40, 48, 64, 128, 256) | ForEach-Object {
		$box = if ($_ -le 64) { $Mark } else { $Full }
		$radius = if ($_ -le 64) { 0.16 } else { 0.11 }
		$bmp = New-Tile $box $_ $radius
		$data = if ($_ -eq 256) { Get-PngBytes $bmp } else { Get-DibBytes $bmp }
		$bmp.Dispose()
		[pscustomobject]@{ Size = $_; Data = $data }
	}
	$ms = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $ms
	$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$frames.Count)
	$offset = 6 + 16 * $frames.Count
	foreach ($f in $frames) {
		$dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
		$w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
		$w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$f.Data.Length); $w.Write([int]$offset)
		$offset += $f.Data.Length
	}
	foreach ($f in $frames) { $w.Write($f.Data) }
	$w.Flush(); [System.IO.File]::WriteAllBytes($path, $ms.ToArray()); $w.Dispose()
	Write-Output ("{0,-60} {1,8} bytes" -f $path.Replace($App + '\', ''), (Get-Item $path).Length)
}

$bg = '#0F172A'   # matches the manifest SplashScreen BackgroundColor and the logo's darkest panel tone

# Application / installer / window icons (same file at every path the project and installer reference).
foreach ($ico in 'app.ico', 'Assets\Icons\app.ico', 'Assets\Icons\pharma_bill_3d.ico', 'Assets\PharmaBill.ico') { Save-Ico (Join-Path $App $ico) }

# In-app brand images.
Save-Png (New-Tile $Mark 384 0.16) (Join-Path $App 'Resources\brand_mark.png')
Save-Png (New-Tile $Full 512 0.11) (Join-Path $App 'Resources\logo_final_3d.png')
Save-Png (New-Tile $Full 256 0.11) (Join-Path $App 'Assets\Icons\app_icon_256.png')
Save-Png (New-Tile $Full 256 0.11) (Join-Path $App 'Assets\Icons\pharma_bill_3d.png')
Save-Png (New-Tile $Full 512 0.11) (Join-Path $App 'Assets\Icons\logo_watermark.png')
Save-Png (New-Tile $Full 512 0.11) (Join-Path $App 'Assets\Icons\pharma_bill_3d_watermark.png')
$master = Join-Path $App 'Resources\logo_3d.png'   # master artwork, not shipped
if ((Resolve-Path $Source).Path -ne [IO.Path]::GetFullPath($master)) { Copy-Item $Source $master -Force }
Write-Output ("{0,-60} {1,8} bytes" -f 'Resources\logo_3d.png (master artwork)', (Get-Item (Join-Path $App 'Resources\logo_3d.png')).Length)

# Microsoft Store package images.
Save-Png (New-Tile $Mark 44 0.16) (Join-Path $App 'Assets\Store\Square44x44Logo.png')
Save-Png (New-Tile $Mark 50 0.16) (Join-Path $App 'Assets\Store\StoreLogo.png')
Save-Png (New-Tile $Full 150 0.11) (Join-Path $App 'Assets\Store\Square150x150Logo.png')
Save-Png (New-Canvas 310 150 $Full 150 0.11 $bg) (Join-Path $App 'Assets\Store\Wide310x150Logo.png')
# Store package images must stay under 200 KB; the photographic logo is shrunk on the splash until it fits.
$splash = Join-Path $App 'Assets\Store\SplashScreen.png'
foreach ($logo in 420, 380, 340, 300, 270, 240) {
	$bmp = New-Canvas 620 620 $Full $logo 0.11 $bg
	$bmp.Save($splash, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
	if ((Get-Item $splash).Length -lt 195KB) { break }
}
Write-Output ("{0,-60} {1,8} bytes (logo {2}px)" -f 'Assets\Store\SplashScreen.png', (Get-Item $splash).Length, $logo)

if ($Preview) {
	# Contact sheet of the small icon sizes, enlarged 4x without smoothing, for checking legibility.
	$sheet = New-Object System.Drawing.Bitmap 1100, 300
	$g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(240, 242, 245)); $g.InterpolationMode = 'NearestNeighbor'; $g.PixelOffsetMode = 'Half'
	$x = 10
	foreach ($s in 16, 24, 32, 48, 64) { $t = New-Tile $Mark $s 0.16; $g.DrawImage($t, $x, 10, $s * 4, $s * 4); $t.Dispose(); $x += $s * 4 + 12 }
	$g.Dispose(); $sheet.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
}
$src.Dispose()
