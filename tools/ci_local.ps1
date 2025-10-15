$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..")
$py = Join-Path $repo ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { $py = "python" }

Write-Host "Ruff..." -ForegroundColor Cyan
& $py -m ruff check $repo; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Black (check)..." -ForegroundColor Cyan
& $py -m black $repo --line-length 120 --check; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "isort (check)..." -ForegroundColor Cyan
& $py -m isort $repo --check-only --profile black --force-single-line-imports; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "flake8..." -ForegroundColor Cyan
& $py -m flake8 $repo --config (Join-Path $repo ".flake8"); if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "pytest..." -ForegroundColor Cyan
& $py -m pytest -q; exit $LASTEXITCODE
