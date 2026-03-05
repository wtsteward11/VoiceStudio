# Start XTTS Service
# Runs the isolated XTTS environment as an HTTP microservice

$XTTSPath = "$PSScriptRoot\..\runtime\xtts_service"
$VenvPath = "$XTTSPath\.venv"
$pythonPath = "$VenvPath\Scripts\python.exe"

# Check if setup is complete
if (-not (Test-Path "$XTTSPath\.setup_complete")) {
    Write-Host "XTTS environment not set up. Running setup..." -ForegroundColor Yellow
    & "$PSScriptRoot\setup\setup_xtts_venv.ps1"
}

if (-not (Test-Path $pythonPath)) {
    Write-Host "ERROR: XTTS virtual environment not found at $VenvPath" -ForegroundColor Red
    exit 1
}

Write-Host "Starting XTTS Service on http://127.0.0.1:8081..." -ForegroundColor Green
Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
Write-Host ""

& $pythonPath "$XTTSPath\xtts_service.py" --mode http --host 127.0.0.1 --port 8081
