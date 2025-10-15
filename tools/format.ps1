$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..")
$py = Join-Path $repo ".venv\Scripts\python.exe"
if (-not (Test-Path $py)) { $py = "python" }

& $py -m autoflake -r --in-place --remove-all-unused-imports --remove-unused-variables $repo
& $py -m isort $repo --profile black --force-single-line-imports
& $py -m ruff check $repo --fix
& $py -m black $repo --line-length 120

exit $LASTEXITCODE
