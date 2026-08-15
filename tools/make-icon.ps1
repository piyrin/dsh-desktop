$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$brandingRoot = Join-Path $repositoryRoot 'assets\branding'
$null = New-Item -ItemType Directory -Force -Path $brandingRoot

# ---- 1. Extract the whale path data from favicon.svg ----
$svgPath = Join-Path $repositoryRoot 'desktop\assets\favicon.svg'
$svg = Get-Content -Raw $svgPath
$m = [regex]::Match($svg, '<path\b[^>]*\bd="([^"]+)"')
if (-not $m.Success) { throw 'path data not found in favicon.svg' }
$d = $m.Groups[1].Value
$tokens = [regex]::Matches($d, '[A-Za-z]|-?[0-9.]+') | ForEach-Object { $_.Value }

# ---- 2. Build a GraphicsPath from the SVG path (M/L/C/Z) ----
$gp = New-Object System.Drawing.Drawing2D.GraphicsPath
$curr = [System.Drawing.PointF]::new(0, 0)
$cmd = ''
$i = 0
$unsupported = @{}
while ($i -lt $tokens.Count) {
    $t = $tokens[$i]
    if ($t -match '^[A-Za-z]$') {
        if ($t -eq 'Z') { $gp.CloseFigure() } else { $cmd = $t }
        $i++
        continue
    }
    $x = [double]$t
    switch ($cmd) {
        'M' {
            $y = [double]$tokens[$i + 1]
            $curr = [System.Drawing.PointF]::new([float]$x, [float]$y)
            $gp.StartFigure()
            $cmd = 'L'
            $i += 2
        }
        'L' {
            $y = [double]$tokens[$i + 1]
            $end = [System.Drawing.PointF]::new([float]$x, [float]$y)
            $gp.AddLine($curr, $end)
            $curr = $end
            $i += 2
        }
        'C' {
            $x1 = $x; $y1 = [double]$tokens[$i + 1]
            $x2 = [double]$tokens[$i + 2]; $y2 = [double]$tokens[$i + 3]
            $xe = [double]$tokens[$i + 4]; $ye = [double]$tokens[$i + 5]
            $p1 = [System.Drawing.PointF]::new([float]$x1, [float]$y1)
            $p2 = [System.Drawing.PointF]::new([float]$x2, [float]$y2)
            $pe = [System.Drawing.PointF]::new([float]$xe, [float]$ye)
            $gp.AddBezier($curr, $p1, $p2, $pe)
            $curr = $pe
            $i += 6
        }
        default {
            if (-not $unsupported.ContainsKey($cmd)) { $unsupported[$cmd] = $true; Write-Warning "unsupported SVG command: $cmd" }
            $i++
        }
    }
}

# ---- 3. Scale the 50x50 viewBox whale to 200px, centered in a 256px tile ----
$mx = New-Object System.Drawing.Drawing2D.Matrix
$mx.Translate(128, 128)
$mx.Scale(4, 4)
$mx.Translate(-25, -25)
$gp.Transform($mx)

$tile = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 52
$tile.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90)
$tile.AddArc(256 - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
$tile.AddArc(256 - 2 * $r, 256 - 2 * $r, 2 * $r, 2 * $r, 0, 90)
$tile.AddArc(0, 256 - 2 * $r, 2 * $r, 2 * $r, 90, 90)
$tile.CloseFigure()

# ---- 4. Render: white rounded tile + black whale on transparent background ----
$bmp = New-Object System.Drawing.Bitmap(256, 256, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$g.FillPath([System.Drawing.Brushes]::White, $tile)
$g.FillPath([System.Drawing.Brushes]::Black, $gp)
$g.Dispose()
$pngPath = Join-Path $brandingRoot 'dsh-whale.png'
$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# ---- 5. Wrap the PNG into a 256x256 .ico (PNG-in-ICO, Vista+) ----
$png = [System.IO.File]::ReadAllBytes($pngPath)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
$bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
$bw.Write($png)
$icoPath = Join-Path $brandingRoot 'dsh-whale.ico'
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())

# ---- 6. Sanity checks ----
$b = [System.IO.File]::ReadAllBytes($pngPath)
$w = ($b[16] * 16777216) + ($b[17] * 65536) + ($b[18] * 256) + $b[19]
$h = ($b[20] * 16777216) + ($b[21] * 65536) + ($b[22] * 256) + $b[23]
Write-Host "PNG: ${w}x${h}, colorType=$($b[25]) (6=RGBA)"

$fs = [System.IO.File]::OpenRead($pngPath)
$img = [System.Drawing.Image]::FromStream($fs)
$fs.Close()
$img.Dispose()
Write-Host "ICO: $((Get-Item -LiteralPath $icoPath).Length) bytes"

# cleanup helpers
Remove-Item (Join-Path $repositoryRoot 'icon-render.html') -ErrorAction SilentlyContinue
Remove-Item (Join-Path $repositoryRoot '.edge-headless') -Recurse -Force -ErrorAction SilentlyContinue
Write-Host 'done'
