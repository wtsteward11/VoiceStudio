<#
.SYNOPSIS
    Post-build check: verifies XamlTypeInfo.g.cs is non-empty and XBF files exist.

.DESCRIPTION
    After a successful build, the XAML compiler should produce:
    1. A non-empty XamlTypeInfo.g.cs (the type registry for runtime XAML resolution)
    2. .xbf files for each .xaml page (compiled XAML binaries)

    If XamlTypeInfo.g.cs is empty or missing, the app will crash at runtime with
    XamlParseException because no types can be resolved.

.PARAMETER ProjectDir
    Path to the VoiceStudio.App project directory.

.PARAMETER Configuration
    Build configuration (Debug or Release). Defaults to Debug.

.PARAMETER Platform
    Build platform. Defaults to x64.

.EXAMPLE
    .\Verify-XamlArtifacts.ps1
    .\Verify-XamlArtifacts.ps1 -Configuration Release
#>
param(
    [string]$ProjectDir = "",
    [string]$Configuration = "Debug",
    [string]$Platform = "x64"
)

$ErrorActionPreference = "Stop"

if (-not $ProjectDir) {
    $repoRoot = (git rev-parse --show-toplevel 2>$null)
    if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path | Split-Path }
    $ProjectDir = Join-Path $repoRoot "src\VoiceStudio.App"
}

$objBase = Join-Path $ProjectDir "obj"
$passed = $true

# --- Check 1: XamlTypeInfo.g.cs ---
Write-Host "Check 1: XamlTypeInfo.g.cs" -ForegroundColor Cyan

$typeInfoFiles = Get-ChildItem -Path $objBase -Recurse -Filter "XamlTypeInfo.g.cs" -ErrorAction SilentlyContinue
if (-not $typeInfoFiles -or $typeInfoFiles.Count -eq 0) {
    Write-Host "  [FAIL] XamlTypeInfo.g.cs not found in obj/" -ForegroundColor Red
    Write-Host "  The XAML compiler did not run or failed silently." -ForegroundColor Red
    $passed = $false
} else {
    $typeInfo = $typeInfoFiles[0]
    $content = Get-Content $typeInfo.FullName -Raw -ErrorAction SilentlyContinue
    $lineCount = ($content -split "`n").Count

    if ($lineCount -lt 10) {
        Write-Host "  [FAIL] XamlTypeInfo.g.cs is nearly empty ($lineCount lines)" -ForegroundColor Red
        Write-Host "  Path: $($typeInfo.FullName)" -ForegroundColor Red
        Write-Host "  This means the XAML compiler crashed silently (exit code 1)." -ForegroundColor Red
        Write-Host "  The app WILL crash at runtime with XamlParseException." -ForegroundColor Red
        $passed = $false
    } else {
        Write-Host "  [PASS] XamlTypeInfo.g.cs has $lineCount lines" -ForegroundColor Green
    }
}

# --- Check 2: XBF files ---
Write-Host "Check 2: XBF files" -ForegroundColor Cyan

$xbfFiles = Get-ChildItem -Path $objBase -Recurse -Filter "*.xbf" -ErrorAction SilentlyContinue
$xamlPages = Get-ChildItem -Path $ProjectDir -Recurse -Filter "*.xaml" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\" }

$xbfCount = if ($xbfFiles) { $xbfFiles.Count } else { 0 }
$xamlCount = if ($xamlPages) { $xamlPages.Count } else { 0 }

if ($xbfCount -eq 0) {
    Write-Host "  [FAIL] No .xbf files found in obj/" -ForegroundColor Red
    Write-Host "  The XAML compiler did not produce any compiled XAML." -ForegroundColor Red
    $passed = $false
} else {
    $ratio = if ($xamlCount -gt 0) { [math]::Round($xbfCount / $xamlCount * 100) } else { 100 }
    if ($ratio -lt 50) {
        Write-Host "  [WARN] Only $xbfCount XBF files for $xamlCount XAML pages ($ratio%)" -ForegroundColor Yellow
        Write-Host "  Some XAML pages may not have been compiled." -ForegroundColor Yellow
    } else {
        Write-Host "  [PASS] $xbfCount XBF files for $xamlCount XAML pages ($ratio%)" -ForegroundColor Green
    }
}

# --- Summary ---
Write-Host ""
if ($passed) {
    Write-Host "[PASS] XAML artifact verification passed." -ForegroundColor Green
    exit 0
} else {
    Write-Host "[FAIL] XAML artifact verification FAILED." -ForegroundColor Red
    Write-Host "       See docs/reports/BUILD_ROOT_CAUSE_ANALYSIS_20260228.md for diagnosis." -ForegroundColor Yellow
    exit 1
}
