<#
.SYNOPSIS
  Canonical CI waiter. Polls a GitHub Actions run until completion.
  Never uses gh run watch (incompatible with fine-grained PATs).

.PARAMETER RunId
  GitHub Actions run ID (string to avoid int32 overflow; real IDs exceed 2^31).
  If omitted, auto-selects the latest run for the current branch.

.PARAMETER Branch
  Git branch to search for runs. Defaults to current branch.

.PARAMETER Workflow
  Filter auto-selected runs by workflow name (case-insensitive contains).

.PARAMETER TimeoutMinutes
  Hard timeout. Default 30. Exit 124 on expiry.

.PARAMETER IntervalSeconds
  Poll interval. Default 20.

.EXAMPLE
  .\scripts\ci\wait_for_gh_run.ps1 -RunId 22752889042
.EXAMPLE
  .\scripts\ci\wait_for_gh_run.ps1 -Workflow "CI" -TimeoutMinutes 10
.EXAMPLE
  .\scripts\ci\wait_for_gh_run.ps1 -Branch main -Workflow "Build" -IntervalSeconds 30
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$RunId,

    [Parameter(Mandatory = $false)]
    [string]$Branch,

    [Parameter(Mandatory = $false)]
    [string]$Workflow,

    [Parameter(Mandatory = $false)]
    [int]$TimeoutMinutes = 30,

    [Parameter(Mandatory = $false)]
    [int]$IntervalSeconds = 20
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── helpers ──────────────────────────────────────────────────────────────────

function Write-Fail {
    param([string]$Message)
    Write-Host "ERROR: $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Cyan
}

function Invoke-GhJson {
    param(
        [string[]]$Arguments,
        [int]$MaxRetries = 3,
        [int]$RetryDelaySec = 5
    )
    for ($attempt = 1; $attempt -le $MaxRetries; $attempt++) {
        try {
            $raw = & gh @Arguments 2>&1
            if ($LASTEXITCODE -ne 0) {
                throw "gh exited $LASTEXITCODE`: $raw"
            }
            return ($raw | Out-String | ConvertFrom-Json)
        }
        catch {
            if ($attempt -eq $MaxRetries) {
                throw "gh command failed after $MaxRetries attempts: $_"
            }
            Write-Host "  Retry $attempt/$MaxRetries after transient failure..." -ForegroundColor Yellow
            Start-Sleep -Seconds $RetryDelaySec
        }
    }
}

function Write-RunSummary {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Url,
        [string]$Status,
        [string]$Conclusion,
        [int]$ElapsedSec,
        [int]$ExitCode
    )
    Write-Host ""
    Write-Host "=== CI Run Result ===" -ForegroundColor White
    Write-Host "  Run ID:     $Id"
    Write-Host "  Workflow:   $Name"
    Write-Host "  URL:        $Url"
    Write-Host "  Status:     $Status"
    Write-Host "  Conclusion: $Conclusion"
    Write-Host "  Elapsed:    ${ElapsedSec}s"
    Write-Host "  Exit code:  $ExitCode"
    Write-Host ""
}

# ── preconditions ────────────────────────────────────────────────────────────

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Fail "gh CLI not installed or not in PATH."
    exit 1
}

$authCheck = & gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "gh CLI not authenticated. Run: gh auth login"
    Write-Fail "Details: $authCheck"
    exit 1
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Fail "git not installed or not in PATH."
    exit 1
}

$gitCheck = & git rev-parse --is-inside-work-tree 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Not inside a git repository."
    exit 1
}

# ── run selection ────────────────────────────────────────────────────────────

$selectedName = ""

if ($RunId) {
    if ($RunId -notmatch '^\d+$') {
        Write-Fail "RunId must be numeric. Got: $RunId"
        exit 1
    }
    Write-Info "Using provided RunId: $RunId"
    try {
        $probe = Invoke-GhJson -Arguments @('run', 'view', $RunId, '--json', 'databaseId,name,status,conclusion,url') -MaxRetries 1
        $selectedName = $probe.name
        Write-Info "  Workflow: $selectedName  Status: $($probe.status)"
    }
    catch {
        Write-Fail "Run $RunId not found or inaccessible."
        Write-Fail "  $_"
        exit 1
    }
}
else {
    if (-not $Branch) {
        $Branch = (& git branch --show-current 2>&1).Trim()
        if (-not $Branch) {
            Write-Fail "Cannot determine branch (detached HEAD?). Use -Branch or -RunId."
            exit 1
        }
    }
    Write-Info "Auto-selecting run on branch: $Branch"

    $ghArgs = @('run', 'list', '--branch', $Branch, '--limit', '20',
                '--json', 'databaseId,name,createdAt,status,conclusion,url')
    try {
        $runs = Invoke-GhJson -Arguments $ghArgs
    }
    catch {
        Write-Fail "Failed to list runs for branch '$Branch': $_"
        exit 1
    }

    if (-not $runs -or $runs.Count -eq 0) {
        Write-Fail "No runs found for branch '$Branch'."
        exit 1
    }

    if ($Workflow) {
        $runs = @($runs | Where-Object { $_.name -like "*$Workflow*" })
        if ($runs.Count -eq 0) {
            Write-Fail "No runs matching workflow '$Workflow' on branch '$Branch'."
            exit 1
        }
    }

    $active = @($runs | Where-Object { $_.status -eq 'queued' -or $_.status -eq 'in_progress' })
    if ($active.Count -gt 0) {
        $selected = $active[0]
    }
    else {
        $selected = $runs[0]
    }

    $RunId = [string]$selected.databaseId
    $selectedName = $selected.name
    Write-Info "Selected run $RunId ($selectedName) — status: $($selected.status)"
}

# ── early completion check ───────────────────────────────────────────────────

$initial = Invoke-GhJson -Arguments @('run', 'view', $RunId, '--json', 'status,conclusion,url,name')
if (-not $selectedName) { $selectedName = $initial.name }

if ($initial.status -eq 'completed') {
    $exitCode = if ($initial.conclusion -eq 'success') { 0 } else { 1 }
    Write-RunSummary -Id $RunId -Name $selectedName -Url $initial.url `
        -Status $initial.status -Conclusion $initial.conclusion `
        -ElapsedSec 0 -ExitCode $exitCode
    exit $exitCode
}

# ── polling loop ─────────────────────────────────────────────────────────────

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$timeoutMs = [long]$TimeoutMinutes * 60 * 1000

Write-Info "Polling every ${IntervalSeconds}s (timeout: ${TimeoutMinutes}m)..."
Write-Host ""

while ($true) {
    $elapsedSec = [math]::Floor($sw.Elapsed.TotalSeconds)

    if ($sw.ElapsedMilliseconds -ge $timeoutMs) {
        Write-Fail "TIMEOUT after ${TimeoutMinutes}m (${elapsedSec}s elapsed)."
        Write-RunSummary -Id $RunId -Name $selectedName -Url $initial.url `
            -Status "timeout" -Conclusion "script_timeout" `
            -ElapsedSec $elapsedSec -ExitCode 124
        exit 124
    }

    try {
        $r = Invoke-GhJson -Arguments @('run', 'view', $RunId, '--json', 'status,conclusion,url,name')
    }
    catch {
        Write-Fail "Lost contact with GitHub API after retries: $_"
        Write-RunSummary -Id $RunId -Name $selectedName -Url "(unavailable)" `
            -Status "api_error" -Conclusion "api_error" `
            -ElapsedSec $elapsedSec -ExitCode 1
        exit 1
    }

    $ts = Get-Date -Format "HH:mm:ss"
    $conclusion = if ($r.conclusion) { $r.conclusion } else { "-" }

    if ($r.status -eq 'completed') {
        $exitCode = if ($r.conclusion -eq 'success') { 0 } else { 1 }
        Write-Host "[$ts] $($r.status) / $conclusion  (${elapsedSec}s)" -ForegroundColor $(if ($exitCode -eq 0) { 'Green' } else { 'Red' })
        Write-RunSummary -Id $RunId -Name $selectedName -Url $r.url `
            -Status $r.status -Conclusion $r.conclusion `
            -ElapsedSec $elapsedSec -ExitCode $exitCode
        exit $exitCode
    }

    Write-Host "[$ts] $($r.status) / $conclusion  (${elapsedSec}s)"
    Start-Sleep -Seconds $IntervalSeconds
}
