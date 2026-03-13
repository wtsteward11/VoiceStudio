# VoiceStudio Quick Verification Script
# 30-second local verification before commit
#
# PREFER: .\scripts\verify.ps1 -Quick (canonical; includes gates)
# This script is a lighter alternative when verify.ps1 is slow.
#
# Usage:
#   .\scripts\quick_verify.ps1
#   .\scripts\quick_verify.ps1 -SkipBuild
#   .\scripts\quick_verify.ps1 -Verbose

param(
    [switch]$SkipBuild,
    [switch]$SkipXaml,
    [switch]$SkipCatch
)

$ErrorActionPreference = "Continue"
$startTime = Get-Date
$failures = @()

Write-Host "=" * 60
Write-Host "VoiceStudio Quick Verification"
Write-Host "=" * 60
Write-Host ""

# Ensure we're in the project root
$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (Test-Path "$projectRoot\VoiceStudio.sln") {
    Set-Location $projectRoot
} elseif (Test-Path ".\VoiceStudio.sln") {
    $projectRoot = Get-Location
} else {
    Write-Error "Cannot find VoiceStudio.sln. Run from project root or scripts directory."
    exit 1
}

# Step 1: Empty catch block check
if (-not $SkipCatch) {
    Write-Host "[1/3] Empty catch block check..."
    $catchResult = & python scripts/check_empty_catches.py 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failures += "empty_catch_check"
        Write-Host "  FAIL: Empty catch blocks found" -ForegroundColor Red
    } else {
        Write-Host "  PASS" -ForegroundColor Green
    }
} else {
    Write-Host "[1/3] Empty catch check: SKIPPED"
}

# Step 2: XAML safety check
if (-not $SkipXaml) {
    Write-Host "[2/3] XAML safety check..."
    $xamlResult = & python scripts/lint_xaml.py 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failures += "xaml_safety_check"
        Write-Host "  FAIL: XAML safety issues found" -ForegroundColor Red
    } else {
        Write-Host "  PASS" -ForegroundColor Green
    }
} else {
    Write-Host "[2/3] XAML safety check: SKIPPED"
}

# Step 3: Quick build
if (-not $SkipBuild) {
    Write-Host "[3/3] Quick build check..."
    $buildOutput = & dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        $failures += "build"
        Write-Host "  FAIL: Build failed" -ForegroundColor Red
        Write-Host $buildOutput
    } else {
        Write-Host "  PASS" -ForegroundColor Green
    }
} else {
    Write-Host "[3/3] Build check: SKIPPED"
}

# Summary
$elapsed = (Get-Date) - $startTime
Write-Host ""
Write-Host "=" * 60

if ($failures.Count -eq 0) {
    Write-Host "Quick verify: PASS ($($elapsed.TotalSeconds.ToString('F1'))s)" -ForegroundColor Green
    Write-Host "=" * 60
    Write-Host ""
    Write-Host "Safe to commit. Consider running full verification:"
    Write-Host "  python scripts/run_verification.py"
    exit 0
} else {
    Write-Host "Quick verify: FAIL ($($elapsed.TotalSeconds.ToString('F1'))s)" -ForegroundColor Red
    Write-Host "=" * 60
    Write-Host ""
    Write-Host "Failed checks:"
    foreach ($fail in $failures) {
        Write-Host "  - $fail" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Fix these issues before committing."
    exit 1
}
