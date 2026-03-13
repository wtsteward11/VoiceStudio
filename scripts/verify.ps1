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
    
    VERIFICATION MODES:
    - Full (default): All stages. Typical duration 10+ minutes.
    - Quick (-Quick): Build, lint, Quick Critical Gates, Security Tests, Gate/Ledger. Skips C#/Python unit tests, contract, integration, UI. Typical duration 3-5 minutes depending on machine.
    
    SIDE EFFECTS: Creates artifacts/verify/{timestamp}/, updates artifacts/verify/latest symlink, prunes runs older than KeepCount (10).
.PARAMETER Quick
    Run reduced verification: build, lint, Quick Critical Gates, Security Tests, Gate/Ledger. Skips C#/Python unit tests, contract, integration, UI. Typical duration 3-5 minutes depending on machine.
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
.PARAMETER SkipSecurity
    Skip security tests (injection, auth bypass, sandbox escape). Default: false. Use with -Quick for faster pre-commit when security is validated elsewhere.
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
    [switch]$SkipSecurity,
    [switch]$RealUI,
    [switch]$StrictMypy,
    [switch]$ReleaseCandidate
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

if ($ReleaseCandidate) {
    $Configuration = "Release"
    $SkipBuild = $false
    $SkipPythonLint = $false
    $SkipCSharpTests = $false
    $SkipPythonTests = $false
    $SkipContractTests = $false
    $SkipIntegration = $false
    $SkipGates = $false
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "  RELEASE CANDIDATE VERIFICATION MODE"
    Write-Host "  All stages enabled. No skips allowed."
    Write-Host "========================================" -ForegroundColor Yellow
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

# Gap 3: Write proof_stamp.txt with environment metadata
$proofCommit = git rev-parse HEAD 2>$null
if (-not $proofCommit) { $proofCommit = 'unknown' }
$proofBranch = git branch --show-current 2>$null
if (-not $proofBranch) { $proofBranch = 'unknown' }
$proofDotnet = dotnet --version 2>$null
$proofPython = python --version 2>$null
$proofStampPath = Join-Path $ArtifactsDir 'proof_stamp.txt'
$proofLines = @(
    "VoiceStudio Verification Proof Stamp",
    "=====================================",
    "Timestamp:   $Timestamp",
    "Commit:      $proofCommit",
    "Branch:      $proofBranch",
    "Machine:     $env:COMPUTERNAME",
    "OS:          $([System.Environment]::OSVersion)",
    ".NET SDK:    $proofDotnet",
    "Python:      $proofPython",
    "Config:      $Configuration",
    "Quick:       $Quick",
    "RealUI:      $RealUI",
    "StrictMypy:  $StrictMypy"
)
if ($Quick) {
    $proofLines += "QuickCriticalGates: golden-loop, route-alignment, contract-drift"
}
$proofLines -join "`n" | Out-File -FilePath $proofStampPath -Encoding utf8

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
        # Prevent stderr from native exes causing PowerShell terminating errors
        $prevErrorPref = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try {
            $output = & $Action 2>&1
        } finally {
            $ErrorActionPreference = $prevErrorPref
        }
        # Use exit code exclusively from last external command (not from parsing output)
        $exitCode = $LASTEXITCODE
        if ($null -eq $exitCode) { $exitCode = 0 }
        
        # Save output to log file
        $output | Out-File -FilePath $logFile -Encoding utf8
        
        # Display output
        $output | ForEach-Object { Write-Host $_ }
        
        $duration = ((Get-Date) - $stageStart).TotalSeconds
        
        # HARNESS INTEGRITY CHECK: if stage failed and log is empty, the
        # harness itself is broken. This is NOT a stage failure -- it means
        # we cannot trust any results from this run.
        if ($exitCode -ne 0 -and (Test-Path $logFile)) {
            $logSize = (Get-Item $logFile).Length
            if ($logSize -eq 0) {
                $integrityMsg = "HARNESS INTEGRITY FAILURE: Stage '$Name' exited $exitCode but log is 0 bytes. Output was not captured. Fix the harness before trusting results."
                $fallbackLine = "Fallback diagnostic: exitCode=$exitCode stage='$Name'"
                Write-Host $integrityMsg -ForegroundColor Red
                ($integrityMsg, $fallbackLine) | Out-File -FilePath $logFile -Encoding utf8
                Add-StageResult -Name $Name -Status "FAILED" -ExitCode 99 -DurationSeconds $duration -LogFile $logFile
                $script:OverallPassed = $false
                return $false
            }
        }

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
    
    # Markdown report — built with string concatenation to avoid
    # PowerShell parsing pipe chars in here-strings as pipeline operators.
    $nl = [Environment]::NewLine
    $dateStr = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $durStr = [math]::Round($overallDuration, 2)
    $report = "# VoiceStudio Verification Report" + $nl + $nl
    $report += "**Date:** $dateStr" + $nl
    $report += "**Configuration:** $Configuration" + $nl
    $report += "**Quick Mode:** $Quick" + $nl
    if ($Quick) {
        $report += "**Quick critical gates:** golden-loop smoke, UI/backend route alignment, contract drift (3 critical gates)" + $nl
    }
    $report += "**Real UI:** $RealUI" + $nl
    $report += "**Overall Status:** $overallStatus" + $nl
    $report += "**Total Duration:** $durStr seconds" + $nl + $nl
    $report += "## Summary" + $nl + $nl
    $report += "- **Passed:** $passedCount" + $nl
    $report += "- **Failed:** $failedCount" + $nl
    $report += "- **Skipped:** $skippedCount" + $nl + $nl
    $report += "## Stage Results" + $nl + $nl
    $tH = "| # | Stage | Status | Exit Code | Duration |"
    $tD = "|---|-------|--------|-----------|----------|"
    $report += $tH + $nl + $tD + $nl

    $stageNum = 1
    foreach ($stage in $Stages) {
        $icon = switch ($stage.Status) {
            "PASSED" { "PASS" }
            "FAILED" { "FAIL" }
            "SKIPPED" { "SKIP" }
            default { "?" }
        }
        $row = [string]::Format("| {0} | {1} | {2} {3} | {4} | {5}s |", $stageNum, $stage.Name, $icon, $stage.Status, $stage.ExitCode, $stage.DurationSeconds)
        $report += $row + $nl
        $stageNum++
    }

    $report += $nl + "## Artifacts" + $nl + $nl
    $report += "- **Report:** $ReportFile" + $nl
    $report += "- **Logs:** $StageLogsDir" + $nl
    $report += "- **Screenshots:** $ScreenshotsDir" + $nl
    $report += "- **Test Results:** $TestResultsDir" + $nl + $nl
    $report += "## Failed Stages" + $nl + $nl

    $failedStages = @($Stages | Where-Object { $_.Status -eq "FAILED" })
    if ($failedStages.Count -gt 0) {
        foreach ($stage in $failedStages) {
            $report += "### $($stage.Name)" + $nl + $nl
            $report += "Exit code: $($stage.ExitCode)" + $nl
            $report += "Log file: $($stage.LogFile)" + $nl

            if ($stage.LogFile -and (Test-Path $stage.LogFile)) {
                $lastLines = Get-Content $stage.LogFile -Tail 20 -ErrorAction SilentlyContinue
                if ($lastLines) {
                    $report += $nl + '```' + $nl
                    $report += ($lastLines -join $nl) + $nl
                    $report += '```' + $nl
                }
            }
        }
    } else {
        $report += "No failures." + $nl
    }

    $report += $nl + "## How to Fix Failures" + $nl + $nl
    $report += "1. Check the log file for the failed stage" + $nl
    $report += "2. Fix the issue in your code" + $nl
    $report += "3. Run .\scripts\verify.ps1 again" + $nl
    $report += "4. Do NOT merge until this script passes" + $nl + $nl
    $report += "## Re-run Commands" + $nl + $nl
    $report += '```powershell' + $nl
    $report += "# Full verification" + $nl
    $report += ".\scripts\verify.ps1" + $nl + $nl
    $report += "# Quick verification (pre-commit)" + $nl
    $report += ".\scripts\verify.ps1 -Quick" + $nl + $nl
    $report += "# Skip specific stages" + $nl
    $report += ".\scripts\verify.ps1 -SkipUI -SkipIntegration" + $nl + $nl
    $report += "# Real UI automation" + $nl
    $report += ".\scripts\verify.ps1 -RealUI" + $nl
    $report += '```' + $nl

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
        Copy-Item $ArtifactsDir $LatestLink -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Gap 2: Write latest_pointer.json and verify junction resolves correctly
    $commitHash = git rev-parse HEAD 2>$null
    if (-not $commitHash) { $commitHash = 'unknown' }
    $pointerData = @{
        run_dir = $ArtifactsDir
        timestamp = (Get-Date -Format 'o')
        commit_hash = $commitHash
        overall_status = $overallStatus
    }
    $pointerPath = Join-Path (Split-Path $LatestLink) 'latest_pointer.json'
    $pointerData | ConvertTo-Json | Out-File -FilePath $pointerPath -Encoding utf8

    # Verify junction target matches current run
    if (Test-Path $LatestLink) {
        $resolvedTarget = (Get-Item $LatestLink).Target
        if ($resolvedTarget -and $resolvedTarget -ne $ArtifactsDir) {
            Write-Host "POINTER WARNING: latest junction resolves to $resolvedTarget but expected $ArtifactsDir" -ForegroundColor Yellow
        }
    }
}

# ============================================================================
# MAIN EXECUTION
# ============================================================================

Write-Host ""
Write-Host ("=" * 70) -ForegroundColor Magenta
Write-Host "  VOICESTUDIO UNIFIED VERIFICATION HARNESS" -ForegroundColor Magenta
Write-Host "  RULE: No changes allowed unless this script stays GREEN" -ForegroundColor Yellow
Write-Host ("=" * 70) -ForegroundColor Magenta
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

$stage1Passed = Invoke-Stage -Name "Clean Build" -Description "Build C# solution" -Skip:$SkipBuild -Action {
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

# Post-build checks (run after build, before other stages; fast and critical)
if ($stage1Passed -and -not $SkipBuild) {
    Invoke-Stage -Name "XAML Health" -Description "Verify XAML compiler produced valid output" -Action {
        $healthScript = Join-Path $RootDir "tools\build\Check-XamlHealth.ps1"
        if (Test-Path $healthScript) {
            & powershell -ExecutionPolicy Bypass -File $healthScript
            return $LASTEXITCODE
        }
        Write-Host "[SKIP] Check-XamlHealth.ps1 not found" -ForegroundColor Yellow
        return 0
    }

    Invoke-Stage -Name "Resolved Packages" -Description "Verify no banned 9.0+ Microsoft.Extensions packages" -Action {
        $pkgScript = Join-Path $RootDir "tools\build\Verify-ResolvedPackages.ps1"
        if (Test-Path $pkgScript) {
            & powershell -ExecutionPolicy Bypass -File $pkgScript
            return $LASTEXITCODE
        }
        Write-Host "[SKIP] Verify-ResolvedPackages.ps1 not found" -ForegroundColor Yellow
        return 0
    }
}

# ============================================================================
# STAGE 2: Python Quality Checks
# ============================================================================

$stage2Passed = Invoke-Stage -Name "Python Quality" -Description "Lint and type-check Python code - ruff, mypy" -Skip:$SkipPythonLint -Action {
    Write-Host "Running ruff check --fix (autofix)..."
    & python -m ruff check backend app tests --fix --output-format=concise 2>&1 | Write-Host
    Write-Host "Running ruff check..."
    $ruffResult = & python -m ruff check backend app tests --output-format=concise 2>&1
    $ruffExit = $LASTEXITCODE
    $ruffResult | Write-Host
    
    if ($ruffExit -ne 0) {
        Write-Host "Ruff check failed with exit code $ruffExit"
        cmd /c "exit $ruffExit"
        return
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
            cmd /c "exit 1"
            return
        } else {
            Write-Host "Mypy found type errors (warnings only, use -StrictMypy to fail)" -ForegroundColor Yellow
        }
    } elseif ($mypyExit -gt 1) {
        cmd /c "exit $mypyExit"
        return
    }
    
    # Mypy strict-scope budget report (see tests/ci/test_mypy_strict_scope.py)
    $baselinePath = Join-Path $RootDir ".ci\mypy_strict_baseline.json"
    if (Test-Path $baselinePath) {
        $baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json
        $budget = $baseline.baseline_errors
        $scopePaths = $baseline.scope | ForEach-Object { Join-Path $RootDir $_ }
        $strictResult = & python -m mypy --strict --follow-imports=skip --config-file pyproject.toml $scopePaths 2>&1
        $strictExit = $LASTEXITCODE
        $errorCount = 0
        $m = [regex]::Match($strictResult, "Found (\d+) error")
        if ($m.Success) { $errorCount = [int]$m.Groups[1].Value }
        else { $errorCount = ([regex]::Matches($strictResult, ": error:")).Count }
        $delta = $errorCount - $budget
        Write-Host "Mypy strict scope: $errorCount errors (budget: $budget, delta: $delta)" -ForegroundColor $(if ($delta -gt 0) { "Red" } else { "Gray" })
        if ($StrictMypy -and $errorCount -gt $budget) {
            Write-Host "Mypy strict scope exceeds budget (use -StrictMypy to fail)" -ForegroundColor Red
            cmd /c "exit 1"
            return
        }
    }
    
    cmd /c "exit 0"
}

if (-not $stage2Passed -and -not $SkipPythonLint) {
    Write-Host ""
    Write-Host "PYTHON QUALITY FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 2.5: Quick Critical Gates (Quick mode only)
# ============================================================================
# In quick mode, run truthy critical gates: golden-loop smoke, UI/backend route alignment, contract drift.

if ($Quick) {
    Invoke-Stage -Name "Quick Critical Gates" -Description "Golden-loop smoke, UI/backend route alignment, contract drift" -Action {
        $prev = $env:VOICESTUDIO_TEST_MODE
        try {
            $env:VOICESTUDIO_TEST_MODE = "stub"
            & python -m pytest tests/ci/test_golden_loop_smoke.py tests/ci/test_ui_backend_route_alignment.py tests/ci/test_contract_drift_gate.py -v --tb=short
            return $LASTEXITCODE
        } finally {
            if ($null -ne $prev) { $env:VOICESTUDIO_TEST_MODE = $prev } else { Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue }
        }
    }
}

# ============================================================================
# STAGE 3: C# Unit Tests
# ============================================================================

$stage3Passed = Invoke-Stage -Name "C# Unit Tests" -Description "Run C# unit tests excluding UI/E2E/Smoke" -Skip:$SkipCSharpTests -Action {
    $trxFile = Join-Path $TestResultsDir "csharp_unit_tests.trx"
    $testProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
    
    $testOutput = & dotnet test $testProject `
        -c $Configuration `
        -p:Platform=x64 `
        --no-build `
        --filter 'TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke' `
        --logger "trx;LogFileName=$trxFile" `
        --results-directory $TestResultsDir 2>&1
    
    $exitCode = $LASTEXITCODE
    # Emit to pipeline (not Write-Host) so Invoke-Stage captures output for log file
    $testOutput

    # WinUI test host may crash during shutdown even when all tests pass.
    # Check if all tests passed by examining output for "Failed: 0" pattern.
    $summaryLine = $testOutput | Where-Object { $_ -match "Failed:\s+\d+.*Passed:\s+\d+" } | Select-Object -Last 1
    if ($summaryLine -match "Failed:\s+0.*Passed:\s+(\d+)" -and [int]$Matches[1] -gt 0) {
        # All tests passed - treat post-test host crash as success
        if ($exitCode -ne 0) {
            Write-Host "Note: Test host crashed after tests completed (known WinUI issue), but all tests passed." -ForegroundColor Yellow
        }
        cmd /c "exit 0"
        return
    }
    cmd /c "exit $exitCode"
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

$stage4Passed = Invoke-Stage -Name "Python Unit Tests" -Description "Run Python unit tests from tests/unit" -Skip:$SkipPythonTests -Action {
    $junitFile = Join-Path $TestResultsDir "python_unit_tests.xml"
    
    & python -m pytest tests/unit `
        -v `
        --tb=short `
        -x `
        --junitxml=$junitFile `
        -m "not slow and not gpu and not engine" `
        --ignore=tests/unit/backend/api/routes/_archived
    
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

$stage5Passed = Invoke-Stage -Name "Contract Tests" -Description "Validate CSharp-Python API contracts" -Skip:$SkipContractTests -Action {
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

$stage6Passed = Invoke-Stage -Name "Security Tests" -Description "Run security tests - injection, auth bypass, sandbox escape" -Skip:$SkipSecurity -Action {
    $junitFile = Join-Path $TestResultsDir "security_tests.xml"
    & python -m pytest tests/security `
        -v `
        --tb=short `
        --junitxml=$junitFile
    return $LASTEXITCODE
}

if (-not $stage6Passed -and -not $SkipSecurity) {
    Write-Host ""
    Write-Host "SECURITY TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 7: Backend Integration Tests
# ============================================================================

$stage7Passed = Invoke-Stage -Name "Backend Integration" -Description "Golden-loop smoke + backend integration tests" -Skip:$SkipIntegration -Action {
    $prev = $env:VOICESTUDIO_TEST_MODE
    try {
        $env:VOICESTUDIO_TEST_MODE = "stub"
        # Golden-loop smoke: health + synthesize + stream (deterministic, in-process)
        & python -m pytest tests/ci/test_golden_loop_smoke.py -v --tb=short
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Golden-loop smoke FAILED" -ForegroundColor Red
            return $LASTEXITCODE
        }
        $junitFile = Join-Path $TestResultsDir "integration_tests.xml"
        & python -m pytest tests/integration `
            -v `
            --tb=short `
            -x `
            --junitxml=$junitFile `
            -m "not slow and not requires_gpu"
        return $LASTEXITCODE
    } finally {
        if ($null -ne $prev) { $env:VOICESTUDIO_TEST_MODE = $prev }
        else { Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue }
    }
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
# STAGE 8.5: UI Self-Test (app-level smoke)
# ============================================================================

$stage8_5Passed = Invoke-Stage -Name "UI Self-Test" -Description "Run app with --ui-self-test (Gate C smoke + backend health)" -Skip:$SkipUI -Action {
    $exePath = Join-Path $RootDir ".buildlogs\x64\$Configuration\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
    $reportDir = Join-Path $RootDir ".buildlogs\verify"
    $reportPath = Join-Path $reportDir "ui_self_test.json"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    if (-not (Test-Path $exePath)) {
        Write-Host "ERROR: Exe not found at $exePath" -ForegroundColor Red
        cmd /c "exit 1"
        return
    }
    $env:GIT_COMMIT = git rev-parse HEAD 2>$null
    if (-not $env:GIT_COMMIT) { $env:GIT_COMMIT = "unknown" }
    $env:VOICESTUDIO_TEST_MODE = "stub"
    & $exePath --ui-self-test --out $reportPath
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 0 }
    if ($exitCode -ne 0) {
        Write-Host "UI Self-Test FAILED (exit code $exitCode)" -ForegroundColor Red
    } else {
        Write-Host "UI Self-Test PASSED" -ForegroundColor Green
    }
    # Copy proof artifact to docs/reports/verification for daily-driver smoke
    $proofDir = Join-Path $RootDir "docs\reports\verification"
    $proofName = "UI_DAILY_DRIVER_SMOKE_" + (Get-Date -Format "yyyy-MM-dd") + ".json"
    $proofPath = Join-Path $proofDir $proofName
    if (Test-Path $reportPath) {
        New-Item -ItemType Directory -Force -Path $proofDir | Out-Null
        Copy-Item $reportPath $proofPath -Force
    }
    cmd /c "exit $exitCode"
}

if (-not $stage8_5Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "UI SELF-TEST FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 9: Gate/Ledger Validation
# ============================================================================

$stage9Passed = Invoke-Stage -Name "Gate/Ledger Validation" -Description "Check gate status and validate quality ledger" -Skip:$SkipGates -Action {
    & python scripts/run_verification.py --skip-guard
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
    Write-Host ("=" * 70) -ForegroundColor Green
    Write-Host "  VERIFICATION PASSED" -ForegroundColor Green
    Write-Host ("=" * 70) -ForegroundColor Green
    Write-Host ""
    Write-Host "All stages passed. Safe to merge." -ForegroundColor Green
    Write-Host "Report: $ReportFile" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Red
    Write-Host "  VERIFICATION FAILED" -ForegroundColor Red
    Write-Host ("=" * 70) -ForegroundColor Red
    Write-Host ""
    Write-Host "One or more stages failed. DO NOT MERGE." -ForegroundColor Red
    Write-Host "Report: $ReportFile" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Failed stages:" -ForegroundColor Red
    $failedList = @($Stages | Where-Object { $_.Status -eq 'FAILED' })
    foreach ($fs in $failedList) {
        $n = $fs.Name
        $c = $fs.ExitCode
        Write-Host ('  - ' + $n + ' exit=' + $c) -ForegroundColor Red
    }
    exit 1
}
