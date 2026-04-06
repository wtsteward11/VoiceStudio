# GOV-VOICESTUDIO-GAP045-TRANSCRIPT-CROSS-CONSUMER-COHERENCE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_01`  
**Status:** **Closed** (2026-04-05) — bounded slice; product **GAP-045** remains **Open** for broader scope.  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md)  
**Depends on:** [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_01_EXECUTION_ROW.md) (**Closed**)  
**Closure:** [VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_GAP045_TRANSCRIPT_CROSS_CONSUMER_COHERENCE_CLOSURE_2026-04-05.md)

## Problem statement

After backend list **rehydrate** in the Transcribe panel, the Timeline subtitle overlay could retain **stale in-memory segment text** even when the operator had previously used **Send to Timeline** for the same transcription id. Consumers must converge on **backend read** truth without duplicate operator actions.

## Frozen architecture decisions

1. **Authority:** Segment text for the Timeline overlay continues to come from `ITimelineTranscriptionService.GetTranscriptionAsync` (backend read). Rehydrate updates the Transcribe list only; Timeline does not infer refreshed text from list DTOs alone.
2. **Coupling surface:** `NavigateToEvent` with `action = coherentReloadAfterRehydrate` and parameters `previousTranscriptionId`, `transcriptionId` (empty string when absent). No new backend routes.
3. **Activation guard:** Timeline applies the coherent reload only when `LoadedSubtitleTranscriptionId` is non-null and, if `previousTranscriptionId` is non-empty, matches that id — i.e. the overlay was tied to the pre-rehydrate selection. If the transcribe selection had no prior id, Timeline still refreshes when a subtitle track is loaded and the prior id parameter is empty (same-id refetch after list refresh).
4. **Clear path:** When the prior selection id was non-empty and the post-rehydrate `transcriptionId` is empty, Timeline **clears** the subtitle track (selection lost / removed).
5. **UX:** Coherent reload uses `LoadTranscriptSegmentsAsync(..., quietNotifications: true)` to avoid duplicate “Transcript Loaded” toasts.
6. **Export:** `TranscriptionExportFormatter` documents that callers must pass backend-authoritative `TranscriptionResponse`; export parity remains on the selected row after rehydrate (same as persistence/reload lanes).

## Acceptance contract (all required)

- [x] After successful `RunBackendTranscriptRehydrateAsync`, Transcribe publishes `coherentReloadAfterRehydrate` with prior and current selection ids.
- [x] Timeline tracks `LoadedSubtitleTranscriptionId` on successful load, `TranscriptionCompletedEvent`, and clears on `ClearTranscript`.
- [x] Navigate handler implements `coherentReloadAfterRehydrate` per guard rules above.
- [x] Seam tests cover loaded id, quiet refetch, and clear semantics (`TimelineViewModelGap045CrossConsumerTests`).
- [x] Inline edit harness tests serialize on `AppServices` to remove full-suite flake (`TranscribeViewModelInlineEditTests`).
- [x] Full closure verification matrix recorded in closure doc + STATE proof index + startup certification note.

## Hard OUT

- New ML / filler scope (**GAP-047**).
- Broad “sync every panel” bus; only Transcribe → Timeline subtitle coherence in this slice.

## Rollback

Revert `PublishTimelineCoherenceAfterRehydrate`, Timeline `coherentReloadAfterRehydrate` branch, `LoadedSubtitleTranscriptionId`, and `quietNotifications` overload; keep reload/rehydrate lane behavior intact.

## Changelog

- **2026-04-05:** Contract frozen; implemented; closed with matrix + startup truth certification pass.
