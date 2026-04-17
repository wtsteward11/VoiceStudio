# VoiceStudio Backend Server Startup Script
# Handles port conflicts and starts the backend API

param(
    [int]$Port = 8000,
    [string]$VenvDir = ".venv",
    [switch]$CoquiTosAgreed,
    [switch]$Gpu
)

Write-Host "=== VoiceStudio Backend Server Startup ===" -ForegroundColor Cyan

# Check if port is available
$portInUse = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($portInUse) {
    Write-Host "Port $Port is already in use!" -ForegroundColor Yellow
    Write-Host "Process ID: $($portInUse.OwningProcess)" -ForegroundColor Yellow
    
    # Try alternative ports
    $alternativePorts = @(8000, 8001, 8002, 8080, 8888)
    $availablePort = $null
    
    foreach ($altPort in $alternativePorts) {
        $check = Get-NetTCPConnection -LocalPort $altPort -ErrorAction SilentlyContinue
        if (-not $check) {
            $availablePort = $altPort
            break
        }
    }
    
    if ($availablePort) {
        Write-Host "Using alternative port: $availablePort" -ForegroundColor Green
        $Port = $availablePort
    }
    else {
        Write-Host "No available ports found. Please free up a port or stop the process using port $Port" -ForegroundColor Red
        Write-Host "To stop the process: Stop-Process -Id $($portInUse.OwningProcess) -Force" -ForegroundColor Yellow
        exit 1
    }
}

Write-Host "Starting backend server on port $Port..." -ForegroundColor Green
Write-Host "Backend URL: http://localhost:$Port" -ForegroundColor Cyan
Write-Host "API Docs: http://localhost:$Port/docs" -ForegroundColor Cyan
Write-Host ""

# Project root (repo root): scripts/backend -> .. -> VoiceStudio
$projectRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName

# Set PYTHONPATH to project root so `backend` and `app` packages resolve
$env:PYTHONPATH = $projectRoot

# Force Hugging Face router endpoint to avoid deprecated inference API
$env:HF_INFERENCE_API_BASE = "https://router.huggingface.co"
$env:HF_ENDPOINT = "https://router.huggingface.co"
$env:HF_HUB_ENDPOINT = "https://router.huggingface.co"

# Ensure HF tokens flow into this process (if set at user scope)
if (-not $env:HF_TOKEN) {
    $userHfToken = [Environment]::GetEnvironmentVariable("HF_TOKEN", "User")
    if ($userHfToken) {
        $env:HF_TOKEN = $userHfToken
    }
}
if (-not $env:HUGGINGFACE_HUB_TOKEN) {
    $userHfHubToken = [Environment]::GetEnvironmentVariable("HUGGINGFACE_HUB_TOKEN", "User")
    if ($userHfHubToken) {
        $env:HUGGINGFACE_HUB_TOKEN = $userHfHubToken
    }
}
if (-not $env:HUGGINGFACEHUB_API_TOKEN -and $env:HF_TOKEN) {
    $env:HUGGINGFACEHUB_API_TOKEN = $env:HF_TOKEN
}

# Ensure model/cache roots default to E:\VoiceStudio\models (avoid C:\ cache spill)
if (-not $env:VOICESTUDIO_MODELS_PATH) {
    $env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
}
if (-not $env:HF_HOME) {
    $env:HF_HOME = Join-Path $env:VOICESTUDIO_MODELS_PATH "hf_cache"
}
if (-not $env:HUGGINGFACE_HUB_CACHE) {
    $env:HUGGINGFACE_HUB_CACHE = Join-Path $env:HF_HOME "hub"
}
if (-not $env:TRANSFORMERS_CACHE) {
    $env:TRANSFORMERS_CACHE = Join-Path $env:HF_HOME "transformers"
}
if (-not $env:HF_DATASETS_CACHE) {
    $env:HF_DATASETS_CACHE = Join-Path $env:HF_HOME "datasets"
}
if (-not $env:TTS_HOME) {
    $env:TTS_HOME = Join-Path $env:VOICESTUDIO_MODELS_PATH "xtts"
}
if (-not $env:TORCH_HOME) {
    $env:TORCH_HOME = Join-Path $env:VOICESTUDIO_MODELS_PATH "torch"
}

# XTTS v2 (Coqui) may require CPML terms acceptance on first model download. For non-interactive runs:
# - set COQUI_TOS_AGREED=1 (indicates you agree to CPML or have a commercial license), or
# - pass -CoquiTosAgreed to set it for this process.
if ($CoquiTosAgreed) {
    $env:COQUI_TOS_AGREED = "1"
    Write-Host "COQUI_TOS_AGREED=1 set for this backend process (XTTS v2 model downloads will not prompt interactively)." -ForegroundColor Yellow
}
elseif (-not $env:COQUI_TOS_AGREED) {
    Write-Host "NOTE: XTTS v2 model downloads may prompt for CPML acceptance. If you have accepted CPML or have a commercial license, re-run with -CoquiTosAgreed (or set COQUI_TOS_AGREED=1)." -ForegroundColor Yellow
}

# Optional GPU venv path (sm_120 for RTX 4000/5000 series)
if ($Gpu -and $VenvDir -eq "venv") {
    $VenvDir = "venv_gpu_sm120"
    Write-Host "GPU venv selected: $VenvDir (RTX 4000/5000 series support)" -ForegroundColor Yellow
    Write-Host "  Expected latency improvement: ~42s CPU -> ~5s GPU" -ForegroundColor Green
}

# Check for venv in multiple locations (project root first, then .venv, then script dir)
$venvPython = $null
$venvCandidates = @(
    (Join-Path $projectRoot "$VenvDir\Scripts\python.exe"),
    (Join-Path $projectRoot ".venv\Scripts\python.exe"),
    (Join-Path $projectRoot ".$VenvDir\Scripts\python.exe"),
    (Join-Path $PSScriptRoot "$VenvDir\Scripts\python.exe")
)

foreach ($candidate in $venvCandidates) {
    if (Test-Path $candidate) {
        $venvPython = $candidate
        break
    }
}

# Fallback to default .venv if GPU venv not found
if (-not $venvPython -and $Gpu) {
    $fallbackVenv = Join-Path $projectRoot ".venv\Scripts\python.exe"
    if (Test-Path $fallbackVenv) {
        Write-Host "WARNING: GPU venv '$VenvDir' not found. Using .venv (CPU mode)." -ForegroundColor Yellow
        Write-Host "  To set up GPU venv, run: .\scripts\setup_gpu_venv.ps1" -ForegroundColor Yellow
        $venvPython = $fallbackVenv
    }
}

if (-not $venvPython) {
    # Final fallback to first candidate path for error message
    $venvPython = $venvCandidates[0]
}

# Start uvicorn from project root using module path
Set-Location $projectRoot
if (Test-Path $venvPython) {
    # Ensure backend runtime deps exist in the same venv (uvicorn/fastapi).
    # Note: FastAPI file-upload/form endpoints require python-multipart (imported as `multipart`).
    & $venvPython -c "import uvicorn, fastapi, multipart" 2>$null
    if ($LASTEXITCODE -ne 0) {
        $backendReq = Join-Path $projectRoot "backend\\requirements.txt"
        if (Test-Path $backendReq) {
            Write-Host "uvicorn/fastapi not found in venv. Installing backend requirements..." -ForegroundColor Yellow
            & $venvPython -m pip install --index-url https://pypi.org/simple -r $backendReq
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Failed to install backend requirements (ExitCode=$LASTEXITCODE): $backendReq" -ForegroundColor Red
                exit 1
            }

            # Re-check after install (fail fast with a clear message)
            & $venvPython -c "import uvicorn, fastapi, multipart; print('uvicorn:', uvicorn.__version__); print('fastapi:', fastapi.__version__); print('multipart:', getattr(multipart, '__version__', 'unknown'))" 2>$null
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Backend requirements installed but uvicorn/fastapi/python-multipart are still not importable. Try: .\\venv\\Scripts\\python.exe -m pip install -r .\\backend\\requirements.txt" -ForegroundColor Red
                exit 1
            }
        }
        else {
            Write-Host "ERROR: backend requirements file not found: $backendReq" -ForegroundColor Red
            exit 1
        }
    }

    & $venvPython -m uvicorn backend.api.main:app --reload --port $Port --host 0.0.0.0
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
else {
    Write-Host "WARNING: venv python not found at: $venvPython" -ForegroundColor Yellow
    Write-Host "Falling back to system uvicorn. This may miss engine deps (coqui-tts)." -ForegroundColor Yellow
    uvicorn backend.api.main:app --reload --port $Port --host 0.0.0.0
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}


