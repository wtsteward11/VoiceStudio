<#
.SYNOPSIS
    Verifies that protected build configuration files match the committed lockfile.

.DESCRIPTION
    Reads buildconfig.lock.json and compares SHA256 hashes of each listed file
    against its current state on disk. Any mismatch indicates an unauthorized or
    accidental modification.

.PARAMETER LockfilePath
    Path to the lockfile. Defaults to buildconfig.lock.json in repo root.

.EXAMPLE
    .\Verify-BuildConfigLock.ps1
#>
param(
    [string]$LockfilePath = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path | Split-Path }

if (-not $LockfilePath) {
    $LockfilePath = Join-Path $repoRoot "buildconfig.lock.json"
}

if (-not (Test-Path $LockfilePath)) {
    Write-Host "[SKIP] No lockfile found at: $LockfilePath" -ForegroundColor Yellow
    Write-Host "       Run Update-BuildConfigLock.ps1 to create one." -ForegroundColor Yellow
    exit 0
}

$lockData = Get-Content $LockfilePath -Raw | ConvertFrom-Json
$files = $lockData.files
$mismatches = @()
$missing = @()

foreach ($prop in $files.PSObject.Properties) {
    $relPath = $prop.Name
    $expectedHash = $prop.Value
    $fullPath = Join-Path $repoRoot $relPath

    if (-not (Test-Path $fullPath)) {
        $missing += $relPath
        continue
    }

    $actualHash = (Get-FileHash $fullPath -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) {
        $mismatches += [PSCustomObject]@{
            File     = $relPath
            Expected = $expectedHash.Substring(0, 16) + "..."
            Actual   = $actualHash.Substring(0, 16) + "..."
        }
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "MISSING FILES:" -ForegroundColor Red
    foreach ($f in $missing) {
        Write-Host "  - $f" -ForegroundColor Red
    }
}

if ($mismatches.Count -gt 0) {
    Write-Host ""
    Write-Host "BUILD CONFIG LOCK VERIFICATION FAILED" -ForegroundColor Red
    Write-Host "======================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "The following protected files have been modified since the lockfile was generated:" -ForegroundColor Red
    Write-Host ""
    foreach ($m in $mismatches) {
        Write-Host "  - $($m.File)" -ForegroundColor Red
        Write-Host "    Expected: $($m.Expected)" -ForegroundColor DarkGray
        Write-Host "    Actual:   $($m.Actual)" -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "If these changes are intentional, run:" -ForegroundColor Yellow
    Write-Host "  .\tools\build\Update-BuildConfigLock.ps1" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Lockfile generated: $($lockData.generated)" -ForegroundColor DarkGray
    exit 1
}

Write-Host "[PASS] All $($files.PSObject.Properties.Count) protected files match the lockfile." -ForegroundColor Green
Write-Host "       Lockfile: $LockfilePath" -ForegroundColor DarkGray
Write-Host "       Generated: $($lockData.generated)" -ForegroundColor DarkGray
exit 0
