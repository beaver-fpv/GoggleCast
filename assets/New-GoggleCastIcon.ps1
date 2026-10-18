param(
    [string]$Source = (Join-Path $PSScriptRoot 'gogglecast.png'),
    [string]$Icon = (Join-Path $PSScriptRoot 'gogglecast.ico'),
    [string]$Preview = (Join-Path $PSScriptRoot 'gogglecast-preview.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Format conversion only: preserve the generated artwork and alpha channel.
$sourceBitmap = [Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Source).Path)
$frameSizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = [Collections.Generic.List[byte[]]]::new()
try {
    foreach ($size in $frameSizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $attributes = [Drawing.Imaging.ImageAttributes]::new()
        $buffer = [IO.MemoryStream]::new()
        try {
            $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.Clear([Drawing.Color]::Transparent)
            $attributes.SetWrapMode([Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($sourceBitmap, [Drawing.Rectangle]::new(0, 0, $size, $size),
                0, 0, $sourceBitmap.Width, $sourceBitmap.Height, [Drawing.GraphicsUnit]::Pixel, $attributes)
            $bitmap.Save($buffer, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($buffer.ToArray())
            if ($size -eq 256) { $bitmap.Save($Preview, [Drawing.Imaging.ImageFormat]::Png) }
        }
        finally {
            $buffer.Dispose()
            $attributes.Dispose()
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    $file = [IO.File]::Create($Icon)
    $writer = [IO.BinaryWriter]::new($file)
    try {
        $writer.Write([uint16]0) # Reserved
        $writer.Write([uint16]1) # Icon
        $writer.Write([uint16]$frameSizes.Count)
        $offset = 6 + (16 * $frameSizes.Count)
        for ($index = 0; $index -lt $frameSizes.Count; $index++) {
            $size = $frameSizes[$index]
            $dimension = if ($size -eq 256) { 0 } else { $size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0) # Color count: true color
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frames[$index].Length)
            $writer.Write([uint32]$offset)
            $offset += $frames[$index].Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    }
    finally { $writer.Dispose() }
}
finally { $sourceBitmap.Dispose() }

Get-Item -LiteralPath $Icon, $Preview | Select-Object Name, Length
