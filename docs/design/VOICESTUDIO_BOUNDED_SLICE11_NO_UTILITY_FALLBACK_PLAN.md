# Bounded Slice 11 — No automatic utility TTS fallback (`_try_utility_tts_fallback` removal)

**Type:** Technical spec / bounded slice plan  
**Date:** 2026-04-18  
**Status:** Accepted (scope Option B per discovery)

## 1. Goal

Remove **automatic** substitution to `gtts` / `pyttsx3` when the primary synthesis engine fails, aligning runtime behavior with `.cursor/rules/core/no-fallbacks.mdc`. Failures must surface as explicit engine/processing errors with **no** `routed_engine` of `gtts_utility` or `pyttsx3_utility` on those paths.

## 2. Scope

- **In scope:** Delete `_try_utility_tts_fallback` and all call sites in `backend/services/synthesis_service.py` and `backend/api/routes/voice/_helpers.py`; update docs/matrix; add regression tests.
- **Out of scope:** Removing `gtts` / `pyttsx3` from `requirements.txt` (Option C); changing `_select_engine_with_fallback` engine-alias behavior (separate governance item).

## 3. Verification

- `tests/unit/backend/services/test_synthesis_no_silent_fallback.py` PASS.
- `EngineFailureTruthfulnessLiveBackendTests` PASS or honest Inconclusive when backend unreachable.
- Full `verify.ps1` GREEN when run in release pipeline.
- `python scripts/run_verification.py` PASS; `completion_guard` PASS.

## 4. Proof artifact

- `docs/reports/verification/PROOF_SLICE11_NO_FALLBACKS_REMOVAL.md`
- Discovery: `docs/reports/verification/PROOF_SLICE11_FALLBACK_REMOVAL_DISCOVERY.md`

## Changelog

| Date | Change |
| ---- | ------ |
| 2026-04-18 | Initial brief; Option B from discovery. |
