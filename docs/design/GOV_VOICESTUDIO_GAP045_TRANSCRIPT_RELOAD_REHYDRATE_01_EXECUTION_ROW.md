# GOV-VOICESTUDIO-GAP045-TRANSCRIPT-RELOAD-REHYDRATE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_01`  
**Status:** **Closed** (2026-04-05) — bounded slice; product **GAP-045** remains **Open** for broader scope.  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md) (**Closed**)  
**Closure:** [VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_LANE_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_LANE_CLOSURE_2026-04-05.md)

## Problem statement

Persisted transcript rows (backend authority after `PUT /api/transcribe/{id}`) must **rehydrate** into the Transcribe panel whenever **project + audio** scope is active—especially after **project switch**, **panel refresh/activation**, and **app session** continuity—without leaving stale in-memory DTOs as the source of truth.

## Frozen architecture decisions

1. **Authority:** Backend transcript rows from `GET` list/read paths are the only source for rehydrated UI lists. No parallel “cached transcript document” overrides the list results for the active `(projectId, audioId)` scope.
2. **API surface:** **No new backend routes** for this lane; use existing `ListTranscriptionsAsync(audioId, projectId)` (and per-id reads only where already used elsewhere).
3. **Coalescing:** Rapid scope changes cancel the prior in-flight list request (`CancellationTokenSource` per schedule, `lock` for lifecycle).
4. **Diagnostics:** If the previously selected transcription id is **not** returned in the new list, set operator-visible `TranscriptOperatorMessage` (no silent keep of stale `SelectedTranscription`).
5. **Export parity:** TXT/SRT export continues to use `TranscriptionExportFormatter` on the **current** `TranscriptionResponse` after rehydrate (same as persistence lane).

## Acceptance contract (all required)

- [x] When `SelectedProjectId` and `SelectedAudioId` are both non-empty, the VM schedules a backend list fetch and replaces `Transcriptions` from the result (order: `Created` descending, same as manual load).
- [x] Rehydrate runs on: project id change, audio id change, `InitializeAsync`, `OnActivatedAsync`, `RefreshAsync` (in addition to explicit **Load transcriptions**).
- [x] Selection: if prior `SelectedTranscription.Id` exists in the new list, reselect it; otherwise select newest row and emit diagnostic text explaining missing id.
- [x] Segment boundaries/order come from backend DTOs as returned (no client-side reorder beyond existing display rules).
- [x] Export after rehydrate reflects backend text/segments (formatter unit path covered via seam test on rehydrated DTO).
- [x] Seam-aware automated tests prove list invocation + diagnostic path.
- [x] Full closure verification matrix recorded in closure doc + STATE proof index.

## Hard OUT (unchanged)

- New ML filler models (**GAP-047**).
- Transcript document redesign.
- New backend routes unless a proven gap in list/read contract (none identified for this slice).

## Rollback

Revert `TranscribeViewModel` rehydrate scheduling + shared list application; restore `StartupReadinessTimeoutSeconds` only if separate ADR/rollback requires; keep persistence lane behavior intact.

## Changelog

- **2026-04-05:** Contract frozen; lane implemented and closed.
