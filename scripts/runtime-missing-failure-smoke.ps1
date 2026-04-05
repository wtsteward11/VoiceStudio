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

$proc = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $proc) {
    Write-Host 'ERROR: Process.Start returned null.' -ForegroundColor Red
    exit 3
}

$deadline = [datetime]::UtcNow.AddSeconds(45)
$passed = $false
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

if ($passed) {
    exit 0
}

exit 1
