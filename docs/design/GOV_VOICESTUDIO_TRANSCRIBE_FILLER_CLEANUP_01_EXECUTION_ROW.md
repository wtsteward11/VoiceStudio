# GOV-VOICESTUDIO-TRANSCRIBE-FILLER-CLEANUP-01 — Transcribe-first draft filler cleanup (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-01)
- **Product scope:** **GAP-047** (filler word detection + removal) — **Open** overall; this lane is a **bounded transcribe-first slice** only. **GAP-045** remains **Open**.
- **Depends on:** **GOV-VOICESTUDIO-MULTI-SEGMENT-EDIT-APPLY-01** + inline edit/apply/regenerate lanes **Closed** (draft buffer + Apply path exists).
- **Closure:** [VOICESTUDIO_GAP047_TRANSCRIBE_FILLER_CLEANUP_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_GAP047_TRANSCRIBE_FILLER_CLEANUP_LANE_CLOSURE_2026-04-01.md)

## Changelog

- **2026-04-01:** Lane closed — `TranscriptFillerCleanupHelper` + Transcribe flyout **Remove fillers**; VM `TryRemoveFillersFromEditingDraft`; tests; verification matrix PASS.
- **2026-04-01:** Lane opened — frozen contract below; implementation: WinUI draft-only cleanup + `TranscriptFillerCleanupHelper`; no backend routes.

## 1. Objective

Let operators **remove common spoken fillers from the Transcribe segment edit draft** (single segment or contiguous same-clip **range** merged string) **before Apply**, using deterministic client-side matching. **Apply** remains the existing **`ReplaceRange` + `TranscriptSegmentRegenerationCoordinator`** path with **no** new API jobs or routes.

## 2. Filler catalog (frozen, lane default)

Removal targets are **case-insensitive** whole-token matches using a normalized form (letters, digits, apostrophe inside words). Default list (order does not affect removal priority; **phrases are matched before single tokens**):

| Kind | Tokens / phrases |
|------|-------------------|
| Phrases (multi-word) | `you know` |
| Single-token | `um`, `uh`, `ugh`, `er`, `ah`, `like`, `hmm`, `umm` |

**Honesty:** Words like `like` cause false positives; this lane accepts that tradeoff and documents it. Future lanes may add per-token toggles or NLP; **out of scope here**.

## 3. Matching policy (frozen)

1. **Phrase-first:** Multi-word patterns are matched as **consecutive whole words** in draft order (after whitespace split). Longer phrases first (this lane: only one phrase).
2. **Token boundaries:** Input is split into **non-whitespace runs** (`\S+`) each followed by its **trailing whitespace** (if any) for reconstruction; matching uses **normalized word** equality (Unicode letters/digits + internal apostrophe, lowercased).
3. **Punctuation:** Leading/trailing punctuation on a word does not prevent a filler match (`Um.` → `um`).
4. **Output whitespace:** Non-removed words are rejoined with a **single ASCII space** between words; leading/trailing trim. Deterministic and stable for the same input.

## 4. Mutation scope (frozen)

- **Only** `EditingSegmentDraftText` may change. **No** mutation to `EditingSegmentOriginalText`, segment list, timeline, or backend until **Apply**.
- If cleanup would produce **empty** trimmed text, the operation **fails closed** (draft unchanged; operator message explains).

## 5. Operator feedback (frozen)

- On success: set **`TranscriptOperatorMessage`** with **removed occurrence count** and a short **terms summary** (e.g. distinct filler keys and counts).
- **`SegmentEditOperatorHint`** continues to reflect dirty/range semantics via existing `RefreshSegmentEditHint`.

## 6. UI (frozen)

- Segment edit flyout (`TranscribeView`): **Remove fillers** control before **Apply** / **Cancel**.
- **Ctrl+Enter** still Apply; **Escape** still Cancel. Shift+click range behavior unchanged.

## 7. Hard OUT

- New backend `/api/*` routes or new job types for filler removal.
- Audio splice / timeline surgery without regen.
- `TranscriptEditIntentKind.RemoveRange` execution (remains non-executable unless a future lane implements it).
- Non-contiguous multi-segment batches beyond existing contiguous range draft.

## 8. Verification

- **MSTest:** `TranscriptFillerCleanupHelper` cases (token, phrase, punctuation, whitespace, false-positive guard where tested); `TranscribeViewModel` cleanup + range + empty-result guard.
- **Matrix:** `dotnet build`, full App.Tests, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` before **Closed**.
- **On close:** execution row **Closed**, closure report under `docs/reports/verification/`, **CANONICAL_REGISTRY** + **STATE** + **PROFESSIONAL_GAP_TRACKER** note this **bounded slice**; **GAP-047** and **GAP-045** remain **Open** for broader product scope unless separately closed.
