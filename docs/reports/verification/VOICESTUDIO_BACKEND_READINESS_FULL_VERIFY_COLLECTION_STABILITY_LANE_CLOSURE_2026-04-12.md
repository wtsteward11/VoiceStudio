# VOICESTUDIO — GAP-069 Slice 5 — Full Verification Python Collection Stabilization — Lane Closure

**Date:** 2026-04-12  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_FULL_VERIFY_COLLECTION_STABILITY_05_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_FULL_VERIFY_COLLECTION_STABILITY_05_EXECUTION_ROW.md)  
**Status:** Closed

## 1. Objective

Remove **collection-time** database/store dependencies from the global search route so `pytest` can collect `tests/unit/backend/api/routes/test_search.py` without a connected SQLite adapter.

## 2. Collection symptom (pre-fix)

- **Command:** `pytest tests/unit/backend/api/routes/test_search.py`
- **Failure:** During **collection**, import of `backend.api.routes.search` ran module-level `get_marker_store()`, `get_profiles_for_search()`, and `get_projects_for_search()`, which reached `DatabaseAdapter` before `connect()` → **`RuntimeError: Database not connected`**.

## 3. Root-cause import chain (exact)

1. `test_search.py` → `from backend.api.routes import search`
2. `search.py` (former lines 30–32) → `_markers = get_marker_store()`, `_profiles = get_profiles_for_search()`, `_projects = get_projects_for_search()` at **import time**
3. `get_projects_for_search()` → `get_project_store_service().list_projects()` → `run_isolated_async` → `ProjectRepository.list_all` → `fetch_all` on adapter with `_connected == False`

Same eager path for markers and profiles (scripts getter returned `{}` only).

## 4. Architectural defect

**Import-time side effects:** Route module eagerly materialized search indices when imported. Unit-test collection is not a runtime request; it must not require DB connectivity.

## 5. Boundary fix (not a band-aid)

- **`backend/api/routes/search.py`**
  - Replaced module-level eager assignments with **`None`** sentinels and **`STORAGE_AVAILABLE = False`**.
  - Added **`_load_search_storage()`**: double-checked locking (`threading.Lock`), assigns `_markers`/`_profiles`/`_projects`/`_scripts` **only after all four getters succeed** (atomic success; partial failure leaves globals unset and `STORAGE_AVAILABLE` false).
  - **`GET /api/search`** handler calls **`_load_search_storage()`** after query length validation and before the existing 503 guard on `STORAGE_AVAILABLE`.

No empty catches, no skipped tests, no weakening of search semantics.

## 6. Tests and harness fix

- **`tests/unit/backend/api/routes/test_search.py`**
  - **Bugfix:** `project_root` used **five** parents (stopped at `tests/`). Corrected to **six** parents to match `routes/conftest.py` (repo root). This was causing `ImportError` + module-level skip when `tests` was prepended to `sys.path` before the real root.
  - Added **`TestSearchCollectionSafety`** and **`TestSearchLazyLoader`** (lazy load, idempotence, error propagation).

## 7. Verification proof

| Step | Result |
|------|--------|
| `pytest tests/unit/backend/api/routes/test_search.py -v` | **10** PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `pytest tests/unit/test_backend_smoke_freshness_v4.py -q` | **6** PASS |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |
| `.\scripts\verify.ps1 -Quick` | PASS (`artifacts/verify/20260412_074629/`) |
| `.\scripts\verify.ps1` (full) | **Search collection blocker cleared** — Python unit stage ran **701+** tests, then **FAILED** on unrelated `test_transcribe_regenerate.py::TestRegenerateSegmentRoute::test_valid_request_returns_202_and_job_id` (`create_job` mock **awaited 0 times**). **Not introduced by this slice.** |

## 8. Umbrella GAP-069

**Remains Open** (continuous CI / verification-hardening track). Slice 5 closes only the **search route collection stability** lane.

## 9. Follow-up (outside slice 5)

- ~~Investigate **`test_transcribe_regenerate`** async mock expectations vs route implementation (full-suite failure).~~ **Addressed GAP-069 slice 6** — wrong `patch()` target (`jobs.create_job` vs `canonical_job_lifecycle.create_job`); see [VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md](VOICESTUDIO_BACKEND_READINESS_TRANSCRIBE_REGENERATE_FULL_VERIFY_LANE_CLOSURE_2026-04-12.md).
- Full **`verify.ps1`** green on all stages remains a broader goal (next Python unit blocker documented in slice 6 closure §7).
