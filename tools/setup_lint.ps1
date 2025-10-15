param([string]$Repo = "C:\VoiceStudio")
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if(-not (Test-Path $Repo)){ throw "Repo not found: $Repo" }
Set-Location $Repo
Write-Host "Using repo: $Repo" -ForegroundColor Green

function Backup($p){ if(Test-Path $p){ $t=Get-Date -Format "yyyyMMdd_HHmmss"; Copy-Item -LiteralPath $p -Destination "$p.bak.$t" -Force } }
function EnsureDir($d){ New-Item -ItemType Directory -Force -Path $d | Out-Null }
function GetPy(){
  foreach($c in @("python","py")){
    $cmd = Get-Command $c -ErrorAction SilentlyContinue
    if($cmd){ return $cmd.Source }
  }
  throw "Python not found. Install Python then rerun."
}

EnsureDir ".github/workflows"

# .flake8 (backup then write)
Backup ".flake8"
Set-Content ".flake8" @"
[flake8]
max-line-length = 120
extend-ignore = E203, W503
exclude =
    .git,
    __pycache__,
    .mypy_cache__,
    .pytest_cache__,
    build,
    dist,
    .eggs,
    *.egg,
    *.egg-info,
    venv,
    .venv,
    site-packages,
    **/site-packages/**
"@ -Encoding UTF8

# GitHub Actions workflow (backup then write)
Backup ".github/workflows/lint.yml"
Set-Content ".github/workflows/lint.yml" @"
name: Lint (flake8)
on:
  push:
    paths:
      - "**/*.py"
      - ".flake8"
      - ".github/workflows/lint.yml"
  pull_request:
    paths:
      - "**/*.py"
      - ".flake8"
jobs:
  flake8:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-python@v5
        with:
          python-version: "3.x"
          cache: "pip"
      - run: python -m pip install --upgrade pip flake8
      - run: python -m flake8 . --config .flake8
"@ -Encoding UTF8

# .gitignore (create if missing)
if(-not (Test-Path ".gitignore")){
Set-Content ".gitignore" @"
__pycache__/
*.pyc
*.pyo
*.pyd
*.egg-info/
.eggs/
build/
dist/
.venv/
venv/
.env
.mypy_cache/
.pytest_cache/
"@ -Encoding UTF8
}

# Log
$log = Join-Path $Repo "tools\lint_last.log"
try { Start-Transcript -Path $log -Append -ErrorAction SilentlyContinue | Out-Null } catch {}

# Install tools
$py = GetPy
& $py -m pip install -U pip
& $py -m pip install flake8 ruff black isort autoflake

# Auto-fix pass (addresses your specific errors: E401/E231/E225/E701/E702/E302/E305/E501/F401/W292/W391)
try{
  & $py -m autoflake -r --in-place --remove-all-unused-imports --remove-unused-variables .
  & $py -m isort . --profile black --force-single-line-imports
  & $py -m ruff check . --fix
  & $py -m black . --line-length 120
} catch {
  Write-Host "Auto-fix step had errors (continuing): $($_.Exception.Message)" -ForegroundColor Yellow
}

# Lint
Write-Host "`nRunning flake8..." -ForegroundColor Cyan
& $py -m flake8 . --config .flake8
$code = $LASTEXITCODE
try { Stop-Transcript | Out-Null } catch {}

if($code -eq 0){
  Write-Host "✅ flake8: clean" -ForegroundColor Green
}else{
  Write-Host "⚠ flake8 found issues (see console and $log)" -ForegroundColor Yellow
}
exit $code
