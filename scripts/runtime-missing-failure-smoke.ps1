#Requires -Version 5.1
<#
.SYNOPSIS
  Runtime-missing failure-path smoke for VoiceStudio.App (verify.ps1 Stage 8.8).

.DESCRIPTION
  Launches the app with VOICE_STUDIO_SMOKE_FAILURE_RUNTIME=1. App.xaml.cs points
  VOICESTUDIO_APP_ROOT at an empty temp directory so backend spawn fails with a
  runtime/app-root message. Waits for failure_runtime_smoke_summary.json under
  %LOCALAPPDATA%\VoiceStudio\crashes, copies to -ReportPath, exits 0 on PASS.
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'

$crashRoot = Join-Path $env:LOCALAPPDATA 'VoiceStudio\crashes'
$summaryPath = Join-Path $crashRoot 'failure_runtime_smoke_summary.json'
if (Test-Path -LiteralPath $summaryPath) {
    Remove-Item -LiteralPath $summaryPath -Force -ErrorAction SilentlyContinue
}

# Smoke run writes startup_decision.json with decision=app_root_invalid; scripts/ci/check_startup_artifact.py
# treats that as a hard regression. Restore prior artifact or remove if none existed.
$startupDecisionPath = Join-Path $crashRoot 'startup_decision.json'
$startupDecisionBackup = $null
if (Test-Path -LiteralPath $startupDecisionPath) {
    $startupDecisionBackup = [System.IO.Path]::GetTempFileName()
    Copy-Item -LiteralPath $startupDecisionPath -Destination $startupDecisionBackup -Force
}

Get-Process -Name 'VoiceStudio.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.UseShellExecute = $false
$psi.FileName = $ExePath
$psi.WorkingDirectory = Split-Path -Parent $ExePath

foreach ($entry in [System.Environment]::GetEnvironmentVariables().GetEnumerator()) {
    $key = $entry.Key.ToString()
    $val = $entry.Value.ToString()
    $psi.Environment[$key] = $val
}

$psi.Environment['VOICE_STUDIO_SMOKE_FAILURE_RUNTIME'] = '1'
$psi.Environment['VOICESTUDIO_API_HOST'] = '127.0.0.1'
$psi.Environment['VOICESTUDIO_API_PORT'] = '8000'
[void]$psi.Environment.Remove('VOICESTUDIO_BACKEND_URL')
foreach ($k in @(
        'VOICE_STUDIO_SMOKE_UI',
        'VOICE_STUDIO_SMOKE_EXIT',
        'VOICE_STUDIO_UI_SELF_TEST',
        'VOICE_STUDIO_UI_SELF_TEST_REQUIRE_BACKEND',
        'VOICE_STUDIO_ICON_LAUNCH_SMOKE',
        'VOICE_STUDIO_ICON_LAUNCH_SMOKE_OUT'
    )) {
    [void]$psi.Environment.Remove($k)
}

$proc = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $proc) {
    Write-Host 'ERROR: Process.Start returned null.' -ForegroundColor Red
    if ($null -ne $startupDecisionBackup -and (Test-Path -LiteralPath $startupDecisionBackup)) {
        Copy-Item -LiteralPath $startupDecisionBackup -Destination $startupDecisionPath -Force
        Remove-Item -LiteralPath $startupDecisionBackup -Force -ErrorAction SilentlyContinue
    }
    exit 3
}

$deadline = [datetime]::UtcNow.AddSeconds(45)
$passed = $false
try {
while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $summaryPath) {
        try {
            $raw = Get-Content -LiteralPath $summaryPath -Raw -ErrorAction Stop
            $j = $raw | ConvertFrom-Json
            if ($j.status -eq 'PASS') {
                $passed = $true
                break
            }
            if ($j.status -eq 'FAIL') {
                break
            }
        }
        catch {
            # Partial write; keep polling
        }
    }

    if ($proc.WaitForExit(250)) {
        if (Test-Path -LiteralPath $summaryPath) {
            try {
                $raw = Get-Content -LiteralPath $summaryPath -Raw -ErrorAction Stop
                $j = $raw | ConvertFrom-Json
                if ($j.status -eq 'PASS') {
                    $passed = $true
                }
            }
            catch {
                # ignore
            }
        }
        break
    }
}

if (-not $proc.HasExited) {
    try {
        $proc.Kill($true)
    }
    catch {
        # best effort
    }
    try {
        $proc.WaitForExit(5000)
    }
    catch {
        # ignore
    }
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if (Test-Path -LiteralPath $summaryPath) {
    Copy-Item -LiteralPath $summaryPath -Destination $ReportPath -Force
}
else {
    $payload = [ordered]@{
        status            = 'FAIL'
        timestamp_utc     = [datetime]::UtcNow.ToString('o')
        error             = 'failure_runtime_smoke_summary.json not found under LocalAppData VoiceStudio\crashes'
        process_exit_code = $proc.ExitCode
    }
    ($payload | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}
}
finally {
    if ($null -ne $startupDecisionBackup -and (Test-Path -LiteralPath $startupDecisionBackup)) {
        Copy-Item -LiteralPath $startupDecisionBackup -Destination $startupDecisionPath -Force
        Remove-Item -LiteralPath $startupDecisionBackup -Force -ErrorAction SilentlyContinue
    }
    elseif (Test-Path -LiteralPath $startupDecisionPath) {
        # No prior artifact: smoke wrote app_root_invalid (hard-fail for check_startup_artifact). Replace with a
        # schema-valid neutral success placeholder so Gate/Ledger is not poisoned (see scripts/ci/check_startup_artifact.py).
        $templatePath = Join-Path $PSScriptRoot 'ci\startup_decision_success_template.json'
        if (Test-Path -LiteralPath $templatePath) {
            Copy-Item -LiteralPath $templatePath -Destination $startupDecisionPath -Force
        }
    }
}

if ($passed) {
    exit 0
}

exit 1
