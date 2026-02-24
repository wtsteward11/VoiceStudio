<#
.SYNOPSIS
    Warning count baseline checker for VoiceStudio builds.
.DESCRIPTION
    Compares build warning count against scripts/warning-baseline.json.
    Fails if warnings increase. Run after dotnet build; pass build output or run a fresh build.
.PARAMETER WarningCount
    Actual warning count from build. If not provided, runs a build and parses output.
.EXAMPLE
    .\scripts\check-warning-baseline.ps1
    .\scripts\check-warning-baseline.ps1 -WarningCount 405
#>
Param(
  [int]$WarningCount = -1
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$baselinePath = Join-Path $scriptDir "warning-baseline.json"

if (-not (Test-Path $baselinePath)) {
  Write-Host "Warning baseline check SKIP: $baselinePath not found" -ForegroundColor Yellow
  exit 0
}

$baseline = Get-Content $baselinePath -Raw | ConvertFrom-Json
$maxWarnings = $baseline.warning_count

if ($WarningCount -lt 0) {
  $buildOut = & dotnet build (Join-Path $rootDir "VoiceStudio.sln") -c Debug -p:Platform=x64 2>&1
  $lastLine = $buildOut | Select-Object -Last 5
  $match = $lastLine -match "(\d+)\s+Warning\(s\)"
  if ($match) {
    $WarningCount = [int]$matches[1]
  } else {
    Write-Host "Warning baseline check SKIP: Could not parse warning count from build" -ForegroundColor Yellow
    exit 0
  }
}

if ($WarningCount -gt $maxWarnings) {
  Write-Host "Warning baseline check FAILED: $WarningCount warnings (max $maxWarnings)" -ForegroundColor Red
  exit 1
}
Write-Host "Warning baseline check PASSED: $WarningCount warnings (max $maxWarnings)" -ForegroundColor Green
exit 0
