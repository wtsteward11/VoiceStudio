<#
.SYNOPSIS
    Branding consistency checker for VoiceStudio.
.DESCRIPTION
    Scans src/ and docs/ (excluding docs/archive/) for "Quantum+" variants.
    Returns exit code 1 if found. Used by verify.ps1 to prevent branding regression.
.EXAMPLE
    .\scripts\check-branding.ps1
#>
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

$patterns = @("Quantum\+", "Quantum-Plus", "QuantumPlus")
$excludeDirs = @(
  "docs\archive",
  "node_modules",
  "obj",
  "bin",
  ".buildlogs",
  "runtime\external"
)
$excludeFiles = @(
  "ruleguard-report.txt",
  "placeholder_verification_report.txt"
)

$found = @()
$searchPaths = @(
  (Join-Path $rootDir "src"),
  (Join-Path $rootDir "docs")
)

foreach ($searchPath in $searchPaths) {
  if (-not (Test-Path $searchPath)) { continue }
  $files = Get-ChildItem -Path $searchPath -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object {
      $path = $_.FullName
      $excluded = $false
      foreach ($ex in $excludeDirs) {
        if ($path -match [regex]::Escape($ex)) { $excluded = $true; break }
      }
      foreach ($ef in $excludeFiles) {
        if ($_.Name -eq $ef) { $excluded = $true; break }
      }
      -not $excluded
    }
  foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    foreach ($pat in $patterns) {
      if ($content -match $pat) {
        $found += $f.FullName
        break
      }
    }
  }
}

$found = $found | Sort-Object -Unique
if ($found.Count -gt 0) {
  Write-Host "Branding check FAILED: Found Quantum+ variants in:" -ForegroundColor Red
  $found | ForEach-Object { Write-Host "  $_" }
  exit 1
}
Write-Host "Branding check PASSED: No Quantum+ variants in src/ or docs/ (excl. archive)" -ForegroundColor Green
exit 0
