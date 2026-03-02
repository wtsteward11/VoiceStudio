# Copy Gate C proof artifacts to repo-tracked path for STATE.md verification.
# Run after: .\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke -UiSmokeTimeoutSeconds 120

param(
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$gatecLatest = Join-Path $RepoRoot ".buildlogs\gatec-latest.txt"
$uiSmokeSummaryPublish = Join-Path $RepoRoot ".buildlogs\x64\$Configuration\gatec-publish\ui_smoke_summary.json"
$uiSmokeSummaryCrash = Join-Path $env:LOCALAPPDATA "VoiceStudio\crashes\ui_smoke_summary.json"
$proofPath = Join-Path $RepoRoot "docs\reports\verification\PROOF_GATE_C_2026-03-02.json"

if (-not (Test-Path $gatecLatest)) {
    Write-Error "Gate C proof source missing: $gatecLatest. Run gatec-publish-launch.ps1 first."
}

# Prefer publish dir (gatec copies there); fallback to crash dir (app writes there directly)
$uiSmokeSummary = $null
if (Test-Path $uiSmokeSummaryPublish) {
    $uiSmokeSummary = $uiSmokeSummaryPublish
}
elseif (Test-Path $uiSmokeSummaryCrash) {
    $uiSmokeSummary = $uiSmokeSummaryCrash
}
if (-not $uiSmokeSummary) {
    Write-Error "UI smoke summary missing. Checked: $uiSmokeSummaryPublish and $uiSmokeSummaryCrash. Run gatec with -UiSmoke first."
}

$gatecContent = (Get-Content -Path $gatecLatest -Raw -Encoding UTF8).ToString()
$smokeJson = Get-Content -Path $uiSmokeSummary -Raw | ConvertFrom-Json

# Machine-verifiable proof schema (GAP E): command, exit_code, git_commit, git_branch, timestamp
$gitCommit = git -C $RepoRoot rev-parse HEAD 2>$null; if (-not $gitCommit) { $gitCommit = "unknown" }
$gitBranch = git -C $RepoRoot branch --show-current 2>$null; if (-not $gitBranch) { $gitBranch = "unknown" }

# App writes nav_steps; some docs expect nav_steps_completed
$navSteps = if ($smokeJson.nav_steps_completed) { $smokeJson.nav_steps_completed } elseif ($smokeJson.nav_steps) { $smokeJson.nav_steps.Count } else { 0 }
$proof = @{
    step       = "gate_c_publish_ui_smoke"
    date       = (Get-Date -Format "yyyy-MM-dd")
    timestamp  = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
    command    = ".\scripts\gatec-publish-launch.ps1 -Configuration Release -RuntimeIdentifier win-x64 -UiSmoke -UiSmokeTimeoutSeconds 120"
    exit_code  = $smokeJson.exit_code
    git_commit = $gitCommit.Trim()
    git_branch = $gitBranch.Trim()
    gatec_log  = $gatecContent
    ui_smoke   = @{
        exit_code             = $smokeJson.exit_code
        binding_failure_count  = $smokeJson.binding_failure_count
        nav_steps_completed    = $navSteps
    }
}
$proofJson = $proof | ConvertTo-Json -Depth 5
$proofDir = Split-Path $proofPath -Parent
if (-not (Test-Path $proofDir)) { New-Item -ItemType Directory -Path $proofDir -Force | Out-Null }
$proofJson | Set-Content -Path $proofPath -Encoding UTF8
Write-Host "Proof written to $proofPath"
