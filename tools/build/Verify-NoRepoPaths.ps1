<#
.SYNOPSIS
    Backend Spine Migration: Tripwire to prevent repo path and route-coupling violations.

.DESCRIPTION
    Fails if:
    1. Repo contains non-empty backups/, data/recordings/, data/audio_uploads/, data/library/
    2. Any backend/api/routes/*.py contains forbidden patterns:
       - Path("backups"), Path("data/
       - os.path.join("data",
       - open("data/
       - from .voice import synthesize, _audio_storage, _register_audio_file, synthesize_core
       - from .voice_morph import, from .prosody import, from .style_transfer import, from .ensemble import (route internals)

.NOTES
    Part of Backend Spine Migration Plan. Run after Phase 3 path migrations.
#>

$ErrorActionPreference = "Stop"
$repoRoot = if ($env:GITHUB_WORKSPACE) { $env:GITHUB_WORKSPACE } else { (Get-Location).Path }

# Resolve repo root from script location if needed
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $repoRoot -or -not (Test-Path (Join-Path $repoRoot ".git"))) {
    $repoRoot = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
}

$failures = @()

# Skip poison check during migration (user may have legacy data in repo)
$skipPoison = $env:VOICESTUDIO_SKIP_POISON_CHECK -eq "1"

# ---- 1. Repo poison directories (non-empty under repo root) ----
$poisonDirs = @(
    "backups",
    "data\recordings",
    "data\audio_uploads",
    "data\library"
)
if (-not $skipPoison) {
    foreach ($d in $poisonDirs) {
        $fullPath = Join-Path $repoRoot $d
        if (Test-Path $fullPath) {
            $items = Get-ChildItem -Path $fullPath -Recurse -File -ErrorAction SilentlyContinue
            $count = ($items | Measure-Object).Count
            if ($count -gt 0) {
                $failures += "REPO POISON: $d has $count files (must be empty or absent). Set VOICESTUDIO_SKIP_POISON_CHECK=1 to skip during migration."
            }
        }
    }
}

# ---- 2. Forbidden patterns in route files ----
$routeDir = Join-Path $repoRoot "backend\api\routes"
$forbiddenPatterns = @(
    @{ Pattern = 'Path\("backups"\)'; Msg = 'Path("backups")' },
    @{ Pattern = 'Path\("data/'; Msg = 'Path("data/...")' },
    @{ Pattern = 'os\.path\.join\("data",'; Msg = 'os.path.join("data",' },
    @{ Pattern = 'open\("data/'; Msg = 'open("data/' },
    @{ Pattern = 'from \.voice import (synthesize|_audio_storage|_register_audio_file|synthesize_core)'; Msg = 'from .voice import (synthesize|_audio_storage|_register_audio_file|synthesize_core)' },
    @{ Pattern = 'from \.voice_morph import'; Msg = 'from .voice_morph import' },
    @{ Pattern = 'from \.prosody import'; Msg = 'from .prosody import' },
    @{ Pattern = 'from \.style_transfer import'; Msg = 'from .style_transfer import' },
    @{ Pattern = 'from \.ensemble import'; Msg = 'from .ensemble import' },
    # Platform Spine Migration: route-to-route import bans
    @{ Pattern = 'from \.audio import _get_audio_path'; Msg = 'from .audio import _get_audio_path (use backend.services.audio_path_resolver)' },
    @{ Pattern = 'from \.\.audio import _get_audio_path'; Msg = 'from ..audio import _get_audio_path (use backend.services.audio_path_resolver)' },
    @{ Pattern = 'from \.\.routes\.audio import _get_audio_path'; Msg = 'from ..routes.audio import _get_audio_path (use backend.services.audio_path_resolver)' },
    @{ Pattern = 'from \.\.routes\.voice import _audio_storage'; Msg = 'from ..routes.voice import _audio_storage (use AudioRegistry)' },
    @{ Pattern = 'from \.\.routes\.voice import _register_audio_file'; Msg = 'from ..routes.voice import _register_audio_file (use AudioRegistry)' },
    @{ Pattern = 'from backend\.api\.routes\.voice import _register_audio_file'; Msg = 'from backend.api.routes.voice import _register_audio_file (use AudioRegistry)' }
)

if (Test-Path $routeDir) {
    $routeFiles = Get-ChildItem -Path $routeDir -Recurse -Filter "*.py" -File |
        Where-Object { $_.FullName -notmatch "_archived" }
    foreach ($file in $routeFiles) {
        $relPath = $file.FullName.Replace($repoRoot, "").TrimStart("\", "/")
        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if (-not $content) { continue }
        foreach ($fp in $forbiddenPatterns) {
            if ($content -cmatch $fp.Pattern) {
                $failures += "FORBIDDEN PATTERN in $relPath : $($fp.Msg)"
            }
        }
    }
}

# ---- Report ----
if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Verify-NoRepoPaths FAILED ($($failures.Count) violation(s))" -ForegroundColor Red
    Write-Host "=============================================" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "See Backend Spine Migration Plan: Phase 0/3." -ForegroundColor Yellow
    exit 1
}

Write-Host "[PASS] Verify-NoRepoPaths: no repo path or route-coupling violations" -ForegroundColor Green
exit 0
