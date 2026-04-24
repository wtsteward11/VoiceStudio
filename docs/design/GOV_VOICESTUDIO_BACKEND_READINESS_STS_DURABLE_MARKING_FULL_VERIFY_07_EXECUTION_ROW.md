# GOV-VOICESTUDIO-BACKEND-READINESS-STS-DURABLE-MARKING-FULL-VERIFY-07 — Execution Row (GAP-069 Slice 7)

**Status:** Closed (2026-04-12) — [closure](../reports/verification/VOICESTUDIO_BACKEND_READINESS_STS_DURABLE_MARKING_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md)  
**Lane:** GAP-069 — STS durable marking full-verify stabilization  
**Date:** 2026-04-12

## Problem statement

Full `.\scripts\verify.ps1` fails during **Python Unit Tests** at:

`tests/unit/backend/services/test_sts_durable_marking.py::test_marking_endpoint_returns_not_transformed_for_plain_artifact`

**Failure message:** `assert True is False` on `r.json()["is_transformed"]` (expected `False`, got `True`).

## Root cause (architectural classification)

**Outcome B — stale test contract + isolation gap.** `GET /api/audio/{audio_id}/marking` (`get_audio_marking` in `backend/api/routes/audio.py`) awaits `get_trust_audit_service().record_marking_read(...)` (GAP-059). The marking unit tests mock `get_registry` but did not mock `get_trust_audit_service`. In the full suite, real singletons and async audit paths interact with `TestClient` in ways that can destabilize the test; additionally, `audio_id="plain"` collides with other tests that use the same id with `is_transformed=True`, risking on-disk registry contamination if mocks do not apply.

Production classification is correct: `is_transformed=bool(meta.get("is_transformed", False))` — presence of `source` alone does not imply transformation.

## Intended contract

- **`is_transformed` on `StsMarkingStatus`:** `True` only when registry `metadata_json` contains a truthy top-level `is_transformed` (e.g. STS output via `create_audio_artifact_from_file(..., is_transformed=True, ...)`).
- **`source_reference_id`:** from `metadata["source"]` (optional lineage reference); **not** a synonym for transformed.
- **Watermark fields:** `watermark_applied` / `watermark_method` / `watermark_verified` are orthogonal to `is_transformed` unless `is_transformed` is also set in metadata.

## Acceptance criteria

1. `pytest tests/unit/backend/services/test_sts_durable_marking.py` — **all tests PASS**.
2. `test_marking_endpoint_returns_not_transformed_for_plain_artifact` mocks **`backend.services.trust_audit_service.get_trust_audit_service`** with **`AsyncMock`** for `record_marking_read`; uses a **non-colliding** `audio_id` (not `"plain"`).
3. `test_marking_endpoint_returns_transformed_status` uses the same trust-audit mock pattern.
4. Regression tests added: source-only metadata; watermark-only metadata; canonical top-level `is_transformed` vs nested `model_provenance`.
5. `python scripts/check_empty_catches.py` — PASS
6. `python scripts/ci/check_ibackendclient_creep.py` — PASS
7. `.\scripts\verify.ps1 -Quick` — PASS
8. Full `.\scripts\verify.ps1` — **no longer fails** at `test_sts_durable_marking` for this reason; document any **next** downstream failure honestly.
9. `python scripts/run_verification.py` — PASS (including `completion_guard` where applicable)

## Hard IN scope

- Test isolation fixes and regression coverage for marking endpoint semantics
- Targeted verification + governance closure

## Hard OUT scope

- Production changes to `TrustAuditService` or marking handler logic (unless investigation proves a real bug)
- Unrelated route or pytest cleanup
- UI changes

## Related closure artifact

- `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_STS_DURABLE_MARKING_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md` (created at slice closure)

## Umbrella note

**GAP-069** may remain **Open** after slice 7 if full end-to-end `verify.ps1` still fails on a subsequent stage.
