# VoiceStudio GAP-028 Training → Profile Metadata Refresh Lane Closure — 2026-04-01

**Lane:** GOV-VOICESTUDIO-GAP028-TRAINING-PROFILE-METADATA-REFRESH-01 — training completion (polling + WebSocket) publishes **`ProfileCreatedEvent`** + **`ProfileUpdatedEvent`** with per-job dedup; **Profiles** subscribes to **`ProfileUpdatedEvent`** and reloads the list for fresh backend metadata.  
**Execution row:** [GOV_VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_01_EXECUTION_ROW.md) **Closed**.  
**Tracker:** **GAP-028** **Closed** — [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).  
**Product:** **GAP-045** remains **Open**.

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — matrix shared with GAP-026 closure session (same machine run).

## 1) Scope summary

- **`TrainingViewModel`:** `TryPublishPollingTrainingCompletion` after polling updates; `OnTrainingJobCompleted` uses `_lastPublishedCompletedTrainingJobId` to avoid double publish when polling already fired. `PublishTrainingCompletedProfileEvents` emits both event types with `training_completed` / `training_job_id` in `ProfileUpdatedEvent.ChangedProperties`.
- **`ProfilesViewModel`:** `_profileUpdatedToken` + `OnProfileUpdatedRefresh` → `LoadProfilesAsync` + reselect by `ProfileId` when source is not self.
- **GAP-024:** Documented as ordering preference only — completion signals do not depend on simulation UX polish.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in other files) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3009** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260401_232510/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **completion_guard** in `.buildlogs/verification/last_run.json` (`timestamp_short` **20260401-233116**) |

## 3) Proof artifacts (code + tests)

- `src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs` — polling + WebSocket publish path, dedup field, seam helpers.
- `src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs` — `ProfileUpdatedEvent` subscription and refresh handler.
- `src/VoiceStudio.App.Tests/ViewModels/TrainingViewModelSeamTests.cs` — GAP-028 seam tests (`Gap028_*`).
- `src/VoiceStudio.App.Tests/ViewModels/ProfilesViewModelSeamTests.cs` — `ProfileUpdatedEvent_FromTraining_TriggersSecondListLoad`.

## 4) Honest limits

- **Event payload** does not carry full quality scores; refresh is **list reload** from API.
- **GAP-045** remains Open per tracker.

## 5) Closure

**GOV-VOICESTUDIO-GAP028-TRAINING-PROFILE-METADATA-REFRESH-01:** **Closed** 2026-04-01 with proof-backed acceptance per execution row.

**Prior hero-path:** [GAP-026](../../design/GOV_VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_01_EXECUTION_ROW.md) — [closure](VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_LANE_CLOSURE_2026-04-01.md).
