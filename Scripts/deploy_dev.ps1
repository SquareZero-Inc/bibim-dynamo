$ErrorActionPreference = "Stop"

# Configuration
$ProjectDir = Get-Location
$ManifestName = "BIBIM_Manifest.xml"

# Auto-detect latest Dynamo version
$DynamoRevitBase = "$env:APPDATA\Dynamo\Dynamo Revit"
$DynamoCoreBase = "$env:APPDATA\Dynamo\Dynamo Core"

$DynamoRoot = $null

# Try Dynamo Revit first (preferred for Revit users)
if (Test-Path $DynamoRevitBase) {
    $versions = Get-ChildItem $DynamoRevitBase -Directory | Where-Object { $_.Name -match '^\d+\.\d+$' } | Sort-Object Name -Descending
    if ($versions.Count -gt 0) {
        $DynamoRoot = $versions[0].FullName
        Write-Host "Found Dynamo Revit $($versions[0].Name)"
    }
}

# Fallback to Dynamo Core
if (-not $DynamoRoot -and (Test-Path $DynamoCoreBase)) {
    $versions = Get-ChildItem $DynamoCoreBase -Directory | Where-Object { $_.Name -match '^\d+\.\d+$' } | Sort-Object Name -Descending
    if ($versions.Count -gt 0) {
        $DynamoRoot = $versions[0].FullName
        Write-Host "Found Dynamo Core $($versions[0].Name)"
    }
}

if (-not $DynamoRoot) {
    Write-Error "Could not find any Dynamo installation in AppData. Please install Dynamo first."
    exit 1
}

# Determine build configuration based on Dynamo version
$DynamoVersion = Split-Path $DynamoRoot -Leaf
Write-Host "Detected Dynamo version: $DynamoVersion"

# Map Dynamo version to build configuration
$BuildConfig = switch ($DynamoVersion) {
    "3.6" { "R2026_D361" }
    "3.5" { "R2026_D350" }
    "3.4" { "R2026_D341" }
    "3.3" { "R2025_D330" }
    "3.2" { "R2025_D321" }
    "3.1" { "R2025_D321" }
    "3.0" { "R2025_D303" }
    "2.19" { "R2024_D293" }
    "2.18" { "R2024_D281" }
    "2.17" { "R2024_D270" }
    "2.16" { "R2023_D261" }
    "2.13" { "R2023_D230" }
    "2.12" { "R2022_D220" }
    default { 
        Write-Warning "Unknown Dynamo version $DynamoVersion, trying latest R2026_D361"
        "R2026_D361" 
    }
}

$BuildDir = Join-Path $ProjectDir "bin\$BuildConfig"

if (-not (Test-Path $BuildDir)) {
    Write-Error "Build directory not found: $BuildDir. Please build the project first with: dotnet build -c $BuildConfig"
    exit 1
}

Write-Host "Using build configuration: $BuildConfig"

$ViewExtensionsDir = Join-Path $DynamoRoot "viewExtensions"
$TargetDir = Join-Path $ViewExtensionsDir "BIBIM_MVP"

Write-Host "Deploying to: $TargetDir"

# Create Directories
if (-not (Test-Path $ViewExtensionsDir)) {
    New-Item -ItemType Directory -Force -Path $ViewExtensionsDir | Out-Null
}
if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
}

# Copy Build Artifacts
Copy-Item "$BuildDir\*" -Destination $TargetDir -Recurse -Force
Write-Host "Copied build artifacts."

# Create/Update Manifest in ViewExtensions Root
$ManifestContent = Get-Content (Join-Path $ProjectDir $ManifestName)
# Update AssemblyPath to point to the subdirectory
$ManifestContent = $ManifestContent -replace "<AssemblyPath>BIBIM_MVP.dll</AssemblyPath>", "<AssemblyPath>BIBIM_MVP\BIBIM_MVP.dll</AssemblyPath>"

$ManifestDest = Join-Path $ViewExtensionsDir "BIBIM_MVP_ViewExtension.xml"
$ManifestContent | Set-Content $ManifestDest

Write-Host "Created manifest at: $ManifestDest"
Write-Host "Deployment Complete! Please restart Dynamo/Revit."
