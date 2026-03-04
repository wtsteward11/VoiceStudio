# Verify-NoHeavyDirsInRepo.ps1
# Tripwire: fail if heavy payload dirs reappear under repo root.
# Prevents "Cursor indexed 80k files and died" regression.
#
# Usage: .\tools\build\Verify-NoHeavyDirsInRepo.ps1
# Exit 1 if any monitored dir has too many or too large files.

param(
    [int]$MaxFilesPerDir = 500,
    [long]$MaxDirSizeBytes = 100MB
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName

$HeavyDirs = @(
    (Join-Path $RepoRoot "installer\runtime"),
    (Join-Path $RepoRoot "models"),
    (Join-Path $RepoRoot ".voicestudio")
)

$failed = $false
foreach ($dir in $HeavyDirs) {
    if (-not (Test-Path $dir)) { continue }
    $items = Get-ChildItem -Path $dir -Recurse -File -ErrorAction SilentlyContinue
    $count = ($items | Measure-Object).Count
    $size = ($items | Measure-Object -Property Length -Sum).Sum
    if ($count -gt $MaxFilesPerDir -or $size -gt $MaxDirSizeBytes) {
        Write-Host "FAIL: $dir has $count files ($([math]::Round($size/1MB,1)) MB) - exceeds limits" -ForegroundColor Red
        $failed = $true
    }
}

if ($failed) {
    Write-Host "Heavy dirs detected. Add to .cursorignore / .cursorindexingignore or move outside repo." -ForegroundColor Yellow
    exit 1
}
Write-Host "OK: No heavy dirs exceed limits" -ForegroundColor Green
exit 0
