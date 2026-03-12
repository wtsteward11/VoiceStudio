<#
.SYNOPSIS
    Unified orchestration script for tracing audio workflows in VoiceStudio.

.DESCRIPTION
    This script provides end-to-end automation for testing and debugging
    audio workflows with comprehensive tracing:
    
    1. Sets up test audio files (canonical or synthetic)
    2. Starts the backend server if not running
    3. Optionally starts WinAppDriver for UI tests
    4. Runs specified tests with tracing enabled
    5. Collects and exports trace reports (JSON/HTML)
    6. Summarizes results and reports any failures

.PARAMETER TestFilter
    Pytest filter expression (e.g., "test_audio_lifecycle" or "allan_watts").
    Default: runs all UI tests.

.PARAMETER SkipBackend
    Skip backend startup check (assume already running).

.PARAMETER SkipWinAppDriver
    Skip WinAppDriver startup (for API-only tests).

.PARAMETER AudioPath
    Override test audio path instead of using canonical audio.

.PARAMETER OutputDir
    Directory for trace outputs. Default: .buildlogs/traces

.PARAMETER Verbose
    Enable verbose output.

.PARAMETER Quick
    Run quick mode (skip LFS, use synthetic audio, minimal tests).

.EXAMPLE
    .\scripts\trace_audio_workflow.ps1
    # Run full audio workflow test with tracing

.EXAMPLE
    .\scripts\trace_audio_workflow.ps1 -TestFilter "test_audio_lifecycle_e2e"
    # Run specific E2E test

.EXAMPLE
    .\scripts\trace_audio_workflow.ps1 -Quick -SkipWinAppDriver
    # Quick API-only test with synthetic audio

.NOTES
    Requires:
    - Python 3.10+ with pytest
    - Backend server (backend/api/main.py)
    - WinAppDriver (for UI tests)
    - Test audio files (auto-provisioned)
#>

param(
    [string]$TestFilter = "",
    [switch]$SkipBackend,
    [switch]$SkipWinAppDriver,
    [string]$AudioPath = "",
    [string]$OutputDir = ".buildlogs/traces",
    [switch]$Verbose,
    [switch]$Quick
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
Set-Location $ProjectRoot

# Configuration
$BackendUrl = "http://127.0.0.1:8000"
$WinAppDriverUrl = "http://127.0.0.1:4723"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$TraceDir = Join-Path $OutputDir $Timestamp

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  VoiceStudio Audio Workflow Tracer" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Timestamp: $Timestamp"
Write-Host "Project Root: $ProjectRoot"
Write-Host "Output Directory: $TraceDir"
Write-Host ""

# Create output directories
New-Item -ItemType Directory -Path $TraceDir -Force | Out-Null
New-Item -ItemType Directory -Path "$TraceDir\screenshots" -Force | Out-Null
New-Item -ItemType Directory -Path "$TraceDir\reports" -Force | Out-Null

# ============================================================================
# Step 1: Set up test audio
# ============================================================================

Write-Host "Step 1: Setting up test audio..." -ForegroundColor Yellow

if ($AudioPath) {
    $env:VOICESTUDIO_TEST_AUDIO = $AudioPath
    Write-Host "  Using provided audio: $AudioPath" -ForegroundColor Gray
} else {
    $setupArgs = @()
    if ($Quick) {
        $setupArgs += "-SkipLFS"
    }
    
    $setupScript = Join-Path $ProjectRoot "scripts\setup\setup_test_audio.ps1"
    if (Test-Path $setupScript) {
        try {
            & $setupScript @setupArgs
            Write-Host "  Audio setup complete" -ForegroundColor Green
        } catch {
            Write-Host "  Warning: Audio setup had issues: $_" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Warning: setup_test_audio.ps1 not found" -ForegroundColor Yellow
    }
}

# ============================================================================
# Step 2: Check/Start Backend
# ============================================================================

Write-Host ""
Write-Host "Step 2: Checking backend..." -ForegroundColor Yellow

$backendRunning = $false
if (-not $SkipBackend) {
    try {
        $response = Invoke-WebRequest -Uri "$BackendUrl/api/health" -TimeoutSec 5 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $backendRunning = $true
            Write-Host "  Backend already running at $BackendUrl" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Backend not running, attempting to start..." -ForegroundColor Gray
        
        # Try to start backend
        $backendPath = Join-Path $ProjectRoot "backend\api\main.py"
        if (Test-Path $backendPath) {
            $backendProcess = Start-Process -FilePath "python" -ArgumentList "-m", "uvicorn", "backend.api.main:app", "--host", "127.0.0.1", "--port", "8000" -WorkingDirectory $ProjectRoot -PassThru -WindowStyle Hidden
            
            # Wait for backend to be ready
            $maxWait = 30
            $waited = 0
            while ($waited -lt $maxWait) {
                Start-Sleep -Seconds 1
                $waited++
                try {
                    $response = Invoke-WebRequest -Uri "$BackendUrl/api/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
                    if ($response.StatusCode -eq 200) {
                        $backendRunning = $true
                        Write-Host "  Backend started (PID: $($backendProcess.Id))" -ForegroundColor Green
                        break
                    }
                } catch {}
            }
            
            if (-not $backendRunning) {
                Write-Host "  Warning: Backend failed to start within ${maxWait}s" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  Warning: Backend script not found" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "  Skipping backend check" -ForegroundColor Gray
}

$env:VOICESTUDIO_BACKEND_URL = $BackendUrl

# ============================================================================
# Step 3: Check WinAppDriver (for UI tests)
# ============================================================================

Write-Host ""
Write-Host "Step 3: Checking WinAppDriver..." -ForegroundColor Yellow

$winAppDriverRunning = $false
if (-not $SkipWinAppDriver) {
    try {
        $response = Invoke-WebRequest -Uri "$WinAppDriverUrl/status" -TimeoutSec 3 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $winAppDriverRunning = $true
            Write-Host "  WinAppDriver running at $WinAppDriverUrl" -ForegroundColor Green
        }
    } catch {
        Write-Host "  WinAppDriver not running" -ForegroundColor Yellow
        Write-Host "  UI tests will be skipped" -ForegroundColor Yellow
        Write-Host "  To enable: Start WinAppDriver from Windows SDK" -ForegroundColor Gray
    }
} else {
    Write-Host "  Skipping WinAppDriver check" -ForegroundColor Gray
}

# ============================================================================
# Step 4: Run Tests with Tracing
# ============================================================================

Write-Host ""
Write-Host "Step 4: Running tests with tracing..." -ForegroundColor Yellow

# Set environment for tracing
$env:VOICESTUDIO_TRACE_OUTPUT = $TraceDir
$env:VOICESTUDIO_TRACE_ENABLED = "1"

# Build pytest arguments
$pytestArgs = @(
    "-m", "pytest",
    "tests/ui/",
    "-v",
    "--tb=short",
    "-x",  # Stop on first failure for debugging
    "--junitxml=$TraceDir\results.xml"
)

if ($TestFilter) {
    $pytestArgs += "-k", $TestFilter
}

if ($Quick) {
    $pytestArgs += "-m", "not slow"
}

if ($Verbose) {
    $pytestArgs += "-s"
}

# Skip UI tests if WinAppDriver not running
if (-not $winAppDriverRunning -and -not $SkipWinAppDriver) {
    $pytestArgs += "-m", "not ui"
    Write-Host "  Note: Excluding UI tests (WinAppDriver not available)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "  Running: python $($pytestArgs -join ' ')" -ForegroundColor Gray
Write-Host ""

$testStart = Get-Date
try {
    & python @pytestArgs 2>&1 | Tee-Object -Variable testOutput
    $testExitCode = $LASTEXITCODE
} catch {
    $testExitCode = 1
    $testOutput = $_
}
$testDuration = (Get-Date) - $testStart

# ============================================================================
# Step 5: Collect Trace Reports
# ============================================================================

Write-Host ""
Write-Host "Step 5: Collecting trace reports..." -ForegroundColor Yellow

# Copy any generated trace files
$sourceTraces = ".buildlogs/validation/reports/workflow_traces"
if (Test-Path $sourceTraces) {
    Copy-Item -Path "$sourceTraces\*" -Destination "$TraceDir\reports\" -Recurse -Force
    Write-Host "  Copied workflow traces to $TraceDir\reports\" -ForegroundColor Gray
}

# Generate summary report
$summaryPath = Join-Path $TraceDir "summary.json"
$summary = @{
    timestamp = $Timestamp
    duration_seconds = [math]::Round($testDuration.TotalSeconds, 2)
    test_filter = $TestFilter
    backend_available = $backendRunning
    winappdriver_available = $winAppDriverRunning
    exit_code = $testExitCode
    status = if ($testExitCode -eq 0) { "PASSED" } else { "FAILED" }
    output_dir = $TraceDir
    artifacts = @(
        Get-ChildItem -Path $TraceDir -Recurse -File | ForEach-Object {
            $_.FullName.Replace($TraceDir, "").TrimStart("\")
        }
    )
}

$summary | ConvertTo-Json -Depth 5 | Set-Content $summaryPath -Encoding UTF8
Write-Host "  Summary written to $summaryPath" -ForegroundColor Gray

# ============================================================================
# Step 6: Report Results
# ============================================================================

Write-Host ""
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Results Summary" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host ""

if ($testExitCode -eq 0) {
    Write-Host "  Status: PASSED" -ForegroundColor Green
} else {
    Write-Host "  Status: FAILED" -ForegroundColor Red
}

Write-Host "  Duration: $([math]::Round($testDuration.TotalSeconds, 1))s"
Write-Host "  Output: $TraceDir"
Write-Host ""

# List generated artifacts
$artifacts = Get-ChildItem -Path $TraceDir -Recurse -File
Write-Host "  Artifacts generated:" -ForegroundColor White
foreach ($artifact in $artifacts | Select-Object -First 10) {
    $relPath = $artifact.FullName.Replace($TraceDir, "").TrimStart("\")
    Write-Host "    - $relPath" -ForegroundColor Gray
}
if ($artifacts.Count -gt 10) {
    Write-Host "    ... and $($artifacts.Count - 10) more" -ForegroundColor Gray
}

Write-Host ""
Write-Host "  View HTML report:" -ForegroundColor White
$htmlReports = Get-ChildItem -Path "$TraceDir\reports" -Filter "*.html" -ErrorAction SilentlyContinue
foreach ($html in $htmlReports) {
    Write-Host "    $($html.FullName)" -ForegroundColor Cyan
}

Write-Host ""

# Exit with test result
exit $testExitCode
