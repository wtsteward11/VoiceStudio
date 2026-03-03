# Real-mode golden path runner
# Requires: backend running at localhost:8000, models installed (whisper_cpp + piper or xtts)
# Produces: PROOF_GOLDEN_PATH_REAL_<date>.json in docs/reports/verification/

$ErrorActionPreference = "Stop"

# Step 1: Preconditions check (fail fast)
$preconditionsJson = python scripts/golden_path_preconditions.py --check-backend http://localhost:8000 --json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Preconditions script failed: $preconditionsJson"
    exit 1
}

$preconditions = $preconditionsJson | ConvertFrom-Json
if (-not $preconditions.ready_for_real_mode) {
    Write-Error "Not ready for real mode. Run: python scripts/golden_path_preconditions.py --check-backend http://localhost:8000 --json"
    exit 1
}

# Step 2: Run E2E test in real mode
$env:VOICESTUDIO_TEST_MODE = "real"
python -m pytest tests/e2e/test_golden_path.py -v --tb=short
if ($LASTEXITCODE -ne 0) {
    Write-Error "Golden path E2E test failed"
    exit 1
}

# Step 3: Locate output WAV (e2e test writes to %TEMP%/voicestudio_golden_path/golden_path_export.wav)
$tempDir = [System.IO.Path]::GetTempPath()
$exportDir = Join-Path $tempDir "voicestudio_golden_path"
$outputWav = Join-Path $exportDir "golden_path_export.wav"

if (-not (Test-Path $outputWav)) {
    Write-Error "No output WAV found at $outputWav. E2E test may not have exported audio."
    exit 1
}

# Step 4: Generate SSOT proof
python scripts/ci/write_golden_path_proof.py --engine-mode real --output-file $outputWav
if ($LASTEXITCODE -ne 0) {
    Write-Error "Proof generation failed"
    exit 1
}

# Step 5: Validate proof (--no-git-match for local dev; proof may have been generated on prior commit)
python scripts/ci/check_state_proofs.py --validate-file "docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json" --no-git-match
if ($LASTEXITCODE -ne 0) {
    Write-Error "Proof validation failed"
    exit 1
}

Write-Host ""
Write-Host "Real-mode golden path PASS. Proof at docs/reports/verification/"
Write-Host "To commit: git add docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json && git commit"
Write-Host ""
