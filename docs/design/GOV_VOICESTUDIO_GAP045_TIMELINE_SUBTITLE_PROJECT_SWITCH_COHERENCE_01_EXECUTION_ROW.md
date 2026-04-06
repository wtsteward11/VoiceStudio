# GOV-VOICESTUDIO-GAP045-TIMELINE-SUBTITLE-PROJECT-SWITCH-COHERENCE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_COHERENCE_01`  
**Status:** **Closed** (2026-04-05) — bounded slice; product **GAP-045** remains **Open**.  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01_EXECUTION_ROW.md) (**Closed**)  
**Closure:** [VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP045_TIMELINE_SUBTITLE_PROJECT_SWITCH_CLOSURE_2026-04-05.md)

## Problem statement

The Timeline subtitle overlay could remain visible after the operator switches **SelectedProject** (or clears selection) while still showing transcript segments tied to the **previous** project’s backend read. That violates reopen / multi-project truth: overlay consumers must not display transcript authority from another project context.

## Frozen architecture decisions

1. **Authority:** Unchanged — segments still come from `ITimelineTranscriptionService.GetTranscriptionAsync`. This slice only **scopes** when the in-memory overlay is valid.
2. **Owner binding:** On successful `LoadTranscriptSegmentsAsync`, record `SelectedProject.Id` as `_subtitleOverlayOwnerProjectId` (internal).
3. **Clear on switch:** In `OnSelectedProjectChanged`, if the new project id differs from `_subtitleOverlayOwnerProjectId` while an overlay was bound, call `ClearTranscript()` before loading tracks for the new project.
4. **Clear on deselect:** If `SelectedProject` becomes null, call `ClearTranscript()` so no orphan overlay remains without a project shell.
5. **No new routes / no event bus redesign.**

## Acceptance contract (all required)

- [x] Project A → load transcript → switch to project B clears `LoadedSubtitleTranscriptionId`, segments, and `ShowTranscriptTrack`.
- [x] Project selected → load transcript → `SelectedProject = null` clears overlay.
- [x] `ClearTranscript()` clears owner id.
- [x] Seam tests in `TimelineViewModelGap045CrossConsumerTests`.
- [x] Closure matrix + governance sync (STATE / tracker / registry).

## Hard OUT

- **GAP-047** filler scope.
- Persisting “last subtitle id per project” in project JSON (optional future row).
- Broad panel-wide transcript sync bus.

## Rollback

Revert `_subtitleOverlayOwnerProjectId`, `OnSelectedProjectChanged` / `else` branch `ClearTranscript()` additions, and extended seam tests.

## Changelog

- **2026-04-05:** Frozen; implemented; closed with verification matrix.
