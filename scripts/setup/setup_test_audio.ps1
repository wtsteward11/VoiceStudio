<#
.SYNOPSIS
    Set up test audio files for VoiceStudio UI tests.

.DESCRIPTION
    This script ensures test audio files are available for running UI tests.
    It can:
    1. Pull canonical audio via Git LFS (if configured)
    2. Generate synthetic fallback audio (if LFS unavailable or files missing)
    3. Verify audio file integrity against manifest.json checksums

.PARAMETER Force
    Force regeneration of synthetic audio even if files exist.

.PARAMETER SkipLFS
    Skip Git LFS pull attempt (use synthetic only).

.PARAMETER Verify
    Only verify existing files without generating new ones.

.EXAMPLE
    .\scripts\setup_test_audio.ps1
    # Standard setup: try LFS, fall back to synthetic generation

.EXAMPLE
    .\scripts\setup_test_audio.ps1 -Force
    # Force regenerate synthetic audio

.EXAMPLE
    .\scripts\setup_test_audio.ps1 -Verify
    # Only verify existing files
#>

param(
    [switch]$Force,
    [switch]$SkipLFS,
    [switch]$Verify
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
if (-not $ProjectRoot) { $ProjectRoot = Get-Location }

$CanonicalDir = Join-Path $ProjectRoot "tests\assets\canonical\standard"
$ManifestPath = Join-Path $ProjectRoot "tests\assets\canonical\manifest.json"

Write-Host "VoiceStudio Test Audio Setup" -ForegroundColor Cyan
Write-Host "=============================" -ForegroundColor Cyan
Write-Host ""

function Test-FileHash {
    param(
        [string]$FilePath,
        [string]$ExpectedHash
    )
    
    if (-not (Test-Path $FilePath)) {
        return $false
    }
    
    $actualHash = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash
    return $actualHash -eq $ExpectedHash
}

function Get-AudioFileInfo {
    param([string]$FilePath)
    
    if (-not (Test-Path $FilePath)) {
        return @{ Exists = $false }
    }
    
    $file = Get-Item $FilePath
    $hash = (Get-FileHash -Path $FilePath -Algorithm SHA256).Hash
    
    return @{
        Exists = $true
        Path = $FilePath
        SizeKB = [math]::Round($file.Length / 1024, 1)
        Hash = $hash
        HashShort = $hash.Substring(0, 16) + "..."
    }
}

# Load manifest
$manifest = $null
if (Test-Path $ManifestPath) {
    $manifest = Get-Content $ManifestPath | ConvertFrom-Json
    Write-Host "[OK] Manifest loaded from $ManifestPath" -ForegroundColor Green
} else {
    Write-Host "[WARN] Manifest not found at $ManifestPath" -ForegroundColor Yellow
}

# Check current state
Write-Host ""
Write-Host "Checking existing audio files..." -ForegroundColor White

$fullAudioPath = Join-Path $CanonicalDir "allan_watts.wav"
$segmentAudioPath = Join-Path $CanonicalDir "allan_watts_15s.wav"
$syntheticMarker = Join-Path $CanonicalDir ".synthetic_marker"

$fullInfo = Get-AudioFileInfo $fullAudioPath
$segmentInfo = Get-AudioFileInfo $segmentAudioPath

if ($fullInfo.Exists) {
    Write-Host "  Full audio: $($fullInfo.SizeKB) KB ($($fullInfo.HashShort))" -ForegroundColor Gray
} else {
    Write-Host "  Full audio: NOT FOUND" -ForegroundColor Yellow
}

if ($segmentInfo.Exists) {
    Write-Host "  Segment audio: $($segmentInfo.SizeKB) KB ($($segmentInfo.HashShort))" -ForegroundColor Gray
} else {
    Write-Host "  Segment audio: NOT FOUND" -ForegroundColor Yellow
}

$isSynthetic = Test-Path $syntheticMarker
if ($isSynthetic) {
    Write-Host "  Type: SYNTHETIC (generated fallback)" -ForegroundColor Yellow
} elseif ($fullInfo.Exists -and $manifest) {
    $expectedHash = $manifest.canonical_audio.formats.wav_full.sha256
    if ($fullInfo.Hash -eq $expectedHash) {
        Write-Host "  Type: CANONICAL (checksum verified)" -ForegroundColor Green
    } else {
        Write-Host "  Type: UNKNOWN (checksum mismatch)" -ForegroundColor Yellow
    }
}

# Verify-only mode
if ($Verify) {
    Write-Host ""
    Write-Host "Verification Results:" -ForegroundColor Cyan
    
    $allOk = $true
    
    if (-not $fullInfo.Exists -and -not $segmentInfo.Exists) {
        Write-Host "  [FAIL] No audio files found" -ForegroundColor Red
        $allOk = $false
    } elseif ($segmentInfo.Exists) {
        Write-Host "  [OK] Test audio available: $segmentAudioPath" -ForegroundColor Green
    } elseif ($fullInfo.Exists) {
        Write-Host "  [OK] Test audio available: $fullAudioPath" -ForegroundColor Green
    }
    
    if ($manifest -and $fullInfo.Exists) {
        $expectedHash = $manifest.canonical_audio.formats.wav_full.sha256
        if ($fullInfo.Hash -eq $expectedHash) {
            Write-Host "  [OK] Full audio checksum verified" -ForegroundColor Green
        } else {
            Write-Host "  [WARN] Full audio checksum mismatch (may be synthetic)" -ForegroundColor Yellow
        }
    }
    
    if ($allOk) {
        Write-Host ""
        Write-Host "Audio setup is complete." -ForegroundColor Green
        exit 0
    } else {
        Write-Host ""
        Write-Host "Audio setup incomplete. Run without -Verify to set up." -ForegroundColor Yellow
        exit 1
    }
}

# Try Git LFS first (unless skipped or files already exist with correct checksums)
$needsGeneration = $true

if (-not $SkipLFS -and -not $Force) {
    Write-Host ""
    Write-Host "Checking Git LFS..." -ForegroundColor White
    
    try {
        $lfsVersion = git lfs version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Git LFS available: $lfsVersion" -ForegroundColor Gray
            
            # Try to pull LFS files
            Write-Host "  Pulling LFS files..." -ForegroundColor Gray
            Push-Location $ProjectRoot
            try {
                git lfs pull --include="tests/assets/canonical/**" 2>&1 | Out-Null
                
                # Check if real files now exist
                $fullInfo = Get-AudioFileInfo $fullAudioPath
                if ($fullInfo.Exists -and $manifest) {
                    $expectedHash = $manifest.canonical_audio.formats.wav_full.sha256
                    if ($fullInfo.Hash -eq $expectedHash) {
                        Write-Host "  [OK] Canonical audio pulled successfully" -ForegroundColor Green
                        $needsGeneration = $false
                    }
                }
            } finally {
                Pop-Location
            }
        } else {
            Write-Host "  Git LFS not available" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "  Git LFS check failed: $_" -ForegroundColor Yellow
    }
}

# Generate synthetic audio if needed
if ($needsGeneration -or $Force) {
    Write-Host ""
    Write-Host "Generating synthetic test audio..." -ForegroundColor White
    
    # Find Python
    $python = $null
    foreach ($candidate in @("py -3.12", "py -3", "python3", "python")) {
        try {
            $version = & cmd /c "$candidate --version 2>&1"
            if ($LASTEXITCODE -eq 0) {
                $python = $candidate
                Write-Host "  Using Python: $python ($version)" -ForegroundColor Gray
                break
            }
        } catch {}
    }
    
    if (-not $python) {
        Write-Host "  [ERROR] Python not found" -ForegroundColor Red
        exit 1
    }
    
    # Run generator
    $generatorPath = Join-Path $ProjectRoot "tests\ui\fixtures\generate_test_audio.py"
    
    if (-not (Test-Path $generatorPath)) {
        Write-Host "  [ERROR] Generator not found: $generatorPath" -ForegroundColor Red
        exit 1
    }
    
    $genArgs = "--canonical"
    if ($Force) { $genArgs += " --force" }
    
    Push-Location $ProjectRoot
    try {
        $env:PYTHONPATH = $ProjectRoot
        $result = & cmd /c "$python $generatorPath $genArgs 2>&1"
        Write-Host $result
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  [WARN] Generator returned non-zero exit code" -ForegroundColor Yellow
        }
    } finally {
        Pop-Location
    }
}

# Final verification
Write-Host ""
Write-Host "Final Status:" -ForegroundColor Cyan

$fullInfo = Get-AudioFileInfo $fullAudioPath
$segmentInfo = Get-AudioFileInfo $segmentAudioPath

if ($segmentInfo.Exists) {
    Write-Host "  [OK] Test audio ready: $segmentAudioPath" -ForegroundColor Green
    Write-Host "       Size: $($segmentInfo.SizeKB) KB" -ForegroundColor Gray
    
    # Set environment variable hint
    Write-Host ""
    Write-Host "To use in tests, set:" -ForegroundColor White
    Write-Host "  `$env:VOICESTUDIO_TEST_AUDIO = `"$segmentAudioPath`"" -ForegroundColor Cyan
    
    exit 0
} elseif ($fullInfo.Exists) {
    Write-Host "  [OK] Test audio ready: $fullAudioPath" -ForegroundColor Green
    Write-Host "       Size: $($fullInfo.SizeKB) KB" -ForegroundColor Gray
    exit 0
} else {
    Write-Host "  [FAIL] No test audio available after setup" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Check Python is installed: py --version" -ForegroundColor Gray
    Write-Host "  2. Run generator manually: py tests\ui\fixtures\generate_test_audio.py --canonical" -ForegroundColor Gray
    Write-Host "  3. Place audio files manually in: $CanonicalDir" -ForegroundColor Gray
    exit 1
}
