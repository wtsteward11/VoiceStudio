# GOV-VOICESTUDIO-GAP004-SINGLE-CANONICAL-SYNTHESIS-PATH-01 — Single canonical synthesis execution path

**Lane ID:** `GOV_VOICESTUDIO_GAP004_SINGLE_CANONICAL_SYNTHESIS_PATH_01`
**Status:** **Closed** (2026-04-08).
**Tracker:** [GAP-004](PROFESSIONAL_GAP_TRACKER.md) — **Closed**.
**Lane type:** **runtime-affecting**
**Depends on:** None (priority 0, foundational).

## Problem statement

VoiceStudio has **three distinct synthesis execution surfaces** that duplicate core logic:

### 1. Route-inline synthesis (`backend/api/routes/voice/synthesis.py`, ~46 KB)

The primary `POST /api/voice/synthesize` handler (~550 lines) contains its own:
- engine router lazy-init + direct `engine_router.synthesize()` calls
- profile loading + reference audio resolution
- SSML policy application (`apply_ssml_synthesis_policy`)
- NLP text preprocessing (inline `try/except ImportError`)
- quality metrics extraction + quality optimization
- audio artifact creation
- provenance + consent checks

Three additional handlers (`synthesize_multipass`, `synthesize_with_style`, `synthesize_cross_lingual`) duplicate subsets of the same logic in the same file.

### 2. `SynthesisService` class (`backend/services/synthesis_service.py`, ~800 lines)

Declared as "canonical entry point" (docstring: "Routes call SynthesisService.synthesize() instead of importing synthesize_core"). Contains parallel implementations of profile resolution, SSML policy, NLP preprocessing, quality metrics, engine routing, provenance, consent. The primary route **does not use it**.

Used by secondary consumers via the re-export shim at `backend/voice/services/synthesis_service.py`:
- `ensemble.py`, `style_transfer.py`, `voice_morph.py`, `ssml.py`, `prosody.py`, `workflows.py`, `assistant_run.py`, `voice_cloning_wizard.py`

### 3. `voice_synthesis_service.py` (`backend/services/voice_synthesis_service.py`)

Thin wrapper that delegates to `SynthesisService`. Used only by `voice/testing.py`.

### Consequences of divergence

- SSML policy, NLP preprocessing, quality metrics, and error handling can evolve independently in the route vs the service, creating silent behavioral drift.
- Testing the service does not test the actual primary route path.
- Bug fixes in one surface are not automatically reflected in the other.
- The "canonical" label on `SynthesisService` is aspirational, not factual — the primary consumer ignores it.

## Scope (bounded slice)

This execution row targets:

1. **Make the primary route delegate to `SynthesisService`** — the route handler becomes thin (validate request → call service → format response).
2. **Consolidate multipass/style/cross-lingual** into `SynthesisService` methods or documented thin-route patterns that share the same engine/profile/SSML/quality pipeline.
3. **Remove `voice_synthesis_service.py`** wrapper (consumers import `SynthesisService` directly).
4. **Verify** that all secondary consumers continue to work via the same service entry point.

## State ownership (frozen)

| Surface | Owner |
|---------|-------|
| Primary synthesis route | `backend/api/routes/voice/synthesis.py` |
| Canonical synthesis service | `backend/services/synthesis_service.py` |
| Re-export shim | `backend/voice/services/synthesis_service.py` |
| Thin wrapper | `backend/services/voice_synthesis_service.py` |
| Secondary route consumers | `ensemble.py`, `style_transfer.py`, `voice_morph.py`, `ssml.py`, `prosody.py`, `workflows.py`, `assistant_run.py`, `voice_cloning_wizard.py` |
| Route helpers | `backend/api/routes/voice/_helpers.py` |

## Acceptance contract (Close)

- [x] `POST /api/voice/synthesize` delegates to `SynthesisService.synthesize()` — route file contains no engine-router calls, no profile resolution, no SSML policy, no NLP preprocessing, no quality metrics extraction.
- [x] `synthesize_multipass`, `synthesize_with_style`, `synthesize_cross_lingual` delegate to service methods or share the canonical pipeline.
- [x] `backend/services/voice_synthesis_service.py` removed or deprecated with import redirect.
- [x] All secondary route consumers (`ensemble`, `style_transfer`, etc.) still resolve via `SynthesisService` — no broken imports.
- [x] `synthesis.py` route file is ≤300 lines (thin-route budget).
- [x] Tests: existing `test_emotion.py`, `test_golden_loop_smoke.py`, `pytest tests/ci` all PASS; targeted synthesis tests if changed surfaces warrant.
- [x] Closure matrix per runtime lane standard; tracker GAP-004 updated; registry + STATE.

## Allowlist (implementation)

`backend/api/routes/voice/synthesis.py`, `backend/services/synthesis_service.py`, `backend/voice/services/synthesis_service.py`, `backend/api/routes/voice/testing.py`, `backend/api/routes/voice/_helpers.py`, `tests/unit/backend/api/routes/test_synthesis.py`, `tests/ci/` (verify), execution row, closure report, tracker, registry, `.cursor/STATE.md`.

## Hard OUT

- Changing engine protocol or engine adapter interfaces.
- Modifying `app/core/runtime/` engine subprocess layer.
- Rewriting secondary route consumers (they keep their existing `SynthesisService` import; only verify it still works).
- Adding new synthesis features or engine capabilities.
- Touching emotion/prosody/preview routes (closed GAP-050/GAP-023 lanes).
- Startup, shell, installer, or UI changes.

## Rollback

Revert this lane's commits; restore inline route logic + wrappers. Secondary consumers are unaffected (they already use `SynthesisService`).

## Changelog

- **2026-04-08:** Row **Frozen** — first GAP-004 execution row.
- **2026-04-08:** Row **Closed** — canonical `SynthesisService` path; thin routes; wrapper removed; [closure](../reports/verification/VOICESTUDIO_GAP004_SINGLE_CANONICAL_SYNTHESIS_PATH_LANE_CLOSURE_2026-04-08.md).
