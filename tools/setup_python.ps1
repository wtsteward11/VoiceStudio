param()
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

function Get-Python {
  foreach($c in @('python','py')){ if(Get-Command $c -ErrorAction SilentlyContinue){ return $c } }
  throw "Python not found"
}
$py = Get-Python

# create venv if missing
$venvPy = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path $venvPy)) {
  & $py -m venv ".venv"
  $venvPy = Join-Path $root ".venv\Scripts\python.exe"
}

# upgrade pip + wheel/setuptools
& $venvPy -m pip install -U pip setuptools wheel

# install from root requirements (dev preferred), then worker requirements if present
$reqs = @()
if (Test-Path "requirements-dev.txt") { $reqs += "requirements-dev.txt" }
elseif (Test-Path "requirements.txt") { $reqs += "requirements.txt" }

$worker = "workers\python\vsdml"
if (Test-Path (Join-Path $worker "requirements.txt")) { $reqs += (Join-Path $worker "requirements.txt") }
if (Test-Path (Join-Path $worker "align_requirements.txt")) { $reqs += (Join-Path $worker "align_requirements.txt") }
if (Test-Path (Join-Path $worker "vad_requirements.txt")) { $reqs += (Join-Path $worker "vad_requirements.txt") }

foreach($r in $reqs){
  Write-Host "Installing from $r..." -ForegroundColor Cyan
  & $venvPy -m pip install -r $r
}

Write-Host "✅ Python env ready at $($root)\.venv" -ForegroundColor Green
