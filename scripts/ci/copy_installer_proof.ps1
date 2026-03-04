# Copy Installer Lifecycle proof to repo-tracked path for STATE.md verification.
# Run after: .\installer\test-installer-lifecycle.ps1 -LogDir ".buildlogs/installer-lifecycle"

param(
    [string]$LogDir = (Join-Path (Resolve-Path "$PSScriptRoot\..\..").Path ".buildlogs\installer-lifecycle"),
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
)

$ErrorActionPreference = "Stop"

$proofPath = Join-Path $RepoRoot "docs\reports\verification\PROOF_INSTALLER_2026-03-02.json"

if (-not (Test-Path $LogDir)) {
    Write-Error "Log dir missing: $LogDir. Run test-installer-lifecycle.ps1 with -LogDir first."
}

$logFiles = Get-ChildItem -Path $LogDir -Filter "voicestudio_lifecycle_*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
if (-not $logFiles -or $logFiles.Count -eq 0) {
    Write-Error "No lifecycle log found in $LogDir. Run test-installer-lifecycle.ps1 first."
}

$latestLog = $logFiles[0].FullName
$logContent = Get-Content -Path $latestLog -Raw

$steps = @("InstallV1", "LaunchV1", "UpgradeV1ToV2", "LaunchV2", "RollbackV2ToV1", "LaunchV1AfterRollback", "UninstallV1")
$results = @{}
foreach ($step in $steps) {
    if ($logContent -match "$step\s*:\s*(PASS|FAIL|SKIPPED)") {
        $results[$step] = $Matches[1]
    }
    else {
        $results[$step] = "UNKNOWN"
    }
}

$allPassed = ($results.Values | Where-Object { $_ -ne "PASS" }).Count -eq 0

if (-not $allPassed) {
    Write-Error "Lifecycle test did not pass. All 7 steps must PASS before copying proof. Run test-installer-lifecycle.ps1 successfully first. Results: $($results | ConvertTo-Json -Compress)"
}

# Machine-verifiable proof schema (GAP E): command, exit_code, git_commit, git_branch, timestamp
$gitCommit = git -C $RepoRoot rev-parse HEAD 2>$null; if (-not $gitCommit) { $gitCommit = "unknown" }
$gitBranch = git -C $RepoRoot branch --show-current 2>$null; if (-not $gitBranch) { $gitBranch = "unknown" }

$proof = @{
    step       = "installer_lifecycle"
    date       = (Get-Date -Format "yyyy-MM-dd")
    timestamp  = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    command    = ".\installer\test-installer-lifecycle.ps1 -LogDir `"$LogDir`""
    exit_code  = if ($allPassed) { 0 } else { 1 }
    git_commit = $gitCommit.Trim()
    git_branch = $gitBranch.Trim()
    log_file   = $latestLog
    log_dir    = $LogDir
    all_passed = $allPassed
    results    = $results
}
$proofJson = $proof | ConvertTo-Json -Depth 5
$proofDir = Split-Path $proofPath -Parent
if (-not (Test-Path $proofDir)) { New-Item -ItemType Directory -Path $proofDir -Force | Out-Null }
$proofJson | Set-Content -Path $proofPath -Encoding UTF8
python (Join-Path $RepoRoot "scripts\ci\add_proof_fingerprint.py") $proofPath
if ($LASTEXITCODE -ne 0) { Write-Error "Failed to add evidence_fingerprint" }
Write-Host "Proof written to $proofPath"
