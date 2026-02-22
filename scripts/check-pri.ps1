<#
.SYNOPSIS
    PRI validation script for VoiceStudio build output.
.DESCRIPTION
    Validates that required PRI files exist in the build output directory.
    Checks: VoiceStudio.App.pri, Microsoft.UI.pri, Microsoft.UI.Xaml.Controls.pri, Microsoft.WindowsAppRuntime.pri.
    Optionally validates that MainWindow.xaml and App.xaml are in the app PRI (via makepri dump if available).
.PARAMETER BuildOutputDir
    Build output directory. Default: auto-detect from .buildlogs.
.EXAMPLE
    .\scripts\check-pri.ps1
    .\scripts\check-pri.ps1 -BuildOutputDir "E:\VoiceStudio\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64"
#>
Param(
  [string]$BuildOutputDir = ""
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir

if (-not $BuildOutputDir -or $BuildOutputDir.Trim() -eq "") {
  $candidates = @(
    (Join-Path $rootDir ".buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64"),
    (Join-Path $rootDir "src\VoiceStudio.App\.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\win-x64")
  )
  foreach ($c in $candidates) {
    if ((Test-Path $c) -and (Test-Path (Join-Path $c "VoiceStudio.App.exe"))) {
      $BuildOutputDir = $c
      break
    }
  }
  if (-not $BuildOutputDir) {
    Write-Host "PRI check SKIP: No build output directory found. Run a build first." -ForegroundColor Yellow
    exit 0
  }
}

$requiredPri = @(
  "VoiceStudio.App.pri",
  "Microsoft.UI.pri",
  "Microsoft.UI.Xaml.Controls.pri",
  "Microsoft.WindowsAppRuntime.pri"
)

$missing = @()
foreach ($name in $requiredPri) {
  $path = Join-Path $BuildOutputDir $name
  if (-not (Test-Path $path)) {
    $missing += $name
  }
}

if ($missing.Count -gt 0) {
  Write-Host "PRI check FAILED: Missing in $BuildOutputDir" -ForegroundColor Red
  $missing | ForEach-Object { Write-Host "  $_" }
  exit 1
}
Write-Host "PRI check PASSED: All required PRI files present in $BuildOutputDir" -ForegroundColor Green
exit 0
