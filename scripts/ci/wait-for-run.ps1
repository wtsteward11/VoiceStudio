<#
.SYNOPSIS
  Wait for a GitHub Actions run to complete, polling every 15s. No fixed sleep.
.PARAMETER RunId
  GitHub Actions run ID (e.g. 22685780471)
.PARAMETER TimeoutSeconds
  Max wait time (default 1800 = 30 min)
.PARAMETER PollIntervalSeconds
  Poll interval (default 15)
.EXAMPLE
  .\scripts\ci\wait-for-run.ps1 -RunId 22685780471
  Exit 0 = success, 1 = failure/timeout
#>
param(
  [Parameter(Mandatory=$true)]
  [string]$RunId,
  [int]$TimeoutSeconds = 1800,
  [int]$PollIntervalSeconds = 15
)

$elapsed = 0
while ($elapsed -lt $TimeoutSeconds) {
  $json = gh run view $RunId --json status,conclusion 2>$null
  if ($LASTEXITCODE -ne 0) {
    Write-Host "gh run view failed (auth? run ID?)"; exit 1
  }
  $r = $json | ConvertFrom-Json
  if ($r.status -eq "completed") {
    Write-Host "Run $RunId : $($r.conclusion)"
    exit [int]($r.conclusion -ne "success")
  }
  Write-Host "Run $RunId : $($r.status) (${elapsed}s)"
  Start-Sleep -Seconds $PollIntervalSeconds
  $elapsed += $PollIntervalSeconds
}
Write-Host "Timeout after ${TimeoutSeconds}s"; exit 1
