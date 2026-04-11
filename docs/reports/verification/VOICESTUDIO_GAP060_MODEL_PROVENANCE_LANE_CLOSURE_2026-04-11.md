# Lane Closure: GOV-VOICESTUDIO-GAP060-MODEL-PROVENANCE-01

**Gap:** GAP-060 — Model version provenance on outputs (bounded STS slice)  
**Status:** CLOSED  
**Date:** 2026-04-11  
**Predecessor:** GAP-059 (trust audit trail) — correlation via `artifact_id` + `correlation_id`

---

## Summary

Introduced **`ModelProvenanceService`** as the single authority for structured **`metadata_json.model_provenance`** on STS success outputs. Records include `engine_id`, `engine_version`, `model_name`, `model_family` (from engine manifest when available), `artifact_id`, `correlation_id` (joins with `TrustAuditEvent`), `is_transformed`, `transformation_type`, `recorded_at`. Persistence uses **`AudioRegistryDB.update_metadata`** (merge into existing registry metadata). **Best-effort:** failures log a warning and do not fail conversion.

**Out of scope (honored):** no changes to `artifact_provenance.py` / file sidecars; no query API; no synthesis-path wiring beyond STS; no historical backfill.

**Test harness:** `test_audio_trust_audit.py` `client` fixture patches **`RateLimitMiddleware.dispatch`** to passthrough — TestClient uses non-loopback host and was hitting **429** after heavy prior modules in the same process (flaky regression).

---

## Proof matrix (recorded)

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings) |
| `pytest tests/unit/backend/services/test_model_provenance_service.py` | 9 PASS |
| Regression: `test_trust_audit_service.py` + `test_audio_trust_audit.py` + `test_audio_auth.py` | 23 PASS |
| `pytest tests/unit/backend/services/test_speech_to_speech_service.py` | 10 PASS |
| Full `VoiceStudio.App.Tests` | 3338 PASS / 274 skipped |
| `check_ibackendclient_creep.py` | PASS |
| `check_empty_catches.py` | PASS |
| `verify.ps1 -Quick` | PASS `artifacts/verify/20260410_215817/` |
| `run_verification.py` | PASS after governance commit (**completion_guard** requires committed closure markers) |

---

## Key files

| Path | Role |
|------|------|
| `backend/services/model_provenance_service.py` | `ModelProvenanceRecord`, `ModelProvenanceService`, `get_model_provenance_service()` |
| `backend/services/audio_artifacts/registry_db.py` | `AudioRegistryDB.update_metadata` |
| `backend/services/speech_to_speech_service.py` | STS success path: `build` + `attach` after artifact creation |
| `tests/unit/backend/services/test_model_provenance_service.py` | 9 tests |
| `tests/unit/backend/services/test_speech_to_speech_service.py` | Autouse mock for model provenance + trust audit |
| `tests/unit/backend/api/routes/test_audio_trust_audit.py` | Rate-limit bypass in `client` fixture |
| `docs/design/GOV_VOICESTUDIO_GAP060_MODEL_PROVENANCE_01_EXECUTION_ROW.md` | Execution row (CLOSED) |

---

## Rollback

Remove `model_provenance_service.py`; remove `update_metadata` from `registry_db.py`; revert STS provenance block; delete new tests; revert `test_audio_trust_audit` fixture if undesired.
