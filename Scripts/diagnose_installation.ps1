# BIBIM Installation Diagnostic Script
# Helps troubleshoot "Open BIBIM Chat" not appearing in Dynamo Extensions tab

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "BIBIM Installation Diagnostic Tool" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$diagLog = @()
$diagLog += "BIBIM Installation Diagnostic Report"
$diagLog += "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$diagLog += "=" * 60
$diagLog += ""

# 1. Check Revit installations
Write-Host "[1/6] Checking Revit installations..." -ForegroundColor Yellow
$revitVersions = @('2022', '2023', '2024', '2025', '2026')
$foundRevit = @()

foreach ($ver in $revitVersions) {
    $path = "C:\Program Files\Autodesk\Revit $ver"
    if (Test-Path $path) {
        $foundRevit += $ver
        Write-Host "  ✓ Revit $ver found" -ForegroundColor Green
        $diagLog += "Revit $ver: FOUND at $path"
    }
}

if ($foundRevit.Count -eq 0) {
    Write-Host "  ✗ No Revit installations found" -ForegroundColor Red
    $diagLog += "Revit: NONE FOUND"
}
$diagLog += ""

# 2. Check Dynamo installations (ProgramData - System)
Write-Host "[2/6] Checking Dynamo installations (System)..." -ForegroundColor Yellow
$dynamoSystemBase = "C:\ProgramData\Autodesk"

foreach ($ver in $foundRevit) {
    $dynamoPath = "$dynamoSystemBase\RVT $ver\Dynamo"
    if (Test-Path $dynamoPath) {
        $dynamoVersions = Get-ChildItem -Path $dynamoPath -Directory | Where-Object { $_.Name -match '^\d+\.\d+' }
        foreach ($dVer in $dynamoVersions) {
            Write-Host "  ✓ Revit $ver → Dynamo $($dVer.Name)" -ForegroundColor Green
            $diagLog += "Dynamo (System): Revit $ver → $($dVer.Name) at $($dVer.FullName)"
            
            # Check if packages folder exists
            $packagesPath = "$($dVer.FullName)\packages"
            if (Test-Path $packagesPath) {
                $packages = Get-ChildItem -Path $packagesPath -Directory
                $diagLog += "  Packages count: $($packages.Count)"
                if ($packages | Where-Object { $_.Name -eq 'BIBIM_MVP' }) {
                    Write-Host "    ✓ BIBIM_MVP found!" -ForegroundColor Green
                    $diagLog += "  → BIBIM_MVP: FOUND"
                } else {
                    Write-Host "    ✗ BIBIM_MVP not found" -ForegroundColor Red
                    $diagLog += "  → BIBIM_MVP: NOT FOUND"
                }
            }
        }
    }
}
$diagLog += ""

# 3. Check Dynamo installations (AppData - User)
Write-Host "[3/6] Checking Dynamo installations (User)..." -ForegroundColor Yellow
$dynamoUserBase = "$env:APPDATA\Dynamo\Dynamo Revit"

if (Test-Path $dynamoUserBase) {
    $dynamoVersions = Get-ChildItem -Path $dynamoUserBase -Directory | Where-Object { $_.Name -match '^\d+\.\d+' }
    foreach ($dVer in $dynamoVersions) {
        Write-Host "  ✓ Dynamo $($dVer.Name)" -ForegroundColor Green
        $diagLog += "Dynamo (User): $($dVer.Name) at $($dVer.FullName)"
        
        # Check if packages folder exists
        $packagesPath = "$($dVer.FullName)\packages"
        if (Test-Path $packagesPath) {
            $packages = Get-ChildItem -Path $packagesPath -Directory -ErrorAction SilentlyContinue
            $diagLog += "  Packages count: $($packages.Count)"
            if ($packages | Where-Object { $_.Name -eq 'BIBIM_MVP' }) {
                Write-Host "    ✓ BIBIM_MVP found!" -ForegroundColor Green
                $diagLog += "  → BIBIM_MVP: FOUND"
                
                # Check critical files
                $bibimPath = "$packagesPath\BIBIM_MVP"
                $diagLog += "  → BIBIM_MVP path: $bibimPath"
                
                $requiredFiles = @(
                    'BIBIM_MVP.dll',
                    'pkg.json',
                    'extra\BIBIM_ViewExtensionDefinition.xml'
                )
                
                foreach ($file in $requiredFiles) {
                    $filePath = "$bibimPath\$file"
                    if (Test-Path $filePath) {
                        Write-Host "      ✓ $file" -ForegroundColor Green
                        $diagLog += "    File: $file - OK"
                    } else {
                        Write-Host "      ✗ $file MISSING!" -ForegroundColor Red
                        $diagLog += "    File: $file - MISSING!"
                    }
                }
            } else {
                Write-Host "    ✗ BIBIM_MVP not found" -ForegroundColor Red
                $diagLog += "  → BIBIM_MVP: NOT FOUND"
            }
        } else {
            $diagLog += "  Packages folder: NOT FOUND"
        }
    }
} else {
    Write-Host "  ✗ User Dynamo folder not found" -ForegroundColor Red
    $diagLog += "Dynamo (User): NOT FOUND"
}
$diagLog += ""

# 4. Check BIBIM log file
Write-Host "[4/6] Checking BIBIM log file..." -ForegroundColor Yellow
$bibimLog = "$env:USERPROFILE\bibim_debug.txt"
if (Test-Path $bibimLog) {
    Write-Host "  ✓ Log file found: $bibimLog" -ForegroundColor Green
    $diagLog += "BIBIM Log: FOUND at $bibimLog"
    
    # Show last 10 lines
    Write-Host "  Last 10 log entries:" -ForegroundColor Gray
    $logLines = Get-Content $bibimLog -Tail 10
    foreach ($line in $logLines) {
        Write-Host "    $line" -ForegroundColor Gray
        $diagLog += "  LOG: $line"
    }
} else {
    Write-Host "  ✗ Log file not found (BIBIM may not have been launched)" -ForegroundColor Red
    $diagLog += "BIBIM Log: NOT FOUND (Never launched or Debug mode not enabled)"
}
$diagLog += ""

# 5. Check Dynamo log
Write-Host "[5/6] Checking Dynamo log..." -ForegroundColor Yellow
$dynamoLog = "$env:APPDATA\Dynamo\Dynamo Revit\DynamoRevit.log"
if (Test-Path $dynamoLog) {
    Write-Host "  ✓ Dynamo log found" -ForegroundColor Green
    $diagLog += "Dynamo Log: FOUND at $dynamoLog"
    
    # Check Dynamo version
    $logContent = Get-Content $dynamoLog -Raw
    if ($logContent -match 'Dynamo -- Build ([\d\.]+)') {
        $dynamoVersion = $matches[1]
        Write-Host "  Dynamo version: $dynamoVersion" -ForegroundColor Cyan
        $diagLog += "  Dynamo Build: $dynamoVersion"
    }
    
    # Check if BIBIM was loaded
    if ($logContent -match 'BIBIM') {
        Write-Host "  ✓ BIBIM mentioned in log" -ForegroundColor Green
        $diagLog += "  BIBIM in log: YES"
    } else {
        Write-Host "  ✗ BIBIM not mentioned in log (not loaded)" -ForegroundColor Red
        $diagLog += "  BIBIM in log: NO (Extension not loaded!)"
    }
} else {
    Write-Host "  ✗ Dynamo log not found" -ForegroundColor Red
    $diagLog += "Dynamo Log: NOT FOUND"
}
$diagLog += ""

# 6. Version mismatch check
Write-Host "[6/6] Checking for version mismatches..." -ForegroundColor Yellow
$diagLog += "VERSION MISMATCH ANALYSIS:"

if ($logContent -match 'Dynamo -- Build 3\.4') {
    Write-Host "  ⚠️  WARNING: Dynamo 3.4.x is running" -ForegroundColor Red
    Write-Host "     But BIBIM might be installed for Dynamo 3.6" -ForegroundColor Red
    $diagLog += "  WARNING: Dynamo 3.4.x detected in log"
    $diagLog += "  This means Revit 2026 is using bundled Dynamo 3.4"
    $diagLog += "  BIBIM must be installed to 3.4 folder, not 3.6!"
}

$diagLog += ""
$diagLog += "=" * 60
$diagLog += "DIAGNOSIS COMPLETE"

# Save diagnostic report
$reportPath = "$env:USERPROFILE\Desktop\BIBIM_Diagnostic_Report.txt"
$diagLog | Out-File -FilePath $reportPath -Encoding UTF8

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Diagnostic report saved to:" -ForegroundColor Green
Write-Host $reportPath -ForegroundColor White
Write-Host ""
Write-Host "📧 Send this file to support for troubleshooting" -ForegroundColor Yellow
Write-Host "============================================" -ForegroundColor Cyan

# Open the report
Start-Process notepad.exe $reportPath
