# GOV-VOICESTUDIO-GAP034-OS-NOTIFICATIONS-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP034-OS-NOTIFICATIONS-01 |
| **GAP** | GAP-034 |
| **Status** | Complete |
| **Phase** | 3 (Wiring / UX) |
| **Role** | UI Engineer |
| **Effort** | 16h |
| **Dependency** | — |
| **Created** | 2026-04-03 |

## §1 Objective (frozen)

Surface **Windows App Runtime** OS-level notifications for **terminal** batch, training, and timeline export operations (success and failure), via a **single** `ICompletionOsNotificationService` authority with **deduplication** by `(category, operationId, success)`. In-app `ToastNotificationService` behavior remains; OS notifications add background visibility when the app is not focused.

## §2 Hard IN

- **Notification authority:** `ICompletionOsNotificationService` + `CompletionOsNotificationService` in `src/VoiceStudio.App/Services/`; Windows implementation uses `Microsoft.Windows.AppNotifications` (`AppNotificationBuilder` / `AppNotificationManager.Default.Show`).
- **Message authority:** Operator-facing titles/bodies from `CompletionOsNotificationMessages` (single place); producers do **not** build ad hoc OS strings beyond safe interpolation (job name, file name, short error).
- **Producer authority (exactly three):**
  - **Batch:** `BatchProcessingViewModel` WebSocket `OnJobCompleted` / `OnJobFailed`; `operationId` = `update.JobId`.
  - **Training:** `TrainingViewModel` `OnTrainingJobCompleted` / `OnTrainingJobFailed`; `operationId` = `update.JobId`.
  - **Export:** `FileOperationsHandler.ExportAudioAsync`; `operationId` = per-invocation `Guid` (each export attempt is unique).
- **Dedupe authority:** `CompletionOsNotificationService` suppresses duplicate `(category, operationId, success)` notifications (in-memory set, process lifetime).
- **Failure isolation:** Dispatch failures are **logged** (`Debug.WriteLine`); **never** thrown into producer workflows.
- **Tests:** `CompletionOsNotificationServiceTests` — dedupe, distinct operations, test-only `Action<string,string>` presenter seam.
- **Runtime proof:** Manual note in lane closure §4 (success + failure paths, packaged vs unpackaged caveats).
- **Verification:** `dotnet build`, App.Tests, `pytest tests/ci`, `run_verification.py` (see §6).

## §3 Hard OUT

- No notification center redesign, no in-app notification inbox, no taskbar/jump-list work.
- No GAP-067 shell polish scope.
- No GAP-007 / PanelHost work.
- No new backend routes or job contracts.
- No coupling of OS notification text to sensitive payloads (tokens, full paths in title).

## §4 Authority map

| Concern | Owner |
|--------|--------|
| OS show / API failure handling | `CompletionOsNotificationService` |
| Title/body templates | `CompletionOsNotificationMessages` |
| When to fire | Batch / Training / Export handlers only |
| Dedupe key | `(CompletionOsNotificationCategory, operationId, success)` |
| Service resolution | `AppServices.TryGetCompletionOsNotificationService()` (lazy singleton) |

## §5 Acceptance criteria

- [x] Batch job WebSocket complete → **one** OS notification (success) per `JobId` (duplicate events suppressed).
- [x] Batch job WebSocket fail → **one** OS notification (failure) per `JobId` (deduped).
- [x] Training job complete → **one** OS notification per `JobId`; training fail → **one** failure notification per `JobId`.
- [x] Timeline export success → **one** OS notification per export invocation; export exception → **one** failure notification.
- [x] OS dispatch exception does not break job or export flow.
- [x] No duplicate message construction in ViewModels beyond calling the service with templates + safe fragments.
- [x] MSTest coverage for dedupe + presenter invocation.
- [x] Governance: execution row, closure report, tracker **GAP-034 Closed**, STATE + CANONICAL_REGISTRY synced.

## §6 Verification commands

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §7 Rollback

Revert order: `FileOperationsHandler` → `TrainingViewModel` → `BatchProcessingViewModel` → `AppServices` → `CompletionOsNotificationService` / interface → execution row + tracker (if rollback is post-close).
