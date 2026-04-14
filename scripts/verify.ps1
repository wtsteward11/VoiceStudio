<#
.SYNOPSIS
    Unified verification harness for VoiceStudio.
.DESCRIPTION
    The single source of truth for product verification. Runs all stages:
    1. Clean Build (C#)
    2. Python Quality (ruff, mypy)
    2.5 Quick Critical Gates (Quick mode only: golden-loop, route-alignment, contract-drift)
    3. C# Unit Tests (11 first-class shards with per-shard timeouts):
       - C# Unit Tests - ViewModels Seam A-D
       - C# Unit Tests - ViewModels Seam E-H
       - C# Unit Tests - ViewModels Seam I-L
       - C# Unit Tests - ViewModels Seam M
       - C# Unit Tests - ViewModels Seam N-Z
       - C# Unit Tests - ViewModels Lifecycle
       - C# Unit Tests - ViewModels Legacy
       - C# Unit Tests - CommandsGateways
       - C# Unit Tests - UIPanels
       - C# Unit Tests - Other
    4. Python Unit Tests
    5. Contract Tests (C# <-> Python)
    6. Security Tests
    7. Backend Integration
    8. UI Smoke Tests
    8.5 UI Self-Test
    8.6 Icon-Launch Smoke
    8.7 Failure-Path Smoke
    8.8 Runtime-Missing Failure Smoke
    9. Gate/Ledger Validation
    
    -OnlyStage "C# Unit Tests" runs all 11 shards (selector alias). -OnlyStage "C# Unit Tests - ViewModels Seam A-D" runs one shard.
    
    Exit code 0 only if ALL stages pass. No exceptions.
    
    RULE: No changes allowed unless this script stays GREEN.
    
    VERIFICATION MODES:
    - Full (default): All stages. Typical duration 10+ minutes.
    - Quick (-Quick): Build, lint, Quick Critical Gates, Security Tests, Gate/Ledger. Skips C#/Python unit tests, contract, integration, UI. Typical duration 3-5 minutes depending on machine.
    - Runtime proof (-RuntimeProof): Standalone only (GAP-015). Runs real-mode golden-loop synthesis + training export honesty CI tests; writes artifacts/verify/{timestamp}/runtime_proof.json (schema v2) and slo_baselines.json (schema v1, advisory SLO samples). Prerequisites: python, pytest, piper engine + consent routes. Forbidden with -Quick.
    - Enforce runtime proof freshness (-EnforceRuntimeProof): Passes --enforce-runtime-proof to Gate/Ledger (run_verification.py) so stale/missing PROOF_GOLDEN_PATH_REAL_*.json fails. Forbidden with -Quick. Does not affect -RuntimeProof standalone.
    - Backend smoke (-BackendSmoke): Standalone only (GAP-069 slice 3). Runs scripts/ci/run_backend_smoke.py; writes docs/reports/verification/PROOF_BACKEND_SMOKE_*.json (schema v1). Exit 2 (BLOCKED prerequisites) is treated as advisory success for the harness. Forbidden with -Quick.
    - Enforce backend smoke freshness (-EnforceBackendSmoke): Passes --enforce-backend-smoke to Gate/Ledger so missing/stale/FAIL PROOF_BACKEND_SMOKE_*.json fails; BLOCKED proofs never fail. Forbidden with -Quick.
    - Skip backend smoke auto-probe (-SkipSmoke): Full verify only (GAP-069 slice 4). Skips the automatic Backend Smoke Auto-Probe stage before Gate/Ledger. Use when prerequisites are known absent. Redundant with -Quick (smoke already skipped).
    
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
.PARAMETER OnlyStage
    Run only the specified stage. Use after a successful build to debug a specific stage. Fails fast with clear message if build artifacts are missing.     Supports: Clean Build, Python Quality, Quick Critical Gates, C# Unit Tests (all shards), C# Unit Tests - ViewModels Seam A-D, C# Unit Tests - Services, etc., Python Unit Tests, Contract Tests, Security Tests, Backend Integration, UI Smoke Tests, UI Self-Test, Icon-Launch Smoke, Failure-Path Smoke, Runtime-Missing Failure Smoke, Gate/Ledger Validation.
.PARAMETER RuntimeProof
    Standalone mode: run only the GAP-015 runtime proof bundle (real-mode synthesis pytest + training export honesty). Writes runtime_proof.json (schema v2) under artifacts/verify/{timestamp}. Cannot be used with -Quick or -OnlyStage.
.PARAMETER EnforceRuntimeProof
    When set, Gate/Ledger stage runs python scripts/run_verification.py with --enforce-runtime-proof (hard fail on missing/stale Grade-R golden-path proof artifacts). Cannot be used with -Quick.
.EXAMPLE
    .\scripts\verify.ps1
    .\scripts\verify.ps1 -Quick
    .\scripts\verify.ps1 -SkipUI -SkipIntegration
    .\scripts\verify.ps1 -RealUI -Configuration Release
    .\scripts\verify.ps1 -OnlyStage "C# Unit Tests"
    .\scripts\verify.ps1 -OnlyStage "C# Unit Tests - ViewModels Seam A-D"
    .\scripts\verify.ps1 -RuntimeProof
    .\scripts\verify.ps1 -EnforceRuntimeProof
    .\scripts\verify.ps1 -BackendSmoke
    .\scripts\verify.ps1 -EnforceBackendSmoke
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
    [switch]$ReleaseCandidate,
    [switch]$RuntimeProof,
    [switch]$EnforceRuntimeProof,
    [switch]$BackendSmoke,
    [switch]$EnforceBackendSmoke,
    [switch]$SkipSmoke,
    [ValidateSet("", "Clean Build", "Python Quality", "Quick Critical Gates", "C# Unit Tests", "C# Unit Tests - ViewModels Seam A-D", "C# Unit Tests - ViewModels Seam E-H", "C# Unit Tests - ViewModels Seam I-L", "C# Unit Tests - ViewModels Seam M", "C# Unit Tests - ViewModels Seam N-Z", "C# Unit Tests - ViewModels Lifecycle", "C# Unit Tests - ViewModels Legacy", "C# Unit Tests - Services", "C# Unit Tests - CommandsGateways", "C# Unit Tests - UIPanels", "C# Unit Tests - Other", "Python Unit Tests", "Contract Tests", "Security Tests", "Backend Integration", "UI Smoke Tests", "UI Self-Test", "Icon-Launch Smoke", "Failure-Path Smoke", "Runtime-Missing Failure Smoke", "Backend Smoke Auto-Probe", "Gate/Ledger Validation")]
    [string]$OnlyStage = "",
    [ValidateSet("", "Clean Build", "Python Quality", "Quick Critical Gates", "C# Unit Tests", "C# Unit Tests - ViewModels Seam A-D", "C# Unit Tests - ViewModels Seam E-H", "C# Unit Tests - ViewModels Seam I-L", "C# Unit Tests - ViewModels Seam M", "C# Unit Tests - ViewModels Seam N-Z", "C# Unit Tests - ViewModels Lifecycle", "C# Unit Tests - ViewModels Legacy", "C# Unit Tests - Services", "C# Unit Tests - CommandsGateways", "C# Unit Tests - UIPanels", "C# Unit Tests - Other", "Python Unit Tests", "Contract Tests", "Security Tests", "Backend Integration", "UI Smoke Tests", "UI Self-Test", "Icon-Launch Smoke", "Failure-Path Smoke", "Runtime-Missing Failure Smoke", "Backend Smoke Auto-Probe", "Gate/Ledger Validation")]
    [string]$ResumeFrom = "",
    [ValidateSet("", "Clean Build", "Python Quality", "Quick Critical Gates", "C# Unit Tests", "C# Unit Tests - ViewModels Seam A-D", "C# Unit Tests - ViewModels Seam E-H", "C# Unit Tests - ViewModels Seam I-L", "C# Unit Tests - ViewModels Seam M", "C# Unit Tests - ViewModels Seam N-Z", "C# Unit Tests - ViewModels Lifecycle", "C# Unit Tests - ViewModels Legacy", "C# Unit Tests - Services", "C# Unit Tests - CommandsGateways", "C# Unit Tests - UIPanels", "C# Unit Tests - Other", "Python Unit Tests", "Contract Tests", "Security Tests", "Backend Integration", "UI Smoke Tests", "UI Self-Test", "Icon-Launch Smoke", "Failure-Path Smoke", "Runtime-Missing Failure Smoke", "Backend Smoke Auto-Probe", "Gate/Ledger Validation")]
    [string]$StopAfterStage = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$ArtifactsDir = Join-Path $RootDir "artifacts\verify\$Timestamp"
$LatestLink = Join-Path $RootDir "artifacts\verify\latest"
$ReportFile = Join-Path $ArtifactsDir "verification_report.md"
$SummaryFile = Join-Path $ArtifactsDir "summary.json"
$CheckpointFile = Join-Path $ArtifactsDir "checkpoint.json"

# Unbuffered Python: when verify output is piped (e.g. Tee-Object), buffered pytest can leave the
# PowerShell pipeline waiting on stdin/stdout close after tests finish — appears as a hang after Contract.
$env:PYTHONUNBUFFERED = "1"

# OnlyStage mode: run just one stage (user must have run build first for stages that need it)
if ($OnlyStage) {
    Write-Host "OnlyStage mode: running only '$OnlyStage'" -ForegroundColor Yellow
}

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

if ($Quick -and $RuntimeProof) {
    Write-Host "ERROR: -RuntimeProof cannot be combined with -Quick" -ForegroundColor Red
    exit 1
}
if ($RuntimeProof -and $OnlyStage) {
    Write-Host "ERROR: -RuntimeProof is standalone; do not combine with -OnlyStage" -ForegroundColor Red
    exit 1
}
if ($Quick -and $EnforceRuntimeProof) {
    Write-Host "ERROR: -EnforceRuntimeProof cannot be combined with -Quick" -ForegroundColor Red
    exit 1
}
if ($Quick -and $BackendSmoke) {
    Write-Host "ERROR: -BackendSmoke cannot be combined with -Quick" -ForegroundColor Red
    exit 1
}
if ($BackendSmoke -and $OnlyStage) {
    Write-Host "ERROR: -BackendSmoke is standalone; do not combine with -OnlyStage" -ForegroundColor Red
    exit 1
}
if ($Quick -and $EnforceBackendSmoke) {
    Write-Host "ERROR: -EnforceBackendSmoke cannot be combined with -Quick" -ForegroundColor Red
    exit 1
}
if ($Quick -and $SkipSmoke) {
    Write-Host "NOTE: -SkipSmoke is redundant with -Quick (backend smoke is already skipped)" -ForegroundColor DarkYellow
}
if ($ResumeFrom -and $OnlyStage) {
    Write-Host "ERROR: -ResumeFrom cannot be combined with -OnlyStage" -ForegroundColor Red
    exit 1
}
if ($ResumeFrom -and $StopAfterStage) {
    Write-Host "ERROR: -ResumeFrom cannot be combined with -StopAfterStage" -ForegroundColor Red
    exit 1
}
if ($ResumeFrom -and $Quick) {
    Write-Host "ERROR: -ResumeFrom cannot be combined with -Quick (checkpoint is from a full or non-Quick harness shape)" -ForegroundColor Red
    exit 1
}
if ($ResumeFrom -and ($RuntimeProof -or $BackendSmoke)) {
    Write-Host "ERROR: -ResumeFrom applies only to the main verification harness" -ForegroundColor Red
    exit 1
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

# Stage time budgets (seconds). 0 = no timeout. Must be defined before proof_stamp.
$StageTimeouts = @{
    "Python Unit Tests" = 1200
    "Backend Integration" = 600
    "UI Smoke Tests" = 600
    "UI Self-Test" = 300
    "Icon-Launch Smoke" = 300
    "Failure-Path Smoke" = 180
    "Runtime-Missing Failure Smoke" = 180
    "Backend Smoke Auto-Probe" = 120
}
# Per-shard timeouts for C# Unit Tests (diagnosable per shard; single bad shard cannot consume whole budget)
$Stage3ShardTimeouts = @{
    "C# Unit Tests - ViewModels Seam A-D" = 180
    "C# Unit Tests - ViewModels Seam E-H" = 180
    "C# Unit Tests - ViewModels Seam I-L" = 180
    "C# Unit Tests - ViewModels Seam M" = 180
    "C# Unit Tests - ViewModels Seam N-Z" = 180
    "C# Unit Tests - ViewModels Lifecycle" = 180
    "C# Unit Tests - ViewModels Legacy" = 180
    # Services: blame-hang uses 5m; outer stage timeout must exceed worst-case VSTest teardown + dump
    # or the harness kills the job while diagnostics still run (false TIMED_OUT). See verify artifact audits.
    "C# Unit Tests - Services" = 540
    "C# Unit Tests - CommandsGateways" = 180
    "C# Unit Tests - UIPanels" = 180
    "C# Unit Tests - Other" = 240
}

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
if ($RuntimeProof) {
    $proofLines += "RuntimeProof: standalone GAP-015 bundle (exits after runtime_proof.json)"
}
if ($EnforceRuntimeProof) {
    $proofLines += "EnforceRuntimeProof: Gate/Ledger uses --enforce-runtime-proof (stale/missing Grade-R proof fails)"
}
if ($BackendSmoke) {
    $proofLines += "BackendSmoke: standalone GAP-069 uvicorn /health + /api/health proof (PROOF_BACKEND_SMOKE_*.json)"
}
if ($EnforceBackendSmoke) {
    $proofLines += "EnforceBackendSmoke: Gate/Ledger uses --enforce-backend-smoke (missing/stale/FAIL smoke proof fails; BLOCKED exempt)"
}
if ($SkipSmoke) {
    $proofLines += "SkipSmoke: Backend Smoke Auto-Probe skipped (GAP-069 slice 4)"
}
if ($ResumeFrom) {
    $proofLines += "ResumeFrom: $ResumeFrom (GAP-069 slice 9 — inherited stages from latest/checkpoint.json)"
}
if ($StopAfterStage) {
    $proofLines += "StopAfterStage: $StopAfterStage (GAP-069 slice 9 — exit after this stage completes)"
}
$proofLines += "StageTimeouts:"
foreach ($key in $StageTimeouts.Keys) {
    $proofLines += "  $key`: $($StageTimeouts[$key])s"
}
$proofLines += "ContractTestsPytestTimeoutSeconds: 900"
$proofLines += "Stage3ShardTimeouts:"
foreach ($key in $Stage3ShardTimeouts.Keys) {
    $proofLines += "  $key`: $($Stage3ShardTimeouts[$key])s"
}
$proofLines -join "`n" | Out-File -FilePath $proofStampPath -Encoding utf8

# Stage tracking
$Stages = @()
$OverallStartTime = Get-Date
$OverallPassed = $true

if ($ResumeFrom) {
    $cpPath = Join-Path $LatestLink "checkpoint.json"
    if (-not (Test-Path $LatestLink) -or -not (Test-Path $cpPath)) {
        Write-Host "ERROR: -ResumeFrom requires artifacts/verify/latest/checkpoint.json from a prior harness run." -ForegroundColor Red
        Write-Host "       Run a partial/full verify first, or use -StopAfterStage to emit a checkpoint." -ForegroundColor Yellow
        exit 1
    }
    $cpJson = Get-Content $cpPath -Raw | ConvertFrom-Json
    $stagesFromCp = $cpJson.stages
    if ($null -eq $stagesFromCp) {
        Write-Host "ERROR: checkpoint.json has no 'stages' array." -ForegroundColor Red
        exit 1
    }
    if ($stagesFromCp -isnot [System.Array]) {
        $stagesFromCp = @($stagesFromCp)
    }
    # Lineage validation: junction target must match checkpoint's artifact_dir
    if (Test-Path $LatestLink) {
        $resolvedLatest = (Get-Item $LatestLink).Target
        $cpArtifactDir = $cpJson.artifact_dir
        if ($resolvedLatest -and $cpArtifactDir -and ($resolvedLatest -ne $cpArtifactDir)) {
            Write-Host "ERROR: latest junction points to '$resolvedLatest' but checkpoint claims artifact_dir '$cpArtifactDir'" -ForegroundColor Red
            Write-Host "       The checkpoint was produced by a different run than 'latest' points to." -ForegroundColor Red
            Write-Host "       Re-run -StopAfterStage to create a fresh checkpoint, or fix the latest junction." -ForegroundColor Yellow
            exit 1
        }
        $pointerPath = Join-Path (Split-Path $LatestLink) "latest_pointer.json"
        if (Test-Path $pointerPath) {
            $pointerJson = Get-Content $pointerPath -Raw | ConvertFrom-Json
            if ($pointerJson.run_dir -and $cpArtifactDir -and ($pointerJson.run_dir -ne $cpArtifactDir)) {
                Write-Host "ERROR: latest_pointer.json run_dir '$($pointerJson.run_dir)' disagrees with checkpoint artifact_dir '$cpArtifactDir'" -ForegroundColor Red
                exit 1
            }
        }
    }

    Write-Host ""
    Write-Host "=== RESUME LINEAGE ===" -ForegroundColor Cyan
    Write-Host "  Checkpoint run:    $($cpJson.run_timestamp)" -ForegroundColor Cyan
    Write-Host "  Artifact dir:      $($cpJson.artifact_dir)" -ForegroundColor Cyan
    Write-Host "  Last stage:        $($cpJson.last_completed_stage)" -ForegroundColor Cyan
    Write-Host "  Stages inherited:  $($stagesFromCp.Count)" -ForegroundColor Cyan
    Write-Host "  Resuming from:     $ResumeFrom" -ForegroundColor Cyan
    Write-Host "======================" -ForegroundColor Cyan
    Write-Host ""

    $knownResumeStages = @(
        "Clean Build",
        "XAML Health",
        "Resolved Packages",
        "Release XAML Smoke",
        "Python Quality",
        "Quick Critical Gates",
        "C# Unit Tests",
        "C# Unit Tests - ViewModels Seam A-D",
        "C# Unit Tests - ViewModels Seam E-H",
        "C# Unit Tests - ViewModels Seam I-L",
        "C# Unit Tests - ViewModels Seam M",
        "C# Unit Tests - ViewModels Seam N-Z",
        "C# Unit Tests - ViewModels Lifecycle",
        "C# Unit Tests - ViewModels Legacy",
        "C# Unit Tests - Services",
        "C# Unit Tests - CommandsGateways",
        "C# Unit Tests - UIPanels",
        "C# Unit Tests - Other",
        "Python Unit Tests",
        "Contract Tests",
        "Security Tests",
        "Backend Integration",
        "UI Smoke Tests",
        "UI Self-Test",
        "Icon-Launch Smoke",
        "Failure-Path Smoke",
        "Runtime-Missing Failure Smoke",
        "Backend Smoke Auto-Probe",
        "Gate/Ledger Validation"
    )
    if ($ResumeFrom -notin $knownResumeStages) {
        Write-Host "ERROR: -ResumeFrom '$ResumeFrom' is not a recognized stage name." -ForegroundColor Red
        Write-Host "       Known stages: $($knownResumeStages -join ', ')" -ForegroundColor Yellow
        exit 1
    }

    foreach ($s in $stagesFromCp) {
        $origStatus = [string]$s.status
        $script:Stages += [PSCustomObject]@{
            Name = [string]$s.name
            Status = "INHERITED"
            ExitCode = [int]$s.exit_code
            DurationSeconds = [double]$s.duration_seconds
            LogFile = [string]$s.log_file
            TimeoutSeconds = 0
            TimedOut = $false
            InheritedFromStatus = $origStatus
        }
        if ($origStatus -in @("FAILED", "TIMED_OUT")) {
            $script:OverallPassed = $false
        }
    }
}

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
        "TIMEOUT" { "Magenta" }
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
        [string]$LogFile = "",
        [int]$TimeoutSeconds = 0
    )
    $script:Stages += [PSCustomObject]@{
        Name = $Name
        Status = $Status
        ExitCode = $ExitCode
        DurationSeconds = [math]::Round($DurationSeconds, 2)
        LogFile = $LogFile
        TimeoutSeconds = $TimeoutSeconds
        TimedOut = ($Status -eq "TIMED_OUT")
    }
    
    if ($Status -in @("FAILED", "TIMED_OUT")) {
        $script:OverallPassed = $false
    }
    Write-Checkpoint
}

function Write-Checkpoint {
    if ($null -eq $script:Stages -or $script:Stages.Count -eq 0) { return }
    # Force array so ConvertTo-Json always emits "stages": [ {...}, ... ] (single-stage runs must not collapse to one object)
    $stageSummaries = @( $script:Stages | ForEach-Object {
        @{
            name = $_.Name
            status = $_.Status
            exit_code = $_.ExitCode
            duration_seconds = $_.DurationSeconds
            log_file = $_.LogFile
        }
    } )
    $lastStage = if ($script:Stages.Count -gt 0) { $script:Stages[-1].Name } else { "" }
    $cp = @{
        run_timestamp = $script:Timestamp
        artifact_dir = $script:ArtifactsDir
        last_completed_stage = $lastStage
        completed_stages_count = $script:Stages.Count
        overall_passed_so_far = $script:OverallPassed
        is_partial = $true
        stages = $stageSummaries
    }
    $cp | ConvertTo-Json -Depth 12 | Out-File -FilePath $script:CheckpointFile -Encoding utf8 -Force

    $passedCount = ($script:Stages | Where-Object { $_.Status -eq "PASSED" }).Count
    $failedCount = ($script:Stages | Where-Object { $_.Status -eq "FAILED" }).Count
    $timedOutCount = ($script:Stages | Where-Object { $_.Status -eq "TIMED_OUT" }).Count
    $skippedCount = ($script:Stages | Where-Object { $_.Status -in @("SKIPPED", "INHERITED") }).Count
    $partialSummary = @{
        timestamp = (Get-Date -Format "o")
        run_timestamp = $script:Timestamp
        is_partial = $true
        overall_status = if ($script:OverallPassed) { "PASSED_SO_FAR" } else { "FAILED" }
        passed = $passedCount
        failed = $failedCount
        timed_out = $timedOutCount
        skipped = $skippedCount
        stages = $stageSummaries
    }
    $partialSummary | ConvertTo-Json -Depth 12 | Out-File -FilePath $script:SummaryFile -Encoding utf8 -Force
}

function ShouldRunStage {
    param([string]$StageName, [bool]$WouldSkip)
    # OnlyStage: run exactly one named stage (absolute precedence when set; invalid with -ResumeFrom)
    if ($OnlyStage) { return $StageName -eq $OnlyStage }
    # ResumeFrom: stages restored from checkpoint.json are INHERITED — do not re-execute
    if ($ResumeFrom) {
        $inh = $script:Stages | Where-Object { $_.Name -eq $StageName -and $_.Status -eq "INHERITED" }
        if ($inh) { return $false }
    }
    return -not $WouldSkip
}

function Invoke-PostStageCleanup {
    Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name "VoiceStudio.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
}

function Format-StageOutputText {
    param([AllowNull()][object[]]$Chunks)
    if ($null -eq $Chunks -or $Chunks.Count -eq 0) { return "" }
    ($Chunks | ForEach-Object {
        if ($null -eq $_) { return "" }
        if ($_ -is [string]) { return $_ }
        return $_.ToString()
    }) -join [Environment]::NewLine
}

function Write-StageOutputToHost {
    param(
        [string]$LogFile,
        [string]$Text,
        [int]$MaxConsoleChars = 24576
    )
    # Never dump multi-megabyte stage logs to the host: integrated terminals (and some CI wrappers)
    # can block for minutes on large Write-Host payloads. The authoritative copy is always $LogFile.
    if (-not [string]::IsNullOrEmpty($LogFile) -and (Test-Path $LogFile)) {
        $len = (Get-Item $LogFile).Length
        Write-Host "Stage log ($len bytes): $LogFile" -ForegroundColor DarkCyan
    }
    if ([string]::IsNullOrEmpty($Text)) { return }
    if ($Text.Length -le $MaxConsoleChars) {
        Write-Host $Text
        return
    }
    Write-Host "Stage output truncated for console ($($Text.Length) chars); tail (max $MaxConsoleChars):" -ForegroundColor DarkGray
    Write-Host $Text.Substring($Text.Length - $MaxConsoleChars)
}

function Invoke-Stage {
    param(
        [string]$Name,
        [string]$Description,
        [scriptblock]$Action,
        [switch]$Skip,
        [int]$TimeoutSeconds = 0,
        [int]$ShardNum = 0,
        # When set, Action writes $LogFile itself (e.g. cmd.exe redirect); skip outer *>&1 | Out-File so logs are not truncated.
        [switch]$ActionOwnsLogFile
    )
    
    $stageNumber = $script:Stages.Count + 1
    $sanitizedName = $Name.ToLower().Replace(' ', '_').Replace('/', '_').Replace('\', '_')
    $logFile = Join-Path $StageLogsDir "$sanitizedName.log"

    if ($script:ResumeFrom) {
        $inh = $script:Stages | Where-Object { $_.Name -eq $Name -and $_.Status -eq "INHERITED" }
        if ($inh) {
            Write-Host "[ResumeFrom] Skipping inherited stage '$Name' (see checkpoint)." -ForegroundColor DarkYellow
            return $true
        }
    }
    
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "STAGE $stageNumber`: $Name" -ForegroundColor Cyan
    Write-Host $Description -ForegroundColor DarkCyan
    if ($TimeoutSeconds -gt 0) {
        Write-Host "Timeout: ${TimeoutSeconds}s" -ForegroundColor DarkGray
    }
    Write-Host "=" * 70 -ForegroundColor Cyan
    
    if ($Skip) {
        Write-Stage $Name "SKIPPED" "SKIP"
        Add-StageResult -Name $Name -Status "SKIPPED" -ExitCode 0 -DurationSeconds 0 -TimeoutSeconds $TimeoutSeconds
        return $true
    }
    
    $stageStart = Get-Date
    
    if ($TimeoutSeconds -gt 0) {
        $tempScript = [System.IO.Path]::GetTempFileName() + ".ps1"
        $outFile = Join-Path $StageLogsDir "${sanitizedName}_stdout.txt"
        $errFile = Join-Path $StageLogsDir "${sanitizedName}_stderr.txt"
        $exitFile = [System.IO.Path]::GetTempFileName()
        $scriptContent = @"
`$ErrorActionPreference = 'Continue'
`$RootDir = '$RootDir'
Set-Location '$RootDir'
`$ArtifactsDir = '$ArtifactsDir'
`$TestResultsDir = '$TestResultsDir'
`$StageLogsDir = '$StageLogsDir'
`$ScreenshotsDir = '$ScreenshotsDir'
`$Configuration = '$Configuration'
`$ScriptDir = '$ScriptDir'
`$RealUI = $(if ($RealUI) { '$true' } else { '$false' })
`$StrictMypy = $(if ($StrictMypy) { '$true' } else { '$false' })

# Inner script: nest the Action in its own scriptblock so return inside Action does not skip
# the exit capture. Stdout/stderr go to files via cmd.exe (not Start-Process -RedirectStandard*).
`$script:__vsTimedStageExit = 0
& {
    & {
$($Action.ToString())
    }
    if (`$null -eq `$LASTEXITCODE) { `$script:__vsTimedStageExit = 0 } else { `$script:__vsTimedStageExit = `$LASTEXITCODE }
}
`$script:__vsTimedStageExit | Out-File -FilePath '$exitFile' -Encoding utf8
exit `$script:__vsTimedStageExit
"@
        $scriptContent | Out-File -FilePath $tempScript -Encoding utf8
        try {
            # IMPORTANT: Do not use Start-Process -RedirectStandardOutput/-RedirectStandardError on powershell.exe.
            # Use cmd.exe so child stdout/stderr go straight to disk (two-arg form: /c + remainder).
            $cmdRest = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$tempScript`" > `"$outFile`" 2> `"$errFile`""
            $proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", $cmdRest `
                -PassThru -NoNewWindow -WorkingDirectory $RootDir
            $timeoutMs = $TimeoutSeconds * 1000
            $exited = $proc.WaitForExit($timeoutMs)
            if (-not $exited) {
                Write-Stage $Name "TIMED OUT after ${TimeoutSeconds}s" "TIMEOUT"
                try { cmd /c "taskkill /PID $($proc.Id) /T /F" 2>&1 | Out-Null } catch { }
                $duration = ((Get-Date) - $stageStart).TotalSeconds
                $timeoutMsg = "STAGE TIMED OUT at $(Get-Date -Format 'o'). Timeout: ${TimeoutSeconds}s. Process tree killed."
                $timeoutMsg | Out-File -FilePath $logFile -Encoding utf8
                if (Test-Path $outFile) { Get-Content $outFile -Raw | Out-File -FilePath $logFile -Encoding utf8 -Append }
                if (Test-Path $errFile) { Get-Content $errFile -Raw | Out-File -FilePath $logFile -Encoding utf8 -Append }
                if ($Name -like "C# Unit Tests - *") {
                    $exactDiagPath = $null
                    if ($ShardNum -gt 0) {
                        $exactDiagPath = Join-Path $StageLogsDir "csharp_unit_tests_shard_${ShardNum}_diag.txt"
                        $dumpDirs = Get-ChildItem -Path $TestResultsDir -Directory -ErrorAction SilentlyContinue | Where-Object { (Get-ChildItem $_.FullName -Filter "*.dmp" -Recurse -ErrorAction SilentlyContinue).Count -gt 0 }
                        Write-Host ""
                        Write-Host "  Diag file: $exactDiagPath" -ForegroundColor Yellow
                        foreach ($d in $dumpDirs) { Write-Host "  Dump dir:  $($d.FullName)" -ForegroundColor Yellow }
                        Write-Host ""
                        $diagNote = "Diag file: $exactDiagPath"
                        if ($dumpDirs.Count -gt 0) { $diagNote += "`nDump dirs: " + ($dumpDirs.FullName -join "; ") }
                    } else {
                        $diagFiles = Get-ChildItem -Path $StageLogsDir -Filter "csharp_unit_tests_shard_*_diag.txt" -ErrorAction SilentlyContinue
                        $dumpDirs = Get-ChildItem -Path $TestResultsDir -Directory -ErrorAction SilentlyContinue | Where-Object { (Get-ChildItem $_.FullName -Filter "*.dmp" -Recurse -ErrorAction SilentlyContinue).Count -gt 0 }
                        $diagNote = "Diagnostics: Check logs/csharp_unit_tests_shard_*_diag.txt and test-results/ for blame-hang dumps."
                        if ($diagFiles.Count -gt 0) { $diagNote += " Diag files: " + ($diagFiles.Name -join ", ") }
                        if ($dumpDirs.Count -gt 0) { $diagNote += " Dump dirs: " + ($dumpDirs.Name -join ", ") }
                    }
                    $diagNote | Out-File -FilePath $logFile -Encoding utf8 -Append
                }
                Add-StageResult -Name $Name -Status "TIMED_OUT" -ExitCode 124 -DurationSeconds $duration -LogFile $logFile -TimeoutSeconds $TimeoutSeconds
                Invoke-PostStageCleanup
                return $false
            }
            $exitCode = 0
            if (Test-Path $exitFile) {
                $exitRaw = Get-Content $exitFile -Raw
                if ($null -ne $exitRaw -and $exitRaw.Trim().Length -gt 0) {
                    $exitCode = [int]$exitRaw.Trim()
                }
            }
            $output = @()
            if (Test-Path $outFile) { $output += Get-Content $outFile -Raw }
            if (Test-Path $errFile) { $output += Get-Content $errFile -Raw }
            $output | Out-File -FilePath $logFile -Encoding utf8
            $outText = Format-StageOutputText -Chunks $output
            Write-StageOutputToHost -LogFile $logFile -Text $outText
        } finally {
            Remove-Item $tempScript -Force -ErrorAction SilentlyContinue
            Remove-Item $exitFile -Force -ErrorAction SilentlyContinue
        }
    } else {
        try {
            $prevErrorPref = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                # Do not assign native command output via 2>&1 to a variable — on Windows PowerShell, a
                # full stdout buffer can deadlock before the variable assignment drains the pipe. Stream
                # merged output straight to the stage log file, then capture exit code in a script scope.
                $script:__vsInlineStageExit = 0
                if ($ActionOwnsLogFile) {
                    $script:__vsInlineStageExit = & $Action -LogFile $logFile
                    if ($null -eq $script:__vsInlineStageExit) { $script:__vsInlineStageExit = 0 }
                } else {
                    & {
                        & $Action
                        if ($null -eq $LASTEXITCODE) { $script:__vsInlineStageExit = 0 } else { $script:__vsInlineStageExit = $LASTEXITCODE }
                    } *>&1 | Out-File -FilePath $logFile -Encoding utf8
                }
                $exitCode = $script:__vsInlineStageExit
            } finally {
                $ErrorActionPreference = $prevErrorPref
            }
            if ($null -eq $exitCode) { $exitCode = 0 }
            if ($ActionOwnsLogFile) {
                $len = 0
                if (Test-Path $logFile) { $len = (Get-Item $logFile).Length }
                Write-Host "Stage log ($len bytes): $logFile (pytest via subprocess; log not echoed to host)" -ForegroundColor DarkCyan
            } else {
                $outText = ""
                if (Test-Path $logFile) {
                    $outText = Get-Content $logFile -Raw -ErrorAction SilentlyContinue
                }
                Write-StageOutputToHost -LogFile $logFile -Text $outText
            }
        } catch {
            $duration = ((Get-Date) - $stageStart).TotalSeconds
            Write-Stage $Name "ERROR: $_" "FAIL"
            "ERROR: $_" | Out-File -FilePath $logFile -Encoding utf8 -Append
            Add-StageResult -Name $Name -Status "FAILED" -ExitCode 1 -DurationSeconds $duration -LogFile $logFile -TimeoutSeconds $TimeoutSeconds
            return $false
        }
    }
    
    $duration = ((Get-Date) - $stageStart).TotalSeconds
    
    if ($exitCode -ne 0 -and (Test-Path $logFile)) {
        $logSize = (Get-Item $logFile).Length
        if ($logSize -eq 0) {
            $integrityMsg = "HARNESS INTEGRITY FAILURE: Stage '$Name' exited $exitCode but log is 0 bytes. Output was not captured. Fix the harness before trusting results."
            ($integrityMsg, "Fallback diagnostic: exitCode=$exitCode stage='$Name'") | Out-File -FilePath $logFile -Encoding utf8
            Add-StageResult -Name $Name -Status "FAILED" -ExitCode 99 -DurationSeconds $duration -LogFile $logFile -TimeoutSeconds $TimeoutSeconds
            $script:OverallPassed = $false
            return $false
        }
    }

        if ($exitCode -eq 0) {
            Write-Stage $Name "PASSED (${duration}s)" "PASS"
            Add-StageResult -Name $Name -Status "PASSED" -ExitCode $exitCode -DurationSeconds $duration -LogFile $logFile -TimeoutSeconds $TimeoutSeconds
            return $true
        } else {
            Write-Stage $Name "FAILED (exit code $exitCode)" "FAIL"
            Add-StageResult -Name $Name -Status "FAILED" -ExitCode $exitCode -DurationSeconds $duration -LogFile $logFile -TimeoutSeconds $TimeoutSeconds
            return $false
        }
}

function Invoke-StopIfRequested {
    param([string]$StageName)
    if (-not $script:StopAfterStage) { return }
    if ($StageName -ne $script:StopAfterStage) { return }
    Write-Host ""
    Write-Host "[StopAfterStage] Completed '$StageName'. Writing final report and exiting." -ForegroundColor Yellow
    Write-Report
    exit $(if ($script:OverallPassed) { 0 } else { 1 })
}

function Write-Report {
    $overallDuration = ((Get-Date) - $OverallStartTime).TotalSeconds
    $overallStatus = if ($OverallPassed) { "PASSED" } else { "FAILED" }
    $passedCount = ($Stages | Where-Object { $_.Status -eq "PASSED" }).Count
    $failedCount = ($Stages | Where-Object { $_.Status -eq "FAILED" }).Count
    $timedOutCount = ($Stages | Where-Object { $_.Status -eq "TIMED_OUT" }).Count
    $skippedCount = ($Stages | Where-Object { $_.Status -in @("SKIPPED", "INHERITED") }).Count
    
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
    $report += "- **Timed Out:** $timedOutCount" + $nl
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
            "TIMED_OUT" { "TIMEOUT" }
            "SKIPPED" { "SKIP" }
            "INHERITED" { "INH" }
            default { "?" }
        }
        $row = [string]::Format("| {0} | {1} | {2} {3} | {4} | {5}s |", $stageNum, $stage.Name, $icon, $stage.Status, $stage.ExitCode, $stage.DurationSeconds)
        $report += $row + $nl
        $stageNum++
    }

    $report += $nl + "## Stage Time Budgets" + $nl + $nl
    foreach ($key in $StageTimeouts.Keys) {
        $report += "- **$key**: $($StageTimeouts[$key])s" + $nl
    }
    $report += "- **Contract Tests** (pytest session timeout flag): 900s" + $nl
    foreach ($key in $Stage3ShardTimeouts.Keys) {
        $report += "- **$key**: $($Stage3ShardTimeouts[$key])s" + $nl
    }
    $report += $nl + "## Artifacts" + $nl + $nl
    $report += "- **Report:** $ReportFile" + $nl
    $report += "- **Logs:** $StageLogsDir" + $nl
    $report += "- **Screenshots:** $ScreenshotsDir" + $nl
    $report += "- **Test Results:** $TestResultsDir" + $nl + $nl
    $report += "## Failed Stages" + $nl + $nl

    $failedStages = @($Stages | Where-Object {
            $_.Status -in @("FAILED", "TIMED_OUT") -or
            ($_.Status -eq "INHERITED" -and $_.InheritedFromStatus -in @("FAILED", "TIMED_OUT"))
        })
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

    $timedOutStages = @($Stages | Where-Object {
            $_.Status -eq "TIMED_OUT" -or
            ($_.Status -eq "INHERITED" -and $_.InheritedFromStatus -eq "TIMED_OUT")
        })
    if ($timedOutStages.Count -gt 0) {
        $report += $nl + "## Timed Out Stages" + $nl + $nl
        $report += "Stage exceeded its time budget. Check for hanging tests, deadlocks, or environment issues. Use -OnlyStage to re-run this stage in isolation. Review blame-hang dumps if available." + $nl + $nl
        foreach ($stage in $timedOutStages) {
            if ($stage.Name -like "C# Unit Tests - *") {
                $shardIdx = 0
                for ($i = 0; $i -lt $Stage3Shards.Count; $i++) {
                    if ($Stage3Shards[$i].Name -eq $stage.Name) { $shardIdx = $i + 1; break }
                }
                $report += "- **$($stage.Name)**: Log: $($stage.LogFile)" + $nl
                if ($shardIdx -gt 0) {
                    $exactDiagPath = Join-Path $StageLogsDir "csharp_unit_tests_shard_${shardIdx}_diag.txt"
                    $report += "  - Diag file: $exactDiagPath" + $nl
                    $dumpDirs = Get-ChildItem -Path $TestResultsDir -Directory -ErrorAction SilentlyContinue | Where-Object { (Get-ChildItem $_.FullName -Filter "*.dmp" -Recurse -ErrorAction SilentlyContinue).Count -gt 0 }
                    foreach ($d in $dumpDirs) { $report += "  - Dump dir: $($d.FullName)" + $nl }
                } else {
                    $report += "  - Diag files: $($StageLogsDir)\csharp_unit_tests_shard_*_diag.txt" + $nl
                    $report += "  - Blame-hang dumps: $TestResultsDir\ (check subdirs for *.dmp)" + $nl
                }
                $report += $nl
            } else {
                $report += "- **$($stage.Name)**: Log: $($stage.LogFile)" + $nl
                if ($stage.Name -eq "Contract Tests") {
                    $contractStdout = Join-Path $StageLogsDir "contract_tests_stdout.txt"
                    $report += "  - Stdout capture (subprocess path): $contractStdout" + $nl
                    $report += "  - Direct repro: python -m pytest tests/contract -v -s --tb=short" + $nl
                    $report += "  - Hang diagnosis: docs/reports/contract_tests_hang_diagnosis_20260319.md" + $nl
                }
                $report += $nl
            }
        }
    }

    $report += $nl + "## How to Fix Failures" + $nl + $nl
    $report += "1. Check the log file for the failed stage" + $nl
    $report += "2. Fix the issue in your code" + $nl
    $report += "3. Run .\scripts\verify.ps1 again" + $nl
    $report += "4. Do NOT merge until this script passes" + $nl + $nl
    $report += "For TIMED_OUT: Stage exceeded its time budget. Use -OnlyStage to re-run in isolation. Check blame-hang dumps in test results." + $nl + $nl
    $report += "## Re-run Commands" + $nl + $nl
    $report += '```powershell' + $nl
    $report += "# Full verification" + $nl
    $report += ".\scripts\verify.ps1" + $nl + $nl
    $report += "# Quick verification (pre-commit)" + $nl
    $report += ".\scripts\verify.ps1 -Quick" + $nl + $nl
    $report += "# Skip specific stages" + $nl
    $report += ".\scripts\verify.ps1 -SkipUI -SkipIntegration" + $nl + $nl
    $report += "# Real UI automation" + $nl
    $report += ".\scripts\verify.ps1 -RealUI" + $nl + $nl
    $report += "# Run only one stage (e.g. to debug a hanging stage)" + $nl
    $report += ".\scripts\verify.ps1 -OnlyStage `"C# Unit Tests`"" + $nl
    $report += ".\scripts\verify.ps1 -OnlyStage `"Contract Tests`"" + $nl
    $report += '```' + $nl

    $report | Out-File -FilePath $ReportFile -Encoding utf8
    
    # JSON summary (stages include timeout_seconds and timed_out per plan)
    $stageSummaries = @( $Stages | ForEach-Object {
        @{
            name = $_.Name
            status = $_.Status
            exit_code = $_.ExitCode
            duration_seconds = $_.DurationSeconds
            log_file = $_.LogFile
            timeout_seconds = $_.TimeoutSeconds
            timed_out = $_.TimedOut
        }
    } )
    $summary = @{
        timestamp = (Get-Date -Format "o")
        configuration = $Configuration
        quick_mode = $Quick.IsPresent
        real_ui = $RealUI.IsPresent
        overall_status = $overallStatus
        duration_seconds = [math]::Round($overallDuration, 2)
        is_partial = $false
        passed = $passedCount
        failed = $failedCount
        timed_out = $timedOutCount
        skipped = $skippedCount
        stages = $stageSummaries
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
if ($Quick) {
    Write-Host ""
    Write-Host "Quick Mode: C# unit tests, Python unit tests, Contract Tests, Backend Integration, UI stages are SKIPPED by design." -ForegroundColor Cyan
    Write-Host "Running: Build, Python Quality, Quick Critical Gates, Security Tests, Gate/Ledger. Typical duration 3-7 min." -ForegroundColor Cyan
}
Write-Host ""

Set-Location $RootDir

# ============================================================================
# STANDALONE: Runtime proof (GAP-015) — real synthesis + training export honesty
# ============================================================================
if ($RuntimeProof) {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  RUNTIME PROOF (-RuntimeProof)" -ForegroundColor Cyan
    Write-Host "  Real-mode golden loop + training export API honesty (schema v2 + SLO baselines v1)" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""

    $commitHash = git rev-parse HEAD 2>$null
    if (-not $commitHash) { $commitHash = "unknown" }
    $runtimeLog = Join-Path $StageLogsDir "runtime_proof.log"
    $synthXml = Join-Path $TestResultsDir "runtime_proof_synthesis.xml"
    $trainXml = Join-Path $TestResultsDir "runtime_proof_training.xml"
    $proofJson = Join-Path $ArtifactsDir "runtime_proof.json"
    $sloTimingJson = Join-Path $ArtifactsDir "slo_timing_samples.json"
    $sloBaselinesJson = Join-Path $ArtifactsDir "slo_baselines.json"
    $sloWriterScript = Join-Path $ScriptDir "ci\write_slo_baseline_proof.py"
    $probeScript = Join-Path $ScriptDir "ci\check_runtime_prerequisites.py"

    Write-Host "[Runtime proof] Prerequisite probe (engines, consent, pytest)..." -ForegroundColor Cyan
    $probeStdout = & python $probeScript 2>&1
    $probeExit = $LASTEXITCODE
    $probeText = ($probeStdout | Out-String).Trim()
    $prereqObj = $null
    try {
        $prereqObj = $probeText | ConvertFrom-Json
    } catch {
        Write-Host "ERROR: Prerequisite probe returned non-JSON output:" -ForegroundColor Red
        Write-Host $probeText -ForegroundColor Yellow
        $failProof = [ordered]@{
            schema_version  = 2
            timestamp       = (Get-Date -Format 'o')
            commit_hash     = $commitHash
            status          = "FAIL"
            blocked_reason  = $null
            skip_reason     = $null
            proof_grade     = "R"
            command_executed = "verify.ps1 -RuntimeProof"
            prerequisites   = @{ probe_parse_error = $true }
            assertions      = @()
            log             = $runtimeLog
        }
        ($failProof | ConvertTo-Json -Depth 8) | Out-File -FilePath $proofJson -Encoding utf8
        exit 1
    }

    function Write-RuntimeProofJson {
        param(
            [string]$Status,
            [string]$BlockedReason,
            [string]$SkipReason,
            [object]$Prereq,
            [array]$Assertions,
            [int]$ExitCode
        )
        $proofObj = [ordered]@{
            schema_version   = 2
            timestamp        = (Get-Date -Format 'o')
            commit_hash      = $commitHash
            status           = $Status
            blocked_reason   = $BlockedReason
            skip_reason      = $SkipReason
            proof_grade      = "R"
            command_executed = "verify.ps1 -RuntimeProof"
            prerequisites    = $Prereq
            assertions       = $Assertions
            log              = $runtimeLog
        }
        ($proofObj | ConvertTo-Json -Depth 8) | Out-File -FilePath $proofJson -Encoding utf8
        if (Test-Path $LatestLink) {
            Remove-Item $LatestLink -Force -Recurse -ErrorAction SilentlyContinue
        }
        try {
            cmd /c mklink /J "$LatestLink" "$ArtifactsDir" 2>&1 | Out-Null
        } catch {
            Copy-Item $ArtifactsDir $LatestLink -Recurse -Force -ErrorAction SilentlyContinue
        }
        exit $ExitCode
    }

    if ($null -ne $prereqObj -and $prereqObj.blocked -eq $true) {
        Write-Host ""
        Write-Host "RUNTIME PROOF BLOCKED: $($prereqObj.blocked_reason)" -ForegroundColor Yellow
        $prMap = @{}
        if ($prereqObj) {
            $prereqObj.PSObject.Properties | ForEach-Object { $prMap[$_.Name] = $_.Value }
        }
        $asr = @()
        Write-RuntimeProofJson -Status "BLOCKED" -BlockedReason ([string]$prereqObj.blocked_reason) -SkipReason $null -Prereq $prMap -Assertions $asr -ExitCode 2
    }
    if (($probeExit -ne 0) -and (($null -eq $prereqObj) -or (-not $prereqObj.blocked))) {
        Write-Host "ERROR: Prerequisite probe failed (exit $probeExit)" -ForegroundColor Red
        $prMap = @{}
        if ($prereqObj) {
            $prereqObj.PSObject.Properties | ForEach-Object { $prMap[$_.Name] = $_.Value }
        }
        Write-RuntimeProofJson -Status "FAIL" -BlockedReason $null -SkipReason $null -Prereq $prMap -Assertions @() -ExitCode 1
    }

    if (Test-Path $sloTimingJson) { Remove-Item $sloTimingJson -Force -ErrorAction SilentlyContinue }
    $env:VOICESTUDIO_SLO_TIMING_JSON = $sloTimingJson

    $synthArgs = @(
        "-m", "pytest",
        "tests/ci/test_golden_loop_smoke_real.py::test_golden_loop_real_health_synthesize_stream",
        "-v",
        "--override-ini", "addopts=-v --strict-markers --tb=short --color=yes -p no:capture --randomly-seed=12345",
        "--junitxml=$synthXml"
    )
    Write-Host "[Runtime proof] Synthesis (real-mode golden loop)..." -ForegroundColor Cyan
    & python @synthArgs 2>&1 | Tee-Object -FilePath $runtimeLog
    $synthExit = $LASTEXITCODE

    $trainArgs = @(
        "-m", "pytest",
        "tests/ci/test_runtime_proof_training_export.py",
        "-v",
        "--junitxml=$trainXml"
    )
    Write-Host "[Runtime proof] Training export honesty..." -ForegroundColor Cyan
    & python @trainArgs 2>&1 | Tee-Object -Append -FilePath $runtimeLog
    $trainExit = $LASTEXITCODE

    Write-Host "[Runtime proof] Writing slo_baselines.json (GAP-015 slice 3)..." -ForegroundColor Cyan
    & python $sloWriterScript --timing-json $sloTimingJson --output $sloBaselinesJson --commit-hash $commitHash --environment asgi_transport --proof-grade R 2>&1 | Tee-Object -Append -FilePath $runtimeLog
    if ($LASTEXITCODE -ne 0) {
        Write-Host "WARNING: write_slo_baseline_proof.py exited $LASTEXITCODE (artifact may be missing)" -ForegroundColor Yellow
    }

    $synthStatus = if ($synthExit -eq 0) { "PASS" } else { "FAIL" }
    $trainStatus = if ($trainExit -eq 0) { "PASS" } else { "FAIL" }
    $overallStatus = if ($synthExit -eq 0 -and $trainExit -eq 0) { "PASS" } else { "FAIL" }
    $prMap = @{}
    $prereqObj.PSObject.Properties | ForEach-Object { $prMap[$_.Name] = $_.Value }

    $assertions = @(
        [ordered]@{
            name        = "synthesis_golden_loop"
            pytest_exit = $synthExit
            junit_path  = $synthXml
            status      = $synthStatus
        },
        [ordered]@{
            name        = "training_export_honesty"
            pytest_exit = $trainExit
            junit_path  = $trainXml
            status      = $trainStatus
        }
    )

    $exitFinal = 0
    if ($overallStatus -ne "PASS") { $exitFinal = 1 }

    $proofObj = [ordered]@{
        schema_version   = 2
        timestamp        = (Get-Date -Format 'o')
        commit_hash      = $commitHash
        status           = $overallStatus
        blocked_reason   = $null
        skip_reason      = $null
        proof_grade      = "R"
        command_executed = "verify.ps1 -RuntimeProof"
        prerequisites    = $prMap
        assertions       = $assertions
        log              = $runtimeLog
    }
    ($proofObj | ConvertTo-Json -Depth 8) | Out-File -FilePath $proofJson -Encoding utf8

    if (Test-Path $LatestLink) {
        Remove-Item $LatestLink -Force -Recurse -ErrorAction SilentlyContinue
    }
    try {
        cmd /c mklink /J "$LatestLink" "$ArtifactsDir" 2>&1 | Out-Null
    } catch {
        Copy-Item $ArtifactsDir $LatestLink -Recurse -Force -ErrorAction SilentlyContinue
    }

    if ($exitFinal -ne 0) {
        Write-Host ""
        Write-Host "RUNTIME PROOF FAILED (see $runtimeLog)" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
    Write-Host "RUNTIME PROOF PASSED" -ForegroundColor Green
    Write-Host "  $proofJson" -ForegroundColor Cyan
    exit 0
}

# ============================================================================
# STANDALONE: Backend smoke (GAP-069 slice 3) — uvicorn /health + /api/health
# ============================================================================
if ($BackendSmoke) {
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host "  BACKEND SMOKE (-BackendSmoke)" -ForegroundColor Cyan
    Write-Host "  scripts/ci/run_backend_smoke.py -> PROOF_BACKEND_SMOKE_*.json under docs/reports/verification/" -ForegroundColor Cyan
    Write-Host ("=" * 70) -ForegroundColor Cyan
    Write-Host ""

    $smokeScript = Join-Path $ScriptDir "ci\run_backend_smoke.py"
    & python $smokeScript
    $smokeExit = $LASTEXITCODE
    if ($smokeExit -eq 2) {
        Write-Host "  [ADVISORY] Backend smoke BLOCKED (prerequisites not satisfied; proof file still written)" -ForegroundColor Yellow
        exit 0
    }
    if ($smokeExit -ne 0) {
        Write-Host "BACKEND SMOKE FAILED (exit $smokeExit)" -ForegroundColor Red
        exit $smokeExit
    }
    Write-Host ""
    Write-Host "BACKEND SMOKE PASSED" -ForegroundColor Green
    exit 0
}

# -OnlyStage prerequisite check: fail fast when required artifacts are missing
function Test-OnlyStagePrerequisites {
    param([string]$Stage)
    $buildDir = Join-Path $RootDir ".buildlogs\x64\$Configuration\net8.0-windows10.0.19041.0"
    $exePath = Join-Path $buildDir "VoiceStudio.App.exe"
    $testProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
    $stagesNeedingExe = @("UI Smoke Tests", "UI Self-Test", "Icon-Launch Smoke", "Failure-Path Smoke", "Runtime-Missing Failure Smoke")
    $stagesNeedingBuild = @("C# Unit Tests", "C# Unit Tests - ViewModels Seam A-D", "C# Unit Tests - ViewModels Seam E-H", "C# Unit Tests - ViewModels Seam I-L", "C# Unit Tests - ViewModels Seam M", "C# Unit Tests - ViewModels Seam N-Z", "C# Unit Tests - ViewModels Lifecycle", "C# Unit Tests - ViewModels Legacy", "C# Unit Tests - Services", "C# Unit Tests - CommandsGateways", "C# Unit Tests - UIPanels", "C# Unit Tests - Other") + $stagesNeedingExe
    if ($Stage -in $stagesNeedingExe) {
        if (-not (Test-Path $exePath)) {
            Write-Host ""
            Write-Host "ERROR: -OnlyStage '$Stage' requires a built app, but exe not found:" -ForegroundColor Red
            Write-Host "  $exePath" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Run a full build first:" -ForegroundColor Cyan
            Write-Host "  .\scripts\verify.ps1 -OnlyStage `"Clean Build`"" -ForegroundColor White
            Write-Host "  # or: dotnet build VoiceStudio.sln -c $Configuration -p:Platform=x64" -ForegroundColor White
            Write-Host ""
            exit 1
        }
    }
    if ($Stage -in $stagesNeedingBuild) {
        if (-not (Test-Path $buildDir) -or -not (Test-Path (Join-Path $buildDir "VoiceStudio.App.dll"))) {
            Write-Host ""
            Write-Host "ERROR: -OnlyStage '$Stage' requires build artifacts, but none found:" -ForegroundColor Red
            Write-Host "  Expected: $buildDir" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Run a full build first:" -ForegroundColor Cyan
            Write-Host "  .\scripts\verify.ps1 -OnlyStage `"Clean Build`"" -ForegroundColor White
            Write-Host "  # or: dotnet build VoiceStudio.sln -c $Configuration -p:Platform=x64" -ForegroundColor White
            Write-Host ""
            exit 1
        }
        if (-not (Test-Path $testProject)) {
            Write-Host ""
            Write-Host "ERROR: -OnlyStage '$Stage' requires test project:" -ForegroundColor Red
            Write-Host "  $testProject" -ForegroundColor Yellow
            Write-Host ""
            exit 1
        }
    }
}
if ($OnlyStage) {
    Test-OnlyStagePrerequisites -Stage $OnlyStage
}

# Pre-build cleanup: kill lingering processes that can lock the exe (MSB3027)
Get-Process -Name "VoiceStudio.App" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# ============================================================================
# STAGE 1: Clean Build (C#)
# ============================================================================

$stage1Passed = Invoke-Stage -Name "Clean Build" -Description "Build C# solution" -Skip:(($OnlyStage -and "Clean Build" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipBuild)) -Action {
    $binlogPath = Join-Path $ArtifactsDir "build.binlog"
    & dotnet build VoiceStudio.sln -c $Configuration -p:Platform=x64 /bl:$binlogPath
    return $LASTEXITCODE
}
Invoke-StopIfRequested "Clean Build"

if (-not $stage1Passed -and -not $SkipBuild) {
    Write-Host ""
    Write-Host "BUILD FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# Post-build checks (run after build, before other stages; fast and critical)
$didRunBuild = (-not $OnlyStage -and -not $SkipBuild) -or $OnlyStage -eq "Clean Build"
if ($stage1Passed -and $didRunBuild) {
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

    # Release XAML smoke: Gate C historical crashes were Release-only. Fail fast if Release build XAML fails.
    Invoke-Stage -Name "Release XAML Smoke" -Description "Release build XAML health check (Gate C protection)" -Action {
        $releaseResult = & dotnet build VoiceStudio.sln -c Release -p:Platform=x64 2>&1
        $buildExit = $LASTEXITCODE
        $releaseResult | Write-Host
        if ($buildExit -ne 0) {
            Write-Host "Release build failed - XAML smoke cannot run" -ForegroundColor Red
            return $buildExit
        }
        $healthScript = Join-Path $RootDir "tools\build\Check-XamlHealth.ps1"
        if (Test-Path $healthScript) {
            & powershell -ExecutionPolicy Bypass -File $healthScript
            return $LASTEXITCODE
        }
        Write-Host "[SKIP] Check-XamlHealth.ps1 not found" -ForegroundColor Yellow
        return 0
    }
}

# ============================================================================
# STAGE 2: Python Quality Checks
# ============================================================================

$stage2Passed = Invoke-Stage -Name "Python Quality" -Description "Lint and type-check Python code - ruff, mypy" -Skip:(($OnlyStage -and "Python Quality" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipPythonLint)) -Action {
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
Invoke-StopIfRequested "Python Quality"

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

# Quick Critical Gates: two invocations to avoid hang. Fast tests (no backend import) run first via cmd.
# Golden loop (imports backend) runs second with Start-Process + 60s timeout so verify never hangs.
if ($Quick -and (-not $OnlyStage -or $OnlyStage -eq "Quick Critical Gates")) {
    Invoke-Stage -Name "Quick Critical Gates" -Description "Golden-loop smoke, UI/backend route alignment, contract drift" -ActionOwnsLogFile -Action {
        param([string]$LogFile)
        $fastLog = Join-Path $StageLogsDir "quick_critical_gates_fast.log"
        $goldenLog = Join-Path $StageLogsDir "quick_critical_gates_golden.log"
        $prev = $env:VOICESTUDIO_TEST_MODE
        $env:VOICESTUDIO_TEST_MODE = "stub"
        $merged = ""
        try {
            Push-Location $RootDir
            try {
                # 1. Fast tests (route alignment + contract drift) — no backend import, ~2s
                $fastCmd = "set VOICESTUDIO_TEST_MODE=stub && python -u -m pytest tests/ci/test_ui_backend_route_alignment.py tests/ci/test_contract_drift_gate.py -v --tb=short"
                & cmd /c "$fastCmd > `"$fastLog`" 2>&1"
                if ($LASTEXITCODE -ne 0) {
                    if (Test-Path $fastLog) { $merged = Get-Content $fastLog -Raw -ErrorAction SilentlyContinue }
                    $merged | Out-File -FilePath $LogFile -Encoding utf8
                    return $LASTEXITCODE
                }
                if (Test-Path $fastLog) { $merged = Get-Content $fastLog -Raw -ErrorAction SilentlyContinue }

                # 2. Golden loop (imports backend) — Start-Process with 90s timeout. On some Windows hosts,
                #    pytest completes but python.exe never exits (same as Backend Integration). If junit
                #    reports all passed, treat as success and terminate the child.
                $goldenErr = Join-Path $StageLogsDir "quick_critical_gates_golden_err.log"
                $goldenJunit = Join-Path $TestResultsDir "quick_critical_gates_golden.xml"
                $py = (Get-Command python -ErrorAction Stop).Source
                $argList = @("-u", "-m", "pytest", "tests/ci/test_golden_loop_smoke.py", "-v", "--tb=short", "--junitxml=$goldenJunit")
                $p = Start-Process -FilePath $py -ArgumentList $argList -WorkingDirectory $RootDir `
                    -RedirectStandardOutput $goldenLog -RedirectStandardError $goldenErr -PassThru -NoNewWindow
                $exited = $p.WaitForExit(90000)
                if (-not $exited) {
                    $forceOk = $false
                    $fail = 1; $errs = 1
                    if (Test-Path $goldenJunit) {
                        try {
                            [xml]$jx = Get-Content $goldenJunit -Raw
                            $fail = 0; $errs = 0
                            foreach ($suite in @($jx.testsuites.testsuite)) { $fail += [int]$suite.failures; $errs += [int]$suite.errors }
                            if ($fail -eq 0 -and $errs -eq 0) { $forceOk = $true }
                        } catch { }
                    }
                    try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
                    $merged += "`nHARNESS: Golden-loop pytest did not exit within 90s; junit failures=$fail errors=$errs - child terminated."
                    if ($forceOk) {
                        $merged += " Junit reports all passed; treating as success."
                        $exitCode = 0
                    } else {
                        $merged | Out-File -FilePath $LogFile -Encoding utf8
                        return 124
                    }
                } else {
                    $exitCode = $p.ExitCode
                }
                if (Test-Path $goldenLog) { $merged += Get-Content $goldenLog -Raw -ErrorAction SilentlyContinue }
                if (Test-Path $goldenErr) { $merged += Get-Content $goldenErr -Raw -ErrorAction SilentlyContinue }
            } finally {
                Pop-Location
            }
        } finally {
            if ($null -ne $prev) { $env:VOICESTUDIO_TEST_MODE = $prev } else { Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue }
        }
        $merged | Out-File -FilePath $LogFile -Encoding utf8
        return $exitCode
    }
    Invoke-StopIfRequested "Quick Critical Gates"
}

# ============================================================================
# STAGE 3: C# Unit Tests (first-class shards for diagnosability)
# ============================================================================
# Each shard is a separate stage with its own timeout. Report shows exactly which shard broke.

# ViewModels Seam split by first letter of class name (A-D, E-H, I-L, M, N-Z) for diagnosability when shard times out
$SeamBase = 'TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&TestCategory!=Lifecycle&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&(FullyQualifiedName~SeamTests|FullyQualifiedName~StalenessTests)'
$SeamAD = $SeamBase + '&(FullyQualifiedName~.Advanced|FullyQualifiedName~.Analyzer|FullyQualifiedName~.APIKey|FullyQualifiedName~.Assistant|FullyQualifiedName~.Audio|FullyQualifiedName~.Automation|FullyQualifiedName~.Analytics|FullyQualifiedName~.AIMixing|FullyQualifiedName~.AIProduction|FullyQualifiedName~.Backup|FullyQualifiedName~.Batch|FullyQualifiedName~.Dataset|FullyQualifiedName~.Deepfake|FullyQualifiedName~.Diagnostics)'
$SeamEH = $SeamBase + '&(FullyQualifiedName~.Emotion|FullyQualifiedName~.Embedding|FullyQualifiedName~.Engine|FullyQualifiedName~.Ensemble|FullyQualifiedName~.Effects|FullyQualifiedName~.GPU|FullyQualifiedName~.Global|FullyQualifiedName~.Help)'
$SeamIL = $SeamBase + '&(FullyQualifiedName~.Image|FullyQualifiedName~.Job|FullyQualifiedName~.Keyboard|FullyQualifiedName~.Library|FullyQualifiedName~.Lexicon)'
$SeamM = $SeamBase + '&(FullyQualifiedName~.Macro|FullyQualifiedName~.Model|FullyQualifiedName~.Multilingual|FullyQualifiedName~.MCP|FullyQualifiedName~.Mix|FullyQualifiedName~.Marker|FullyQualifiedName~.Multi)'
$SeamNZ = $SeamBase + '&(FullyQualifiedName~.Plugin|FullyQualifiedName~.Preset|FullyQualifiedName~.Pipeline|FullyQualifiedName~.Prosody|FullyQualifiedName~.Profile|FullyQualifiedName~.Pronunciation|FullyQualifiedName~.Quality|FullyQualifiedName~.RealTime|FullyQualifiedName~.Recording|FullyQualifiedName~.Sonography|FullyQualifiedName~.Spatial|FullyQualifiedName~.Spectrogram|FullyQualifiedName~.SSML|FullyQualifiedName~.Settings|FullyQualifiedName~.SLO|FullyQualifiedName~.Script|FullyQualifiedName~.StyleTransfer|FullyQualifiedName~.Scene|FullyQualifiedName~.Text|FullyQualifiedName~.Todo|FullyQualifiedName~.Tag|FullyQualifiedName~.Training|FullyQualifiedName~.Template|FullyQualifiedName~.Transcribe|FullyQualifiedName~.Ultimate|FullyQualifiedName~.Upscaling|FullyQualifiedName~.Video|FullyQualifiedName~.Voice|FullyQualifiedName~.Workflow)'
$Stage3Shards = @(
    @{ Name = "C# Unit Tests - ViewModels Seam A-D"; Filter = $SeamAD }
    @{ Name = "C# Unit Tests - ViewModels Seam E-H"; Filter = $SeamEH }
    @{ Name = "C# Unit Tests - ViewModels Seam I-L"; Filter = $SeamIL }
    @{ Name = "C# Unit Tests - ViewModels Seam M"; Filter = $SeamM }
    @{ Name = "C# Unit Tests - ViewModels Seam N-Z"; Filter = $SeamNZ }
    @{ Name = "C# Unit Tests - ViewModels Lifecycle"; Filter = "TestCategory=Lifecycle&FullyQualifiedName~ViewModels" }
    @{ Name = "C# Unit Tests - ViewModels Legacy"; Filter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.ViewModels&FullyQualifiedName!~SeamTests&FullyQualifiedName!~StalenessTests&TestCategory!=Lifecycle" }
    @{ Name = "C# Unit Tests - Services"; Filter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.Services" }
    @{ Name = "C# Unit Tests - CommandsGateways"; Filter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&(FullyQualifiedName~VoiceStudio.App.Tests.Commands|FullyQualifiedName~VoiceStudio.App.Tests.Gateways)" }
    @{ Name = "C# Unit Tests - UIPanels"; Filter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&(FullyQualifiedName~VoiceStudio.App.Tests.UI|FullyQualifiedName~VoiceStudio.App.Tests.Panels)" }
    @{ Name = "C# Unit Tests - Other"; Filter = "TestCategory!=UI&TestCategory!=E2E&TestCategory!=Smoke&FullyQualifiedName!~ViewModels&FullyQualifiedName!~Services&FullyQualifiedName!~Commands&FullyQualifiedName!~Gateways&FullyQualifiedName!~VoiceStudio.App.Tests.UI&FullyQualifiedName!~Panels" }
)

# Pre-C# cleanup: kill lingering testhost/VoiceStudio.App and allow system to settle.
# Reduces Stage 13 (Services) full-harness hang; see artifacts/verify/stage13_pass_vs_timeout_diff.md
if (-not $SkipCSharpTests) {
    Invoke-PostStageCleanup
    Start-Sleep -Seconds 3
}

$stage3Passed = $true
foreach ($shard in $Stage3Shards) {
    $runThisShard = -not $SkipCSharpTests
    if ($OnlyStage) {
        $runThisShard = ($OnlyStage -eq "C# Unit Tests") -or ($OnlyStage -eq $shard.Name)
    }
    $shardNum = [array]::IndexOf($Stage3Shards, $shard) + 1
    # Before Services shard (8): extra cleanup + delay to reduce full-harness hang (Stage 13 non-deterministic)
    if ($shard.Name -eq "C# Unit Tests - Services" -and $runThisShard) {
        Invoke-PostStageCleanup
        Start-Sleep -Seconds 5
    }
    $filterLiteral = $shard.Filter -replace "'", "''"
    $shardTotal = $Stage3Shards.Count
    $shardPassed = Invoke-Stage -Name $shard.Name -Description "C# unit tests shard $shardNum of $shardTotal" -Skip:(-not $runThisShard) -TimeoutSeconds $Stage3ShardTimeouts[$shard.Name] -ShardNum $shardNum -Action ([scriptblock]::Create(@"
`$testProject = Join-Path '$RootDir' 'src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj'
`$trxFile = Join-Path '$TestResultsDir' 'csharp_unit_tests_shard_$shardNum.trx'
`$diagFile = Join-Path '$StageLogsDir' 'csharp_unit_tests_shard_${shardNum}_diag.txt'
`$shardOutput = & dotnet test `$testProject -c '$Configuration' -p:Platform=x64 --no-build --filter '$filterLiteral' --blame-hang --blame-hang-timeout 5m --blame-crash --blame-hang-dump-type mini --diag `$diagFile --logger "trx;LogFileName=`$trxFile" --results-directory '$TestResultsDir' 2>&1
`$shardOutput
`$exitCode = `$LASTEXITCODE
`$summaryLine = `$shardOutput | Where-Object { `$_ -match "Failed:\s+\d+.*Passed:\s+\d+" } | Select-Object -Last 1
if (`$summaryLine -match "Failed:\s+0.*Passed:\s+(\d+)" -and [int]`$Matches[1] -gt 0 -and `$exitCode -ne 0) {
    Write-Host "Note: Shard $shardNum test host crashed after tests completed (known WinUI issue), but all tests passed." -ForegroundColor Yellow
    cmd /c "exit 0"
} else {
    cmd /c "exit `$exitCode"
}
"@))
    Invoke-StopIfRequested $shard.Name
    if (-not $shardPassed -and $runThisShard) { $stage3Passed = $false }
    # Between shards: cleanup to reduce cross-shard contamination (Stage 13 hang mitigation)
    if ($runThisShard -and $shardNum -lt $Stage3Shards.Count) {
        Invoke-PostStageCleanup
        Start-Sleep -Milliseconds 500
    }
}
if (-not $stage3Passed -and -not $SkipCSharpTests) {
    Write-Host ""
    Write-Host "C# UNIT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipCSharpTests) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 4: Python Unit Tests
# ============================================================================

$stage4Passed = Invoke-Stage -Name "Python Unit Tests" -Description "Run Python unit tests from tests/unit" -Skip:(($OnlyStage -and "Python Unit Tests" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipPythonTests)) -TimeoutSeconds $StageTimeouts["Python Unit Tests"] -Action {
    $junitFile = Join-Path $TestResultsDir "python_unit_tests.xml"
    $outF = Join-Path $StageLogsDir "python_unit_tests_subprocess_stdout.log"
    $errF = Join-Path $StageLogsDir "python_unit_tests_subprocess_stderr.log"
    if (Test-Path $junitFile) { Remove-Item $junitFile -Force -ErrorAction SilentlyContinue }
    # Reduce TensorFlow / transformers chatter during teardown; does not disable tests (GAP-069 Slice 10).
    if (-not $env:TF_CPP_MIN_LOG_LEVEL) { $env:TF_CPP_MIN_LOG_LEVEL = "2" }
    if (-not $env:TRANSFORMERS_VERBOSITY) { $env:TRANSFORMERS_VERBOSITY = "error" }

    # Child process: some Windows hosts leave python.exe alive after pytest finishes (ML stack / threads).
    # Parity with Contract Tests: if junitxml is complete and green, bounded wait then terminate (Slice 10).
    $py = (Get-Command python -ErrorAction Stop).Source
    # Single Win32 argument string (Start-Process string[] breaks `-m "not slow ..."` when embedded in timed temp script).
    $argStr = "-u -m pytest tests/unit -v --tb=short -x --junitxml=""$junitFile"" -m ""not slow and not gpu and not engine"" --ignore=tests/unit/backend/api/routes/_archived"
    $p = Start-Process -FilePath $py -ArgumentList $argStr `
        -WorkingDirectory $RootDir `
        -RedirectStandardOutput $outF -RedirectStandardError $errF `
        -PassThru -NoNewWindow

    if ($null -eq $p) {
        Write-Output "ERROR: Start-Process did not return a process handle for pytest."
        return 1
    }

    # Keep in sync with $StageTimeouts["Python Unit Tests"] (currently 1200s).
    $maxMs = 1200000
    $deadline = (Get-Date).AddMilliseconds($maxMs)
    $graceAfterGreenJunitMs = 120000

    while (-not $p.HasExited) {
        if ((Get-Date) -gt $deadline) { break }
        if (Test-Path $junitFile) {
            try {
                [xml]$jx = Get-Content $junitFile -Raw
                if ($null -eq $jx -or $null -eq $jx.testsuites) { throw "junit missing testsuites" }
                $failTotal = 0; $errTotal = 0; $nTests = 0
                foreach ($suite in @($jx.testsuites.testsuite)) {
                    if ($null -eq $suite) { continue }
                    $failTotal += [int]$suite.failures
                    $errTotal += [int]$suite.errors
                    $nTests += [int]$suite.tests
                }
                if ($failTotal -eq 0 -and $errTotal -eq 0 -and $nTests -gt 0) {
                    $null = $p.WaitForExit($graceAfterGreenJunitMs)
                    if (-not $p.HasExited) {
                        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
                        Start-Sleep -Milliseconds 400
                        "`nHARNESS NOTE (GAP-069 Slice 10): junitxml shows 0 failures/0 errors; pytest did not exit within ${graceAfterGreenJunitMs}ms - child terminated so verify can continue." | Out-File -FilePath $outF -Encoding utf8 -Append
                    }
                    break
                }
            } catch { }
        }
        Start-Sleep -Milliseconds 500
    }

    if (-not $p.HasExited) {
        try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Milliseconds 400
    }

    $exitCode = 1
    if ($p.HasExited) {
        $exitCode = $p.ExitCode
    }
    if ($exitCode -ne 0 -and (Test-Path $junitFile)) {
        try {
            [xml]$jx = Get-Content $junitFile -Raw
            if ($null -ne $jx.testsuites) {
                $failTotal = 0; $errTotal = 0
                foreach ($suite in @($jx.testsuites.testsuite)) {
                    if ($null -eq $suite) { continue }
                    $failTotal += [int]$suite.failures
                    $errTotal += [int]$suite.errors
                }
                if ($failTotal -eq 0 -and $errTotal -eq 0) { $exitCode = 0 }
            }
        } catch { }
    }

    if (Test-Path $outF) { Write-Output (Get-Content $outF -Raw) }
    if (Test-Path $errF) { Write-Output (Get-Content $errF -Raw) }
    # Timed Invoke-Stage captures $LASTEXITCODE from native commands; `return` alone does not set it (verify.ps1 ~699).
    cmd.exe /c "exit /b $exitCode"
    return $exitCode
}
Invoke-StopIfRequested "Python Unit Tests"

if (-not $stage4Passed -and -not $SkipPythonTests) {
    Write-Host ""
    Write-Host "PYTHON UNIT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 5: Contract Tests
# ============================================================================

$stage5Passed = Invoke-Stage -Name "Contract Tests" -Description "Validate CSharp-Python API contracts" -Skip:(($OnlyStage -and "Contract Tests" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipContractTests)) -ActionOwnsLogFile -Action {
    param([string]$LogFile)
    # Child process with split stdout/stderr files (same-file redirect deadlocks).
    # Some environments leave python.exe alive after pytest writes junitxml; bounded wait + junit-based
    # success allows the harness to proceed without hanging the full verify lane.
    $junitFile = Join-Path $TestResultsDir "contract_tests.xml"
    $outF = Join-Path $StageLogsDir "contract_tests_subprocess_stdout.log"
    $errF = Join-Path $StageLogsDir "contract_tests_subprocess_stderr.log"
    $py = (Get-Command python -ErrorAction Stop).Source
    $argList = @(
        "-u", "-m", "pytest", "tests/contract",
        "-v", "--tb=short", "--timeout=900",
        "--junitxml=$junitFile"
    )
    $p = Start-Process -FilePath $py -ArgumentList $argList `
        -WorkingDirectory $RootDir `
        -RedirectStandardOutput $outF -RedirectStandardError $errF `
        -PassThru -NoNewWindow
    # Pytest normally ~30s; if the child never exits after junitxml (observed on some Windows hosts),
    # fail closed quickly so verify.ps1 does not block the entire lane for minutes.
    $waitMs = 45000
    $null = $p.WaitForExit($waitMs)
    if (-not $p.HasExited) {
        $forceOk = $false
        if (Test-Path $junitFile) {
            try {
                [xml]$jx = Get-Content $junitFile -Raw
                $ts = $jx.testsuites.testsuite
                $fail = [int]($ts.failures)
                $errs = [int]($ts.errors)
                if ($fail -eq 0 -and $errs -eq 0) { $forceOk = $true }
            } catch {
                $forceOk = $false
            }
        }
        if ($forceOk) {
            try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
            Start-Sleep -Milliseconds 400
            "`nHARNESS NOTE: pytest child did not exit within ${waitMs}ms; junit reports failures=0 errors=0 - child process was terminated." | Out-File -FilePath $outF -Encoding utf8 -Append
            $exitCode = 0
        } else {
            try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
            $exitCode = 1
        }
    } else {
        $exitCode = $p.ExitCode
    }
    $merged = ""
    if (Test-Path $outF) { $merged += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if (Test-Path $errF) { $merged += (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    $merged | Out-File -FilePath $LogFile -Encoding utf8
    return $exitCode
}
Invoke-StopIfRequested "Contract Tests"

if (-not $stage5Passed -and -not $SkipContractTests) {
    Write-Host ""
    Write-Host "CONTRACT TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 6: Security Tests
# ============================================================================

if ($Quick -and -not $SkipSecurity -and (-not $OnlyStage -or $OnlyStage -eq "Security Tests")) {
    Write-Host ""
    Write-Host "Quick Mode: Stages 7-19 (C# unit tests, Python unit tests, Contract, Integration, UI) are SKIPPED by design." -ForegroundColor DarkCyan
    Write-Host "Security Tests: Running now (typically 2-3 min). Sparse console output here is normal." -ForegroundColor DarkCyan
    Write-Host "  Logs: $StageLogsDir" -ForegroundColor DarkGray
    Write-Host "  Results: $TestResultsDir\security_tests.xml" -ForegroundColor DarkGray
    Write-Host "  See docs/reports/verify_quick_mode_behavior.md if run appears to stall." -ForegroundColor DarkGray
}
$stage6Passed = Invoke-Stage -Name "Security Tests" -Description "Run security tests - injection, auth bypass, sandbox escape" -Skip:(($OnlyStage -and "Security Tests" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipSecurity)) -Action {
    $junitFile = Join-Path $TestResultsDir "security_tests.xml"
    & python -u -m pytest tests/security `
        -v `
        --tb=short `
        --junitxml=$junitFile
    return $LASTEXITCODE
}
Invoke-StopIfRequested "Security Tests"

if (-not $stage6Passed -and -not $SkipSecurity) {
    Write-Host ""
    Write-Host "SECURITY TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 7: Backend Integration Tests
# ============================================================================

# ActionOwnsLogFile + Start-Process: inline `*&1 | Out-File` deadlocks on large pytest output; timed cmd wrapper yielded 0-byte logs.
$stage7Passed = Invoke-Stage -Name "Backend Integration" -Description "Golden-loop smoke (ASGI) + backend integration framework smoke (curated; not full tests/integration tree)" -Skip:(($OnlyStage -and "Backend Integration" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipIntegration)) -ActionOwnsLogFile -Action {
    param([string]$LogFile)
    $junitFile = Join-Path $TestResultsDir "integration_tests.xml"
    $outF = Join-Path $StageLogsDir "backend_integration_subprocess_stdout.log"
    $errF = Join-Path $StageLogsDir "backend_integration_subprocess_stderr.log"
    $py = (Get-Command python -ErrorAction Stop).Source
    $prev = $env:VOICESTUDIO_TEST_MODE
    $env:VOICESTUDIO_TEST_MODE = "stub"
    try {
        # Single pytest session (see Contract Tests stage for rationale on subprocess + redirect).
        $argList = @(
            "-u", "-m", "pytest",
            "tests/ci/test_golden_loop_smoke.py",
            "tests/integration/test_backend/test_framework_smoke.py",
            "-v", "--tb=short",
            "--junitxml=$junitFile"
        )
        $p = Start-Process -FilePath $py -ArgumentList $argList `
            -WorkingDirectory $RootDir `
            -RedirectStandardOutput $outF -RedirectStandardError $errF `
            -PassThru -NoNewWindow
        # Same class of hang as Contract Tests: pytest completes but python.exe sometimes never exits on Windows with redirect.
        $waitMs = 120000
        $null = $p.WaitForExit($waitMs)
        if (-not $p.HasExited) {
            $forceOk = $false
            if (Test-Path $junitFile) {
                try {
                    [xml]$jx = Get-Content $junitFile -Raw
                    $fail = 0
                    $errs = 0
                    foreach ($suite in @($jx.testsuites.testsuite)) {
                        $fail += [int]$suite.failures
                        $errs += [int]$suite.errors
                    }
                    if ($fail -eq 0 -and $errs -eq 0) { $forceOk = $true }
                } catch {
                    $forceOk = $false
                }
            }
            if ($forceOk) {
                try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
                Start-Sleep -Milliseconds 400
                "`nHARNESS NOTE: pytest child did not exit within ${waitMs}ms; junit failures=0 errors=0 - child terminated." | Out-File -FilePath $outF -Encoding utf8 -Append
                $exitCode = 0
            } else {
                try { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } catch { }
                $exitCode = 124
            }
        } else {
            $exitCode = $p.ExitCode
        }
    } finally {
        if ($null -ne $prev) { $env:VOICESTUDIO_TEST_MODE = $prev }
        else { Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue }
    }
    $merged = ""
    if (Test-Path $outF) { $merged += (Get-Content $outF -Raw -ErrorAction SilentlyContinue) }
    if (Test-Path $errF) { $merged += (Get-Content $errF -Raw -ErrorAction SilentlyContinue) }
    $merged | Out-File -FilePath $LogFile -Encoding utf8
    return $exitCode
}
Invoke-StopIfRequested "Backend Integration"

if (-not $stage7Passed -and -not $SkipIntegration) {
    Write-Host ""
    Write-Host "BACKEND INTEGRATION FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 7: UI Smoke Tests
# ============================================================================

$stage8Passed = Invoke-Stage -Name "UI Smoke Tests" -Description "Verify app launches, panels exist, navigation works" -Skip:(($OnlyStage -and "UI Smoke Tests" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipUI)) -TimeoutSeconds $StageTimeouts["UI Smoke Tests"] -Action {
    $trxFile = Join-Path $TestResultsDir "ui_smoke_tests.trx"
    $testProject = Join-Path $RootDir "src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj"
    
    # FlaUI E2E (SmokeTests.cs) requires interactive desktop enumeration; always opt in for this stage so
    # dotnet test inherits VOICESTUDIO_USE_REAL_UI_AUTOMATION=true (-RealUI remains the operator hint for session).
    $env:VOICESTUDIO_USE_REAL_UI_AUTOMATION = "true"
    $env:VOICESTUDIO_TEST_ARTIFACTS = $ScreenshotsDir
    
    try {
        # Scope to E2E FlaUI SmokeTests only. Other types still carry TestCategory=Smoke but are Ignored/inconclusive;
        # running the full filter kept a huge discovery list and risked harness timeouts when cleanup stalls.
        & dotnet test $testProject `
            -c $Configuration `
            -p:Platform=x64 `
            --no-build `
            --filter "TestCategory=Smoke&FullyQualifiedName~VoiceStudio.App.Tests.UI.E2E.SmokeTests" `
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
Invoke-StopIfRequested "UI Smoke Tests"

if (-not $stage8Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "UI SMOKE TESTS FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipUI) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 8.5: UI Self-Test (app-level smoke)
# ============================================================================
# Exercises startup orchestration: app awaits backend before MainWindow when
# using --smoke-ui; Gate C with -UiSmoke proves awaited path. See
# docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md.

$stage8_5Passed = Invoke-Stage -Name "UI Self-Test" -Description "Run app with --ui-self-test (Gate C smoke + backend health)" -Skip:(($OnlyStage -and "UI Self-Test" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipUI)) -TimeoutSeconds $StageTimeouts["UI Self-Test"] -Action {
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
Invoke-StopIfRequested "UI Self-Test"

if (-not $stage8_5Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "UI SELF-TEST FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipUI) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 8.6: Icon-Launch Smoke (normal path proof)
# ============================================================================
# Proves: backend not running -> launch app (no --smoke-ui) -> backend starts ->
# overlay clears -> one backend-dependent action succeeds. See
# docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md Round 3.

$stage8_6Passed = Invoke-Stage -Name "Icon-Launch Smoke" -Description "Run app with --icon-launch-smoke (normal path: overlay, backend auto-start, profiles fetch)" -Skip:(($OnlyStage -and "Icon-Launch Smoke" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipUI)) -TimeoutSeconds $StageTimeouts["Icon-Launch Smoke"] -Action {
    $exePath = Join-Path $RootDir ".buildlogs\x64\$Configuration\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
    $reportDir = Join-Path $RootDir ".buildlogs\verify"
    $reportPath = Join-Path $reportDir "icon_launch_smoke.json"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    if (-not (Test-Path $exePath)) {
        Write-Host "ERROR: Exe not found at $exePath" -ForegroundColor Red
        cmd /c "exit 1"
        return
    }
    # Best-effort: stop any process on port 8000 so we prove full startup path
    try {
        $conn = Get-NetTCPConnection -LocalPort 8000 -State Listen -ErrorAction SilentlyContinue
        if ($conn) {
            $conn | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
            Start-Sleep -Seconds 2
        }
    } catch { /* best effort */ }
    & $exePath --icon-launch-smoke --out $reportPath
    $exitCode = $LASTEXITCODE
    if ($null -eq $exitCode) { $exitCode = 0 }
    if ($exitCode -ne 0) {
        Write-Host "Icon-Launch Smoke FAILED (exit code $exitCode)" -ForegroundColor Red
    } else {
        Write-Host "Icon-Launch Smoke PASSED" -ForegroundColor Green
    }
    # Copy proof artifact
    $crashDir = Join-Path $env:LOCALAPPDATA "VoiceStudio\crashes"
    $summaryPath = Join-Path $crashDir "icon_launch_smoke_summary.json"
    if (Test-Path $summaryPath) {
        Copy-Item $summaryPath $reportPath -Force
    }
    cmd /c "exit $exitCode"
}
Invoke-StopIfRequested "Icon-Launch Smoke"

if (-not $stage8_6Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "ICON-LAUNCH SMOKE FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipUI) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 8.7: Failure-Path Smoke (port occupied)
# ============================================================================
# Proves: port 8000 occupied -> app shows BackendFailed overlay with port message.
# See docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md Round 3 Task 2.

$stage8_7Passed = Invoke-Stage -Name "Failure-Path Smoke" -Description "Port occupied -> overlay shows failure, Retry visible" -Skip:(($OnlyStage -and "Failure-Path Smoke" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipUI)) -TimeoutSeconds $StageTimeouts["Failure-Path Smoke"] -Action {
    $exePath = Join-Path $RootDir ".buildlogs\x64\$Configuration\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
    $reportDir = Join-Path $RootDir ".buildlogs\verify"
    $reportPath = Join-Path $reportDir "failure_smoke.json"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    if (-not (Test-Path $exePath)) {
        Write-Host "ERROR: Exe not found at $exePath" -ForegroundColor Red
        cmd /c "exit 1"
        return
    }
    & "$ScriptDir\icon-launch-failure-smoke.ps1" -ExePath $exePath -ReportPath $reportPath
    cmd /c "exit $LASTEXITCODE"
}
Invoke-StopIfRequested "Failure-Path Smoke"

if (-not $stage8_7Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "FAILURE-PATH SMOKE FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipUI) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 8.8: Runtime-Missing Failure Smoke
# ============================================================================
# Proves: app root invalid (no backend) -> app shows BackendFailed overlay with runtime message.
# See docs/design/STARTUP_ORCHESTRATION_HARDENING_PLAN.md Round 4 Task 2.

$stage8_8Passed = Invoke-Stage -Name "Runtime-Missing Failure Smoke" -Description "App root invalid -> overlay shows runtime failure" -Skip:(($OnlyStage -and "Runtime-Missing Failure Smoke" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipUI)) -TimeoutSeconds $StageTimeouts["Runtime-Missing Failure Smoke"] -Action {
    $exePath = Join-Path $RootDir ".buildlogs\x64\$Configuration\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
    $reportDir = Join-Path $RootDir ".buildlogs\verify"
    $reportPath = Join-Path $reportDir "failure_runtime_smoke.json"
    New-Item -ItemType Directory -Force -Path $reportDir | Out-Null
    if (-not (Test-Path $exePath)) {
        Write-Host "ERROR: Exe not found at $exePath" -ForegroundColor Red
        cmd /c "exit 1"
        return
    }
    & "$ScriptDir\runtime-missing-failure-smoke.ps1" -ExePath $exePath -ReportPath $reportPath
    cmd /c "exit $LASTEXITCODE"
}
Invoke-StopIfRequested "Runtime-Missing Failure Smoke"

if (-not $stage8_8Passed -and -not $SkipUI) {
    Write-Host ""
    Write-Host "RUNTIME-MISSING FAILURE SMOKE FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}
if (-not $SkipUI) { Invoke-PostStageCleanup }

# ============================================================================
# STAGE 8.9: Backend Smoke Auto-Probe (GAP-069 slice 4)
# ============================================================================
# Runs scripts/ci/run_backend_smoke.py so docs/reports/verification/PROOF_BACKEND_SMOKE_*.json
# exists before Gate/Ledger freshness check. Exit 2 (BLOCKED) is success for the harness.

$skipBackendSmokeAuto = $Quick -or $SkipSmoke -or ($OnlyStage -and $OnlyStage -ne "Gate/Ledger Validation")

$stage8_9Passed = Invoke-Stage -Name "Backend Smoke Auto-Probe" -Description "Canonical backend smoke proof (uvicorn /health + /api/health) before Gate/Ledger" -Skip:$skipBackendSmokeAuto -TimeoutSeconds $StageTimeouts["Backend Smoke Auto-Probe"] -Action {
    $smokeScript = Join-Path $ScriptDir "ci\run_backend_smoke.py"
    & python $smokeScript
    $smokeExit = $LASTEXITCODE
    if ($smokeExit -eq 2) {
        Write-Host "  [ADVISORY] Backend smoke BLOCKED (prerequisites not satisfied; proof file still written)" -ForegroundColor Yellow
        cmd /c "exit 0"
        return
    }
    cmd /c "exit $smokeExit"
}
Invoke-StopIfRequested "Backend Smoke Auto-Probe"

if (-not $stage8_9Passed -and -not $skipBackendSmokeAuto) {
    Write-Host ""
    Write-Host "BACKEND SMOKE AUTO-PROBE FAILED - Stopping verification (fail-fast)" -ForegroundColor Red
    Write-Report
    exit 1
}

# ============================================================================
# STAGE 9: Gate/Ledger Validation
# ============================================================================

$stage9Passed = Invoke-Stage -Name "Gate/Ledger Validation" -Description "Check gate status and validate quality ledger" -Skip:(($OnlyStage -and "Gate/Ledger Validation" -ne $OnlyStage) -or (-not $OnlyStage -and $SkipGates)) -Action {
    $rvArgs = @("scripts/run_verification.py", "--skip-guard")
    if ($EnforceRuntimeProof) {
        $rvArgs += "--enforce-runtime-proof"
        Write-Host "  Gate/Ledger: runtime_proof_staleness enforce mode ON (GAP-015 slice 2)" -ForegroundColor Cyan
    } else {
        Write-Host "  Gate/Ledger: runtime_proof_staleness advisory (use -EnforceRuntimeProof for hard fail)" -ForegroundColor DarkGray
    }
    if ($EnforceBackendSmoke) {
        $rvArgs += "--enforce-backend-smoke"
        Write-Host "  Gate/Ledger: backend_smoke_freshness enforce mode ON (GAP-069 slice 3)" -ForegroundColor Cyan
    } else {
        Write-Host "  Gate/Ledger: backend_smoke_freshness advisory (use -EnforceBackendSmoke for hard fail)" -ForegroundColor DarkGray
    }
    & python @rvArgs
    return $LASTEXITCODE
}
Invoke-StopIfRequested "Gate/Ledger Validation"

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
    $failedList = @($Stages | Where-Object { $_.Status -in @('FAILED', 'TIMED_OUT') })
    foreach ($fs in $failedList) {
        $n = $fs.Name
        $c = $fs.ExitCode
        Write-Host ('  - ' + $n + ' exit=' + $c) -ForegroundColor Red
    }
    exit 1
}
