# GOV-VOICESTUDIO-BACKEND-READINESS-PROFILE-STORE-FULL-VERIFY-08 — Execution Row (GAP-069 Slice 8)

**Status:** Closed (2026-04-13) — [closure](../reports/verification/VOICESTUDIO_BACKEND_READINESS_PROFILE_STORE_FULL_VERIFY_LANE_CLOSURE_2026-04-13.md)  
**Lane:** GAP-069 — Profile store / TrackStore full-verify stabilization  
**Date:** 2026-04-12 (opened); closed 2026-04-13

## Problem statement

Full `.\scripts\verify.ps1` fails during **Python Unit Tests** at:

`tests/unit/backend/services/test_profile_store.py::TestTrackStore::test_get_track`

**Failure message:** `RuntimeError: Database not connected`

## Root cause (architectural classification)

**Outcome B — stale / incomplete test harness.** Production `TrackStore` is SQLite-backed and uses the process-wide `get_database_adapter()` singleton; callers must ensure migrations have run and `await db.connect()` has succeeded before `save_track` / `get_track` / `list_tracks` / `delete_track`. `TestTrackStore` in `test_profile_store.py` only created `TrackStore(projects_dir=temp_dir)` and never initialized the adapter — unlike the canonical harness in `tests/unit/backend/test_track_store.py`.

## Intended contract

- **`TrackStore` does not own DB connection lifecycle.** It calls `get_database_adapter()` inside `run_isolated_async` coroutines; `DatabaseAdapter.execute` / `fetch_*` require `_connected == True`.
- **Tests** that exercise persistence must: reset adapter singleton, run `run_migrations` for an isolated SQLite path, `get_database_adapter(dbp)`, `await db.connect()`, then construct `TrackStore`; teardown must `close_database_adapter`, `reset_database_adapter_singleton`, and `reset_track_store()`.

## Acceptance criteria

1. `pytest tests/unit/backend/services/test_profile_store.py::TestTrackStore::test_get_track` — **PASS** (isolated and full suite).
2. All `TestTrackStore` tests that touch SQLite — **PASS** with explicit per-method DB setup (no order dependence on other modules).
3. Regression: **disconnected** singleton → `save_track` raises `RuntimeError` matching `Database not connected`.
4. Regression: **connected** store → save → get → delete → get `None` (round-trip contract).
5. `python scripts/check_empty_catches.py` — PASS
6. `python scripts/ci/check_ibackendclient_creep.py` — PASS
7. `.\scripts\verify.ps1 -Quick` — PASS
8. Full `.\scripts\verify.ps1` — **no longer fails** at `TestTrackStore::test_get_track`; document any **next** downstream failure honestly.
9. `python scripts/run_verification.py` — PASS (including `completion_guard` where applicable)

## Hard IN scope

- `tests/unit/backend/services/test_profile_store.py` — `TestTrackStore` harness + regression tests
- Governance closure artifacts for this lane

## Hard OUT scope

- Production changes to `TrackStore` unless investigation proves a real boundary bug (not expected)
- Unrelated pytest or route refactors

## Related closure artifact

- `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_PROFILE_STORE_FULL_VERIFY_LANE_CLOSURE_2026-04-13.md`

## Umbrella note

**GAP-069** may remain **Open** after slice 8 if full end-to-end `verify.ps1` still fails on a subsequent stage.
