# GOV-VOICESTUDIO-INLINE-TRANSCRIPT-EDIT-APPLY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_01`  
**Status:** **Closed** 2026-03-31 — bounded lane under product **GAP-045** (text-based audio editing); [closure](../reports/verification/VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_LANE_CLOSURE_2026-03-31.md).  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md) — product row remains **Open** when this lane closes.  
**Depends on:** [GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_REGENERATE_SEGMENT_01_EXECUTION_ROW.md) (**Closed**), [GOV_VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIPT_TRUTH_RECONCILIATION_01_EXECUTION_ROW.md) (**Closed**), [GOV_VOICESTUDIO_TEXT_EDITING_FOUNDATION_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TEXT_EDITING_FOUNDATION_01_EXECUTION_ROW.md) (**Closed**)

## Frozen architecture decisions

1. **Buffered edit model:** Pending segment text lives in `TranscribeViewModel` until explicit Apply. No canonical transcript or project mutation until Apply succeeds.
2. **UI surface:** Flyout with `TextBox` + Apply/Cancel (segment double-tap or context menu “Edit segment text…”).
3. **Apply path:** Existing `TranscriptSegmentRegenerationCoordinator` with `replacementText` → `POST /api/transcribe/regenerate-segment` (`replacement_text`). No new backend route.
4. **Undo:** Existing `TranscriptClipAudioReplaceUndoAction` + transcript truth semantics (Option B after regen).
5. **Intent:** `TranscriptEditIntentKind.ReplaceRange` is **executable**; `TryRecordIntent` may carry optional `replacementText` for linkage with intent diagnostics.

## Hard IN

- [x] Single-segment edit buffer (segment id, original text, draft text).
- [x] Explicit Apply / Cancel; dirty indicator in operator messaging when draft differs from original.
- [x] Apply calls regenerate with non-null `replacement_text` when draft is non-empty and differs from stored segment text (or always pass draft as replacement when operator edited — coordinator/backend validate empty).
- [x] Failure: no clip mutation; preserve edit buffer for retry.
- [x] Success: clear edit buffer; existing stale transcript truth + refresh path applies.
- [x] MSTest for VM edit state + intent ReplaceRange executable.
- [x] Full verification matrix + governance closure.

## Hard OUT

- Multi-segment / batch edit, Descript-class document editor, waveform editing, subtitle system, transcript JSON rewrite before regen, autosave of draft text.

## Binary acceptance (closure gate)

- [x] Flyout edit + Apply routes `replacementText` through coordinator (proof via tests + manual smoke optional).
- [x] Cancel clears buffer without HTTP/regen.
- [x] `ReplaceRange` intent no longer blocked; `ReplacementText` on `TranscriptEditIntent` when recorded with draft.
- [x] Closure report + tracker + registry + STATE + proof index agree; product **GAP-045** stays **Open** unless tracker says otherwise.

## Proof

- [VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_LANE_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_INLINE_TRANSCRIPT_EDIT_APPLY_LANE_CLOSURE_2026-03-31.md)

## Rollback

Revert Transcribe panel flyout + VM edit buffer; revert `TranscriptEditIntent` / `ITranscriptEditIntentService` / `TranscriptEditIntentService` ReplaceRange changes; keep regen + truth lanes intact.
