# GOV-VOICESTUDIO-GAP045-LAST-SUBTITLE-PER-PROJECT-RESTORE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO-GAP045-LAST-SUBTITLE-PER-PROJECT-RESTORE-01`  
**Status:** **Closed** (2026-04-05) — bounded slice; product **GAP-045** remains **Open**.  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_COHERENCE_01_EXECUTION_ROW.md) (**Closed**)  
**Closure:** [VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_LANE_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP045_LAST_SUBTITLE_PER_PROJECT_RESTORE_LANE_CLOSURE_2026-04-05.md)

## Problem statement

After reopening the app or returning to a project, the Timeline subtitle overlay does not restore the last backend-authoritative transcription the user had loaded for that project. In-session rehydrate already restores `SelectedTranscription` when `previousSelectionId` is in memory; cross-session restore requires persisting the last subtitle transcription id **per project** in local project JSON and feeding it into the existing rehydrate / `coherentReloadAfterRehydrate` path.

## Frozen architecture decisions

1. **Storage authority:** `IProjectRepository` / `JsonProjectRepository` — add nullable `LastSubtitleTranscriptionId` on [`Project`](../../src/VoiceStudio.Core/Models/Project.cs) (camelCase JSON). No new backend route.
2. **Write trigger:** On successful `TimelineViewModel.LoadTranscriptSegmentsAsync`, persist id for `SelectedProject.Id` (fire-and-forget with logging; same `CancellationToken` as load where practical).
3. **Clear semantics:** explicit user `ClearTranscript` clears persisted restore id for the active (or overlay-owner) project; automatic clears (project switch / null selection / coherent reload clear) do **not** wipe persisted restore state.
4. **Read trigger:** `TranscribeViewModel.RunBackendTranscriptRehydrateAsync`: if `SelectedTranscription` is null, read stored id for `SelectedProjectId` and use as `previousSelectionId` for `ApplyTranscriptionListFromBackend`.
5. **Timeline coherence:** `PublishTimelineCoherenceAfterRehydrate` continues to receive **in-memory** previous id only (not the repository-augmented id). Extend `coherentReloadAfterRehydrate` so when `_loadedSubtitleTranscriptionId` is empty and `transcriptionId` (`cur`) is non-empty, load segments (cold-session restore).
6. **List membership = validity:** If stored id is absent from backend list, fail closed (first row / existing behavior) and set operator diagnostic when restore was attempted from project file.
7. **Validation authority:** Backend `ListTranscriptionsAsync` result is the only validity source for restore.

## Acceptance contract (all required)

- [x] Project JSON carries `lastSubtitleTranscriptionId` when a subtitle load succeeds; omitted when null.
- [x] Reopen / rehydrate with no in-memory selection restores `SelectedTranscription` from stored id when still in backend list.
- [x] In-memory selection is not overridden by stored id.
- [x] Missing stored transcription → operator message for restore path; coherent first row selection.
- [x] `coherentReloadAfterRehydrate` loads timeline overlay when timeline was empty and `cur` is set (cross-session).
- [x] Project-switch / null-project guard behavior unchanged (`_subtitleOverlayOwnerProjectId` lane).
- [x] Seam tests: `TranscribeViewModelLastSubtitleRestoreTests` + extended `TimelineViewModelGap045CrossConsumerTests`.
- [x] Closure matrix + governance sync (STATE / tracker / registry / proof index).

## Hard OUT

- Broad transcript event bus or cross-panel redesign.
- Cross-project subtitle carryover.
- New backend routes or SQLite schema for this field (JSON project file is sufficient).
- Changing export authority contract beyond existing `TranscriptionResponse` usage.

## Rollback

Revert `Project.LastSubtitleTranscriptionId`, `IProjectRepository` new methods, `JsonProjectRepository` implementation, `TimelineViewModel` persist/clear paths, `TranscribeViewModel` rehydrate + `ApplyTranscriptionListFromBackend` signature, `coherentReloadAfterRehydrate` empty-overlay branch, and new/extended tests.

## Changelog

- **2026-04-05:** Row frozen and implemented.
- **2026-04-05:** Closed with matrix: App.Tests **3093** / skipped **274**; `pytest tests/ci` **217** (**2** deselected); Quick `artifacts/verify/20260405_211745/`; rolling `.buildlogs/verification/last_run.json` **20260405-212821** (**completion_guard** PASS); OnlyStage `20260405_212256` / `212308` / `212322` / `212344`.
