# VOICESTUDIO — GAP-069 Slice 6 — Transcribe Regenerate Full-Verify Test Stabilization — Lane Closure

**Date:** 2026-04-12  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_06_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_06_EXECUTION_ROW.md)  
**Status:** Closed

## 1. Objective

Fix **stale mock patch target** in `tests/unit/backend/api/routes/test_transcribe_regenerate.py` so **`create_job`** is correctly mocked for the regenerate-segment route’s **function-body local import** of `create_job`.

## 2. Symptom (pre-fix)

- **Test:** `TestRegenerateSegmentRoute::test_valid_request_returns_202_and_job_id`
- **Failure:** `AssertionError: Expected mock to have been awaited once. Awaited 0 times.` on `create_job_mock.assert_awaited_once()`

## 3. Root cause (Outcome B — stale test contract)

**Production code is correct.** `start_regenerate_segment` in `backend/api/routes/transcribe.py` uses:

```python
from backend.services.canonical_job_lifecycle import create_job
```

inside the handler. The test patched **`backend.api.routes.jobs.create_job`**, which the handler never imports. The real `canonical_job_lifecycle.create_job` ran; the mock was never bound → **0 awaits**.

## 4. Correct patch target

Patch **`backend.services.canonical_job_lifecycle.create_job`** so the name resolved at import time inside the handler is the `AsyncMock`.

**Rule:** For **function-body** `from module import name`, patch **`module.name`**, not an unrelated re-export or a different route module.

## 5. Code change

**File:** `tests/unit/backend/api/routes/test_transcribe_regenerate.py`  

- **Before:** `patch("backend.api.routes.jobs.create_job", create_job_mock)`  
- **After:** `patch("backend.services.canonical_job_lifecycle.create_job", create_job_mock)`

No production route changes.

## 6. Verification proof

| Step | Result |
|------|--------|
| `pytest tests/unit/backend/api/routes/test_transcribe_regenerate.py -v` | **7** PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260412_081613/`) |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |
| `.\scripts\verify.ps1` (full) | **Does not fail on `test_transcribe_regenerate`.** Python Unit stage **FAILED** later on **`tests/unit/backend/services/test_sts_durable_marking.py::test_marking_endpoint_returns_not_transformed_for_plain_artifact`** (`assert True is False` on `is_transformed`). **Out of slice 6 scope.** |

## 7. Next blocker (outside slice 6)

- **`test_sts_durable_marking.test_marking_endpoint_returns_not_transformed_for_plain_artifact`** — marking endpoint returned `is_transformed=True` for a plain artifact; triage under a separate lane or GAP-069 follow-up.

Full **`verify.ps1` green** on all stages remains a broader **GAP-069** goal.

## 8. Umbrella GAP-069

**Remains Open** (continuous CI / full-verify hardening). Slice 6 closes only the **transcribe regenerate mock contract** lane.
