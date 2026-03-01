# Generate Release Notes from Changelog
# Usage: .\scripts\generate-release-notes.ps1 -Version "1.1.0" -ChangelogPath "CHANGELOG.md" -OutputPath "RELEASE_NOTES_v1.1.0.md"

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    
    [string]$ChangelogPath = "CHANGELOG.md",
    [string]$OutputPath = "RELEASE_NOTES_v$Version.md",
    [string]$TemplatePath = "docs/release/RELEASE_NOTES_TEMPLATE.md"
)

Write-Host "Generating release notes for version $Version..." -ForegroundColor Cyan

# Check if changelog exists
if (-not (Test-Path $ChangelogPath)) {
    Write-Error "Changelog file not found: $ChangelogPath"
    exit 1
}

# Read changelog
$changelog = Get-Content $ChangelogPath -Raw

# Extract version section from changelog
$pattern = "## \[$Version\].*?(?=## \[|\z)"
$match = [regex]::Match($changelog, $pattern, [System.Text.RegularExpressions.RegexOptions]::Singleline)

if (-not $match.Success) {
    Write-Error "Version $Version not found in changelog"
    exit 1
}

$versionSection = $match.Value

# Extract date from version section
$datePattern = "\[$Version\] - (\d{4}-\d{2}-\d{2})"
$dateMatch = [regex]::Match($versionSection, $datePattern)
$releaseDate = if ($dateMatch.Success) { $dateMatch.Groups[1].Value } else { Get-Date -Format "yyyy-MM-dd" }

# Read template
if (Test-Path $TemplatePath) {
    $template = Get-Content $TemplatePath -Raw
} else {
    Write-Warning "Template not found: $TemplatePath. Using basic template."
    $template = @"
# VoiceStudio Quantum+ v$Version

**Release Date:** $releaseDate  
**Release Type:** Stable  
**Status:** Production Ready

---

## 🎉 Overview

[Brief overview of the release]

---

## ✨ New Features

[Features from changelog]

---

## 🚀 Improvements

[Improvements from changelog]

---

## 🐛 Bug Fixes

[Bug fixes from changelog]

---

## 📚 Documentation Updates

[Documentation updates]

---

## ⚠️ Breaking Changes

[Breaking changes if any]

---

**Thank you for using VoiceStudio Quantum+!**
"@
}

# Parse changelog sections
$added = @()
$changed = @()
$deprecated = @()
$removed = @()
$fixed = @()
$security = @()

$lines = $versionSection -split "`n"
$currentSection = $null

foreach ($line in $lines) {
    if ($line -match "### (Added|Changed|Deprecated|Removed|Fixed|Security)") {
        $currentSection = $matches[1]
    } elseif ($currentSection -and $line.Trim().StartsWith("-")) {
        switch ($currentSection) {
            "Added" { $added += $line.Trim() }
            "Changed" { $changed += $line.Trim() }
            "Deprecated" { $deprecated += $line.Trim() }
            "Removed" { $removed += $line.Trim() }
            "Fixed" { $fixed += $line.Trim() }
            "Security" { $security += $line.Trim() }
        }
    }
}

# Replace template placeholders
$releaseNotes = $template
$releaseNotes = $releaseNotes -replace "\[VERSION\]", $Version
$releaseNotes = $releaseNotes -replace "\[YYYY-MM-DD\]", $releaseDate

# Replace sections with actual content
if ($added.Count -gt 0) {
    $releaseNotes = $releaseNotes -replace "\[Features from changelog\]", ($added -join "`n")
}

if ($changed.Count -gt 0) {
    $releaseNotes = $releaseNotes -replace "\[Improvements from changelog\]", ($changed -join "`n")
}

if ($fixed.Count -gt 0) {
    $releaseNotes = $releaseNotes -replace "\[Bug fixes from changelog\]", ($fixed -join "`n")
}

# Write output
$releaseNotes | Out-File $OutputPath -Encoding UTF8

Write-Host "Release notes generated: $OutputPath" -ForegroundColor Green
Write-Host "  - Added: $($added.Count) items" -ForegroundColor Gray
Write-Host "  - Changed: $($changed.Count) items" -ForegroundColor Gray
Write-Host "  - Fixed: $($fixed.Count) items" -ForegroundColor Gray
Write-Host "  - Security: $($security.Count) items" -ForegroundColor Gray

