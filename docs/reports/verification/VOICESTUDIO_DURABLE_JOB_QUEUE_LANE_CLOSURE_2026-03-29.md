# VOICESTUDIO — Durable Job Queue lane closure (2026-03-29)

**Lane:** `GOV-VOICESTUDIO-DURABLE-JOB-QUEUE-01`  
**Execution row:** [GOV_VOICESTUDIO_DURABLE_JOB_QUEUE_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_DURABLE_JOB_QUEUE_01_EXECUTION_ROW.md)  
**Gap:** GAP-019 **Closed**

## 1. Acceptance summary

| Criterion | Evidence |
|-----------|----------|
| Canonical SQLite `job_history` aligned with `JobEntity` | Migration `v004_job_history_columns.py`; `JobRepository._entity_to_dict` includes `name`, `current_step_index`, `result_id`, `estimated_time_remaining`. |
| `/api/jobs` remains authority surface | Existing routes + helpers; cache invalidation on mutations. |
| Restart reconciliation | `backend/services/job_queue_recovery.py` + `lifecycle.py` after migrations. |
| Batch adapter | `batch.py` registers and updates canonical rows; rollback on failed `create_job`. |
| Tests | `tests/unit/backend/services/test_job_queue_recovery.py` (in-memory recovery + temp SQLite schema). |

## 2. Verification commands (recorded at closure)

```powershell
python -m pytest tests/unit/backend/services/test_job_queue_recovery.py -q
python -m pytest tests/unit/backend/api/routes/test_jobs.py -q
python -m pytest tests/ci/ -q --randomly-seed=12345
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## 3. Risks / follow-ups

- **Training / export:** Still use domain-specific stores; adapters to `job_history` are future work (documented in execution row).
- **InMemoryJobRepository:** Reconciliation skipped when DB unavailable (by design).

## 4. Rollback

Revert lane commits: remove v004 registration, recovery hook, batch canonical calls, and migration file; restore prior `batch.py` and `job_repository._entity_to_dict` if needed.
