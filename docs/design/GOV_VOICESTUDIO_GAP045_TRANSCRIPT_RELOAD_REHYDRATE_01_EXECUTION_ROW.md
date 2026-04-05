# GOV-VOICESTUDIO-GAP045-TRANSCRIPT-RELOAD-REHYDRATE-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_01`  
**Status:** **Open** — bounded slice under product **GAP-045** (text-based audio editing).  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md) — **selected next slice** after [transcript persistence lane closure](../reports/verification/VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_LANE_CLOSURE_2026-04-05.md).  
**Depends on:** [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md) (**Closed**)

## Problem statement

Persisted transcript text (post-regeneration / `PUT /api/transcribe/{id}`) must **rehydrate coherently** after **project reload** and app reopen: UI, exports, and segment ordering must match backend truth.

## Frozen architecture decisions (draft — refine at lane kickoff)

1. **Authority:** Backend transcript rows remain source of truth; client reload paths must not invent alternate text.
2. **Scope:** In-session + cold reopen; focus on transcribe/timeline consumers and export formatters already touched by GAP-045 persistence.
3. **Diagnostics:** On mismatch or empty rehydrate, surface operator-visible diagnostics (no silent fallback to stale UI text).

## Hard IN (acceptance targets)

- [ ] After persistence + **project close/reopen**, regenerated transcript text matches backend fetch.
- [ ] Segment boundaries / order consistent with persisted data (within defined tolerance for empty segments).
- [ ] TXT/SRT export after reload matches persisted truth (same formatter as persistence lane).
- [ ] Targeted automated tests (VM or service seam) for reload/rehydrate path.
- [ ] Full verification matrix on closure.

## Hard OUT

- New ML filler models (see **GAP-047** alternative slice).
- Transcript document redesign.
- New backend routes unless a gap is proven in existing GET/reload contract.

## Alternative product slice

If Overseer deprioritizes reload: **GAP-047** next bounded slice — ML / heuristic filler detection beyond transcribe-first draft cleanup (see tracker).

## Proof (on closure)

- To be filed as `docs/reports/verification/VOICESTUDIO_GAP045_TRANSCRIPT_RELOAD_REHYDRATE_LANE_CLOSURE_YYYY-MM-DD.md`.

## Rollback

Revert reload/rehydrate wiring and tests for this lane only; keep transcript persistence lane behavior intact.
