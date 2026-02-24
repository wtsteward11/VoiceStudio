<#
.SYNOPSIS
    Runs VoiceStudio performance benchmarks that require engines/models.
.DESCRIPTION
    These benchmarks are excluded from normal CI because they require
    GPU, engine models, and a running backend. Run them manually or
    in a nightly CI job.
.EXAMPLE
    .\scripts\run-benchmarks.ps1
    .\scripts\run-benchmarks.ps1 -Category inference
    .\scripts\run-benchmarks.ps1 -Category api -Verbose
#>
Param(
    [ValidateSet("all", "inference", "api", "engine", "memory", "load")]
    [string]$Category = "all",
    [int]$TimeoutSeconds = 300
)

$ErrorActionPreference = "Continue"
$Python = "E:\VoiceStudio\.venv\Scripts\python.exe"

Write-Host "=== VoiceStudio Performance Benchmarks ===" -ForegroundColor Cyan
Write-Host "Category: $Category"
Write-Host "Timeout: ${TimeoutSeconds}s"
Write-Host ""

$markers = @()
switch ($Category) {
    "inference" { $markers = @("-k", "inference") }
    "api"       { $markers = @("-k", "api_performance") }
    "engine"    { $markers = @("-k", "engine_performance") }
    "memory"    { $markers = @("-k", "memory") }
    "load"      { $markers = @("-k", "load") }
}

$args = @(
    "-m", "pytest",
    "tests/performance/",
    "--no-cov",
    "--timeout=$TimeoutSeconds",
    "-v",
    "--tb=short",
    "-p", "no:cacheprovider"
)

if ($markers.Count -gt 0) {
    $args += $markers
}

& $Python @args
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "Benchmarks PASSED" -ForegroundColor Green
} else {
    Write-Host "Benchmarks FAILED (exit code: $exitCode)" -ForegroundColor Red
}

exit $exitCode
