# BIBIM MVP - Build Artifacts Cleanup Script
# This script cleans up old build artifacts to reduce disk space usage

Write-Host "=== BIBIM MVP Build Cleanup ===" -ForegroundColor Cyan
Write-Host ""

$projectRoot = Split-Path -Parent $PSScriptRoot

# Function to get directory size
function Get-DirectorySize {
    param([string]$path)
    if (Test-Path $path) {
        $size = (Get-ChildItem -Path $path -Recurse -File -ErrorAction SilentlyContinue | 
                 Measure-Object -Property Length -Sum).Sum
        return [math]::Round($size / 1MB, 2)
    }
    return 0
}

# Show current sizes
Write-Host "Current disk usage:" -ForegroundColor Yellow
$binSize = Get-DirectorySize "$projectRoot\bin"
$objSize = Get-DirectorySize "$projectRoot\obj"
$outputSize = Get-DirectorySize "$projectRoot\Output"

Write-Host "  bin\     : $binSize MB"
Write-Host "  obj\     : $objSize MB"
Write-Host "  Output\  : $outputSize MB"
Write-Host "  Total    : $($binSize + $objSize + $outputSize) MB"
Write-Host ""

# Ask for confirmation
$response = Read-Host "Do you want to clean these folders? (y/n)"
if ($response -ne 'y' -and $response -ne 'Y') {
    Write-Host "Cleanup cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Green

# Clean bin folder
if (Test-Path "$projectRoot\bin") {
    Write-Host "  Removing bin\" -ForegroundColor Gray
    Remove-Item -Path "$projectRoot\bin" -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean obj folder
if (Test-Path "$projectRoot\obj") {
    Write-Host "  Removing obj\" -ForegroundColor Gray
    Remove-Item -Path "$projectRoot\obj" -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean Output folder (keep only the latest installer)
if (Test-Path "$projectRoot\Output") {
    Write-Host "  Cleaning Output\ (keeping latest installer only)" -ForegroundColor Gray
    
    $installers = Get-ChildItem -Path "$projectRoot\Output" -Filter "*.exe" -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending
    
    if ($installers.Count -gt 1) {
        $latest = $installers[0]
        Write-Host "    Keeping: $($latest.Name)" -ForegroundColor Green
        
        foreach ($installer in $installers[1..($installers.Count - 1)]) {
            Write-Host "    Deleting: $($installer.Name)" -ForegroundColor Red
            Remove-Item $installer.FullName -Force
        }
    } elseif ($installers.Count -eq 1) {
        Write-Host "    Only one installer found, keeping it: $($installers[0].Name)" -ForegroundColor Green
    } else {
        Write-Host "    No installers found in Output\" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "Cleanup complete!" -ForegroundColor Green
Write-Host ""

# Show final sizes
Write-Host "Disk space recovered:" -ForegroundColor Yellow
$totalRecovered = $binSize + $objSize
Write-Host "  ~$totalRecovered MB" -ForegroundColor Cyan
Write-Host ""
