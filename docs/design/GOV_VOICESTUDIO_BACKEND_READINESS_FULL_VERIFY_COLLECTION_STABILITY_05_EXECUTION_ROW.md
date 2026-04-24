# GOV-VOICESTUDIO-BACKEND-READINESS-FULL-VERIFY-COLLECTION-STABILITY-05 — Execution Row (GAP-069 Slice 5)

**Status:** Closed (2026-04-12) — [closure](../reports/verification/VOICESTUDIO_BACKEND_READINESS_FULL_VERIFY_COLLECTION_STABILITY_LANE_CLOSURE_2026-04-12.md)  
**Lane:** GAP-069 — Full verification Python collection stabilization  
**Date:** 2026-04-12

## Problem statement

Full `.\scripts\verify.ps1` (non-Quick) fails during **Python unit test collection** because importing `tests/unit/backend/api/routes/test_search.py` triggers import of `backend.api.routes.search`, which **eagerly** called `get_marker_store()`, `get_profiles_for_search()`, and `get_projects_for_search()` at **module load time**. Those paths reach SQLite via `ProjectStoreService.list_projects()` → `run_isolated_async` → repository → `DatabaseAdapter.fetch_all` while **`_connected` is false**, producing:

`RuntimeError: Database not connected`

## Failing command (pre-fix)

```powershell
pytest tests/unit/backend/api/routes/test_search.py
```

(Collection fails before any test runs.)

## Target module / tests

- **Route:** `backend/api/routes/search.py` (module-level lines 30–33)
- **Tests:** `tests/unit/backend/api/routes/test_search.py` (module-level `from backend.api.routes import search`)

## Root cause (architectural)

**Import-time side effects:** search data stores were materialized when the route module was imported, coupling pytest collection to a live, connected database. Unit-test collection must not require DB connectivity.

## Acceptance criteria

1. `pytest tests/unit/backend/api/routes/test_search.py` completes collection and all tests pass.
2. No database connectivity is required at import/collection time; storage loads on first route request via `_load_search_storage()` (thread-safe lazy init).
3. Full `.\scripts\verify.ps1` is not blocked by this collection failure.
4. `python scripts/check_empty_catches.py` — PASS  
5. `python scripts/ci/check_ibackendclient_creep.py` — PASS  
6. `.\scripts\verify.ps1 -Quick` — PASS  
7. `python scripts/run_verification.py` — PASS (including `completion_guard` where applicable)

## Hard IN scope

- Root-cause trace and documentation in closure report  
- Lazy / request-time initialization for search route storage globals  
- Targeted regression tests (collection safety + lazy loader behavior)  
- Full verify re-run and governance closure  

## Hard OUT scope

- Search feature expansion or ranking changes  
- Broad database layer refactor  
- Unrelated pytest or route cleanups  
- UI / WinUI / shell work  

## Related closure artifact

- `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_FULL_VERIFY_COLLECTION_STABILITY_LANE_CLOSURE_2026-04-12.md` (created at slice closure)

## Umbrella note

**GAP-069** may remain **Open** after slice 5 if other continuous verification items exist; slice 5 closes only this collection-stability lane.
