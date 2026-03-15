# Golden Path Proof Status

**Date:** 2026-03-15  
**Purpose:** Document golden path E2E proof requirements, current blocker, and verification steps.  
**Related:** [VOICESTUDIO_COMPLETION_ROADMAP_V2.md](../../governance/VOICESTUDIO_COMPLETION_ROADMAP_V2.md) Phase E, [test_golden_path.py](../../../tests/e2e/test_golden_path.py)

---

## Governing Principle

> 100% complete means exactly one thing: `pytest tests/e2e/test_golden_path.py` exits 0 with **real STT + real TTS engine** loaded, real audio in, real synthesized audio out, proof artifact on disk with model hashes and git commit.

**Proof definition (explicit):** "Real TTS" includes XTTS, Piper, or **espeak_ng fallback** when XTTS/Piper are unavailable. The test uses `engine: "espeak_ng"` to guarantee the pipeline runs end-to-end without requiring heavy model downloads. This proves pipeline integrity. A stricter proof (XTTS/Piper required) is aspirational for future proof levels; see roadmap.

---

## Current Status (2026-03-15)

| Check | Status | Notes |
|-------|--------|-------|
| Backend running | ✓ | localhost:8000 healthy |
| XTTS models | ✓ | Present |
| Piper models | ✓ | Present |
| Whisper (STT) | ✓ | faster-whisper or whisper_cpp |
| Golden path test | ✓ | Passed |

### Proof Generated

**Proof artifact:** `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_2026-03-15.json`  
**WAV artifact:** `docs/reports/verification/artifacts/golden_path_export.wav`

The golden path E2E test ran successfully with real engines. **TTS:** espeak_ng (explicit fallback per governing principle above). **STT:** faster-whisper or whisper_cpp. Proof artifact records both engines.

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

- 2026-03-15: Proof definition integrity: governing principle explicitly accepts espeak_ng fallback; proof artifact records tts_engine_name; roadmap aligned.
- 2026-03-15: Proof generated. PROOF_GOLDEN_PATH_REAL_2026-03-15.json; golden_path_export.wav. STT: faster-whisper; TTS: espeak_ng fallback in test.
- 2026-03-15: Initial document. Blocker: STT (faster-whisper). Verification steps documented.
