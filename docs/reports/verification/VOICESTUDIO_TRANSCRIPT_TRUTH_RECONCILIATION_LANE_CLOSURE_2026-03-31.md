# VoiceStudio — Transcript Truth Reconciliation lane closure

**Date:** 2026-03-31  
**Execution row:** [GOV_VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_01_EXECUTION_ROW.md)  
**Policy:** GAP-045 Option B (explicit stale state + operator-triggered canonical refresh)

## Scope

- **Model:** `TranscriptTruthState` enum + `AudioClip.TranscriptTruth` (persisted in project JSON).
- **Orchestration:** `TranscriptTruthRefreshCoordinator`, `ITranscriptTruthRefreshCoordinator`, DI + `AppServices.TryGetTranscriptTruthRefreshCoordinator`.
- **Regen integration:** `TranscriptSegmentRegenerationCoordinator` sets stale + publishes `TranscriptTruthStateChangedEvent`; `TranscriptClipAudioReplaceUndoAction` syncs truth on undo/redo + events.
- **UX:** `TranscribeView` InfoBar + `RefreshTranscriptTruthCommand`; `TranscribeViewModel` hint resolution (single stale clip per `SelectedAudioId`); `TimelineViewModel` toasts on `TranscriptTruthStateChangedEvent`.
- **Events:** `TranscriptTruthStateChangedEvent` in `PanelEvents.cs`.

## Non-goals (per execution row)

- New backend transcribe route (reuses existing `ITranscriptionClient` / `/api/transcribe`).
- Auto refresh, batch regen, multi-segment editor.

## Verification matrix (this lane)

| Step | Command | Result |
|------|---------|--------|
| Build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| Targeted tests | `TranscriptTruthRefreshCoordinatorTests`, `TranscriptSegmentRegenerationCoordinatorTests.TryExecuteAsync_Success`, `ClipTranscriptLinkRoundTripTests.SaveAsync_roundTrip_preserves_clipTranscriptTruth_gap045` | PASS |
| CI gate | `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (216 passed, 2 deselected) |
| Quick gate | `.\scripts\verify.ps1 -Quick` | PASS — report `artifacts/verify/20260331_063239/verification_report.md` |
| Ledger | `python scripts/run_verification.py` (no `--skip-guard`) | PASS — **completion_guard** included; JSON `.buildlogs/verification/last_run.json` (`timestamp_short` **20260331-063650**) |

*Note:* `verify.ps1 -Quick` invokes `run_verification.py` with `--skip-guard` for latency. For closure, **completion_guard** is asserted by a separate `python scripts/run_verification.py` run after Quick (see JSON above).

## Touched files (primary)

- `src/VoiceStudio.Core/Transcription/TranscriptTruthState.cs`, `Models/AudioClip.cs`, `Events/PanelEvents.cs`
- `src/VoiceStudio.App/Services/TranscriptTruthRefreshCoordinator.cs`, `TranscriptSegmentRegenerationCoordinator.cs`, `AppServices.cs`
- `src/VoiceStudio.App/Core/Services/ITranscriptTruthRefreshCoordinator.cs`
- `src/VoiceStudio.App/Services/UndoableActions/TranscriptClipAudioReplaceUndoAction.cs`
- `src/VoiceStudio.App/Views/Panels/TranscribeViewModel.cs`, `TranscribeView.xaml`, `TimelineViewModel.cs`
- Tests: `TranscriptTruthRefreshCoordinatorTests.cs`, `TranscriptSegmentRegenerationCoordinatorTests.cs`, `ClipTranscriptLinkRoundTripTests.cs`
- Docs: this file, execution row, `CANONICAL_REGISTRY.md`, `PROFESSIONAL_GAP_TRACKER.md`, `.cursor/STATE.md`

## Risks

1. **Ambiguous audio id:** Multiple stale clips with same `AudioId` — Transcribe panel refuses refresh until resolved (fail-closed).
2. **Enum JSON:** Stored as numeric; older app versions ignore unknown fields — new field defaults to `Current` on read.

## Rollback

See execution row § Rollback.
