param([switch]$Force)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

$targets = @(
  ".flake8",
  "pyproject.toml",
  ".editorconfig",
  ".gitattributes",
  "requirements-dev.txt",
  ".github\workflows\ci.yml",
  "tools",
  ".git\hooks\pre-commit",
  ".venv"
) | ForEach-Object { Join-Path $root $_ }

Write-Host "This will remove dev setup files and the venv from $root" -ForegroundColor Yellow
if (-not $Force) {
  $ans = Read-Host "Type YES to continue"
  if ($ans -ne "YES") { Write-Host "Aborted."; exit 1 }
}

foreach($t in $targets){
  if (Test-Path $t) {
    Write-Host "Removing $t"
    Remove-Item -Recurse -Force $t
  }
}
Write-Host "Cleanup complete." -ForegroundColor Green
