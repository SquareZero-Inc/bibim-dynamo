# BIBIM Icon Resizer (PowerShell + .NET)
# Resizes icon files to multiple sizes

$whiteIcon = "Assets\Icons\bibim-logo-white-512.png"
$blueIcon = "Assets\Icons\bibim-logo-blue-512.png"
$outputDir = "Assets\Icons"

$sizes = @(16, 24, 32, 48, 64, 128, 256)

function Resize-Image {
    param(
        [string]$InputPath,
        [string]$OutputPrefix,
        [int[]]$Sizes
    )
    
    Add-Type -AssemblyName System.Drawing
    
    $sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $InputPath))
    
    foreach ($size in $Sizes) {
        $destImage = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($destImage)
        
        # High quality resize
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        
        $graphics.DrawImage($sourceImage, 0, 0, $size, $size)
        
        $outputPath = "$outputDir\$OutputPrefix-$size.png"
        $destImage.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "Created: $outputPath" -ForegroundColor Green
        
        $graphics.Dispose()
        $destImage.Dispose()
    }
    
    $sourceImage.Dispose()
}

Write-Host "Resizing white icons..." -ForegroundColor Cyan
Resize-Image -InputPath $whiteIcon -OutputPrefix "bibim-logo-white" -Sizes $sizes

Write-Host "`nResizing blue icons..." -ForegroundColor Cyan
Resize-Image -InputPath $blueIcon -OutputPrefix "bibim-logo-blue" -Sizes $sizes

Write-Host "`n✅ All PNG icons generated successfully!" -ForegroundColor Green
Write-Host "Note: ICO files need to be created manually or with a tool like ImageMagick" -ForegroundColor Yellow
