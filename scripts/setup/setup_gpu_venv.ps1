<#
.SYNOPSIS
    Setup GPU-optimized virtual environment for VoiceStudio

.DESCRIPTION
    Creates a virtual environment with PyTorch 2.6+ for RTX 4000/5000 series GPUs
    that require SM 120 compute capability support.

.PARAMETER VenvName
    Name of the virtual environment (default: venv_gpu_sm120)

.EXAMPLE
    .\scripts\setup_gpu_venv.ps1
    .\scripts\setup_gpu_venv.ps1 -VenvName my_gpu_venv
#>

param(
    [string]$VenvName = "venv_gpu_sm120"
)

$ErrorActionPreference = "Stop"
$VenvPath = Join-Path $PSScriptRoot "..\$VenvName"

Write-Host "=== VoiceStudio GPU Venv Setup ===" -ForegroundColor Cyan
Write-Host "Target: $VenvPath"

# Check CUDA availability
$nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if ($nvidiaSmi) {
    Write-Host "GPU detected:" -ForegroundColor Green
    nvidia-smi --query-gpu=name,driver_version,compute_cap --format=csv,noheader
} else {
    Write-Host "WARNING: nvidia-smi not found. GPU may not be available." -ForegroundColor Yellow
}

# Create venv if it doesn't exist
if (-not (Test-Path $VenvPath)) {
    Write-Host "Creating virtual environment..." -ForegroundColor Yellow
    python -m venv $VenvPath
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create virtual environment"
    }
}

$pipExe = Join-Path $VenvPath "Scripts\pip.exe"
$pythonExe = Join-Path $VenvPath "Scripts\python.exe"

# Upgrade pip
Write-Host "Upgrading pip..." -ForegroundColor Yellow
& $pythonExe -m pip install --upgrade pip

# Install PyTorch 2.6+ with CUDA 12.4 (supports SM 120)
Write-Host "Installing PyTorch 2.6+ with CUDA 12.4 support..." -ForegroundColor Yellow
& $pipExe install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu124

if ($LASTEXITCODE -ne 0) {
    throw "Failed to install PyTorch"
}

# Verify PyTorch installation
Write-Host "Verifying PyTorch installation..." -ForegroundColor Yellow
& $pythonExe -c "import torch; print('PyTorch version:', torch.__version__); print('CUDA available:', torch.cuda.is_available()); print('Device:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'N/A')"

# Install core dependencies for TTS
Write-Host "Installing TTS dependencies..." -ForegroundColor Yellow
& $pipExe install coqui-tts==0.27.2 transformers>=4.55.4 numpy scipy librosa soundfile

# Install additional quality metrics dependencies
& $pipExe install resemblyzer speechbrain pesq pystoi

Write-Host ""
Write-Host "=== GPU Venv Setup Complete ===" -ForegroundColor Green
Write-Host "To use GPU lane, start backend with:"
Write-Host "  .\scripts\backend\start_backend.ps1 -Gpu -CoquiTosAgreed" -ForegroundColor Cyan
Write-Host ""
Write-Host "Or set VenvDir explicitly:"
Write-Host "  .\scripts\backend\start_backend.ps1 -VenvDir $VenvName -CoquiTosAgreed" -ForegroundColor Cyan
