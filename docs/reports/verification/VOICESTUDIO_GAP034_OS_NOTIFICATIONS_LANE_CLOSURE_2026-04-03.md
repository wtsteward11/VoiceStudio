# VoiceStudio GAP-034 OS-level completion notifications — 2026-04-03

**Lane:** **GOV-VOICESTUDIO-GAP034-OS-NOTIFICATIONS-01** — Windows App Runtime notifications for **terminal** batch, training, and timeline export outcomes (success and failure), single `ICompletionOsNotificationService` authority, in-process dedupe `(category, operationId, success)`. In-app toasts unchanged.

**Execution row:** [GOV_VOICESTUDIO_GAP034_OS_NOTIFICATIONS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP034_OS_NOTIFICATIONS_01_EXECUTION_ROW.md)

**Tracker:** **GAP-034** **Closed** — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).

**Product:** **GAP-045** / **GAP-047** remain **Open** per tracker.

## 0) Verification provenance

**Label:** **Independently repo-verified locally** — full matrix below executed on a developer machine (Windows, repo toolchain).

## 1) Scope summary

- **`VoiceStudio.Core`:** `ICompletionOsNotificationService`, `CompletionOsNotificationCategory` (Batch / Training / Export).
- **`CompletionOsNotificationService`:** `Microsoft.Windows.AppNotifications` show path; optional test `Action<string,string>` presenter; dedupe `HashSet`; dispatch failures logged, not rethrown.
- **`CompletionOsNotificationMessages` + `Shorten`:** canonical titles; body truncation for safe operator copy.
- **`AppServices.TryGetCompletionOsNotificationService()`:** lazy singleton.
- **Producers:**
  - `BatchProcessingViewModel` — WebSocket `OnJobCompleted` / `OnJobFailed`; `operationId` = `update.JobId`.
  - `TrainingViewModel` — `OnTrainingJobCompleted` / `OnTrainingJobFailed`; `operationId` = `update.JobId`.
  - `FileOperationsHandler.ExportAudioAsync` — per-invocation `Guid`; terminal failure paths (no audio, no timeline use case, no backend, exception) each emit at most one failure notification for that attempt.
- **Tests:** `CompletionOsNotificationServiceTests` (dedupe, distinct success/failure same id, empty id skip, presenter exception swallowed).
- **No** new backend routes or shared schema changes.

## 2) Verification matrix (closure run)

| Command | Result (closure run) |
|--------|----------------------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors; pre-existing warnings in repo) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3033** passed, **274** skipped, **0** failed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed, **2** deselected |
| `.\scripts\verify.ps1 -Quick` | PASS — report `artifacts/verify/20260403_072122/verification_report.md` |
| `python scripts/run_verification.py` | PASS — **9/9** gates; `.buildlogs/verification/last_run.json` **timestamp_short** **20260403-072930** (**completion_guard** PASS) |

## 3) Proof artifacts (code + docs)

- [GOV_VOICESTUDIO_GAP034_OS_NOTIFICATIONS_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP034_OS_NOTIFICATIONS_01_EXECUTION_ROW.md)
- `src/VoiceStudio.Core/Services/ICompletionOsNotificationService.cs`
- `src/VoiceStudio.App/Services/CompletionOsNotificationService.cs`
- `src/VoiceStudio.App/Services/CompletionOsNotificationMessages.cs`
- `src/VoiceStudio.App/Services/AppServices.cs`
- `src/VoiceStudio.App/Views/Panels/BatchProcessingViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs`
- `src/VoiceStudio.App/Commands/FileOperationsHandler.cs`
- `src/VoiceStudio.App.Tests/Services/CompletionOsNotificationServiceTests.cs`
- `.cursor/STATE.md`, `docs/governance/CANONICAL_REGISTRY.md`, `docs/design/PROFESSIONAL_GAP_TRACKER.md`

## 4) Runtime proof (honest limits)

- **In proof:** Automated build, full App.Tests, CI pytest slice, `run_verification.py`, `verify.ps1 -Quick`.
- **Operator display addendum:** [VOICESTUDIO_GAP034_OS_NOTIFICATIONS_RUNTIME_ADDENDUM_2026-04-03.md](./VOICESTUDIO_GAP034_OS_NOTIFICATIONS_RUNTIME_ADDENDUM_2026-04-03.md) — packaged vs unpackaged, success/failure spot-check checklist, privacy reminder.
- **Not in proof (operator class):** Packaged MSIX vs unpackaged dev — Windows may suppress or queue notifications differently; focus/quiet hours may hide banners; **manual spot-check** recommended once: run a batch job to completion, run training completion, run **File → Export Audio** success and a forced failure (e.g. export with backend stopped) and confirm **one** OS toast per terminal path plus existing in-app toast.
- **Privacy:** Bodies use job names, file **names** (not full paths), and truncated error text — no API tokens in notification strings.

## 5) Closure

**GOV-VOICESTUDIO-GAP034-OS-NOTIFICATIONS-01:** **Closed** 2026-04-03 with proof-backed acceptance per execution row and this report.

**Next:** See [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md) — next prominent Phase 4 open row **GAP-037** (waveform editing); **GAP-067** shell polish remains **Open** and may now treat **GAP-034** dependency as satisfied for notification-center scope only (lane is bounded; no inbox/taskbar work shipped here).
