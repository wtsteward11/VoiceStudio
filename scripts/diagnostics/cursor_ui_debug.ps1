<#
.SYNOPSIS
    VoiceStudio UI Debug Orchestrator for Cursor Agent Integration

.DESCRIPTION
    Orchestrates UI test execution with structured output optimized for Cursor AI agent consumption.
    Runs preflight checks, executes tests with the cursor reporter plugin, and aggregates results
    into machine-readable JSON and human-readable reports.

.PARAMETER TestPath
    Path to test file or directory. Defaults to tests/ui/.

.PARAMETER Marker
    Pytest marker to filter tests (e.g., smoke, visual, accessibility).

.PARAMETER FailFast
    Stop on first test failure.

.PARAMETER CaptureScreenshots
    Capture screenshots on test failures.

.PARAMETER OutputDir
    Directory for test artifacts. Defaults to .buildlogs/ui-tests/.

.PARAMETER SkipPreflight
    Skip prerequisite validation checks.

.PARAMETER Verbose
    Enable verbose output.

.EXAMPLE
    .\scripts\cursor_ui_debug.ps1 -Marker smoke

.EXAMPLE
    .\scripts\cursor_ui_debug.ps1 -TestPath tests/ui/test_performance.py -CaptureScreenshots
#>

param(
    [string]$TestPath = "tests/ui/",
    [string]$Marker = "",
    [switch]$FailFast,
    [switch]$CaptureScreenshots,
    [string]$OutputDir = ".buildlogs/ui-tests",
    [switch]$SkipPreflight,
    [switch]$Verbose
)

$ErrorActionPreference = "Stop"
$script:StartTime = Get-Date
$projectRoot = Split-Path -Parent $PSScriptRoot

# Ensure output directory exists
$outputPath = Join-Path $projectRoot $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$runId = "ui_test_$timestamp"
$jsonOutput = Join-Path $outputPath "$runId.json"
$htmlOutput = Join-Path $outputPath "$runId.html"
$logOutput = Join-Path $outputPath "$runId.log"

# =============================================================================
# Helper Functions
# =============================================================================

function Write-StructuredLog {
    param(
        [string]$Level,
        [string]$Component,
        [string]$Message,
        [hashtable]$Data = @{}
    )
    
    $logEntry = @{
        timestamp = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss.fffZ")
        level = $Level
        component = $Component
        message = $Message
        data = $Data
    }
    
    $jsonLine = $logEntry | ConvertTo-Json -Compress
    Add-Content -Path $logOutput -Value $jsonLine
    
    $color = switch ($Level) {
        "INFO" { "White" }
        "SUCCESS" { "Green" }
        "WARN" { "Yellow" }
        "ERROR" { "Red" }
        default { "Gray" }
    }
    
    Write-Host "[$Level] [$Component] $Message" -ForegroundColor $color
}

function Get-CursorContext {
    # Generate context information for Cursor agent consumption
    @{
        run_id = $runId
        project_root = $projectRoot
        output_dir = $outputPath
        test_path = $TestPath
        marker = $Marker
        timestamp = $script:StartTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
        environment = @{
            python_version = (python --version 2>&1).ToString()
            os = [System.Environment]::OSVersion.VersionString
            machine = $env:COMPUTERNAME
        }
    }
}

# =============================================================================
# Phase 1: Preflight Checks
# =============================================================================

Write-Host "`n" + ("=" * 70) -ForegroundColor Cyan
Write-Host "VOICESTUDIO UI DEBUG ORCHESTRATOR" -ForegroundColor Cyan
Write-Host "Run ID: $runId" -ForegroundColor Cyan
Write-Host ("=" * 70) + "`n" -ForegroundColor Cyan

Write-StructuredLog -Level "INFO" -Component "orchestrator" -Message "Starting UI test orchestration" `
    -Data @{ run_id = $runId; test_path = $TestPath; marker = $Marker }

if (-not $SkipPreflight) {
    Write-Host "Phase 1: Preflight Validation" -ForegroundColor Yellow
    Write-Host ("-" * 40) -ForegroundColor Gray
    
    $preflightScript = Join-Path $PSScriptRoot "preflight_ui_tests.ps1"
    if (Test-Path $preflightScript) {
        $preflightResult = & $preflightScript -Fix 2>&1
        $preflightExitCode = $LASTEXITCODE
        
        if ($preflightExitCode -ne 0) {
            Write-StructuredLog -Level "ERROR" -Component "preflight" -Message "Preflight checks failed" `
                -Data @{ exit_code = $preflightExitCode }
            
            $result = @{
                status = "PREFLIGHT_FAILED"
                run_id = $runId
                preflight_output = $preflightResult -join "`n"
                context = Get-CursorContext
            }
            $result | ConvertTo-Json -Depth 10 | Set-Content $jsonOutput
            
            Write-Host "`nPreflight failed. Results saved to: $jsonOutput" -ForegroundColor Red
            exit 1
        }
        
        Write-StructuredLog -Level "SUCCESS" -Component "preflight" -Message "All preflight checks passed"
    } else {
        Write-StructuredLog -Level "WARN" -Component "preflight" -Message "Preflight script not found, skipping"
    }
}

# =============================================================================
# Phase 2: Environment Setup
# =============================================================================

Write-Host "`nPhase 2: Environment Setup" -ForegroundColor Yellow
Write-Host ("-" * 40) -ForegroundColor Gray

# Set PYTHONPATH
$env:PYTHONPATH = $projectRoot
Write-StructuredLog -Level "INFO" -Component "setup" -Message "Set PYTHONPATH" -Data @{ path = $projectRoot }

# Create screenshots directory if capturing
if ($CaptureScreenshots) {
    $screenshotDir = Join-Path $outputPath "screenshots"
    if (-not (Test-Path $screenshotDir)) {
        New-Item -ItemType Directory -Path $screenshotDir -Force | Out-Null
    }
    $env:VOICESTUDIO_UI_SCREENSHOT_DIR = $screenshotDir
    Write-StructuredLog -Level "INFO" -Component "setup" -Message "Screenshot capture enabled" -Data @{ dir = $screenshotDir }
}

# =============================================================================
# Phase 3: Test Execution
# =============================================================================

Write-Host "`nPhase 3: Test Execution" -ForegroundColor Yellow
Write-Host ("-" * 40) -ForegroundColor Gray

# Build pytest arguments
$pytestArgs = @(
    "-v"
    "--tb=short"
    "--json-report"
    "--json-report-file=$jsonOutput"
)

# Add cursor reporter plugin if available
$cursorReporter = Join-Path $projectRoot "tests\ui\plugins\cursor_reporter.py"
if (Test-Path $cursorReporter) {
    $pytestArgs += "-p", "tests.ui.plugins.cursor_reporter"
    Write-StructuredLog -Level "INFO" -Component "pytest" -Message "Using Cursor reporter plugin"
}

# Add marker filter
if ($Marker) {
    $pytestArgs += "-m", $Marker
}

# Add fail-fast
if ($FailFast) {
    $pytestArgs += "-x"
}

# Add test path
$fullTestPath = Join-Path $projectRoot $TestPath
$pytestArgs += $fullTestPath

Write-StructuredLog -Level "INFO" -Component "pytest" -Message "Executing tests" `
    -Data @{ args = $pytestArgs -join " " }

# Execute pytest
$testOutput = python -m pytest @pytestArgs 2>&1
$testExitCode = $LASTEXITCODE

# =============================================================================
# Phase 4: Results Aggregation
# =============================================================================

Write-Host "`nPhase 4: Results Aggregation" -ForegroundColor Yellow
Write-Host ("-" * 40) -ForegroundColor Gray

$endTime = Get-Date
$duration = ($endTime - $script:StartTime).TotalSeconds

# Parse test results if JSON report was generated
$testResults = $null
if (Test-Path $jsonOutput) {
    try {
        $testResults = Get-Content $jsonOutput -Raw | ConvertFrom-Json
    } catch {
        Write-StructuredLog -Level "WARN" -Component "results" -Message "Could not parse JSON report"
    }
}

# Build final result structure for Cursor agent
$finalResult = @{
    status = if ($testExitCode -eq 0) { "PASSED" } else { "FAILED" }
    run_id = $runId
    exit_code = $testExitCode
    duration_seconds = [math]::Round($duration, 2)
    context = Get-CursorContext
    artifacts = @{
        json_report = $jsonOutput
        html_report = $htmlOutput
        log_file = $logOutput
        screenshots = if ($CaptureScreenshots) { Join-Path $outputPath "screenshots" } else { $null }
    }
    summary = @{
        total = 0
        passed = 0
        failed = 0
        skipped = 0
        errors = 0
    }
    failures = @()
}

# Extract summary from pytest-json-report
if ($testResults -and $testResults.summary) {
    $finalResult.summary.total = $testResults.summary.total
    $finalResult.summary.passed = $testResults.summary.passed
    $finalResult.summary.failed = $testResults.summary.failed
    $finalResult.summary.skipped = $testResults.summary.skipped
    $finalResult.summary.errors = $testResults.summary.error
    
    # Extract failure details for Cursor agent
    if ($testResults.tests) {
        foreach ($test in $testResults.tests) {
            if ($test.outcome -eq "failed") {
                $finalResult.failures += @{
                    nodeid = $test.nodeid
                    duration = $test.duration
                    message = if ($test.call.longrepr) { $test.call.longrepr } else { "Unknown failure" }
                }
            }
        }
    }
}

# Save final result
$finalResult | ConvertTo-Json -Depth 10 | Set-Content $jsonOutput

Write-StructuredLog -Level $(if ($testExitCode -eq 0) { "SUCCESS" } else { "ERROR" }) `
    -Component "results" -Message "Test execution complete" `
    -Data @{ 
        status = $finalResult.status
        total = $finalResult.summary.total
        passed = $finalResult.summary.passed
        failed = $finalResult.summary.failed
        duration = $finalResult.duration_seconds
    }

# =============================================================================
# Summary Output (Cursor Agent Optimized)
# =============================================================================

Write-Host "`n" + ("=" * 70) -ForegroundColor Cyan
Write-Host "TEST EXECUTION SUMMARY" -ForegroundColor Cyan
Write-Host ("=" * 70) -ForegroundColor Cyan

Write-Host "`nStatus: " -NoNewline
if ($testExitCode -eq 0) {
    Write-Host "PASSED" -ForegroundColor Green
} else {
    Write-Host "FAILED" -ForegroundColor Red
}

Write-Host "Duration: $([math]::Round($duration, 2))s"
Write-Host "Tests: $($finalResult.summary.total) total, $($finalResult.summary.passed) passed, $($finalResult.summary.failed) failed, $($finalResult.summary.skipped) skipped"

if ($finalResult.failures.Count -gt 0) {
    Write-Host "`nFailures:" -ForegroundColor Red
    foreach ($failure in $finalResult.failures) {
        Write-Host "  - $($failure.nodeid)" -ForegroundColor Red
        if ($Verbose) {
            Write-Host "    $($failure.message)" -ForegroundColor Gray
        }
    }
}

Write-Host "`nArtifacts:"
Write-Host "  JSON Report: $jsonOutput"
Write-Host "  Log File:    $logOutput"
if ($CaptureScreenshots) {
    Write-Host "  Screenshots: $($finalResult.artifacts.screenshots)"
}

# Output JSON summary for Cursor agent parsing
Write-Host "`n--- CURSOR_AGENT_RESULT_START ---"
$finalResult | ConvertTo-Json -Depth 5 -Compress
Write-Host "--- CURSOR_AGENT_RESULT_END ---`n"

exit $testExitCode
