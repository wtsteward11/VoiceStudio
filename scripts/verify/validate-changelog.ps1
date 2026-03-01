# Validate Changelog Format
# Usage: .\scripts\validate-changelog.ps1 -ChangelogPath "CHANGELOG.md"

param(
    [string]$ChangelogPath = "CHANGELOG.md"
)

Write-Host "Validating changelog format..." -ForegroundColor Cyan

if (-not (Test-Path $ChangelogPath)) {
    Write-Error "Changelog file not found: $ChangelogPath"
    exit 1
}

$errors = @()
$warnings = @()

$content = Get-Content $ChangelogPath -Raw
$lines = Get-Content $ChangelogPath

# Check for required sections
$requiredSections = @("Added", "Changed", "Deprecated", "Removed", "Fixed", "Security")
$foundSections = @()

# Check version format
$versionPattern = '## \[(\d+\.\d+\.\d+.*?)\] - (\d{4}-\d{2}-\d{2})'
$versionMatches = [regex]::Matches($content, $versionPattern)

if ($versionMatches.Count -eq 0) {
    $errors += "No valid version entries found. Expected format: ## [1.0.0] - 2025-01-28"
}

foreach ($match in $versionMatches) {
    $version = $match.Groups[1].Value
    $date = $match.Groups[2].Value
    
    # Validate date format
    try {
        [DateTime]::ParseExact($date, "yyyy-MM-dd", $null) | Out-Null
    } catch {
        $errors += "Invalid date format for version $version: $date (expected: YYYY-MM-DD)"
    }
    
    # Validate version format (basic SemVer check)
    if ($version -notmatch '^\d+\.\d+\.\d+') {
        $warnings += "Version $version may not follow SemVer format (MAJOR.MINOR.PATCH)"
    }
}

# Check for Unreleased section
if ($content -notmatch "## \[Unreleased\]") {
    $warnings += "No [Unreleased] section found. Consider adding one for ongoing changes."
}

# Check section format
$sectionPattern = '### (Added|Changed|Deprecated|Removed|Fixed|Security)'
$sectionMatches = [regex]::Matches($content, $sectionPattern)

foreach ($section in $requiredSections) {
    if ($content -match "### $section") {
        $foundSections += $section
    }
}

# Report results
Write-Host "`nValidation Results:" -ForegroundColor Cyan
Write-Host "  Versions found: $($versionMatches.Count)" -ForegroundColor Gray
Write-Host "  Sections found: $($foundSections.Count)" -ForegroundColor Gray

if ($errors.Count -gt 0) {
    Write-Host "`nErrors:" -ForegroundColor Red
    foreach ($error in $errors) {
        Write-Host "  ❌ $error" -ForegroundColor Red
    }
}

if ($warnings.Count -gt 0) {
    Write-Host "`nWarnings:" -ForegroundColor Yellow
    foreach ($warning in $warnings) {
        Write-Host "  ⚠️  $warning" -ForegroundColor Yellow
    }
}

if ($errors.Count -eq 0 -and $warnings.Count -eq 0) {
    Write-Host "`n✅ Changelog format is valid!" -ForegroundColor Green
    exit 0
} elseif ($errors.Count -eq 0) {
    Write-Host "`n⚠️  Changelog has warnings but is valid." -ForegroundColor Yellow
    exit 0
} else {
    Write-Host "`n❌ Changelog validation failed!" -ForegroundColor Red
    exit 1
}

