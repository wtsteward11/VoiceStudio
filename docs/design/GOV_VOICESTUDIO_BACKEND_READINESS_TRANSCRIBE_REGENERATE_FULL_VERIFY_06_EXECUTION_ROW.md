# GOV-VOICESTUDIO-BACKEND-READINESS-TRANSCRIBE-REGENERATE-FULL-VERIFY-06 — Execution Row (GAP-069 Slice 6)

**Status:** Closed (2026-04-12) — [closure](../reports/verification/VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md)  
**Lane:** GAP-069 — Full verify transcribe regenerate stabilization  
**Date:** 2026-04-12

## Problem statement

Full `.\scripts\verify.ps1` fails during **Python Unit Tests** at:

`tests/unit/backend/api/routes/test_transcribe_regenerate.py::TestRegenerateSegmentRoute::test_valid_request_returns_202_and_job_id`

**Failure message:** `AssertionError: Expected mock to have been awaited once. Awaited 0 times.` (on `create_job_mock.assert_awaited_once()`)

## Root cause (architectural classification)

**Outcome B — stale test contract.** Production route `start_regenerate_segment` in `backend/api/routes/transcribe.py` uses a **function-body local import**:

`from backend.services.canonical_job_lifecycle import create_job`

The unit test patched `backend.api.routes.jobs.create_job`, which the handler **never** imports or calls. The mock was never invoked; the real `canonical_job_lifecycle.create_job` ran.

## Intended contract

- Valid `POST /api/transcribe/regenerate-segment` returns **202** with `job_id` (and response model fields as defined).
- Success path **awaits** `create_job` from **`backend.services.canonical_job_lifecycle`** once, then schedules `run_transcript_segment_regeneration_job`.

## Acceptance criteria

1. `pytest tests/unit/backend/api/routes/test_transcribe_regenerate.py` — **all tests PASS** (7).
2. Patch target for `create_job` in the happy-path test: **`backend.services.canonical_job_lifecycle.create_job`**.
3. `python scripts/check_empty_catches.py` — PASS  
4. `python scripts/ci/check_ibackendclient_creep.py` — PASS  
5. `.\scripts\verify.ps1 -Quick` — PASS  
6. `.\scripts\verify.ps1` — no longer fails at `test_transcribe_regenerate` for this reason  
7. `python scripts/run_verification.py` — PASS (including `completion_guard` where applicable)

## Hard IN scope

- Correct patch namespace in `test_transcribe_regenerate.py`  
- Targeted verification + governance closure  

## Hard OUT scope

- Transcribe feature expansion  
- Batch regeneration / generic job queue refactors  
- Unrelated route or pytest cleanup  
- UI changes  

## Related closure artifact

- `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md` (created at slice closure)

## Umbrella note

**GAP-069** may remain **Open** after slice 6 if other continuous verification items exist.
