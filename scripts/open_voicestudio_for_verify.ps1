# Launch VoiceStudio desktop app against local backend (human verification path).
# Run from repo root:  .\scripts\open_voicestudio_for_verify.ps1
# Does NOT substitute for automated tests — use this when you need to SEE the program working.

$ErrorActionPreference = "Stop"
# PSScriptRoot = ...\VoiceStudio\scripts
$Root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $Root "VoiceStudio.sln"))) {
    $Root = "E:\VoiceStudio"
}

$Exe = Join-Path $Root "src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\VoiceStudio.App.exe"
if (-not (Test-Path $Exe)) {
    $Exe = Get-ChildItem -Path (Join-Path $Root "src\VoiceStudio.App") -Recurse -Filter "VoiceStudio.App.exe" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "\\bin\\" } | Select-Object -First 1 -ExpandProperty FullName
}

if (-not $Exe -or -not (Test-Path $Exe)) {
    Write-Host "VoiceStudio.App.exe not found. Build first:" -ForegroundColor Red
    Write-Host "  dotnet build VoiceStudio.sln -c Debug -p:Platform=x64" -ForegroundColor Yellow
    exit 1
}

try {
    $r = Invoke-WebRequest -Uri "http://127.0.0.1:8000/health" -UseBasicParsing -TimeoutSec 3
    Write-Host "Backend OK: http://127.0.0.1:8000/health -> $($r.StatusCode)" -ForegroundColor Green
} catch {
    Write-Host "No backend on 127.0.0.1:8000. Start it in another terminal, then re-run this script:" -ForegroundColor Yellow
    Write-Host '  cd E:\VoiceStudio; $env:PYTHONPATH="E:\VoiceStudio"; Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue' -ForegroundColor Gray
    Write-Host '  .\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000' -ForegroundColor Gray
    exit 2
}

Remove-Item Env:VOICESTUDIO_TEST_MODE -ErrorAction SilentlyContinue
Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe)
Write-Host "Launched: $Exe" -ForegroundColor Green
Write-Host "In the app: Modules -> Voice -> Voice Synthesis (or your shortcut). Backend: http://127.0.0.1:8000" -ForegroundColor Cyan
