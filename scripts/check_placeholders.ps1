<#
.SYNOPSIS
    Scans production C# source for forbidden placeholder patterns.
    Returns exit 0 if clean, exit 1 if violations found.
.DESCRIPTION
    The Definition of Done requires no TODOs, no NotImplementedException,
    no HACK/FIXME markers, and no placeholder stubs in shipped code.
    This script enforces that requirement.
#>
param(
    [string]$SourceDir = ""
)

$ErrorActionPreference = "Stop"

if (-not $SourceDir) {
    $repoRoot = git rev-parse --show-toplevel 2>$null
    if (-not $repoRoot) { $repoRoot = $PSScriptRoot | Split-Path }
    $SourceDir = Join-Path $repoRoot "src\VoiceStudio.App"
}

$patterns = @(
    @{ Name = 'NotImplementedException'; Pattern = 'NotImplementedException' },
    @{ Name = 'TODO:';                   Pattern = 'TODO:' },
    @{ Name = 'HACK:';                   Pattern = 'HACK:' },
    @{ Name = 'FIXME:';                  Pattern = 'FIXME:' }
)

$excludeDirs = @('obj', 'bin', '.buildlogs', 'TestResults')

$violations = @()

Write-Host "Scanning $SourceDir for placeholder patterns..." -ForegroundColor Cyan

$csFiles = Get-ChildItem -Path $SourceDir -Recurse -Filter '*.cs' | Where-Object {
    $path = $_.FullName
    $excluded = $false
    foreach ($dir in $excludeDirs) {
        if ($path -match "\\$dir\\") { $excluded = $true; break }
    }
    -not $excluded
}

foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($p in $patterns) {
            if ($line -match [regex]::Escape($p.Pattern)) {
                $trimmed = $line.Trim()
                if ($trimmed.Length -gt 120) { $trimmed = $trimmed.Substring(0, 120) + '...' }
                $relPath = $file.FullName.Replace($SourceDir, '').TrimStart('\', '/')
                $violations += [PSCustomObject]@{
                    File = $relPath
                    Line = $i + 1
                    Pattern = $p.Name
                    Text = $trimmed
                }
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host ""
    Write-Host "PLACEHOLDER SCAN FAILED" -ForegroundColor Red
    Write-Host "Found $($violations.Count) violations:" -ForegroundColor Red
    Write-Host ""
    foreach ($v in $violations) {
        Write-Host "  $($v.File):$($v.Line) [$($v.Pattern)] $($v.Text)" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "Fix these before marking the release complete." -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "[PASS] No placeholder patterns found in $($csFiles.Count) C# files." -ForegroundColor Green
    exit 0
}
