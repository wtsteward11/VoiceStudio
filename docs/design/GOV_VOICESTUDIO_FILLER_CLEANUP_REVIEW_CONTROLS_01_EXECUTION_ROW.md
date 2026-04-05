# GOV-VOICESTUDIO-FILLER-CLEANUP-REVIEW-CONTROLS-01 — Transcribe filler cleanup review controls (GAP-047 bounded slice)

## 0. Status

- **State:** **Closed** (2026-04-01) — closure [VOICESTUDIO_GAP047_FILLER_CLEANUP_REVIEW_CONTROLS_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_REVIEW_CONTROLS_CLOSURE_2026-04-01.md)
- **Product scope:** **GAP-047** — **Open** overall; **GAP-045** — **Open**; this lane is a **bounded** continuation of draft-only filler assist (no full product detection scope).
- **Depends on:** **GOV-VOICESTUDIO-TRANSCRIBE-FILLER-CLEANUP-01** **Closed** (deterministic cleanup + flyout action).
- **Closure:** [VOICESTUDIO_GAP047_FILLER_CLEANUP_REVIEW_CONTROLS_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_GAP047_FILLER_CLEANUP_REVIEW_CONTROLS_CLOSURE_2026-04-01.md)

## Changelog

- **2026-04-01:** Lane **closed** — preview + toggles + enabled-key removal; proof in closure report.
- **2026-04-02:** Lane opened — operator preview + per-term toggles; session-local flyout state; `like` default **off**; no backend routes.

## 1. Objective

Before **Apply**, let operators **inspect which default-catalog fillers match** the current `EditingSegmentDraftText`, **toggle which terms/phrases may be removed**, see a **read-only cleaned preview** for the current selection, then run **Remove fillers** using only enabled keys. **Apply** remains the existing **`ReplaceRange` + regen** path.

## 2. Filler catalog (inherits prior lane)

Same default list as [GOV_VOICESTUDIO_TRANSCRIBE_FILLER_CLEANUP_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIBE_FILLER_CLEANUP_01_EXECUTION_ROW.md) §2. Matching policy unchanged (phrase-first, whole-token, normalized compare).

## 3. Operator controls (frozen)

| Control | Behavior |
|--------|-----------|
| Match list | One row per **distinct key** with occurrence count ≥ 1 in current draft (`GetRemovalPlan`). |
| Toggle | **Remove** when checked; unchecked keys are **never** removed in that pass. |
| Risky token | **`like`** is **risky**; default **unchecked** in the flyout. Other keys default **checked** when present. |
| **You know** phrase | Not risky; default **checked** when present. |
| Cleaned preview | Read-only text showing draft after removal **if** only enabled keys were removed (single-space normalization). Updates when toggles change or plan rebuilds. |
| Session scope | Toggle state and preview are **flyout session–local**; **not** persisted to settings or project. Discarded on flyout close / **Cancel**. |

## 4. Mutation scope (frozen)

- **Remove fillers** still mutates **only** `EditingSegmentDraftText` after operator confirms via the button (same as prior lane).
- Empty trimmed result after enabled-only removal **fails closed** (draft unchanged; operator message).
- If **no catalog matches** in draft, Remove reports no matches (or equivalent).
- If matches exist but **no toggles enabled**, removal request **fails closed** with an explicit message (do not silently no-op).

## 5. Hard IN

- Execution row + closure + registry + tracker + STATE sync.
- `TranscriptFillerCleanupHelper` — removal with explicit enabled phrase/single key sets; `GetRemovalPlan` / `RemovalPlanEntry`.
- `TranscribeViewModel` — toggle collection, preview text, rebuild on flyout/draft sync.
- `TranscribeView` flyout — preview + checkboxes + Remove button wiring.
- Tests — toggle-off preserves token; `like` default off; phrase-first; empty-all-enabled guard; range draft path.

## 6. Hard OUT

- New backend `/api/*` routes or new job types.
- NLP / ML classification.
- Audio timing / timeline surgery.
- Persisted per-user filler prefs (defer to future lane).
- Batch transcript-wide cleanup.

## 7. Verification

- `dotnet build` VoiceStudio.sln Debug x64
- Full `VoiceStudio.App.Tests`
- `pytest tests/ci` `--randomly-seed=12345`
- `verify.ps1 -Quick`
- `run_verification.py` — **completion_guard** PASS

## 8. Acceptance

- Operators see preview + toggles in segment edit flyout; `like` defaults off when present.
- Removal respects enabled keys only; Apply path unchanged.
- Governance closure with honest limits (false positives possible; preview is heuristic for enabled set only).
