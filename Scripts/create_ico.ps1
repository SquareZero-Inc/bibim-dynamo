# Create ICO file from multiple PNG sizes
param(
    [string]$Color = "white"
)

Add-Type -AssemblyName System.Drawing

$sizes = @(16, 32, 48, 256)
$outputIco = "Assets\Icons\bibim-icon-$Color.ico"

# Load images
$images = @()
foreach ($size in $sizes) {
    $path = "Assets\Icons\bibim-logo-$Color-$size.png"
    if (Test-Path $path) {
        $images += [System.Drawing.Image]::FromFile((Resolve-Path $path))
    }
}

if ($images.Count -eq 0) {
    Write-Host "No images found!" -ForegroundColor Red
    exit 1
}

# Create ICO using first image and save with multiple sizes
$ms = New-Object System.IO.MemoryStream
$images[0].Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
$ms.Position = 0

# Simple ICO creation - use largest image
$largestImage = $images | Sort-Object -Property Width -Descending | Select-Object -First 1
$icon = [System.Drawing.Icon]::FromHandle($largestImage.GetHicon())
$fs = [System.IO.File]::Create($outputIco)
$icon.Save($fs)
$fs.Close()

# Cleanup
foreach ($img in $images) {
    $img.Dispose()
}

Write-Host "Created: $outputIco" -ForegroundColor Green
