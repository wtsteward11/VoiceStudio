# GOV-VOICESTUDIO-MULTI-SEGMENT-EDIT-APPLY-01 — Contiguous multi-segment transcript edit/apply (GAP-045)

## 0. Status

- **State:** **Closed** (2026-04-01)
- **Product scope:** **GAP-045** (text-based audio editing) — **Open**; this lane is a bounded sub-lane only.
- **Depends on:** Inline single-segment edit/apply + regenerate segment + transcript truth lanes **Closed**.
- **Closure:** [VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_MULTI_SEGMENT_EDIT_APPLY_LANE_CLOSURE_2026-04-01.md)

## Changelog

- **2026-04-01:** Lane closed — VM range validation + `TranscribeView` Shift+click; `TranscribeViewModelInlineEditTests` range cases; test fixtures aligned to `BackendClientConfig.DefaultHttpBaseUrl`.

## 1. Objective

Enable a **contiguous** multi-segment transcript range (same clip, same project) to be edited as one replacement string, applied via the **existing** single-segment regeneration job (`replacement_text`), with honest blocked behavior for illegal ranges and one undo transaction consistent with `TranscriptClipAudioReplaceUndoAction`.

## 2. Allowed range (frozen)

- **Contiguous** segment indices in the **display order** of `SelectedTranscription.Segments`.
- All segments in the range must resolve via `ITranscriptSegmentTargetResolver` to the **same** `ClipId`.
- **Same project** as the active timeline project.

## 3. Blocked cases (frozen)

- Range spans **different** timeline clips → fail closed with operator message.
- Any segment in the range **unlinked** or **ambiguous** resolution → fail closed (reuse resolver reasons).
- **Non-contiguous** selection (e.g. cherry-picked indices) — **out of scope**: only inclusive index ranges between two endpoints.
- **Replacement text** empty after trim → reject (same as single-segment apply).

## 4. Apply semantics (frozen)

- **One** `RegenerateSegmentStartRequest` anchored on the **first** segment in the range (`SegmentId` = first segment id).
- `replacement_text` = full operator draft for the **entire** range (single string).
- `TranscriptEditIntentService.TryRecordIntent`: `ReplaceRange` with `segmentStartSeconds` = first segment start, `segmentEndSeconds` = last segment end (same as single-segment intent geometry extended to span).
- Clip/audio apply and linkage removal follow **existing** `TranscriptSegmentRegenerationCoordinator.TryExecuteAsync` (no second apply path).
- Transcript truth: existing **StaleAfterClipRegeneration** + reconciliation messaging.

## 5. Post-apply display (frozen)

- **First** segment in range receives full **trimmed** draft text.
- **Other** segments in range receive **empty** display text until a future transcript refresh (bounded honesty; avoids fake word-level split of one synthesis blob).

## 6. Undo (frozen)

- **One** coordinator success → **one** existing `TranscriptClipAudioReplaceUndoAction` (no per-subsegment undo stack).
- Cancel / failure: clear range edit buffer; no mutation.

## 7. UI (frozen)

- **Shift+click** second segment after **first click** sets anchor on first segment for range (`TranscribeView` code-behind).
- Reuse segment edit flyout; hint text documents range behavior.

## 8. Hard OUT

- Full document / Descript-class editor.
- Non-contiguous multi-select batches.
- Subtitle authoring, diarization rewrite, waveform editing.
- New backend routes or job types (reuse `replacement_text` path only).
- Changing backend `transcript_segment_regeneration` contract beyond single anchor `segment_id`.

## 9. Verification

- MSTest: legal same-clip range, cross-clip blocked, empty draft rejected, coordinator invoked with anchor + replacement text.
- `dotnet build`, `pytest tests/ci/`, `verify.ps1 -Quick`, `run_verification.py` before closure.
- Closure report + tracker + registry + STATE when **Closed**.

