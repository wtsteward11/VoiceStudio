# Durable Job Authority Inventory (2026-04-29)

This inventory catalogs all long-running / background work in VoiceStudio, their durability mechanisms, and risk classification.

## Durable Job Framework

The canonical job lifecycle is implemented in `backend/services/canonical_job_lifecycle.py` backed by `backend/data/repositories/job_repository.py` (SQLite `job_history`). Recovery on restart is handled by `backend/services/job_queue_recovery.py`.

## Inventory

| # | Domain | Route/Service | Execution Mechanism | Job ID | Progress Persistence | Cancellation | Failure Persistence | Restart Behavior | Simulation/Real | Risk | Remediation |
|---|--------|---------------|--------------------|----|---------------------|-------------|-------------------|-----------------|----------------|------|-------------|
| 1 | Training | `backend/services/training_service.py` `run_training` | `asyncio.create_task` via `start_training` | `training_{uuid}` | `PersistentStore` (in-memory dict + optional file) | `cancel_training()` sets status | In-memory dict + log | No restart recovery — `PersistentStore` is volatile | `SIMULATION_STATUS = "simulation_complete"` (explicit). **Now fails closed on ImportError** (Runtime Truth v1). | **P1** | Wire to canonical_job_lifecycle for SQLite persistence. |
| 2 | Voice Cloning Wizard | `backend/api/routes/voice_cloning_wizard.py` `process_wizard` | `asyncio.create_task(process_voice_cloning())` | `wizard_{uuid}` | `get_job_state_store` (file-backed) — **now persists on completion and failure** (Runtime Truth v1). | Not exposed | `failed` status + `error_message` in job dict + **disk persist** (Runtime Truth v1). | On restart: `processing` → `failed` with "Backend restarted during processing". | N/A (real cloning only) | **P1** (fixed: persistence + no placeholder metrics) | Persist on each status transition, not just terminal states. |
| 3 | Batch Generation | `backend/api/routes/batch.py` `_process_batch_job` | `asyncio.create_task` | Batch job ID from canonical lifecycle | Canonical job lifecycle (SQLite) | Cancellation via canonical lifecycle | `fail_job()` in canonical lifecycle | `job_queue_recovery.reconcile_job_history_after_restart` | N/A | **P2** | Already uses canonical lifecycle. Quality metrics now `None` instead of `{}` when unavailable (Runtime Truth v1). |
| 4 | Transcription | `backend/api/routes/transcribe.py` | `asyncio.create_task` | Route-level job tracking | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P1** | Wire to canonical_job_lifecycle. |
| 5 | Style Transfer | `backend/api/routes/style_transfer.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 6 | Multi-Voice Gen | `backend/api/routes/multi_voice_generator.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 7 | Ensemble | `backend/api/routes/ensemble.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 8 | Video Enhance | `backend/api/routes/video_enhance.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 9 | Audio Analysis | `backend/api/routes/audio_analysis.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 10 | Upscaling | `backend/api/routes/upscaling.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 11 | Mixer | `backend/api/routes/mixer.py` | `asyncio.create_task` | Route-level ID | In-memory dict | Not exposed | In-memory dict | No restart recovery | N/A | **P2** | Wire to canonical_job_lifecycle. |
| 12 | Model Download | `backend/services/model_download_service.py` | `asyncio.create_task` | Download ID | Service-level tracking | Not exposed | Service-level | No restart recovery | N/A | **P2** | Track downloads in canonical lifecycle. |
| 13 | Engine Startup | `backend/api/main.py` lifespan | `asyncio.create_task(on_startup_heavy)` | N/A (infrastructure) | N/A | Shutdown signal | Log only | Re-runs on next startup | N/A | **Low** | Infrastructure — acceptable as-is. |

## Summary

- **P0 fixes applied this cycle:** None (no P0 items identified).
- **P1 fixes applied this cycle:** Voice cloning wizard (#2): disk persistence on completion/failure, placeholder metrics removed.
- **P1 remaining:** Training (#1) needs canonical_job_lifecycle wiring. Transcription (#4) needs canonical_job_lifecycle.
- **P2 remaining:** Items #5–#12 need canonical_job_lifecycle wiring.
- **Infrastructure (acceptable):** Item #13 (engine startup).

## Non-claims

- This inventory covers Python backend routes/services only. C# frontend `Task.Run` patterns are inventoried by the async durability scanner but not remediated here.
- Restart recovery only applies to items using canonical_job_lifecycle (currently: batch generation #3).
