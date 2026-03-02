<#
.SYNOPSIS
    Restore payload files from external payload root back into repo working tree.

.DESCRIPTION
    Reads .payload_pointer.json files and copies payloads back to their original
    repo locations. For devs who need files in-repo temporarily.

.WARNING
    Do NOT commit restored payloads. They exceed repo size limits.
#>

param()

$ErrorActionPreference = "Stop"
$RepoRoot = (Get-Item $PSScriptRoot).Parent.Parent.FullName

Write-Host ""
Write-Host "*** WARNING ***" -ForegroundColor Yellow
Write-Host "Do not commit restored payloads. They exceed repo size limits." -ForegroundColor Yellow
Write-Host ""

$pointers = Get-ChildItem -Path $RepoRoot -Recurse -Filter "*.payload_pointer.json" -ErrorAction SilentlyContinue
$restored = 0
foreach ($ptr in $pointers) {
    $json = Get-Content $ptr.FullName -Raw | ConvertFrom-Json
    $payloadPath = $json.payload_path
    $originalRel = $json.original_path -replace "/", "\"
    $originalFull = Join-Path $RepoRoot $originalRel

    if (-not (Test-Path $payloadPath -PathType Leaf)) {
        Write-Host "Payload missing, skipping: $originalRel"
        continue
    }

    $dir = Split-Path $originalFull -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    Copy-Item -Path $payloadPath -Destination $originalFull -Force
    Write-Host "Restored: $originalRel"
    $restored++
}

Write-Host ""
Write-Host "Restored $restored file(s). Do NOT commit them." -ForegroundColor Yellow
