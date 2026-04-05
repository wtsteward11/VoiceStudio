# GOV — GAP-037 successor: GAP-040 / GAP-038 sequence freeze

**Status:** Planning freeze (implementation **not** started in this artifact)  
**Date:** 2026-04-04  
**Source:** GAP-037 successor closure-grade plan (Phase 2–3 ordering)

## §1 Frozen order

1. **GAP-040** — Non-destructive edit **model authority** (clip identity / lineage, transcript-linkage invariants, compound edit chains, alignment with persistence + export).
2. **GAP-038** — **GPU** waveform / spectrogram rendering **only after** GAP-040 is stable (no edit-authority bypass in render path).

## §2 GAP-040 — Hard IN (draft for next execution row)

- Single canonical **edit representation** and reversible operation boundaries.
- Split-derived clip **lineage** rules frozen; save/load round-trip preserves semantics.
- **Transcript-linkage** behavior under trim/split/fade defined and tested.
- Undo / persistence / export coherent under **compound** chains.

## §3 GAP-040 — Hard OUT

- GPU / Win2D performance work.
- PanelHost GAP-007 shell redesign.

## §4 GAP-038 — Hard IN (draft for next execution row)

- Cache-friendly waveform path; deterministic viewport.
- **No** changes to edit / use-case authority seams introduced for GAP-040.

## §5 GAP-038 — Hard OUT

- Changing undo or persistence semantics except fixes required by rendering bugs (must be isolated PRs).

## §6 References

- [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) — GAP-038, GAP-040 rows  
- [GOV_VOICESTUDIO_GAP012_TIMELINE_EDIT_UNDO_BOUNDED_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP012_TIMELINE_EDIT_UNDO_BOUNDED_01_EXECUTION_ROW.md) — Phase 1 closed prerequisite  
