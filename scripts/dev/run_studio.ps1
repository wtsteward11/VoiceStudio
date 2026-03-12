<#
.SYNOPSIS
  Start backend + app in one command for local development.

.DESCRIPTION
  1. Checks if the backend is already running on the configured port.
  2. If not, starts uvicorn in a background process.
  3. Waits for /api/health to return 200.
  4. Builds and launches the WinUI app.
  Press Ctrl+C to stop both processes.

.PARAMETER Port
  Backend port (default: value of VOICESTUDIO_API_PORT or 8000).

.PARAMETER SkipBuild
  Launch the last build output without rebuilding.
#>
[CmdletBinding()]
param(
    [int]$Port = $(if ($env:VOICESTUDIO_API_PORT) { [int]$env:VOICESTUDIO_API_PORT } else { 8000 }),
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$VenvPython  = Join-Path $ProjectRoot ".venv\Scripts\python.exe"
$BackendJob  = $null

function Write-Banner([string]$msg) {
    Write-Host "`n$('=' * 60)" -ForegroundColor Cyan
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host "$('=' * 60)`n" -ForegroundColor Cyan
}

function Test-BackendRunning {
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/api/health" -TimeoutSec 2 -ErrorAction Stop
        return $r.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Stop-BackendJob {
    if ($BackendJob -and $BackendJob.HasExited -eq $false) {
        Write-Host "Stopping backend (PID $($BackendJob.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $BackendJob.Id -Force -ErrorAction SilentlyContinue
    }
}

trap { Stop-BackendJob; break }

# ── Backend ──────────────────────────────────────────────────────

Write-Banner "VoiceStudio Dev Launcher"

if (-not (Test-Path $VenvPython)) {
    Write-Host "ERROR: Python venv not found at $VenvPython" -ForegroundColor Red
    Write-Host "Run:  python -m venv .venv && .venv\Scripts\pip install -r requirements.txt" -ForegroundColor Yellow
    exit 1
}

if (Test-BackendRunning) {
    Write-Host "Backend already running on http://127.0.0.1:$Port" -ForegroundColor Green
} else {
    Write-Host "Starting backend on http://127.0.0.1:$Port ..." -ForegroundColor Yellow

    $env:PYTHONPATH = $ProjectRoot
    $script:BackendJob = Start-Process -FilePath $VenvPython `
        -ArgumentList "-m", "uvicorn", "backend.api.main:app", "--host", "127.0.0.1", "--port", $Port, "--reload" `
        -WorkingDirectory $ProjectRoot `
        -PassThru -WindowStyle Minimized

    $deadline = (Get-Date).AddSeconds(30)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if (Test-BackendRunning) { $ready = $true; break }
        Start-Sleep -Milliseconds 500
    }

    if (-not $ready) {
        Write-Host "ERROR: Backend did not become healthy within 30 s" -ForegroundColor Red
        Stop-BackendJob
        exit 1
    }
    Write-Host "Backend healthy (PID $($BackendJob.Id))" -ForegroundColor Green
}

Write-Host "Backend URL: http://127.0.0.1:$Port" -ForegroundColor Cyan

# ── Build ────────────────────────────────────────────────────────

$ExeDir = Join-Path $ProjectRoot ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0"
$ExePath = Join-Path $ExeDir "VoiceStudio.App.exe"

if (-not $SkipBuild) {
    Write-Banner "Building VoiceStudio (Debug|x64)"
    & dotnet build (Join-Path $ProjectRoot "VoiceStudio.sln") -c Debug -p:Platform=x64 --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed" -ForegroundColor Red
        Stop-BackendJob
        exit 1
    }
    Write-Host "Build succeeded" -ForegroundColor Green
}

if (-not (Test-Path $ExePath)) {
    Write-Host "ERROR: Exe not found at $ExePath" -ForegroundColor Red
    Stop-BackendJob
    exit 1
}

# ── Launch ───────────────────────────────────────────────────────

Write-Banner "Launching VoiceStudio"

$env:VOICESTUDIO_API_HOST = "127.0.0.1"
$env:VOICESTUDIO_API_PORT = "$Port"

& $ExePath
$AppExit = $LASTEXITCODE

Stop-BackendJob
exit $AppExit
