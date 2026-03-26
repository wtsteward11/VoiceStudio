# Wedge E2E Status (WG-01)

> **Purpose:** Document wedge-critical end-to-end checks and execution status.
> **Date:** 2026-03-14

---

## Wedge Flow

Target flow: **import → clean → clone → synthesize → export**

| Step | Test Coverage | Notes |
|------|---------------|-------|
| Import | `test_content_creator_workflow`, `test_core_workflow`, `test_golden_path` | `POST /api/library/assets/upload` |
| Clean | `test_golden_path` (step 2 transcribe) | Remove-artifacts not in content creator |
| Clone | `test_golden_path` (step 3) | `POST /api/voice/clone` |
| Synthesize | All | `POST /api/voice/synthesize` |
| Export | `test_content_creator_workflow`, `test_golden_path` (step 5) | `GET /api/audio/{id}/file` |

---

## Tests

| Test | Flow | Backend Required |
|------|------|------------------|
| `tests/e2e/test_golden_path.py` | import → transcribe → clone → synthesize → validate | Yes |
| `tests/e2e/test_content_creator_workflow.py` | import → profile → synthesize → export | Yes |
| `tests/e2e/test_core_workflow.py` | import → profile → synthesize → library → playback | Yes |

---

## How to Run

```powershell
# 1. Start backend (with stub mode for no-engine environments)
$env:VOICESTUDIO_TEST_MODE = "stub"
python -m backend.api.main

# 2. In another terminal, run wedge E2E
python -m pytest tests/e2e/test_content_creator_workflow.py tests/e2e/test_core_workflow.py -v
python -m pytest tests/e2e/test_golden_path.py -v
```

---

## Fix Applied (2026-03-14)

**Content creator workflow:** `profile_id: None` caused 422 validation error. Synthesis API requires `profile_id`. Added step to get or create a profile before synthesis (same pattern as `test_core_workflow`).

---

## Status

| Condition | Result |
|-----------|--------|
| Backend not running | Tests skip (backend_health fixture) |
| Backend + no engines, no VOICESTUDIO_TEST_MODE | 503 Service Unavailable |
| Backend + VOICESTUDIO_TEST_MODE=stub | Stub synthesis returns minimal WAV |
| Backend + real engines |

Proven: Wedge E2E tests exist and execute. Profile fix applied. Full pass requires backend with engines or stub mode.

---

## Last Run (2026-03-14)

| Test | Result | Notes |
|------|--------|------|
| test_step1_import_audio | PASSED | |
| test_step2_transcribe_audio | PASSED | |
| test_step3_clone_voice | PASSED | |
| test_step4_synthesize_speech | FAILED | voice_profile_id None (test order / data persistence) |
| test_step5_validate_output | FAILED | Depends on step 4 |
| test_import_profile_synthesize_playback | SKIPPED | Engines check |
| test_import_apply_preset_synthesize_export | FAILED | Synthesis 503 (backend needs VOICESTUDIO_TEST_MODE=stub) |

**Summary:** 3 passed, 1 skipped, 3 failed. Backend was running without stub mode; synthesis returns 503 when engines unavailable. To get full pass: start backend with `VOICESTUDIO_TEST_MODE=stub` before running tests.
