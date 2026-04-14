<#
.SYNOPSIS
    Display checkpoint lineage for the verify harness.
.DESCRIPTION
    Reads artifacts/verify/latest/checkpoint.json and artifacts/verify/latest_pointer.json,
    prints both, and reports whether junction target, checkpoint, and pointer agree.
    Useful for manual pre-resume sanity checks.
#>
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$latestLink = Join-Path $root "artifacts\verify\latest"
$pointerPath = Join-Path $root "artifacts\verify\latest_pointer.json"

if (-not (Test-Path $latestLink)) {
    Write-Host "No artifacts/verify/latest link found. Run verify.ps1 first." -ForegroundColor Yellow
    exit 1
}

$resolvedTarget = (Get-Item $latestLink).Target
$cpPath = Join-Path $latestLink "checkpoint.json"

Write-Host ""
Write-Host "=== CHECKPOINT LINEAGE ===" -ForegroundColor Cyan
Write-Host "  Junction target: $resolvedTarget" -ForegroundColor White

if (Test-Path $cpPath) {
    $cp = Get-Content $cpPath -Raw | ConvertFrom-Json
    Write-Host "  Checkpoint run:  $($cp.run_timestamp)" -ForegroundColor White
    Write-Host "  Artifact dir:    $($cp.artifact_dir)" -ForegroundColor White
    Write-Host "  Last stage:      $($cp.last_completed_stage)" -ForegroundColor White
    Write-Host "  Stages:          $(@($cp.stages).Count)" -ForegroundColor White
} else {
    Write-Host "  Checkpoint:      NOT FOUND (no checkpoint.json in latest)" -ForegroundColor Yellow
}

if (Test-Path $pointerPath) {
    $ptr = Get-Content $pointerPath -Raw | ConvertFrom-Json
    Write-Host "  Pointer run_dir: $($ptr.run_dir)" -ForegroundColor White
    Write-Host "  Pointer status:  $($ptr.overall_status)" -ForegroundColor White
    Write-Host "  Pointer commit:  $($ptr.commit_hash)" -ForegroundColor White
} else {
    Write-Host "  Pointer:         NOT FOUND (no latest_pointer.json)" -ForegroundColor Yellow
}

Write-Host "==========================" -ForegroundColor Cyan

$allAgree = $true
if ((Test-Path $cpPath) -and $resolvedTarget) {
    $cp = Get-Content $cpPath -Raw | ConvertFrom-Json
    if ($cp.artifact_dir -and ($resolvedTarget -ne $cp.artifact_dir)) {
        Write-Host "  MISMATCH: junction -> '$resolvedTarget' vs checkpoint artifact_dir -> '$($cp.artifact_dir)'" -ForegroundColor Red
        $allAgree = $false
    }
}
if ((Test-Path $pointerPath) -and (Test-Path $cpPath)) {
    $ptr = Get-Content $pointerPath -Raw | ConvertFrom-Json
    $cp = Get-Content $cpPath -Raw | ConvertFrom-Json
    if ($ptr.run_dir -and $cp.artifact_dir -and ($ptr.run_dir -ne $cp.artifact_dir)) {
        Write-Host "  MISMATCH: pointer run_dir -> '$($ptr.run_dir)' vs checkpoint artifact_dir -> '$($cp.artifact_dir)'" -ForegroundColor Red
        $allAgree = $false
    }
}

if ($allAgree) {
    Write-Host "  RESULT: All sources agree." -ForegroundColor Green
} else {
    Write-Host "  RESULT: Lineage disagreement detected. Re-run -StopAfterStage before -ResumeFrom." -ForegroundColor Red
    exit 1
}
