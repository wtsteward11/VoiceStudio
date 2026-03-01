<#
.SYNOPSIS
    Pre-XAML dependency gate: scans project.assets.json for banned .NET 9.0+ packages
    that crash the net472 XAML compiler.

.DESCRIPTION
    The WinUI 3 XamlCompiler.exe is a .NET Framework 4.7.2 process. When it loads
    reference assemblies built with .NET 9.0+ metadata, it silently crashes (exit code 1,
    no output). This script scans the resolved dependency graph and fails if any
    Microsoft.Extensions.* package at version 9.0+ is detected.

.PARAMETER ProjectDir
    Path to the project directory containing project.assets.json in obj/.
    Defaults to src/VoiceStudio.App.

.PARAMETER BannedPrefixes
    Package name prefixes to check. Defaults to Microsoft.Extensions.

.PARAMETER MaxMajorVersion
    Maximum allowed major version. Packages at or above this version are blocked.
    Defaults to 9.

.EXAMPLE
    .\Verify-ResolvedPackages.ps1
    .\Verify-ResolvedPackages.ps1 -ProjectDir "src/VoiceStudio.App" -MaxMajorVersion 9
#>
param(
    [string]$ProjectDir = "",
    [string[]]$BannedPrefixes = @("Microsoft.Extensions."),
    [int]$MaxMajorVersion = 9,
    [string[]]$AllowedExceptions = @(
        "Microsoft.Extensions.DependencyInjection/10.0.2",
        "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.2",
        "Microsoft.Extensions.Logging.Abstractions/9.0.0"
    )
)

$ErrorActionPreference = "Stop"

if (-not $ProjectDir) {
    $repoRoot = (git rev-parse --show-toplevel 2>$null)
    if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path | Split-Path }
    $ProjectDir = Join-Path $repoRoot "src\VoiceStudio.App"
}

$objDirs = Get-ChildItem -Path $ProjectDir -Directory -Recurse -Filter "obj" | Select-Object -First 1
if (-not $objDirs) {
    Write-Host "[SKIP] No obj/ directory found. Run 'dotnet restore' first." -ForegroundColor Yellow
    exit 0
}

$assetsFiles = Get-ChildItem -Path $objDirs.FullName -Recurse -Filter "project.assets.json"
if (-not $assetsFiles -or $assetsFiles.Count -eq 0) {
    Write-Host "[SKIP] No project.assets.json found. Run 'dotnet restore' first." -ForegroundColor Yellow
    exit 0
}

$assetsPath = $assetsFiles[0].FullName
Write-Host "Scanning: $assetsPath" -ForegroundColor Cyan

$assets = Get-Content $assetsPath -Raw | ConvertFrom-Json
$violations = @()

foreach ($target in $assets.targets.PSObject.Properties) {
    foreach ($pkg in $target.Value.PSObject.Properties) {
        $pkgName = $pkg.Name.Split("/")[0]
        $pkgVersion = $pkg.Name.Split("/")[1]

        foreach ($prefix in $BannedPrefixes) {
            if ($pkgName.StartsWith($prefix)) {
                $major = 0
                if ($pkgVersion -match "^(\d+)\.") {
                    $major = [int]$Matches[1]
                }
                if ($major -ge $MaxMajorVersion) {
                    $pkgKey = "$pkgName/$pkgVersion"
                    if ($AllowedExceptions -contains $pkgKey) { continue }
                    $violations += [PSCustomObject]@{
                        Package = $pkgName
                        Version = $pkgVersion
                        Target  = $target.Name
                    }
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "DEPENDENCY GATE FAILED" -ForegroundColor Red
    Write-Host "======================" -ForegroundColor Red
    Write-Host ""
    Write-Host "The following packages are at version $MaxMajorVersion.0+ and will crash the net472 XAML compiler:" -ForegroundColor Red
    Write-Host ""
    $violations | Sort-Object Package -Unique | ForEach-Object {
        Write-Host "  - $($_.Package) v$($_.Version)" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "FIX: Downgrade these packages to 8.x or earlier in the .csproj files." -ForegroundColor Yellow
    Write-Host "REF: docs/reports/BUILD_ROOT_CAUSE_ANALYSIS_20260228.md" -ForegroundColor Yellow
    exit 1
}

Write-Host "[PASS] No banned .NET ${MaxMajorVersion}.0+ Microsoft.Extensions packages found." -ForegroundColor Green
exit 0
