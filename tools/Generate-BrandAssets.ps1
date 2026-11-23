# Run from any directory with Windows PowerShell. Uses only the built-in drawing library.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetRoot = Join-Path $PSScriptRoot '../src/AppTunnel.Windows'
$ink = [System.Drawing.ColorTranslator]::FromHtml('#101827')
$gold = [System.Drawing.ColorTranslator]::FromHtml('#f6ca47')

function New-BrandBitmap([int]$Size, [System.Drawing.Color]$Color, [bool]$Tile) {
    # Supersample each icon separately so the small tray sizes keep smooth, thick strokes.
    $bitmap = New-Object System.Drawing.Bitmap ($Size * 4), ($Size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($Size * 4 / 24, $Size * 4 / 24)
        if ($Tile) {
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $brush = New-Object System.Drawing.SolidBrush $ink
            try {
                $path.AddArc(0.5, 0.5, 16, 16, 180, 90)
                $path.AddArc(7.5, 0.5, 16, 16, 270, 90)
                $path.AddArc(7.5, 7.5, 16, 16, 0, 90)
                $path.AddArc(0.5, 7.5, 16, 16, 90, 90)
                $path.CloseFigure()
                $graphics.FillPath($brush, $path)
            }
            finally { $brush.Dispose(); $path.Dispose() }
            # Leave a small margin around the mark inside the rounded tile.
            $graphics.TranslateTransform(2, 2)
            $graphics.ScaleTransform(20 / 24, 20 / 24)
        }
        $pen = New-Object System.Drawing.Pen $Color, 3.8
        try {
            $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            # Same 24-unit geometry as Glyph.Intersect in DesktopTheme.cs.
            $graphics.DrawLine($pen, 3, 12, 21, 12)
            $graphics.DrawLine($pen, 8, 20, 16, 4)
        }
        finally { $pen.Dispose() }
    }
    finally { $graphics.Dispose() }

    $result = New-Object System.Drawing.Bitmap $Size, $Size
    $graphics = [System.Drawing.Graphics]::FromImage($result)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($bitmap, 0, 0, $Size, $Size)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
    return $result
}

function Write-BrandIcon([string]$Path, [System.Drawing.Color]$Color, [bool]$Tile) {
    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = foreach ($size in $sizes) {
        $bitmap = New-BrandBitmap $size $Color $Tile
        $stream = New-Object System.IO.MemoryStream
        try {
            if ($size -eq 256) {
                $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            else {
                # Small Windows icons use a bottom-up BGRA DIB plus the legacy transparency mask.
                $frameWriter = New-Object System.IO.BinaryWriter $stream
                $pixels = $bitmap.LockBits([System.Drawing.Rectangle]::new(0, 0, $size, $size),
                    [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try {
                    $maskLength = [int]([Math]::Ceiling($size / 32) * 4 * $size)
                    $frameWriter.Write([uint32]40)
                    $frameWriter.Write([int]$size)
                    $frameWriter.Write([int]($size * 2))
                    $frameWriter.Write([uint16]1)
                    $frameWriter.Write([uint16]32)
                    $frameWriter.Write([uint32]0)
                    $frameWriter.Write([uint32]($size * $size * 4))
                    $frameWriter.Write([byte[]]::new(16))
                    $row = [byte[]]::new($size * 4)
                    for ($y = $size - 1; $y -ge 0; $y--) {
                        [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($pixels.Scan0, $y * $pixels.Stride), $row, 0, $row.Length)
                        $frameWriter.Write($row)
                    }
                    $frameWriter.Write([byte[]]::new($maskLength))
                    $frameWriter.Flush()
                }
                finally { $bitmap.UnlockBits($pixels) }
            }
            ,$stream.ToArray()
        }
        finally { $stream.Dispose(); $bitmap.Dispose() }
    }
    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter $stream
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dimension = [byte]($sizes[$i] % 256)
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$i].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$i].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose(); $stream.Dispose() }
}

$logo = New-BrandBitmap 512 $gold $true
try { $logo.Save((Join-Path $assetRoot 'Assets/logo.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
finally { $logo.Dispose() }
Write-BrandIcon (Join-Path $assetRoot 'AppTunnel.ico') $gold $true
Write-BrandIcon (Join-Path $assetRoot 'Assets/Tray/tray-dark.ico') $gold $false
Write-BrandIcon (Join-Path $assetRoot 'Assets/Tray/tray-light.ico') $ink $false
