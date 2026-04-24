# GOV-VOICESTUDIO-BACKEND-READINESS-PROFILE-STORE-FULL-VERIFY-08 — Lane closure (GAP-069 Slice 8)

**Date:** 2026-04-13  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_PROFILE_STORE_FULL_VERIFY_08_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_PROFILE_STORE_FULL_VERIFY_08_EXECUTION_ROW.md) (**Closed**)

## 1. Objective

Stabilize **`tests/unit/backend/services/test_profile_store.py::TestTrackStore::test_get_track`** (and sibling DB-touching `TestTrackStore` tests) for full **`verify.ps1`** Python unit coverage by aligning the test harness with the **`DatabaseAdapter` singleton + migrations + `connect()`** contract used by canonical **`tests/unit/backend/test_track_store.py`**.

## 2. Symptom

**`RuntimeError: Database not connected`** when calling `TrackStore.save_track` / `get_track` without a connected global adapter.

## 3. Root cause (classification)

**Outcome B — stale / incomplete test harness.** `TrackStore` uses `get_database_adapter()` and `run_isolated_async`; it does not open SQLite by itself. `TestTrackStore` constructed only `TrackStore(projects_dir=...)` and never ran **`run_migrations`** + **`await db.connect()`** on the singleton.

## 4. Fix (summary)

- Added **`setup_method` / `teardown_method`** to **`TestTrackStore`**: `close_database_adapter` → `reset_database_adapter_singleton` → `reset_track_store` → `run_migrations` (isolated temp SQLite) → `get_database_adapter(dbp)` → `await db.connect()`; teardown resets singletons and **`asyncio.set_event_loop(asyncio.new_event_loop())`** (matches **`test_track_store.py`**, avoids autouse fixture / `asyncio.Lock()` issues without a current loop).
- Removed the class **`temp_dir`** fixture; tests use **`self._temp_db_dir`**.
- **Regression:** **`test_disconnected_adapter_save_raises`** — disconnected singleton → `save_track` raises **`Database not connected`**.
- **Regression:** **`test_connected_store_round_trips`** — save → get → delete → miss.

## 5. Files changed

| File | Role |
|------|------|
| `tests/unit/backend/services/test_profile_store.py` | TrackStore harness + regressions |
| `docs/design/GOV_VOICESTUDIO_BACKEND_READINESS_PROFILE_STORE_FULL_VERIFY_08_EXECUTION_ROW.md` | Frozen row (Closed) |

## 6. Verification proof

| Command | Result |
|---------|--------|
| `python -m pytest tests/unit/backend/services/test_profile_store.py -v` | **23** PASS |
| `python -m pytest tests/unit/backend/services/test_profile_store.py -v -k TestTrackStore` | **8** PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260412_190316/` |
| `.\scripts\verify.ps1 -OnlyStage "Python Unit Tests"` | Pytest completed per log — **`5432` passed**, **`TestTrackStore::test_get_track` PASSED** (`artifacts/verify/20260412_232527/logs/python_unit_tests.log`, line ~4616). Harness `summary.json` was not observed (runner time limit after pytest finished). |
| `.\scripts\verify.ps1 -OnlyStage "Gate/Ledger Validation"` | PASS — `artifacts/verify/20260413_090140/` |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |

### Canonical contract (explicit)

Before **`TrackStore.save_track` / `get_track` / `list_tracks` / `delete_track`**, the process-wide **`DatabaseAdapter`** from **`get_database_adapter(...)`** must be **connected** after **idempotent migrations** for that SQLite path. **`TrackStore` does not connect the DB.**

## 7. Full `verify.ps1` (non-Quick, all stages)

Not re-certified to **final `summary.json` / full report** in this session (long-running harness; tool time limits). **Python Unit Tests** content is green for this lane in **`20260412_232527`** log (and **`20260412_205904`** / **`20260412_190823`** full-suite Python logs show **`5432` passed** with **`test_get_track` PASSED**). **Re-run full `.\scripts\verify.ps1` locally** to capture the next downstream stage if any.

## 8. Umbrella

**GAP-069** remains **Open** until full end-to-end **`verify.ps1`** is green or the next blocker is recorded with proof.

## 9. Related

- Canonical pattern: `tests/unit/backend/test_track_store.py` `setup_method` / `teardown_method`
- Production: `backend/project/tracks/track_store.py`, `backend/infrastructure/adapters/database.py`
