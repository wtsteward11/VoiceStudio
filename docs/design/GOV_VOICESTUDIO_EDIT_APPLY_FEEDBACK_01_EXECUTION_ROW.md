# GOV-VOICESTUDIO-EDIT-APPLY-FEEDBACK-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_EDIT_APPLY_FEEDBACK_01`  
**Status:** **Closed** 2026-03-31 — bounded lane under product **GAP-045**; [closure](../reports/verification/VOICESTUDIO_EDIT_APPLY_FEEDBACK_LANE_CLOSURE_2026-03-31.md).  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md) — product row remains **Open** when this lane closes.  
**Depends on:** [GOV_VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_01_EXECUTION_ROW.md) (**Closed**)

## Frozen architecture decisions

1. **Post-apply text sync:** Local ViewModel update only (no new `PUT /transcribe` client seam). Replace `Segments` list entry + `TranscriptSegmentLayoutRevision` / `ItemsRepeater` rebind.
2. **Busy state:** `RegeneratingSegmentId`; `ApplyEditedSegmentCommand` false while set.
3. **Regenerated markers:** Session `HashSet`; cleared on transcription / project change and successful truth refresh.
4. **Keyboard:** `F2` / `Enter` on segment row; flyout `Ctrl+Enter` apply; `Escape` cancel.

## Hard IN

- [x] Post-apply segment text matches applied draft in the segment list (local sync).
- [x] In-progress indicator for the segment undergoing regeneration.
- [x] Session indicator for segments regenerated this session (accent vs linked highlight).
- [x] Keyboard shortcuts documented in Transcribe help + wired in code-behind.
- [x] MSTest coverage for sync, busy, tracking, guard.
- [x] Full verification matrix + governance closure.

## Hard OUT

- Backend `PUT /api/transcribe/{id}` persistence seam; multi-segment editing; `INotifyPropertyChanged` on `TranscriptionSegment`; persistent regen badges across save/load; transcript panel redesign; waveform/subtitle scope.

## Binary acceptance (closure gate)

- [x] Success path updates displayed segment text without requiring list reload.
- [x] Busy state during coordinator execution; cleared on success and failure.
- [x] Session markers cleared on transcription / project / truth-refresh boundaries per contract.
- [x] Closure report + tracker + registry + STATE + proof index agree; product **GAP-045** stays **Open**.

## Proof

- [VOICESTUDIO_EDIT_APPLY_FEEDBACK_LANE_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_EDIT_APPLY_FEEDBACK_LANE_CLOSURE_2026-03-31.md)

## Rollback

Revert `TranscribeViewModel` / `TranscribeView` feedback UX; keep inline edit/apply + truth lanes intact.
