# GOV-VOICESTUDIO-GAP030-BATCH-QUALITY-DASHBOARD-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP030-BATCH-QUALITY-DASHBOARD-01 |
| **GAP** | GAP-030 |
| **Status** | Complete |
| **Phase** | 3 (Wiring) |
| **Role** | UI Engineer |
| **Effort** | 12h |
| **Dependency** | GAP-002 (Closed) |
| **Created** | 2026-04-02 |

## §1 Objective (frozen)

Wire batch job completion quality metrics into the quality history service so the Quality Dashboard panel reflects batch outcomes. On the frontend, `BatchProcessingViewModel` publishes `JobCompletedEvent` on WebSocket completion; `QualityDashboardViewModel` subscribes to batch-type events and refreshes.

## §2 Hard IN

- **Backend:** `_process_batch_job` calls `quality_history_service.store_entry()` with `QualityHistoryEntry` when `quality_metrics` is truthy and `quality_score is not None`
- **Frontend (publish):** `BatchProcessingViewModel` publishes `JobCompletedEvent` on WebSocket `JobCompleted` / `JobFailed`
- **Frontend (subscribe):** `QualityDashboardViewModel` subscribes to `JobCompletedEvent` (batch type, success only) and triggers `LoadOverviewAsync`
- **Pytest:** batch → quality history bridge test (store on success, skip on empty metrics, correct fields)
- **MSTest:** dashboard refresh on batch event; batch VM publishes event; no refresh on non-batch or failed events
- **Verification matrix:** `dotnet build`, App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py`

## §3 Hard OUT

- No new FastAPI routes (use existing internal `store_entry` call)
- No persistent quality history (remains in-memory per existing architecture)
- No batch panel redesign
- No new shared schema / contract changes
- No GAP-007 / PanelHost work
- No live WinUI gesture certification

## §4 Field mapping

| QualityHistoryEntry field | Source from batch completion |
|--|--|
| `id` | `str(uuid.uuid4())` |
| `profile_id` | `job_data["voice_profile_id"]` |
| `project_id` | `job_data["project_id"]` |
| `timestamp` | `datetime.now(timezone.utc).isoformat()` |
| `engine` | `job_data["engine_id"]` |
| `metrics` | `quality_metrics` dict |
| `quality_score` | `quality_score` (float; skip entry if None) |
| `synthesis_text` | `job_data["text"]` |
| `audio_url` | None |
| `enhanced_quality` | `job_data.get("enhance_quality", False)` |
| `metadata` | `{"source": "batch", "job_id": job_id, "batch_name": ..., "result_audio_id": ..., "quality_status": ...}` |

## §5 Acceptance criteria

- [x] Backend: batch job with quality metrics → entry appears in `quality_history_service.get_entries(profile_id)` — **Done.** `_store_batch_quality_history` in `batch.py`
- [x] Backend: batch job without quality metrics → no entry stored — **Done.** Fail-closed guard: `if quality_metrics and quality_score is not None`
- [x] Frontend: WebSocket batch completion → `JobCompletedEvent` published via `IEventAggregator` — **Done.** `OnJobCompleted`/`OnJobFailed` in `BatchProcessingViewModel`
- [x] Frontend: `QualityDashboardViewModel` refreshes overview on batch `JobCompletedEvent` (success only) — **Done.** `OnJobCompleted` handler + `InitializeAsync` subscription
- [x] Frontend: non-batch or failed events do not trigger dashboard refresh — **Done.** Guard: `e.Success && e.JobType == "batch"`
- [x] Tests: pytest batch quality bridge ≥ 3 cases — **Done.** 6 pytest cases (test_batch_quality_bridge.py)
- [x] Tests: MSTest dashboard + batch VM ≥ 4 cases — **Done.** 8 MSTest cases (QualityDashboardGap030Tests + BatchProcessingGap030Tests)
- [x] Verification: `dotnet build` 0 errors, App.Tests 3024 passed (baseline 3016), `pytest tests/ci` 217 passed, `run_verification.py` 9/9 PASS — **2026-04-03**

## §6 Verification commands

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/ci/ -q --randomly-seed=12345
python -m pytest tests/unit/backend/api/routes/test_batch_quality_bridge.py -q
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```
