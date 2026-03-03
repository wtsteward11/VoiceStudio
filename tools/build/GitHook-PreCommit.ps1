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

# -------------------------------
# REPO PATH / ROUTE COUPLING CHECK (Backend Spine Migration)
# -------------------------------
$routeFilesStaged = $stagedFiles | Where-Object { $_ -match "backend/api/routes/.*\.py" }
if ($routeFilesStaged -and $routeFilesStaged.Count -gt 0) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $verifyScript = Join-Path $scriptDir "Verify-NoRepoPaths.ps1"
    if (Test-Path $verifyScript) {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $verifyScript
        if ($LASTEXITCODE -ne 0) {
            Write-Host ""
            Write-Host "COMMIT BLOCKED: Route files have forbidden path or coupling patterns." -ForegroundColor Red
            Write-Host "See Backend Spine Migration Plan. Fix violations before committing." -ForegroundColor Yellow
            exit 1
        }
    }
}

# -------------------------------
# CURSOR-BRICK FIREWALL
# -------------------------------
if ($env:VOICE_STUDIO_ALLOW_RISKY_COMMIT -eq "1") {
    Write-Host "[BUILD GUARD] Risky commit override active." -ForegroundColor Yellow
} else {
    # Phase A complete: voice.py deleted, voice/ package is canonical. No critical no-delete routes.
    $criticalNoDelete = @()
    $neverCommitPrefixes = @("installer/runtime/", "models/", ".voicestudio/")
    $maxStagedFiles = 500
    $maxFileSizeBytes = 25MB

    $stagedStatus = git diff --cached --name-status 2>$null
    if ($stagedStatus) {
        foreach ($line in $stagedStatus) {
            $parts = $line -split "`t"
            if ($parts.Count -lt 2) { continue }
            $status = $parts[0]
            $p1 = ($parts[1] -replace "\\", "/")
            $p2 = if ($parts.Count -ge 3) { ($parts[2] -replace "\\", "/") } else { $null }

            if ($status -eq "D" -and ($criticalNoDelete -contains $p1)) {
                Write-Host ""
                Write-Host "COMMIT BLOCKED: Attempted to delete critical route file: $p1" -ForegroundColor Red
                Write-Host "This file must remain for backend route registration stability." -ForegroundColor Yellow
                exit 1
            }

            if ($status -like "R*" -and (($criticalNoDelete -contains $p1) -or ($criticalNoDelete -contains $p2))) {
                Write-Host ""
                Write-Host "COMMIT BLOCKED: Attempted to rename/move critical route file: $p1 -> $p2" -ForegroundColor Red
                Write-Host "Do NOT move/split/delete voice.py. Keep module identity stable." -ForegroundColor Yellow
                exit 1
            }

            foreach ($prefix in $neverCommitPrefixes) {
                if ($p1.StartsWith($prefix)) {
                    Write-Host ""
                    Write-Host "COMMIT BLOCKED: Forbidden payload staged: $p1" -ForegroundColor Red
                    Write-Host "This directory must never be committed. Move payloads outside repo." -ForegroundColor Yellow
                    exit 1
                }
            }

            if ($status -match "^[AM]$") {
                try {
                    $sizeStr = git cat-file -s ":$p1" 2>$null
                    if ($sizeStr) {
                        $size = [int64]$sizeStr
                        if ($size -gt $maxFileSizeBytes) {
                            $mb = [math]::Round($size / 1MB, 1)
                            Write-Host ""
                            Write-Host "COMMIT BLOCKED: Huge file staged ($mb MB): $p1" -ForegroundColor Red
                            Write-Host "This is how you accidentally commit runtimes/models and brick tooling." -ForegroundColor Yellow
                            exit 1
                        }
                    }
                } catch { }
            }
        }
    }

    $stagedCount = ($stagedFiles | Measure-Object).Count
    if ($stagedCount -gt $maxStagedFiles) {
        Write-Host ""
        Write-Host "COMMIT BLOCKED: Too many files staged ($stagedCount > $maxStagedFiles)" -ForegroundColor Red
        Write-Host "This is almost always a generated/runtime dump. Revert and isolate." -ForegroundColor Yellow
        exit 1
    }
}

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
