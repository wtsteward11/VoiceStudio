<#
.SYNOPSIS
    Collects VoiceStudio diagnostic artifacts into a timestamped zip for support.
.DESCRIPTION
    Gathers crash logs, dumps, verification results, and backend logs into a single
    support bundle zip file for troubleshooting.
.PARAMETER OutputDir
    Directory to write the zip file. Defaults to current directory.
#>
param(
    [string]$OutputDir = "."
)

$ErrorActionPreference = "Continue"
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$bundleDir = Join-Path $env:TEMP "voicestudio_support_$timestamp"
New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

Write-Host "Collecting VoiceStudio support bundle..." -ForegroundColor Cyan

$collected = 0

$crashDir = Join-Path $env:LOCALAPPDATA "VoiceStudio\crashes"
if (Test-Path $crashDir) {
    $dest = Join-Path $bundleDir "crashes"
    Copy-Item $crashDir $dest -Recurse -Force -ErrorAction SilentlyContinue
    $files = (Get-ChildItem $dest -Recurse -File -ErrorAction SilentlyContinue).Count
    Write-Host "  Crash logs: $files files"
    $collected += $files
}

$dumpDir = Join-Path $env:LOCALAPPDATA "VoiceStudio\dumps"
if (Test-Path $dumpDir) {
    $dest = Join-Path $bundleDir "dumps"
    Copy-Item $dumpDir $dest -Recurse -Force -ErrorAction SilentlyContinue
    $files = (Get-ChildItem $dest -Recurse -File -ErrorAction SilentlyContinue).Count
    Write-Host "  Dump files: $files files"
    $collected += $files
}

$repoRoot = git rev-parse --show-toplevel 2>$null
if (-not $repoRoot) { $repoRoot = Split-Path $PSScriptRoot }

$verifyJson = Join-Path $repoRoot ".buildlogs\verification\last_run.json"
if (Test-Path $verifyJson) {
    Copy-Item $verifyJson (Join-Path $bundleDir "last_verification.json") -Force
    Write-Host "  Verification report: copied"
    $collected++
}

$gatecLog = Join-Path $repoRoot ".buildlogs\gatec-latest.txt"
if (Test-Path $gatecLog) {
    Copy-Item $gatecLog (Join-Path $bundleDir "gatec-latest.txt") -Force
    Write-Host "  Gate C log: copied"
    $collected++
}

$bootJson = Join-Path $env:LOCALAPPDATA "VoiceStudio\crashes\boot_latest.json"
if (Test-Path $bootJson) {
    Copy-Item $bootJson (Join-Path $bundleDir "boot_latest.json") -Force
    Write-Host "  Boot marker: copied"
    $collected++
}

# Engine manifest snapshot (Item 30)
$enginesRoot = Join-Path $repoRoot "engines"
if (Test-Path $enginesRoot) {
    $manifestList = Get-ChildItem $enginesRoot -Recurse -Filter "engine.manifest.json" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
    $manifestList | Out-File (Join-Path $bundleDir "engine_manifests_list.txt") -Encoding utf8
    Write-Host "  Engine manifests: $($manifestList.Count) listed"
    $collected++
}

# Build version (Item 30)
$versionFile = Join-Path $repoRoot "src\VoiceStudio.App\Package.appxmanifest.xml"
if (-not (Test-Path $versionFile)) { $versionFile = Join-Path $repoRoot "Directory.Build.props" }
if (Test-Path $versionFile) {
    Get-Content $versionFile -Raw | Out-File (Join-Path $bundleDir "build_version_source.txt") -Encoding utf8 -ErrorAction SilentlyContinue
}

$info = @{
    timestamp = $timestamp
    hostname = $env:COMPUTERNAME
    os_version = [System.Environment]::OSVersion.ToString()
    dotnet_sdk = (dotnet --version 2>$null)
    python_version = (python --version 2>$null)
    collected_files = $collected
}
$info | ConvertTo-Json | Out-File (Join-Path $bundleDir "system_info.json") -Encoding utf8

$zipPath = Join-Path $OutputDir "voicestudio-support-$timestamp.zip"
Compress-Archive -Path "$bundleDir\*" -DestinationPath $zipPath -Force

Remove-Item $bundleDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Support bundle created: $zipPath" -ForegroundColor Green
Write-Host "  Files collected: $collected"
Write-Host "  Send this file to support for troubleshooting."
