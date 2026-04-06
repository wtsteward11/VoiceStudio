# VOICESTUDIO — GAP-047 Persist Failure After Clip Apply Recovery — Lane Closure (2026-04-06)

**Execution row:** [GOV_VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP047_PERSIST_FAILURE_AFTER_CLIP_APPLY_RECOVERY_01_EXECUTION_ROW.md) **Closed**  
**Product:** **GAP-047** remains **Open** (bounded slice only).

## 1. Goal

Atomic failure when `UpdateTranscriptionTextAsync` throws after successful `UpdateClipAsync`: compensate clip on backend, skip in-memory partial apply, linkage removal, success events, and undo registration.

## 2. Runtime delta

- [TranscriptSegmentRegenerationCoordinator.cs](../../../src/VoiceStudio.App/Services/TranscriptSegmentRegenerationCoordinator.cs): on non-null persistence message, `UpdateClipAsync` restores `prevAudioId` / `prevUrl` / `prevDur`; `ReportJobProgress` `apply_failed`; return (with appended message if rollback throws).

## 3. Tests

- `TranscriptSegmentRegenerationCoordinatorTests`: `Apply_WithTranscriptPersistFailure_RestoresPreApplyClipState`, undo/events/contract/range/double-failure coverage.
- `TranscribeViewModelInlineEditTests`: `InstallHarness(..., transcriptPersistFails)`; `Apply_WithTranscriptPersistFailure_DoesNotCorruptUndoStack`, `Apply_WithTranscriptPersistFailure_DoesNotLeaveTimelineOverlayStale`.
- `TimelineViewModelGap045CrossConsumerTests`: `FailedApply_DoesNotTriggerTimelineCoherenceReload`.
- `TranscribeViewModelSeamTests`: `FailedApply_Rehydrate_UsesAuthoritativeBackendTruth`.

## 4. Verification matrix (this closure)

| Step | Command / artifact | Result |
|------|-------------------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| App.Tests | `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3135** passed / **274** skipped |
| Pytest CI | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **217** passed (**2** deselected) |
| XAML | `python scripts/validate_xaml_resources.py` | PASS |
| Quick | `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260406_155153/` |
| Rolling | `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **20260406-155717** (**completion_guard** PASS) |
| OnlyStage (UI Self-Test, Icon-Launch, Failure-Path, Runtime-Missing) | Stages 23–26 in Quick harness | **SKIPPED** (not executed in this Quick run; documented honestly) |

## 5. Honest limits

- Compensation assumes backend `UpdateClipAsync` can restore prior audio; if rollback fails, operator message includes both persistence and rollback errors.

## 6. Rollback

Revert lane-scoped commit; re-open execution row if post-closure.
