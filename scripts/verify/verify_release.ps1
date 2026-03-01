# VoiceStudio Release Build Verification Script
# Run this before committing significant C# changes to catch release-only issues.
#
# Usage:
#   .\scripts\verify_release.ps1
#   .\scripts\verify_release.ps1 -SkipTests
#   .\scripts\verify_release.ps1 -Verbose

param(
    [switch]$SkipTests,
    [switch]$NoIncremental
)

$ErrorActionPreference = "Stop"

Write-Host "=" * 70
Write-Host "VoiceStudio Release Build Verification"
Write-Host "=" * 70
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

Write-Host "Project root: $projectRoot"
Write-Host ""

# Build parameters
$buildArgs = @(
    "build",
    "VoiceStudio.sln",
    "-c", "Release",
    "-p:Platform=x64"
)

if ($NoIncremental) {
    $buildArgs += "--no-incremental"
}

# Step 1: Clean previous Release artifacts
Write-Host "Step 1: Cleaning Release artifacts..."
$cleanDirs = Get-ChildItem -Path "src" -Recurse -Directory | Where-Object { $_.Name -eq "bin" -or $_.Name -eq "obj" }
foreach ($dir in $cleanDirs) {
    $releasePath = Join-Path $dir.FullName "Release"
    if (Test-Path $releasePath) {
        Remove-Item -Recurse -Force $releasePath
        Write-Host "  Cleaned: $releasePath"
    }
}
Write-Host "  Clean complete."
Write-Host ""

# Step 2: Restore NuGet packages
Write-Host "Step 2: Restoring NuGet packages..."
dotnet restore VoiceStudio.sln
if ($LASTEXITCODE -ne 0) {
    Write-Error "NuGet restore failed!"
    exit 1
}
Write-Host "  Restore complete."
Write-Host ""

# Step 3: Build Release configuration
Write-Host "Step 3: Building Release configuration..."
Write-Host "  Command: dotnet $($buildArgs -join ' ')"
Write-Host ""

& dotnet @buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "=" * 70
    Write-Host "RELEASE BUILD FAILED" -ForegroundColor Red
    Write-Host "=" * 70
    Write-Host ""
    Write-Host "The Release build failed. This may indicate:"
    Write-Host "  - Code that only works in Debug configuration"
    Write-Host "  - Missing #if DEBUG blocks for debug-only code"
    Write-Host "  - Optimization issues"
    Write-Host ""
    Write-Host "Review the build output above for specific errors."
    exit 1
}

Write-Host ""
Write-Host "  Release build: PASS" -ForegroundColor Green
Write-Host ""

# Step 4: Run tests (optional)
if (-not $SkipTests) {
    Write-Host "Step 4: Running unit tests (Release)..."
    
    $testArgs = @(
        "test",
        "src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj",
        "-c", "Release",
        "-p:Platform=x64",
        "--no-build",
        "--verbosity", "normal"
    )
    
    & dotnet @testArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "=" * 70
        Write-Host "RELEASE TESTS FAILED" -ForegroundColor Red
        Write-Host "=" * 70
        Write-Host ""
        Write-Host "Tests passed in Debug but failed in Release."
        Write-Host "This may indicate timing-sensitive or optimization-sensitive code."
        exit 1
    }
    
    Write-Host ""
    Write-Host "  Release tests: PASS" -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 4: Skipping tests (--SkipTests specified)"
    Write-Host ""
}

# Summary
Write-Host "=" * 70
Write-Host "RELEASE VERIFICATION COMPLETE" -ForegroundColor Green
Write-Host "=" * 70
Write-Host ""
Write-Host "All checks passed. Safe to commit C# changes."
Write-Host ""

exit 0
