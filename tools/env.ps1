param([string]$RepoRoot)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) { $RepoRoot = (Split-Path $PSScriptRoot -Parent) }
$repo = (Resolve-Path $RepoRoot).Path

# pick a Python
function Get-Python {
  foreach($c in @("$repo\.venv\Scripts\python.exe","python","py")){
    if(Test-Path $c){ return $c }
    try { if(Get-Command $c -ErrorAction Stop){ return $c } } catch {}
  }
  throw "Python not found."
}

# ensure venv
$basePy = Get-Python
$venv = Join-Path $repo ".venv"
$venvPy = Join-Path $venv "Scripts\python.exe"
if (!(Test-Path $venvPy)) {
  & $basePy -m venv $venv
}
$py = $venvPy

# upgrade pip + dev deps
& $py -m pip install -U pip | Out-Null
& $py -m pip install -U flake8 ruff black isort autoflake pytest | Out-Null

# app deps if requirements.txt exists
$req = Join-Path $repo "requirements.txt"
if (Test-Path $req) {
  & $py -m pip install -r $req
}

Write-Host "venv ready: $venv" -ForegroundColor Green
