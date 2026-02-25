<#
.SYNOPSIS
    Pre-commit hook that validates staged XAML files compile correctly.
.DESCRIPTION
    Runs a scoped diagnostic build on any staged .xaml files to catch
    XAML compiler crashes before they reach the main branch.
.EXAMPLE
    .\scripts\validate-xaml-change.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

$stagedXaml = git diff --cached --name-only --diff-filter=ACM -- "*.xaml" 2>$null
if (-not $stagedXaml) {
    Write-Host "[xaml-validate] No staged XAML files. Skipping."
    exit 0
}

$count = ($stagedXaml | Measure-Object -Line).Lines
Write-Host "[xaml-validate] Validating $count staged XAML file(s)..."

foreach ($file in $stagedXaml -split "`n") {
    $file = $file.Trim()
    if (-not $file) { continue }
    Write-Host "  Checking: $file"
}

Write-Host "[xaml-validate] Running incremental build to verify XAML compilation..."
$buildOutput = & dotnet build "$RootDir\VoiceStudio.sln" -c Debug -p:Platform=x64 --no-restore --verbosity quiet 2>&1
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    Write-Host "[xaml-validate] FAIL: Build failed after XAML changes." -ForegroundColor Red
    $buildOutput | Select-String "error " | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Fix the XAML errors above before committing." -ForegroundColor Yellow
    exit 1
}

Write-Host "[xaml-validate] PASS: All staged XAML files compile successfully." -ForegroundColor Green
exit 0
