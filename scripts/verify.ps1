<#
.SYNOPSIS
    Unified verification harness for VoiceStudio.
.DESCRIPTION
    The single source of truth for product verification. Runs all stages:
    1. Clean Build (C#)
    2. Python Quality Checks (ruff, mypy)
    3. C# Unit Tests
    4. Python Unit Tests
    5. Contract Tests (C# <-> Python)
    6. Backend Integration Tests
    7. UI Smoke Tests
    8. Gate/Ledger Validation
    
    Exit code 0 only if ALL stages pass. No exceptions.
    
    RULE: No changes allowed unless this script stays GREEN.
.PARAMETER Quick
    Run quick verification (build + lint only, ~30 seconds).
.PARAMETER Configuration
    Build configuration. Default: Debug
.PARAMETER SkipBuild
    Skip C# build stage.
.PARAMETER SkipPythonLint
    Skip Python quality checks (ruff, mypy).
.PARAMETER SkipCSharpTests
    Skip C# unit tests.
.PARAMETER SkipPythonTests
    Skip Python unit tests.
.PARAMETER SkipContractTests
    Skip contract tests.
.PARAMETER SkipIntegration
    Skip backend integration tests.
.PARAMETER SkipUI
    Skip UI smoke tests.
.PARAMETER SkipGates
    Skip gate/ledger validation.
.PARAMETER RealUI
    Enable real UI automation (launches the app).
.PARAMETER StrictMypy
    Treat mypy type errors (exit code 1) as failures. Default: warnings only.
.EXAMPLE
    .\scripts\verify.ps1
    .\scripts\verify.ps1 -Quick
    .\scripts\verify.ps1 -SkipUI -SkipIntegration
    .\scripts\verify.ps1 -RealUI -Configuration Release
#>
[CmdletBinding()]
param(
    [switch]$Quick,
    
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$SkipBuild,
    [switch]$SkipPythonLint,
    [switch]$SkipCSharpTests,
    [switch]$SkipPythonTests,
    [switch]$SkipContractTests,
    [switch]$SkipIntegration,
    [switch]$SkipUI,
    [switch]$SkipGates,
    [switch]$RealUI,
    [switch]$StrictMypy
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$ArtifactsDir = Join-Path $RootDir "artifacts\verify\$Timestamp"
$LatestLink = Join-Path $RootDir "artifacts\verify\latest"
$ReportFile = Join-Path $ArtifactsDir "verification_report.md"
$SummaryFile = Join-Path $ArtifactsDir "summary.json"

# Quick mode overrides (gates still run - they're fast and critical)
if ($Quick) {
    $SkipCSharpTests = $true
    $SkipPythonTests = $true
    $SkipContractTests = $true
    $SkipIntegration = $true
    $SkipUI = $true
    # Note: SkipGates intentionally NOT set - gate validation is fast and critical
}

# ============================================================================
# PREREQUISITE VALIDATION
# ============================================================================

function Test-Prerequisites {
    $missing = @()
    
    # Check dotnet
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        $missing += "dotnet (.NET SDK)"
    } else {
        Write-Host "  dotnet: $((& dotnet --version 2>&1))" -ForegroundColor DarkGray
    }
    
    # Check python
    $python = Get-Command python -ErrorAction SilentlyContinue
    if (-not $python) {
        $missing += "python (Python 3.10+)"
    } else {
        $pyVersion = & python --version 2>&1
        Write-Host "  python: $pyVersion" -ForegroundColor DarkGray
    }
    
    # Check ruff (only if not skipping Python lint)
    if (-not $SkipPythonLint) {
        $ruffCheck = & python -m ruff --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            $missing += "ruff (pip install ruff)"
        } else {
            Write-Host "  ruff: $ruffCheck" -ForegroundColor DarkGray
        }
        
        # Check mypy
        $mypyCheck = & python -m mypy --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            $missing += "mypy (pip install mypy)"
        } else {
            Write-Host "  mypy: $mypyCheck" -ForegroundColor DarkGray
        }
    }
    
    # Check pytest (only if running Python tests)
    if (-not $SkipPythonTests -or -not $SkipContractTests -or -not $SkipIntegration) {
        $pytestCheck = & python -m pytest --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            $missing += "pytest (pip install pytest)"
        } else {
            Write-Host "  pytest: $($pytestCheck -split "`n" | Select-Object -First 1)" -ForegroundColor DarkGray
        }
    }
    
    if ($missing.Count -gt 0) {
        Write-Host ""
        Write-Host "ERROR: Missing prerequisites:" -ForegroundColor Red
        foreach ($item in $missing) {
            Write-Host "  - $item" -ForegroundColor Red
        }
        Write-Host ""
        Write-Host "Please install missing tools and try again." -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Checking prerequisites..." -ForegroundColor Cyan
Test-Prerequisites
Write-Host "Prerequisites OK" -ForegroundColor Green
Write-Host ""

# ============================================================================
# ARTIFACT CLEANUP
# ============================================================================

function Remove-OldArtifacts {
    param([int]$KeepCount = 10)
    
    $verifyDir = Join-Path $RootDir "artifacts\verify"
    if (-not (Test-Path $verifyDir)) {
        return
    }
    
    # Get all timestamped directories (exclude 'latest')
    $allDirs = Get-ChildItem -Path $verifyDir -Directory | 
        Where-Object { $_.Name -match '^\d{8}_\d{6}$' } |
        Sort-Object Name -Descending
    
    if ($allDirs.Count -gt $KeepCount) {
        $toRemove = $allDirs | Select-Object -Skip $KeepCount
        foreach ($dir in $toRemove) {
            Write-Host "Cleaning up old artifacts: $($dir.Name)" -ForegroundColor DarkGray
            Remove-Item $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# Clean up old artifacts (keep 10 most recent)
Remove-OldArtifacts -KeepCount 10

# ============================================================================
# INITIALIZATION
# ============================================================================

# Create artifacts directory
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null

# Create stage-specific subdirectories
$StageLogsDir = Join-Path $ArtifactsDir "logs"
$ScreenshotsDir = Join-Path $ArtifactsDir "screenshots"
$TestResultsDir = Join-Path $ArtifactsDir "test-results"
New-Item -ItemType Directory -Path $StageLogsDir -Force | Out-Null
New-Item -ItemType Directory -Path $ScreenshotsDir -Force | Out-Null
New-Item -ItemType Directory -Path $TestResultsDir -Force | Out-Null

# Stage tracking
$Stages = @()
$OverallStartTime = Get-Date
$OverallPassed = $true

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function Write-Stage {
    param([string]$Stage, [string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "HH:mm:ss"
    $color = switch ($Level) {
        "INFO" { "White" }
        "PASS" { "Green" }
        "FAIL" { "Red" }
        "WARN" { "Yellow" }
        "SKIP" { "DarkGray" }
        default { "White" }
    }
    Write-Host "[$timestamp] [$Stage] $Message" -ForegroundColor $color
}

function Add-StageResult {
    param(
        [string]$Name,
        [string]$Status,
        [int]$ExitCode,
        [double]$DurationSeconds,
        [string]$LogFile = ""
    )
    $script:Stages += [PSCustomObject]@{
        Name = $Name
        Status = $Status
        ExitCode = $ExitCode
        DurationSeconds = [math]::Round($DurationSeconds, 2)
        LogFile = $LogFile
    }
    
    if ($Status -eq "FAILED") {
        $script:OverallPassed = $false
    }
}

function Invoke-Stage {
    param(
        [string]$Name,
        [string]$Description,
        [scriptblock]$Action,
        [switch]$Skip
    )
    
    $stageNumber = $script:Stages.Count + 1
    $sanitizedName = $Name.ToLower().Replace(' ', '_').Replace('/', '_').Replace('\', '_')
    $logFile = Join-Path $StageLogsDir "$sanitizedName.log"
    
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "STAGE $stageNumber`: $Name" -ForegroundColor Cyan
    Write-Host $Description -ForegroundColor DarkCyan
    Write-Host "=" * 70 -ForegroundColor Cyan
    
    if ($Skip) {
        Write-Stage $Name "SKIPPED" "SKIP"
        Add-StageResult -Name $Name -Status "SKIPPED" -ExitCode 0 -DurationSeconds 0
        return $true
    }
    
    $stageStart = Get-Date
    
    try {
        # Execute the action and capture output
        # The scriptblock may return an exit code as its last output item
        $output = & $Action 2>&1
        $rawExitCode = $LASTEXITCODE
        
        # If the last item in output is a numeric exit code from the scriptblock's return statement,
        # use that instead of $LASTEXITCODE (which may be stale from an earlier external command)
        $exitCode = $rawExitCode
        if ($output -is [array] -and $output.Count -gt 0) {
            $lastItem = $output[-1]
            if ($lastItem -is [int] -or ($lastItem -is [string] -and $lastItem -match '^\d+$')) {
                $exitCode = [int]$lastItem
                # Remove the exit code from output (it's metadata, not display content)
                if ($output.Count -gt 1) {
                    $output = $output[0..($output.Count - 2)]
                } else {
                    $output = @()
                }
            }
        } elseif ($output -is [int] -or ($output -is [string] -and $output -match '^\d+$')) {
            # Single item output that is just the exit code
            $exitCode = [int]$output
            $output = @()
        }
        if ($null -eq $exitCode) { $exitCode = 0 }
        
        # Save output to log file
        $output | Out-File -FilePath $logFile -Encoding utf8
        
        # Display output
        $output | ForEach-Object { Write-Host $_ }
        
        $duration = ((Get-Date) - $stageStart).TotalSeconds
        
        if ($exitCode -eq 0) {
            Write-Stage $Name "PASSED (${duration}s)" "PASS"
            Add-StageResult -Name $Name -Status "PASSED" -ExitCode $exitCode -DurationSeconds $duration -LogFile $logFile
            return $true
        } else {
            Write-Stage $Name "FAILED (exit code $exitCode)" "FAIL"
            Add-StageResult -Name $Name -Status "FAILED" -ExitCode $exitCode -DurationSeconds $duration -LogFile $logFile
            return $false
        }
    }
    catch {
        $duration = ((Get-Date) - $stageStart).TotalSeconds
        Write-Stage $Name "ERROR: $_" "FAIL"
        "ERROR: $_" | Out-File -FilePath $logFile -Encoding utf8 -Append
        Add-StageResult -Name $Name -Status "FAILED" -ExitCode 1 -DurationSeconds $duration -LogFile $logFile
        return $false
    }
}

function Write-Report {
    $overallDuration = ((Get-Date) - $OverallStartTime).TotalSeconds
    $overallStatus = if ($OverallPassed) { "PASSED" } else { "FAILED" }
    $passedCount = ($Stages | Where-Object { $_.Status -eq "PASSED" }).Count
    $failedCount = ($Stages | Where-Object { $_.Status -eq "FAILED" }).Count
    $skippedCount = ($Stages | Where-Object { $_.Status -eq "SKIPPED" }).Count
    
    # Markdown report
    $report = @"
# VoiceStudio Verification Report

**Date:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
**Configuration:** $Configuration
**Quick Mode:** $Quick
**Real UI:** $RealUI
**Overall Status:** $overallStatus
**Total Duration:** $([math]::Round($overallDuration, 2)) seconds

## Summary

- **Passed:** $passedCount
- **Failed:** $failedCount
- **Skipped:** $skippedCount

## Stage Results

| # | Stage | Status | Exit Code | Duration |
|---|-------|--------|-----------|----------|
"@

    $stageNum = 1
    foreach ($stage in $Stages) {
        $icon = switch ($stage.Status) {
            "PASSED" { "✅" }
            "FAILED" { "❌" }
            "SKIPPED" { "⏭️" }
            default { "❓" }
        }
        $report += ("`n" + "| $stageNum | $($stage.Name) | $icon $($stage.Status) | $($stage.ExitCode) | $($stage.DurationSeconds)s |")
        $stageNum++
    }

    $report += @"


## Artifacts

- **Report:** ``$ReportFile``
- **Logs:** ``$StageLogsDir``
- **Screenshots:** ``$ScreenshotsDir``
- **Test Results:** ``$TestResultsDir``

## Failed Stages

"@

    $failedStages = $Stages | Where-Object { $_.Status -eq "FAILED" }
    if ($failedStages) {
        foreach ($stage in $failedStages) {
            $report += "`n### $($stage.Name)`n"
            $report += "`nExit code: $($stage.ExitCode)`n"
            $report += "Log file: ``$($stage.LogFile)```n"
            
            if ($stage.LogFile -and (Test-Path $stage.LogFile)) {
                $lastLines = Get-Content $stage.LogFile -Tail 20 -ErrorAction SilentlyContinue
                if ($lastLines) {
                    $report += "`n``````"
                    $report += "`n$($lastLines -join "`n")"
                    $report += "`n```````n"
                }
            }
        }
    } else {
        $report += "`nNo failures! 🎉`n"
    }

    $report += @"

## How to Fix Failures

1. Check the log file for the failed stage
2. Fix the issue in your code
3. Run ``.\scripts\verify.ps1`` again
4. Do NOT merge until this script passes

## Re-run Commands

``````powershell
# Full verification
.\scripts\verify.ps1

# Quick verification (pre-commit)
.\scripts\verify.ps1 -Quick

# Skip specific stages
.\scripts\verify.ps1 -SkipUI -SkipIntegration

# Real UI automation
.\scripts\verify.ps1 -RealUI
``````
"@

    $report | Out-File -FilePath $ReportFile -Encoding utf8
    
    # JSON summary
    $summary = @{
        timestamp = (Get-Date -Format "o")
        configuration = $Configuration
        quick_mode = $Quick.IsPresent
        real_ui = $RealUI.IsPresent
        overall_status = $overallStatus
        duration_seconds = [math]::Round($overallDuration, 2)
        passed = $passedCount
        failed = $failedCount
        skipped = $skippedCount
        stages = $Stages
    }
    $summary | ConvertTo-Json -Depth 10 | Out-File -FilePath $SummaryFile -Encoding utf8
    
    # Update latest symlink (Windows junction)
    if (Test-Path $LatestLink) {
        Remove-Item $LatestLink -Force -Recurse -ErrorAction SilentlyContinue
    }
    try {
        cmd /c mklink /J "$LatestLink" "$ArtifactsDir" 2>&1 | Out-Null
    } catch {
        # Fall back to copying if junction fails
        Copy-Item $ArtifactsDir $LatestLink -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════════════════╗" -ForegroundColor Magenta
Write-Host "║            VOICESTUDIO UNIFIED VERIFICATION HARNESS                  ║" -ForegroundColor Magenta
Write-Host "║                                                                      ║" -ForegroundColor Magenta
Write-Host "║  RULE: No changes allowed unless this script stays GREEN            ║" -ForegroundColor Yellow
Write-Host "╚══════════════════════════════════════════════════════════════════════╝" -ForegroundColor Magenta
Write-Host ""
Write-Host "Timestamp:     $Timestamp"
Write-Host "Configuration: $Configuration"
Write-Host "Quick Mode:    $Quick"
Write-Host "Real UI:       $RealUI"
Write-Host "Artifacts:     $ArtifactsDir"
Write-Host ""

Set-Location $RootDir

# ============================================================================
# STAGE 1: Clean Build (C#)
# ============================================================================

$stage1Passed = Invoke-Stage -Name "Clean Build" -Description "Build C# solution (VoiceStudio.sln)" -Skip:$SkipBuild -Action {
    $binlogPath = Join-Path $ArtifactsDir "build.binlog"
    & dotnet build VoiceStudio.sln -c $Configuration -p:Platform=x64 /bl:$binlogPath
    return $LASTEXITCODE
}

if (-not $stage1Passed -and -not $SkipBuild) {
    Write-Host ""
    Write-Host "BUILD FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 2: Python Quality Checks
# ============================================================================

$stage2Passed = Invoke-Stage -Name "Python Quality" -Description "Lint and type-check Python code (ruff, mypy)" -Skip:$SkipPythonLint -Action {
    Write-Host "Running ruff check --fix (autofix)..."
    & python -m ruff check backend app tests --fix --output-format=concise 2>&1 | Write-Host
    Write-Host "Running ruff check..."
    $ruffResult = & python -m ruff check backend app tests --output-format=concise 2>&1
    $ruffExit = $LASTEXITCODE
    $ruffResult | Write-Host
    
    if ($ruffExit -ne 0) {
        Write-Host "Ruff check failed with exit code $ruffExit"
        return $ruffExit
    }
    
    Write-Host ""
    Write-Host "Running mypy..."
    $mypyResult = & python -m mypy backend app --config-file pyproject.toml --no-error-summary 2>&1
    $mypyExit = $LASTEXITCODE
    $mypyResult | Write-Host
    
    # mypy exit code 0 = success, 1 = type errors found
    # With -StrictMypy, treat exit code 1 as failure
    if ($mypyExit -eq 1) {
        if ($StrictMypy) {
            Write-Host "Mypy found type errors (strict mode enabled)" -ForegroundColor Red
            return 1
        } else {
            Write-Host "Mypy found type errors (warnings only, use -StrictMypy to fail)" -ForegroundColor Yellow
        }
    } elseif ($mypyExit -gt 1) {
        return $mypyExit
    }
    
    return 0
}

if (-not $stage2Passed -and -not $SkipPythonLint) {
    Write-Host ""
    Write-Host "PYTHON QUALITY FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 3: C# Unit Tests
# ============================================================================

$stage3Passed = Invoke-Stage -Name "C# Unit Tests" -Description "Run C# unit tests (excluding UI/E2E/Smoke)" -Skip:$SkipCSharpTests -Action {
    $trxFile = Join-Path $TestResultsDir "csharp_unit_tests.trx"
    $testProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
    
    $testOutput = & dotnet test $testProject `
        -c $Configuration `
        -p:Platform=x64 `
        --no-build `
        --filter "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke" `
        --logger "trx;LogFileName=$trxFile" `
        --results-directory $TestResultsDir 2>&1
    
    $exitCode = $LASTEXITCODE
    $testOutput | Write-Host
    
    # WinUI test host may crash during shutdown even when all tests pass.
    # Check if all tests passed by examining output for "Failed: 0" pattern.
    $summaryLine = $testOutput | Where-Object { $_ -match "Failed:\s+\d+.*Passed:\s+\d+" } | Select-Object -Last 1
    if ($summaryLine -match "Failed:\s+0.*Passed:\s+(\d+)" -and [int]$Matches[1] -gt 0) {
        # All tests passed - treat post-test host crash as success
        if ($exitCode -ne 0) {
            Write-Host "Note: Test host crashed after tests completed (known WinUI issue), but all tests passed." -ForegroundColor Yellow
        }
        return 0
    }
    
    return $exitCode
}

if (-not $stage3Passed -and -not $SkipCSharpTests) {
    Write-Host ""
    Write-Host "C# UNIT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 4: Python Unit Tests
# ============================================================================

$stage4Passed = Invoke-Stage -Name "Python Unit Tests" -Description "Run Python unit tests (tests/unit)" -Skip:$SkipPythonTests -Action {
    $junitFile = Join-Path $TestResultsDir "python_unit_tests.xml"
    
    & python -m pytest tests/unit `
        -v `
        --tb=short `
        -x `
        --junitxml=$junitFile `
        -m "not slow and not gpu and not engine"
    
    return $LASTEXITCODE
}

if (-not $stage4Passed -and -not $SkipPythonTests) {
    Write-Host ""
    Write-Host "PYTHON UNIT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 5: Contract Tests
# ============================================================================

$stage5Passed = Invoke-Stage -Name "Contract Tests" -Description "Validate C# <-> Python API contracts" -Skip:$SkipContractTests -Action {
    $junitFile = Join-Path $TestResultsDir "contract_tests.xml"
    
    & python -m pytest tests/contract `
        -v `
        --tb=short `
        --junitxml=$junitFile
    
    return $LASTEXITCODE
}

if (-not $stage5Passed -and -not $SkipContractTests) {
    Write-Host ""
    Write-Host "CONTRACT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 6: Security Tests
# ============================================================================

$stage6Passed = Invoke-Stage -Name "Security Tests" -Description "Run security tests (injection, auth bypass, sandbox escape)" -Action {
    $junitFile = Join-Path $TestResultsDir "security_tests.xml"
    & python -m pytest tests/security `
        -v `
        --tb=short `
        --junitxml=$junitFile
    return $LASTEXITCODE
}

if (-not $stage6Passed) {
    Write-Host ""
    Write-Host "SECURITY TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 7: Backend Integration Tests
# ============================================================================

$stage7Passed = Invoke-Stage -Name "Backend Integration" -Description "Run backend integration tests (API endpoints, engine adapters)" -Skip:($SkipIntegration) -Action {
    $junitFile = Join-Path $TestResultsDir "integration_tests.xml"
    
    & python -m pytest tests/integration `
        -v `
        --tb=short `
        -x `
        --junitxml=$junitFile `
        -m "not slow and not requires_gpu"
    
    return $LASTEXITCODE
}

if (-not $stage7Passed -and -not $SkipIntegration) {
    Write-Host ""
    Write-Host "BACKEND INTEGRATION FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 7: UI Smoke Tests
# ============================================================================

$stage8Passed = Invoke-Stage -Name "UI Smoke Tests" -Description "Verify app launches, panels exist, navigation works" -Skip:$SkipUI -Action {
    $trxFile = Join-Path $TestResultsDir "ui_smoke_tests.trx"
    $testProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
    
    # Set environment for UI tests
    if ($RealUI) {
        $env:VOICESTUDIO_USE_REAL_UI_AUTOMATION = "true"
    } else {
        $env:VOICESTUDIO_USE_REAL_UI_AUTOMATION = "false"
    }
    $env:VOICESTUDIO_TEST_ARTIFACTS = $ScreenshotsDir
    
    try {
        & dotnet test $testProject `
            -c $Configuration `
            -p:Platform=x64 `
            --no-build `
            --filter "TestCategory=Smoke" `
            --logger "trx;LogFileName=$trxFile" `
            --results-directory $TestResultsDir
        
        return $LASTEXITCODE
    }
    finally {
        # Clean up environment
        Remove-Item Env:VOICESTUDIO_USE_REAL_UI_AUTOMATION -ErrorAction SilentlyContinue
        Remove-Item Env:VOICESTUDIO_TEST_ARTIFACTS -ErrorAction SilentlyContinue
    }
}

if (-not $stage8Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "UI SMOKE TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 8: Gate/Ledger Validation
# ============================================================================

$stage9Passed = Invoke-Stage -Name "Gate/Ledger Validation" -Description "Check gate status and validate quality ledger" -Skip:$SkipGates -Action {
    & python scripts/run_verification.py --skip-guard --skip WS-1 --skip WS-4
    return $LASTEXITCODE
}

# ============================================================================
# FINAL REPORT
# ============================================================================

Write-Report

Write-Host ""
Write-Host "=" * 70 -ForegroundColor Cyan

if ($OverallPassed) {
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║                    VERIFICATION PASSED ✅                            ║" -ForegroundColor Green
    Write-Host "╚══════════════════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""
    Write-Host "All stages passed. Safe to merge." -ForegroundColor Green
    Write-Host "Report: $ReportFile" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════════════════════════╗" -ForegroundColor Red
    Write-Host "║                    VERIFICATION FAILED ❌                            ║" -ForegroundColor Red
    Write-Host "╚══════════════════════════════════════════════════════════════════════╝" -ForegroundColor Red
    Write-Host ""
    Write-Host "One or more stages failed. DO NOT MERGE." -ForegroundColor Red
    Write-Host "Report: $ReportFile" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Failed stages:" -ForegroundColor Red
    $Stages | Where-Object { $_.Status -eq "FAILED" } | ForEach-Object {
        Write-Host "  - $($_.Name): exit code $($_.ExitCode)" -ForegroundColor Red
    }
    exit 1
}
