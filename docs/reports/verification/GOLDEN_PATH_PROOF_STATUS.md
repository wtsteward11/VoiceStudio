# Golden Path Proof Status

**Date:** 2026-03-15  
**Purpose:** Document golden path E2E proof requirements, current blocker, and verification steps.  
**Related:** [VOICESTUDIO_COMPLETION_ROADMAP_V2.md](../../governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md) Phase E, [test_golden_path.py](../../../tests/e2e/test_golden_path.py)

---

## Governing Principle

> 100% complete means exactly one thing: `pytest tests/e2e/test_golden_path.py` exits 0 with real XTTS + STT engine loaded, real audio in, real synthesized audio out, proof artifact on disk with model hashes and git commit.

---

## Current Status (2026-03-15)

| Check | Status | Notes |
|-------|--------|-------|
| Backend running | ✓ | localhost:8000 healthy |
| XTTS models | ✓ | Present |
| Piper models | ✓ | Present |
| Whisper (STT) | ✗ | **BLOCKER** |
| Golden path test | ✗ | Fails at Step 2 (transcribe) |

### Blocker: STT Engine

The transcription step fails with:

```
Transcription engine 'whisper' is not available. Please ensure the engine is properly installed.
Install with: pip install faster-whisper==1.0.3
```

**Root cause:** Transcription uses `engine_service.get_engine(engine_id)`. For `whisper_cpp`, the router loads WhisperCPPEngine (requires whisper-cpp-python). For `whisper`, it loads WhisperEngine (requires faster-whisper). When the requested engine is not available, transcription fails with a clear 503 and install instructions.

**Mitigation options:**
1. **whisper_cpp:** `pip install whisper-cpp-python` + ensure GGUF model exists (ensure_whisper_cpp preflight)
2. **whisper (faster-whisper):** `pip install faster-whisper==1.0.3`
3. Restart the backend after installing (backend must load the package at runtime)

---

## Verification Steps

### Preconditions

```powershell
python scripts/golden_path_preconditions.py --check-backend http://localhost:8000 --json
```

Expect `ready_for_real_mode: true` when STT smoke passes.

### Run Golden Path (when ready)

```powershell
# 1. Start backend (if not running)
python -m backend.api.main

# 2. Run proof writer (runs preconditions + pytest)
python scripts/ci/write_golden_path_real_proof.py
```

Proof artifact: `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_YYYY-MM-DD.json`

### Manual Test (without proof)

```powershell
$env:VOICESTUDIO_GOLDEN_PATH_OUTPUT_DIR = ".buildlogs/proof_runs/golden_path_attempt"
python -m pytest tests/e2e/test_golden_path.py -v --tb=short
```

---

## CI Integration

The plan allows: "Add CI job or gate that runs this test with real engines (or document why it cannot run in CI)."

**Current:** Documented. Golden path requires:
- Backend running
- XTTS + STT engines installed and loadable
- Model files present

CI typically runs without a live backend or real engines. Manual proof runs are required for release evidence.

---

## Changelog

- 2026-03-15: Initial document. Blocker: STT (faster-whisper). Verification steps documented.
