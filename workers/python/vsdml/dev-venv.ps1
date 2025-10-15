$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
if (!(Test-Path ".venv")) { python -m venv .venv }
. .\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
pip install -r requirements.txt
pip freeze --require-virtualenv > requirements.lock
Write-Host "venv ready. Use: . .\.venv\Scripts\Activate.ps1"
Pop-Location
