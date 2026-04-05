#Requires -Version 5.1
<#
.SYNOPSIS
  Port-occupied failure-path smoke for VoiceStudio.App (verify.ps1 Stage 8.7).

.DESCRIPTION
  Binds 127.0.0.1:VOICESTUDIO_API_PORT (default 8000), launches the app with
  VOICE_STUDIO_SMOKE_FAILURE_PORT=1, waits for failure_smoke_summary.json under
  %LOCALAPPDATA%\VoiceStudio\crashes, copies payload to -ReportPath, exits 0 on PASS.
#>
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [Parameter(Mandatory = $true)][string]$ReportPath
)

$ErrorActionPreference = 'Stop'

$port = 8000
$portEnv = [Environment]::GetEnvironmentVariable('VOICESTUDIO_API_PORT')
if (-not [string]::IsNullOrWhiteSpace($portEnv)) {
    $parsed = 0
    if ([int]::TryParse($portEnv, [ref]$parsed) -and $parsed -gt 0 -and $parsed -le 65535) {
        $port = $parsed
    }
}

$listener = $null
try {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $port)
    $listener.Start()
}
catch {
    Write-Host "ERROR: Could not bind 127.0.0.1:${port}: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}

$crashRoot = Join-Path $env:LOCALAPPDATA 'VoiceStudio\crashes'
$summaryPath = Join-Path $crashRoot 'failure_smoke_summary.json'
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

$psi.Environment['VOICE_STUDIO_SMOKE_FAILURE_PORT'] = '1'
$psi.Environment['VOICESTUDIO_API_HOST'] = '127.0.0.1'
$psi.Environment['VOICESTUDIO_API_PORT'] = "$port"
[void]$psi.Environment.Remove('VOICESTUDIO_BACKEND_URL')

$proc = [System.Diagnostics.Process]::Start($psi)
if ($null -eq $proc) {
    Write-Host 'ERROR: Process.Start returned null.' -ForegroundColor Red
    if ($null -ne $listener) { $listener.Stop() }
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
        # Process may exit immediately after writing the summary; re-read before leaving the loop.
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

if ($null -ne $listener) {
    try {
        $listener.Stop()
    }
    catch {
        # ignore
    }
    $listener = $null
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ReportPath) | Out-Null
if (Test-Path -LiteralPath $summaryPath) {
    Copy-Item -LiteralPath $summaryPath -Destination $ReportPath -Force
}
else {
    $payload = [ordered]@{
        status               = 'FAIL'
        timestamp_utc        = [datetime]::UtcNow.ToString('o')
        error                = 'failure_smoke_summary.json not found under LocalAppData VoiceStudio\crashes'
        process_exit_code    = $proc.ExitCode
    }
    ($payload | ConvertTo-Json -Depth 6) | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

if ($passed) {
    exit 0
}

exit 1
