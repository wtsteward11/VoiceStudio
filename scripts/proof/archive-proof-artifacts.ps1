# Archive proof artifacts under .buildlogs\proof_runs for release packaging.

param(
  [string]$SourceDir,
  [string]$DestinationRoot = ".\.buildlogs\proof_runs"
)

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
  $proofRoot = Join-Path $repoRoot "proof_runs"
  if (-not (Test-Path $proofRoot)) {
    Write-Host "Error: proof_runs directory not found at $proofRoot" -ForegroundColor Red
    exit 1
  }

  $latest = Get-ChildItem -Path $proofRoot -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $latest) {
    Write-Host "Error: No proof runs found under $proofRoot" -ForegroundColor Red
    exit 1
  }

  $SourceDir = $latest.FullName
}

$sourcePath = Resolve-Path $SourceDir
if (-not (Test-Path $DestinationRoot)) {
  New-Item -ItemType Directory -Path $DestinationRoot | Out-Null
}
$destRootPath = Resolve-Path $DestinationRoot
$destDir = Join-Path $destRootPath (Split-Path $sourcePath -Leaf)

Copy-Item -Path $sourcePath -Destination $destDir -Recurse -Force
Write-Host "Archived proof artifacts to $destDir" -ForegroundColor Green
