$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..")
$py = Join-Path $repo ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { $py = "python" }

& $py -m ruff check $repo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $py -m black $repo --line-length 120 --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $py -m isort $repo --check-only --profile black --force-single-line-imports
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $py -m flake8 $repo --config (Join-Path $repo ".flake8")
exit $LASTEXITCODE
