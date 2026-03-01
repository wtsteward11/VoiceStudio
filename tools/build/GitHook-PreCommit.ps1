<#
.SYNOPSIS
    Git pre-commit hook: blocks commits that modify protected build config files
    unless VOICE_STUDIO_ALLOW_CONFIG_CHANGE=1 is set.

.DESCRIPTION
    Certain build configuration files are protected because changes to them have
    historically caused silent XAML compiler crashes and multi-day debugging sessions.

    This hook prevents accidental modifications to these files. To intentionally
    modify a protected file, set the environment variable before committing:

        $env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE = "1"
        git commit -m "chore: intentional config change"
        $env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE = ""

.NOTES
    Install: git config core.hooksPath .githooks
    Bypass:  $env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE = "1"
#>

$protectedFiles = @(
    "Directory.Build.props",
    "Directory.Build.targets",
    "global.json",
    "src/VoiceStudio.App/VoiceStudio.App.csproj",
    "src/VoiceStudio.Core/VoiceStudio.Core.csproj",
    "VoiceStudio.sln"
)

if ($env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE -eq "1") {
    Write-Host "[BUILD GUARD] Config change override active. Protected files allowed." -ForegroundColor Yellow
    exit 0
}

$stagedFiles = git diff --cached --name-only 2>$null
if (-not $stagedFiles) { exit 0 }

$blocked = @()
foreach ($staged in $stagedFiles) {
    $normalized = $staged -replace "\\", "/"
    foreach ($protected in $protectedFiles) {
        if ($normalized -eq $protected) {
            $blocked += $staged
        }
    }
}

if ($blocked.Count -gt 0) {
    Write-Host "" -ForegroundColor Red
    Write-Host "COMMIT BLOCKED: Protected build config files modified" -ForegroundColor Red
    Write-Host "=====================================================" -ForegroundColor Red
    Write-Host ""
    foreach ($f in $blocked) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "These files are protected because changes can silently break the XAML compiler." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To proceed (if this change is intentional):" -ForegroundColor Cyan
    Write-Host '  $env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE = "1"' -ForegroundColor Cyan
    Write-Host '  git commit -m "your message"' -ForegroundColor Cyan
    Write-Host '  $env:VOICE_STUDIO_ALLOW_CONFIG_CHANGE = ""' -ForegroundColor Cyan
    Write-Host ""
    Write-Host "REF: docs/reports/BUILD_PROTECTION_PROTOCOL_20260228.md" -ForegroundColor Yellow
    exit 1
}

exit 0
