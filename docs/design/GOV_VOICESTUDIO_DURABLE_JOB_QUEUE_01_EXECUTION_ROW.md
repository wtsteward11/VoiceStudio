# GOV-VOICESTUDIO-DURABLE-JOB-QUEUE-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-DURABLE-JOB-QUEUE-01`  
**Status:** Closed (2026-03-29)  
**Tracker:** **GAP-019 — Closed** (this lane)  
**Closure:** [VOICESTUDIO_DURABLE_JOB_QUEUE_LANE_CLOSURE_2026-03-29.md](../reports/verification/VOICESTUDIO_DURABLE_JOB_QUEUE_LANE_CLOSURE_2026-03-29.md)

## Frozen objective

**Single canonical durable job queue** for cross-surface job truth: SQLite table `job_history` accessed through `JobRepository` and HTTP surface `/api/jobs`. Survives restarts; **running** and **paused** rows that survive a crash are reconciled to **failed** with an explicit `RECOVERY_BACKEND_RESTART` message on backend startup. Batch synthesis jobs **register and update** canonical rows via the shared helpers in `backend/api/routes/jobs.py`.

## Hard IN

- Canonical store: `job_history` + `JobRepository` + `/api/jobs` routes.
- Migration **v004** adds columns required by `JobEntity`: `name`, `current_step_index`, `result_id`, `estimated_time_remaining`.
- Startup reconciliation: `reconcile_job_history_after_restart` in `backend/services/job_queue_recovery.py`, invoked from `backend/api/lifecycle.py` after `MigrationRunner` succeeds (real `JobRepository` only).
- Batch adapter: `backend/api/routes/batch.py` calls `create_job`, `mark_job_running`, `update_job_progress`, `complete_job`, `fail_job`, `cancel_canonical_job`, `soft_delete_canonical_job` where applicable; secondary `PersistentStore` / `JobStateStore` remain for batch-specific replay, not as a second authority for `/api/jobs`.
- Cache: `invalidate_api_response_cache()` on canonical mutations that affect listed job endpoints (helpers and route mutators in `jobs.py`).

## Hard OUT (unchanged from plan)

- Autosave (GAP-020), export redesign (GAP-029), transcript, waveform, metering, collaboration, marketplace, telemetry expansion.
- Redis/Celery or multi-process queue without ADR.

---

## Authority map (frozen)

| Writer / domain | Role vs `job_history` | Notes |
|-----------------|------------------------|-------|
| `/api/jobs` + `JobRepository` | **Canonical** | All UI job progress surfaces should read this API. |
| `jobs.py` helpers (`create_job`, `update_job_progress`, `complete_job`, `fail_job`, …) | **Canonical write API** | Preferred entry for producers. |
| `batch.py` | **Adapter** | Registers batch jobs and mirrors lifecycle into canonical store; keeps `PersistentStore("batch_jobs")` for batch route semantics. |
| `training_jobs` table + training routes | **Separate domain** | Future: adapter into `job_history` when training is integrated (not this lane). |
| `JobStateStore` / `PersistentStore("batch_jobs")` | **Secondary / batch-local** | Not authoritative for `/api/jobs`. |
| Enhanced job queue / orchestrator in-memory | **Ephemeral** | Must not contradict SQLite truth for user-visible job status. |

**Inventory command (honesty):**

```bash
rg "create_job|mark_job_running|fail_job|complete_job|get_job_repository|job_history" backend -g "*.py"
```

---

## Lifecycle semantics

| Status | Meaning |
|--------|---------|
| `pending` | Accepted; not started. |
| `running` | Work in progress. |
| `paused` | Paused (API); on restart → **failed** with recovery message (resume not restored). |
| `completed` / `failed` / `cancelled` | Terminal. |

**Restart policy:** Any non-deleted job in `running` or `paused` is marked `failed` with a stable `RECOVERY_BACKEND_RESTART` prefix so logs and UI show honest state. `pending` jobs remain **pending** (durable queue).

---

## Verification

- `python -m pytest tests/unit/backend/services/test_job_queue_recovery.py -q`
- `python -m pytest tests/unit/backend/api/routes/test_jobs.py -q`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py`

---

## Changelog

| Date | Change |
|------|--------|
| 2026-03-29 | Lane implemented: v004 migration, recovery service, batch adapter, tests, closure. |
