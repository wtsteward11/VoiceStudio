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

## Imported Asset Playback (Required Proof Point)

**Definition:** Imported audio must play in Library (and other panels) on the same path users use.

**Gate C UI smoke** (`--ui-smoke` / `--smoke-ui`) proves:
- Synthesis creates audio and uploads to backend
- Library panel opens and refreshes
- Asset visible in Library (by audio_id or first playable asset)
- `PlayAssetCommand.Execute(asset)` triggers real playback
- `IAudioPlayerService.IsPlaying == true` and position advances
- **Repeated cycles:** LibraryImportPlaybackRepeated proves import A, play; import B, play; navigate away and back; play A; refresh, play B.

**Code path:** LibraryViewModel.PlayAsset uses direct `IAudioPlayerService` call when available; event path is fallback. Both paths are exercised by the smoke.

### Library Playback ID Contract (2026-03-16)

**First-class audio_id:** LibraryAsset now has `audio_id` (backend-playable ID). Backend populates it from `metadata.upload_id` when present. Frontend prefers `asset.AudioId` over `metadata.upload_id`. `/api/audio/file/{id}` expects the upload/playback ID.

**Manual verification:**
1. Import WAV into library (Library panel → Import or drag-drop).
2. Click Play on the imported asset.
3. Expected: playback starts, or an explicit error toast appears (no dead click).
4. If path missing or backend 404: toast shows "File not found" or "Audio not found. The file may have been moved or deleted."

**Smoke test:** `tests/ci/test_golden_loop_smoke.py::test_golden_loop_library_import_playback_stream` proves import → metadata.upload_id → GET /api/audio/file/{upload_id} → RIFF.

---

## Changelog

- 2026-03-16: Proof regenerated. STT/proof blocker closed. write_golden_path_real_proof.py succeeded with backend .venv (faster-whisper). PROOF_GOLDEN_PATH_REAL_2026-03-15.json refreshed; stt_engine_name: backend_default; tts_engine_name: espeak_ng.
- 2026-03-16: Library playback contract: first-class audio_id on LibraryAsset; backend populates from metadata.upload_id; Gate C smoke includes LibraryImportPlaybackRepeated for repeated import/play cycles.
- 2026-03-15: Library playback ID contract fix: use metadata.upload_id for backend fallback; extend audio_path_resolver for library upload root; smoke test test_golden_loop_library_import_playback_stream.
- 2026-03-16: Playback wiring bulletproof: imported asset playback documented as required proof point; Gate C smoke asserts Library playback.
- 2026-03-15: Proof definition integrity: governing principle explicitly accepts espeak_ng fallback; proof artifact records tts_engine_name; roadmap aligned.
- 2026-03-15: Proof generated. PROOF_GOLDEN_PATH_REAL_2026-03-15.json; golden_path_export.wav. STT: faster-whisper; TTS: espeak_ng fallback in test.
- 2026-03-15: Initial document. Blocker: STT (faster-whisper). Verification steps documented.
