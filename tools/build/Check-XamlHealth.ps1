<#
.SYNOPSIS
    Post-build XAML health check: verifies XamlTypeInfo.g.cs exists and is non-trivial.
.DESCRIPTION
    The WinUI 3 XAML compiler produces XamlTypeInfo.g.cs during build. If it is missing
    or suspiciously small (<1 KB), the XAML compiler silently failed. This script catches
    "fake green" builds where dotnet reports success but no XAML was actually compiled.
.EXAMPLE
    .\tools\build\Check-XamlHealth.ps1
#>
param(
    [string]$ProjectDir = "",
    [int]$MinSizeBytes = 1024
)

$ErrorActionPreference = "Stop"

if (-not $ProjectDir) {
    $repoRoot = (git rev-parse --show-toplevel 2>$null)
    if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path | Split-Path }
    $ProjectDir = Join-Path $repoRoot "src\VoiceStudio.App"
}

$ti = Get-ChildItem -Path $ProjectDir -Recurse -Filter "XamlTypeInfo.g.cs" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match "\\obj\\" } |
    Select-Object -First 1

if (-not $ti) {
    Write-Host "[FAIL] XamlTypeInfo.g.cs not found in obj/. XAML compiler did not run." -ForegroundColor Red
    exit 1
}

if ($ti.Length -lt $MinSizeBytes) {
    Write-Host "[FAIL] XamlTypeInfo.g.cs is only $($ti.Length) bytes (threshold: $MinSizeBytes). XAML compiler produced empty output." -ForegroundColor Red
    exit 1
}

$sizeKB = [math]::Round($ti.Length / 1KB, 1)
Write-Host "[PASS] XAML health OK: $sizeKB KB ($($ti.Length) bytes)" -ForegroundColor Green
exit 0
