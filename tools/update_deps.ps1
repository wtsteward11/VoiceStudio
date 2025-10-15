$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$py = Join-Path $root ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { throw "No venv found at .venv. Create it first (run tools\run_all.ps1 once)." }

& $py -m pip install -U pip
if (Test-Path (Join-Path $root "requirements-dev.txt")) {
  & $py -m pip install -r (Join-Path $root "requirements-dev.txt")
} else {
  & $py -m pip install -U flake8 ruff black isort autoflake pytest
}
Write-Host "Dev tools updated." -ForegroundColor Green

Write-Host "Versions:" -ForegroundColor Cyan
& $py - << 'PY'
import pkgutil, importlib
for m in ["pip","flake8","ruff","black","isort","autoflake","pytest"]:
    try:
        mod = importlib.import_module(m)
        ver = getattr(mod, "__version__", "unknown")
    except Exception:
        ver = "not installed"
    print(f"{m}: {ver}")
PY
