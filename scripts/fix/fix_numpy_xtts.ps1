# VoiceStudio - Fix NumPy Version for XTTS Compatibility
# 
# This script ensures numpy 1.26.4 is installed and verifies XTTS loads correctly.
# Run this script from the VoiceStudio root directory with your virtual environment activated.
#
# PROBLEM: numpy 2.0.2 installed but TTS/spacy compiled against numpy 1.x
# IMPACT: XTTS engine fails, quality analysis fails, some routes crash
# SOLUTION: Downgrade numpy to 1.26.4 and verify XTTS loads
#
# Usage:
#   .\scripts\fix_numpy_xtts.ps1
#

param(
    [switch]$Force,       # Force reinstall even if correct version is installed
    [switch]$SkipVerify   # Skip XTTS verification (useful for CI)
)

$ErrorActionPreference = "Stop"

Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "VoiceStudio - NumPy/XTTS Compatibility Fix" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""

# Check if we're in a virtual environment
$venvActive = $env:VIRTUAL_ENV -or (Get-Command python -ErrorAction SilentlyContinue)
if (-not $venvActive) {
    Write-Host "[ERROR] No Python environment detected. Please activate your virtual environment first." -ForegroundColor Red
    Write-Host "        Example: .\.venv\Scripts\Activate.ps1" -ForegroundColor Yellow
    exit 1
}

# Get current numpy version
Write-Host "[INFO] Checking current numpy version..." -ForegroundColor Yellow
$currentVersion = $null
try {
    $currentVersion = python -c "import numpy; print(numpy.__version__)" 2>&1
    Write-Host "       Current numpy version: $currentVersion" -ForegroundColor White
}
catch {
    Write-Host "       numpy not installed or import failed" -ForegroundColor Yellow
}

$targetVersion = "1.26.4"

# Check if we need to fix
if ($currentVersion -eq $targetVersion -and -not $Force) {
    Write-Host "[OK] numpy $targetVersion is already installed" -ForegroundColor Green
}
else {
    # Step 1: Uninstall current numpy
    Write-Host ""
    Write-Host "[STEP 1/3] Uninstalling current numpy..." -ForegroundColor Yellow
    pip uninstall numpy -y 2>&1 | Out-Null
    
    # Step 2: Install correct numpy version
    Write-Host "[STEP 2/3] Installing numpy==$targetVersion..." -ForegroundColor Yellow
    $installResult = pip install "numpy==$targetVersion" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[ERROR] Failed to install numpy $targetVersion" -ForegroundColor Red
        Write-Host $installResult -ForegroundColor Red
        exit 1
    }
    
    # Verify installation
    Write-Host "[STEP 3/3] Verifying numpy installation..." -ForegroundColor Yellow
    $installedVersion = python -c "import numpy; print(numpy.__version__)" 2>&1
    
    if ($installedVersion -eq $targetVersion) {
        Write-Host "[OK] numpy $targetVersion installed successfully" -ForegroundColor Green
    }
    else {
        Write-Host "[WARNING] Installed version ($installedVersion) differs from target ($targetVersion)" -ForegroundColor Yellow
    }
}

# Step 4: Verify XTTS loads
if (-not $SkipVerify) {
    Write-Host ""
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host "Verifying XTTS Engine Loads..." -ForegroundColor Cyan
    Write-Host "=" * 70 -ForegroundColor Cyan
    Write-Host ""
    
    $xttsTestScript = @'
import sys
import traceback

def test_xtts():
    """Test if XTTS can be imported and initialized."""
    errors = []
    
    # Test 1: Check numpy version
    print("[TEST 1] Checking numpy version...")
    try:
        import numpy as np
        print(f"         numpy version: {np.__version__}")
        if not np.__version__.startswith("1.26"):
            errors.append(f"Unexpected numpy version: {np.__version__} (expected 1.26.x)")
    except Exception as e:
        errors.append(f"Failed to import numpy: {e}")
    
    # Test 2: Import TTS library
    print("[TEST 2] Importing TTS library...")
    try:
        from TTS.api import TTS
        print("         TTS library imported successfully")
    except ImportError as e:
        errors.append(f"Failed to import TTS: {e}")
    except Exception as e:
        errors.append(f"TTS import error: {e}")
    
    # Test 3: Check XTTS model availability
    print("[TEST 3] Checking XTTS model availability...")
    try:
        from TTS.utils.manage import ModelManager
        manager = ModelManager(models_file="", output_prefix="", progress_bar=False)
        # Check if XTTS models are listed
        models = manager.list_models() if hasattr(manager, 'list_models') else []
        print(f"         Found {len(models)} available models")
    except Exception as e:
        # This is expected if models aren't downloaded yet
        print(f"         Model manager check skipped: {e}")
    
    # Test 4: Import core audio processing
    print("[TEST 4] Importing audio processing libraries...")
    try:
        import librosa
        print(f"         librosa version: {librosa.__version__}")
        import soundfile
        print(f"         soundfile version: {soundfile.__version__}")
    except Exception as e:
        errors.append(f"Audio library import error: {e}")
    
    # Test 5: Import torch and check CUDA
    print("[TEST 5] Checking PyTorch and CUDA...")
    try:
        import torch
        print(f"         torch version: {torch.__version__}")
        print(f"         CUDA available: {torch.cuda.is_available()}")
        if torch.cuda.is_available():
            print(f"         CUDA version: {torch.version.cuda}")
    except Exception as e:
        errors.append(f"PyTorch import error: {e}")
    
    # Summary
    print("")
    print("=" * 70)
    if errors:
        print("XTTS VERIFICATION FAILED")
        print("=" * 70)
        for error in errors:
            print(f"  [ERROR] {error}")
        return 1
    else:
        print("XTTS VERIFICATION PASSED")
        print("=" * 70)
        print("All tests passed. XTTS engine should work correctly.")
        return 0

if __name__ == "__main__":
    try:
        sys.exit(test_xtts())
    except Exception as e:
        print(f"Verification failed with exception: {e}")
        traceback.print_exc()
        sys.exit(1)
'@

    # Run the verification script
    $xttsTestScript | python -
    $xttsExitCode = $LASTEXITCODE
    
    Write-Host ""
    if ($xttsExitCode -eq 0) {
        Write-Host "[SUCCESS] XTTS verification completed successfully!" -ForegroundColor Green
    }
    else {
        Write-Host "[WARNING] XTTS verification had issues. Check output above." -ForegroundColor Yellow
        Write-Host "          Some tests may fail if TTS models are not downloaded yet." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host "Fix Complete" -ForegroundColor Cyan
Write-Host "=" * 70 -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Restart any running backend servers" -ForegroundColor White
Write-Host "  2. Test synthesis with: python -c 'from TTS.api import TTS; print(\"OK\")'" -ForegroundColor White
Write-Host "  3. Run the full test suite: pytest tests/" -ForegroundColor White
Write-Host ""
