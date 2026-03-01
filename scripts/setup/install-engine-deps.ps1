Param(
  [string]$VenvDir = "venv",
  [string]$PythonExe = "python",
  [string]$TorchVersion = "2.2.2+cu121",
  [string]$TorchaudioVersion = "2.2.2+cu121",
  [string]$PytorchIndexUrl = "https://download.pytorch.org/whl/cu121",
  [ValidateSet("xtts", "full")]
  [string]$InstallProfile = "xtts",
  [switch]$Gpu
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== VoiceStudio engine dependency install ($InstallProfile) ===" -ForegroundColor Cyan

$repoRoot = Resolve-Path "$PSScriptRoot\.."
if ($repoRoot -is [System.Management.Automation.PathInfo]) {
  $repoRoot = $repoRoot.Path
}

$gpuVenvDir = "venv_xtts_gpu_sm120"
if ($Gpu) {
  $TorchVersion = "2.7.1+cu128"
  $TorchaudioVersion = "2.7.1+cu128"
  $PytorchIndexUrl = "https://download.pytorch.org/whl/cu128"
  if ($VenvDir -eq "venv") {
    $VenvDir = $gpuVenvDir
  }
}

$venvPath = Join-Path $repoRoot $VenvDir
$venvPython = Join-Path $venvPath "Scripts\python.exe"
$requirementsEngines = Join-Path $repoRoot "requirements_engines.txt"

Write-Host "RepoRoot: $repoRoot" -ForegroundColor DarkGray
Write-Host "VenvDir:   $VenvDir" -ForegroundColor DarkGray
Write-Host "Pytorch:   $PytorchIndexUrl" -ForegroundColor DarkGray
Write-Host "Profile:   $InstallProfile" -ForegroundColor DarkGray

if (-not (Test-Path $requirementsEngines)) {
  throw "requirements_engines.txt not found: $requirementsEngines"
}

if (-not (Test-Path $venvPython)) {
  Write-Host "Creating venv: $venvPath" -ForegroundColor Yellow
  & $PythonExe -m venv $venvPath
  if ($LASTEXITCODE -ne 0) {
    throw "python -m venv failed (ExitCode=$LASTEXITCODE)"
  }
}

Write-Host "Updating pip tooling..." -ForegroundColor Yellow
& $venvPython -m pip install --upgrade pip wheel setuptools
if ($LASTEXITCODE -ne 0) {
  throw "pip tooling update failed (ExitCode=$LASTEXITCODE)"
}

Write-Host "Checking existing PyTorch install..." -ForegroundColor Yellow
$needTorchInstall = $true
try {
  $installedTorch = (& $venvPython -c "import torch; print(torch.__version__)" 2>$null)
  $installedTorchaudio = (& $venvPython -c "import torchaudio; print(torchaudio.__version__)" 2>$null)

  if (($installedTorch -is [string]) -and ($installedTorchaudio -is [string])) {
    $installedTorch = $installedTorch.Trim()
    $installedTorchaudio = $installedTorchaudio.Trim()
    if (($installedTorch -eq $TorchVersion) -and ($installedTorchaudio -eq $TorchaudioVersion)) {
      $needTorchInstall = $false
      Write-Host "PyTorch already installed: torch=$installedTorch, torchaudio=$installedTorchaudio" -ForegroundColor Green
    }
  }
}
catch {
  $needTorchInstall = $true
}

if ($needTorchInstall) {
  Write-Host "Installing PyTorch ($TorchVersion) + torchaudio ($TorchaudioVersion)..." -ForegroundColor Yellow
  $uninstallCmd = "`"$venvPython`" -m pip uninstall -y torch torchaudio torchvision 1>nul 2>nul"
  cmd /c $uninstallCmd | Out-Null
  $global:LASTEXITCODE = 0
  & $venvPython -m pip install "torch==$TorchVersion" "torchaudio==$TorchaudioVersion" --index-url $PytorchIndexUrl
  if ($LASTEXITCODE -ne 0) {
    throw "PyTorch install failed (ExitCode=$LASTEXITCODE)"
  }
}

if ($InstallProfile -eq "full") {
  Write-Host "Installing engine requirements (full: requirements_engines.txt)..." -ForegroundColor Yellow
  
  # aeneas has a known build-time dependency on numpy, but does not declare it for build isolation.
  # Install everything else first, then install aeneas last with no-build-isolation so it can see numpy.
  $tempReq = Join-Path $env:TEMP ("requirements_engines.noaeneas." + [Guid]::NewGuid().ToString("N") + ".txt")
  try {
    Get-Content -Path $requirementsEngines |
    Where-Object { $_ -notmatch '^\s*aeneas\s*([=<>~!].*)?\s*(#.*)?$' } |
    Set-Content -Path $tempReq -Encoding UTF8

    & $venvPython -m pip install --index-url https://pypi.org/simple --extra-index-url $PytorchIndexUrl -r $tempReq
    if ($LASTEXITCODE -ne 0) {
      throw "requirements_engines install failed (ExitCode=$LASTEXITCODE)"
    }

    # Ensure numpy is installed in the venv (required for aeneas build).
    & $venvPython -m pip install "numpy==1.26.4"
    if ($LASTEXITCODE -ne 0) {
      throw "numpy preinstall failed (ExitCode=$LASTEXITCODE)"
    }

    # Install aeneas last, without build isolation so it can see numpy.
    & $venvPython -m pip install --no-build-isolation "aeneas>=1.7.3"
    if ($LASTEXITCODE -ne 0) {
      throw "aeneas install failed (ExitCode=$LASTEXITCODE)"
    }
  }
  finally {
    if (Test-Path $tempReq) {
      Remove-Item -Force $tempReq -ErrorAction SilentlyContinue
    }
  }
}
else {
  # Minimal dependency set to unblock XTTS voice cloning + baseline proof runs.
  # Full engine stacks can include optional engines with heavy/native deps (e.g. aeneas), which should not block XTTS.
  $xttsPkgs = @(
    "numpy==1.26.4",
    "scipy>=1.9.0",
    "transformers==4.55.4",
    "huggingface_hub==0.36.0",
    "tokenizers==0.21.4",
    "safetensors==0.6.2",
    "fsspec==2025.9.0",
    "librosa==0.11.0",
    "soundfile==0.12.1",
    # Quality + metrics deps (voice cloning)
    "pyloudnorm==0.1.1",
    "noisereduce==3.0.2",
    "pesq>=0.0.4",
    "pystoi>=0.3.3",
    "resemblyzer>=0.1.1",
    "speechbrain>=0.5.0",
    "coqui-tts==0.27.2",
    "coqui-tts-trainer==0.3.1"
  )

  Write-Host "Installing XTTS voice engine requirements (minimal set)..." -ForegroundColor Yellow
  & $venvPython -m pip install --index-url https://pypi.org/simple --extra-index-url $PytorchIndexUrl @xttsPkgs
  if ($LASTEXITCODE -ne 0) {
    throw "XTTS requirements install failed (ExitCode=$LASTEXITCODE)"
  }
}

Write-Host "Verifying XTTS imports..." -ForegroundColor Yellow
& $venvPython -c "import torch; from TTS.api import TTS; print('torch:', torch.__version__); print('coqui-tts: OK')"
if ($LASTEXITCODE -ne 0) {
  throw "XTTS import verification failed (ExitCode=$LASTEXITCODE)"
}

# Backend runtime deps (needed for start_backend.ps1 / baseline proof)
$backendRequirements = Join-Path $repoRoot "backend\\requirements.txt"
if (Test-Path $backendRequirements) {
  Write-Host "Verifying backend runtime deps (FastAPI/uvicorn)..." -ForegroundColor Yellow
  $backendOk = $false
  try {
    & $venvPython -c "import fastapi, uvicorn; print('fastapi:', fastapi.__version__); print('uvicorn:', uvicorn.__version__)" 2>$null
    $backendOk = ($LASTEXITCODE -eq 0)
  } catch {
    $backendOk = $false
  }
  if (-not $backendOk) {
    Write-Host "Installing backend requirements (backend/requirements.txt)..." -ForegroundColor Yellow
    & $venvPython -m pip install --index-url https://pypi.org/simple --extra-index-url $PytorchIndexUrl -r $backendRequirements
    if ($LASTEXITCODE -ne 0) {
      throw "backend requirements install failed (ExitCode=$LASTEXITCODE)"
    }
  }
}
else {
  Write-Host "WARNING: backend requirements file not found: $backendRequirements" -ForegroundColor Yellow
}

Write-Host "Installed package versions:" -ForegroundColor Cyan
& $venvPython -c "import sys; from importlib import metadata as m; pkgs=['torch','torchaudio','transformers','tokenizers','huggingface-hub','coqui-tts','numpy','librosa','soundfile','fastapi','uvicorn']; print('python:', sys.version.split()[0]); [print(f'{p}: {m.version(p)}') if (m.version(p)) else print(f'{p}: <missing>') for p in pkgs]"
if ($LASTEXITCODE -ne 0) {
  throw "package version report failed (ExitCode=$LASTEXITCODE)"
}

Write-Host "Engine dependency install complete." -ForegroundColor Green
Write-Host "Activate: .\$VenvDir\Scripts\Activate.ps1" -ForegroundColor Cyan
