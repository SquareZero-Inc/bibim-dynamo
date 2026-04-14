# BIBIM Installer Build Script
# Builds for Revit 2022-2026

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "BIBIM MVP Installer Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Ensure we're in the project root
$scriptDir = Split-Path -Parent $PSCommandPath
$projectRoot = Split-Path -Parent $scriptDir
Set-Location $projectRoot
Write-Host "Working directory: $projectRoot" -ForegroundColor Gray
Write-Host ""

# Read version from Directory.Build.props (Single Source of Truth)
$propsFile = "Directory.Build.props"
if (Test-Path $propsFile) {
    [xml]$props = Get-Content $propsFile
    $version = $props.Project.PropertyGroup.BibimVersion
    Write-Host "Version from Directory.Build.props: $version" -ForegroundColor Green
} else {
    Write-Host "❌ Directory.Build.props not found!" -ForegroundColor Red
    exit 1
}

$configs = @(
    "R2027_D402",
    "R2026_D361", "R2026_D360", "R2026_D350", "R2026_D341",
    "R2025_D330", "R2025_D321", "R2025_D303",
    "R2024_D293", "R2024_D281", "R2024_D270",
    "R2023_D261", "R2023_D230",
    "R2022_D220"
)
$languages = @("kr", "en")

foreach ($language in $languages) {
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Building language: $language" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    foreach ($config in $configs) {
        Write-Host "----------------------------------------"
        Write-Host "Building Configuration: $config [$language]" -ForegroundColor Yellow
        Write-Host "----------------------------------------"

        # Restore (Essential for multi-target switching)
        Write-Host "Restoring..."
        dotnet restore BIBIM_MVP.csproj -p:Configuration=$config -p:AppLanguage=$language
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Restore failed for $config [$language]" -ForegroundColor Red
            exit 1
        }

        # Clean
        Write-Host "Cleaning..."
        dotnet clean BIBIM_MVP.csproj -c $config -p:AppLanguage=$language
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Clean failed for $config [$language]" -ForegroundColor Red
            exit 1
        }

        # Build
        Write-Host "Building..."
        dotnet build BIBIM_MVP.csproj -c $config -p:AppLanguage=$language
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Build failed for $config [$language]" -ForegroundColor Red
            exit 1
        }
        
        # Simple check for main DLL
        $dllPath = "bin\$language\$config\BIBIM_MVP.dll"
        if (Test-Path $dllPath) {
            Write-Host "✅ Success: $dllPath" -ForegroundColor Green
        }
        else {
            Write-Host "❌ Error: DLL not found at $dllPath" -ForegroundColor Red
            exit 1
        }
        Write-Host ""
    }
}

# Compile Installer
Write-Host "[Installer] Compiling with Inno Setup..." -ForegroundColor Yellow

# Update pkg.json version
$pkgJson = Get-Content "pkg.json" -Raw | ConvertFrom-Json
$pkgJson.version = $version
$pkgJson | ConvertTo-Json -Depth 10 | Set-Content "pkg.json" -Encoding UTF8
Write-Host "  Updated pkg.json to version $version" -ForegroundColor Gray

$isccPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$iscc = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $iscc = $path
        break
    }
}

if ($null -eq $iscc) {
    Write-Host "❌ Inno Setup not found!" -ForegroundColor Red
    Write-Host "   Please install Inno Setup 6 for the installer." -ForegroundColor Yellow
    exit 1
}

Write-Host "  Found Inno Setup: $iscc" -ForegroundColor Gray

foreach ($language in $languages) {
    Write-Host "  Compiling installer for language build: $language" -ForegroundColor Gray
    # Pass version and build language to Inno Setup via /D define
    & $iscc "/DMyAppVersion=$version" "/DAppLanguage=$language" "BIBIM_Installer.iss"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Installer compilation failed for $language" -ForegroundColor Red
        exit 1
    }
}

Write-Host "✅ Installer compiled successfully" -ForegroundColor Green
Write-Host ""

# Code Signing
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Code Signing with DigiCert EV Certificate" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"

if (-not (Test-Path $signtool)) {
    Write-Host "❌ signtool.exe not found!" -ForegroundColor Red
    Write-Host "   Install Windows SDK to enable code signing." -ForegroundColor Yellow
    exit 1
}

foreach ($language in $languages) {
    $exePath = "Output\BIBIM_Setup_${language}_v${version}.exe"
    if (Test-Path $exePath) {
        Write-Host "  Signing: $exePath" -ForegroundColor Gray
        & $signtool sign /a /tr http://timestamp.digicert.com /td sha256 /fd sha256 /v $exePath
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Signing failed for $exePath" -ForegroundColor Red
            Write-Host "   Make sure USB token is connected and PIN is ready." -ForegroundColor Yellow
            exit 1
        }
        Write-Host "✅ Signed: $exePath" -ForegroundColor Green
    } else {
        Write-Host "❌ File not found: $exePath" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Output located in Output/ directory."
