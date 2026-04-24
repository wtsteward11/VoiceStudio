# VOICESTUDIO — GAP-069 Slice 7 — STS Durable Marking Full-Verify Stabilization — Lane Closure

**Date:** 2026-04-12  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_STS_DURABLE_MARKING_FULL_VERIFY_07_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_STS_DURABLE_MARKING_FULL_VERIFY_07_EXECUTION_ROW.md)  
**Status:** Closed

## 1. Objective

Stabilize **`GET /api/audio/{audio_id}/marking`** unit tests for full-suite **`verify.ps1`** by fixing test isolation after **GAP-059** added `await get_trust_audit_service().record_marking_read(...)`, and by eliminating **`audio_id`** collision with other tests / dev registry rows.

## 2. Symptom (pre-fix)

- **Test:** `tests/unit/backend/services/test_sts_durable_marking.py::test_marking_endpoint_returns_not_transformed_for_plain_artifact`
- **Failure:** `assert True is False` on `r.json()["is_transformed"]` (expected `False`, observed `True` in full Python unit stage).

## 3. Root cause (Outcome B — stale test contract + isolation gap)

**Production `get_audio_marking` is correct:** `is_transformed=bool(meta.get("is_transformed", False))` from registry top-level metadata.

The marking tests mocked **`get_registry`** but not **`get_trust_audit_service`**. After GAP-059, every marking response awaits **`record_marking_read`** on the real **`TrustAuditService`** singleton. Combined with **`audio_id="plain"`** (also used elsewhere with `is_transformed=True` in fixtures), full-suite runs could observe **`is_transformed=True`** when the mock path did not fully isolate from real registry / audit behavior.

## 4. Fix

**File:** `tests/unit/backend/services/test_sts_durable_marking.py`

1. Patch **`backend.services.trust_audit_service.get_trust_audit_service`** and set **`record_marking_read = AsyncMock()`** on the returned service mock for all marking endpoint tests that hit **`get_audio_marking`**.
2. Rename the negative test artifact / URL id from **`plain`** to **`plain-no-transform`** to avoid id collision with **`test_sts_sample_watermark.py`** and on-disk **`audio_registry.db`** rows.
3. Add regression tests:
   - **`test_marking_endpoint_source_field_alone_does_not_imply_transformed`**
   - **`test_marking_endpoint_watermark_alone_does_not_imply_transformed`** (patches **`_verify_watermark_on_artifact`** to avoid filesystem coupling)
   - **`test_marking_endpoint_is_transformed_derived_from_canonical_metadata_only`**

**Canonical `is_transformed` for marking:** `True` iff registry **`metadata_json`** has a truthy top-level **`is_transformed`** (STS / store path). **`source`** and watermark flags alone do not imply transformation.

No production route or **`TrustAuditService`** code changes.

## 5. Verification proof

| Step | Result |
|------|--------|
| `pytest tests/unit/backend/services/test_sts_durable_marking.py -v` | **9** PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260412_183432/`) |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |
| `.\scripts\verify.ps1` (full) | **Does not fail on `test_sts_durable_marking`.** Python Unit stage **FAILED** later on **`tests/unit/backend/services/test_profile_store.py::TestTrackStore::test_get_track`** (`RuntimeError: Database not connected`). **Out of slice 7 scope.** |

## 6. Next blocker (outside slice 7)

- **`test_profile_store.py::TestTrackStore::test_get_track`** — `TrackStore.save_track` → `run_isolated_async` → `Database not connected` on `db.execute`. Triage as a separate GAP-069 lane or platform test fix.

Full **`verify.ps1` green** end-to-end remains a broader **GAP-069** goal.

## 7. Umbrella GAP-069

**Remains Open** until full **`verify.ps1`** exits 0 on all stages. Slice 7 closes only the **STS durable marking test isolation + regression coverage** lane.
