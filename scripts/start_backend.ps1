<# 
VoiceStudio Backend Starter
Ensures the correct Python venv is used
#>

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProjectRoot) { $ProjectRoot = "E:\VoiceStudio" }

Write-Host "=" * 60 -ForegroundColor Cyan
Write-Host "VoiceStudio Backend Starter" -ForegroundColor Cyan
Write-Host "=" * 60 -ForegroundColor Cyan

# Set environment
$env:PYTHONPATH = $ProjectRoot
$VenvPython = Join-Path $ProjectRoot ".venv\Scripts\python.exe"

if (-not (Test-Path $VenvPython)) {
    Write-Host "ERROR: Virtual environment not found at $VenvPython" -ForegroundColor Red
    exit 1
}

# Check Python version
$PyVersion = & $VenvPython --version 2>&1
Write-Host "Python: $PyVersion" -ForegroundColor Green

# Check key dependencies
Write-Host "`nChecking key dependencies..." -ForegroundColor Yellow
$CheckCmd = @"
import sys
checks = []
try:
    import torch
    checks.append(f'torch: {torch.__version__}')
except: checks.append('torch: MISSING')
try:
    import librosa
    checks.append(f'librosa: {librosa.__version__}')
except: checks.append('librosa: MISSING')
try:
    from TTS.api import TTS
    checks.append('coqui-tts: OK')
except: checks.append('coqui-tts: MISSING')
try:
    from faster_whisper import WhisperModel
    checks.append('faster-whisper: OK')
except: checks.append('faster-whisper: MISSING')
print('\n'.join(checks))
"@
& $VenvPython -c $CheckCmd

# Check if port 8000 is free
$Port8000 = netstat -ano 2>$null | Select-String ":8000.*LISTENING"
if ($Port8000) {
    Write-Host "`nWARNING: Port 8000 already in use!" -ForegroundColor Yellow
    Write-Host $Port8000 -ForegroundColor Yellow
    $response = Read-Host "Kill existing process? (y/n)"
    if ($response -eq 'y') {
        $pid = ($Port8000 -split '\s+')[-1]
        taskkill /PID $pid /F
        Start-Sleep -Seconds 1
    }
}

Write-Host "`nStarting backend on http://127.0.0.1:8000 ..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop`n" -ForegroundColor Gray

# Start uvicorn
& $VenvPython -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000 --reload
