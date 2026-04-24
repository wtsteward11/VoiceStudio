# Dev backend on 127.0.0.1:8030 — same port as Slice 10/12 live-backend proofs.
# Run:  .\scripts\backend\dev_server_8030.ps1
# Or:   powershell -NoExit -File .\scripts\backend\dev_server_8030.ps1

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Set-Location $projectRoot

$venvPython = Join-Path $projectRoot ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    Write-Host "ERROR: Not found: $venvPython" -ForegroundColor Red
    Write-Host "Create venv at repo root or set up .venv before starting." -ForegroundColor Yellow
    exit 1
}

$env:PYTHONPATH = $projectRoot
$env:HF_ENDPOINT = "https://router.huggingface.co"
$env:HF_INFERENCE_API_BASE = "https://router.huggingface.co"
if (-not $env:VOICESTUDIO_MODELS_PATH) {
    $env:VOICESTUDIO_MODELS_PATH = Join-Path $projectRoot "models"
}

Write-Host ""
Write-Host "VoiceStudio API (dev)" -ForegroundColor Cyan
Write-Host "  URL:    http://127.0.0.1:8030" -ForegroundColor Green
Write-Host "  Docs:   http://127.0.0.1:8030/docs" -ForegroundColor Green
Write-Host "  Ready:  GET http://127.0.0.1:8030/api/health/ready" -ForegroundColor Gray
Write-Host "  Ctrl+C to stop." -ForegroundColor Gray
Write-Host ""

& $venvPython -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8030
