$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..")
$py = Join-Path $repo ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { $py = "python" }

# quick format+lint (no tests to keep commits fast)
& $py -m ruff check $repo --fix; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $py -m black $repo --line-length 120; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $py -m flake8 $repo --config (Join-Path $repo ".flake8")
exit $LASTEXITCODE
