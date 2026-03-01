<#
.SYNOPSIS
    Reproducible single-threaded build with diagnostic logging for XAML compiler debugging.

.DESCRIPTION
    This script performs a clean single-threaded build with full diagnostic logging and
    binary log capture. It is designed to help diagnose WinUI 3 XAML compiler issues
    where XamlCompiler.exe exits with code 1 and produces no output.json.

    Key features:
    - Cleans .vs/, bin/, obj/ directories for reproducible builds
    - Runs dotnet restore to ensure packages are present
    - Single-threaded build (-m:1) eliminates race conditions
    - Captures binary log for MSBuild Structured Log Viewer analysis
    - Human-readable summary output

    References:
    - GitHub Issue #10027: Can't get error output from XamlCompiler.exe
    - GitHub Issue #10947: XamlCompiler.exe exits code 1 for Views subfolders

.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug

.PARAMETER Platform
    Target platform. Default: x64

.PARAMETER Clean
    If specified, cleans .vs/, bin/, obj/ before building. Default: $true

.PARAMETER Restore
    If specified, runs dotnet restore before building. Default: $true

.PARAMETER BinlogPath
    Path for the binary log file. Default: .buildlogs/build_diagnostic_{timestamp}.binlog

.PARAMETER Verbosity
    MSBuild verbosity level (quiet, minimal, normal, detailed, diagnostic). Default: diagnostic

.EXAMPLE
    .\scripts\build-with-binlog.ps1
    # Runs a clean Debug x64 build with diagnostic logging

.EXAMPLE
    .\scripts\build-with-binlog.ps1 -Configuration Release -Clean:$false
    # Runs a Release build without cleaning

.EXAMPLE
    .\scripts\build-with-binlog.ps1 -Verbosity normal
    # Runs with normal verbosity (less output) but still captures binlog
#>

Param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [string]$Platform = "x64",
    
    [bool]$Clean = $true,
    
    [bool]$Restore = $true,
    
    [string]$BinlogPath = "",
    
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "diagnostic"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ============================================================================
# Configuration
# ============================================================================

$repoRoot = Resolve-Path "$PSScriptRoot\.."
if ($repoRoot -is [System.Management.Automation.PathInfo]) {
    $repoRoot = $repoRoot.Path
}

$solutionPath = Join-Path $repoRoot "VoiceStudio.sln"
$buildlogsDir = Join-Path $repoRoot ".buildlogs"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"

if (-not $BinlogPath -or $BinlogPath.Trim() -eq "") {
    $BinlogPath = Join-Path $buildlogsDir "build_diagnostic_$timestamp.binlog"
}

# Ensure buildlogs directory exists
New-Item -ItemType Directory -Path $buildlogsDir -Force | Out-Null

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  VoiceStudio Diagnostic Build with Binlog" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Configuration : $Configuration"
Write-Host "Platform      : $Platform"
Write-Host "Clean         : $Clean"
Write-Host "Restore       : $Restore"
Write-Host "Verbosity     : $Verbosity"
Write-Host "Binlog        : $BinlogPath"
Write-Host "Solution      : $solutionPath"
Write-Host ""

# ============================================================================
# Step 1: Clean intermediates
# ============================================================================

if ($Clean) {
    Write-Host "Step 1: Cleaning intermediates..." -ForegroundColor Yellow
    
    $dirsToClean = @(
        (Join-Path $repoRoot ".vs"),
        (Join-Path $repoRoot "bin"),
        (Join-Path $repoRoot "obj"),
        (Join-Path $repoRoot "src\VoiceStudio.App\bin"),
        (Join-Path $repoRoot "src\VoiceStudio.App\obj"),
        (Join-Path $repoRoot "src\VoiceStudio.Core\bin"),
        (Join-Path $repoRoot "src\VoiceStudio.Core\obj"),
        (Join-Path $repoRoot "src\VoiceStudio.App.Tests\bin"),
        (Join-Path $repoRoot "src\VoiceStudio.App.Tests\obj"),
        (Join-Path $repoRoot "src\VoiceStudio.Common.UI\bin"),
        (Join-Path $repoRoot "src\VoiceStudio.Common.UI\obj")
    )
    
    foreach ($dir in $dirsToClean) {
        if (Test-Path $dir) {
            Write-Host "  Removing: $dir"
            Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue
        }
    }
    
    Write-Host "  Clean complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 1: Skipping clean (Clean=false)" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================================
# Step 2: Restore packages
# ============================================================================

if ($Restore) {
    Write-Host "Step 2: Restoring NuGet packages..." -ForegroundColor Yellow
    
    $restoreArgs = @(
        "restore",
        $solutionPath
    )
    
    Write-Host "  dotnet $($restoreArgs -join ' ')"
    & dotnet @restoreArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERROR: Restore failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    
    Write-Host "  Restore complete." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "Step 2: Skipping restore (Restore=false)" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================================
# Step 3: Build single-threaded with binlog
# ============================================================================

Write-Host "Step 3: Building single-threaded with diagnostic logging..." -ForegroundColor Yellow
Write-Host ""
Write-Host "  IMPORTANT: Single-threaded build (-m:1) eliminates race conditions" -ForegroundColor Cyan
Write-Host "  that can mask XAML compiler issues." -ForegroundColor Cyan
Write-Host ""

$buildArgs = @(
    "build",
    $solutionPath,
    "-c", $Configuration,
    "-p:Platform=$Platform",
    "-m:1",                          # Single-threaded - critical for XAML debugging
    "-v:$Verbosity",                 # Full diagnostic output
    "/bl:$BinlogPath",               # Binary log for StructuredLogger analysis
    "--no-restore"                   # Already restored above
)

$startTime = Get-Date
Write-Host "  dotnet $($buildArgs -join ' ')"
Write-Host ""

& dotnet @buildArgs
$buildExitCode = $LASTEXITCODE
$endTime = Get-Date
$duration = $endTime - $startTime

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  BUILD SUMMARY" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Exit Code     : $buildExitCode"
Write-Host "Duration      : $($duration.TotalSeconds.ToString('F2')) seconds"
Write-Host "Binlog        : $BinlogPath"
Write-Host ""

if ($buildExitCode -eq 0) {
    Write-Host "BUILD SUCCEEDED" -ForegroundColor Green
} else {
    Write-Host "BUILD FAILED" -ForegroundColor Red
    Write-Host ""
    Write-Host "Next steps for XAML compiler issues:" -ForegroundColor Yellow
    Write-Host "  1. Open the binlog in MSBuild Structured Log Viewer"
    Write-Host "  2. Search for 'XamlCompiler.exe' to find the compiler invocation"
    Write-Host "  3. Check the input.json and output.json paths"
    Write-Host "  4. Run: .\scripts\analyze-binlog.ps1 -BinlogPath `"$BinlogPath`""
    Write-Host ""
    Write-Host "If exit code 1 with no error output:" -ForegroundColor Yellow
    Write-Host "  - Check for XAML files in nested Views subfolders (GitHub #10947)"
    Write-Host "  - Check for TextElement.* attached properties on ContentPresenter"
    Write-Host "  - Run xaml-binary-search.ps1 to isolate the problematic file"
    Write-Host ""
}

# Write summary to a text file for CI consumption
$summaryPath = Join-Path $buildlogsDir "build_diagnostic_$timestamp.txt"
@(
    "VoiceStudio Diagnostic Build Summary",
    "=====================================",
    "Timestamp     : $(Get-Date -Format o)",
    "Configuration : $Configuration",
    "Platform      : $Platform",
    "Exit Code     : $buildExitCode",
    "Duration      : $($duration.TotalSeconds.ToString('F2')) seconds",
    "Binlog        : $BinlogPath",
    "Result        : $(if ($buildExitCode -eq 0) { 'SUCCESS' } else { 'FAILED' })"
) | Set-Content -Path $summaryPath

Write-Host "Summary written to: $summaryPath" -ForegroundColor Gray
Write-Host ""

exit $buildExitCode
