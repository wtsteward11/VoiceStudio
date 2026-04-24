# PROOF — Slice 24 — STT router fail-closed

**Status:** **PASS** (policy + unit tests). **Not** a claim that full `verify.ps1` (all stages) ran on every host — default closure used **`verify.ps1 -Quick`** (many stages skipped; regression guard only).

**Date:** 2026-04-23  

## What changed

| Area | Change |
| --- | --- |
| Router | Single effective STT default chain; **`normalize_engine_request_id`** (`faster_whisper` → `whisper`); **`select_engine_with_fallback(..., explicit_engine_id=...)`**; **no** STT substitution in **`_get_lower_load_alternative`** |
| Config | [`config/engines.config.yaml`](../../config/engines.config.yaml) — STT `fallback_chains.stt` cannot reintroduce multi-engine silent walk |
| Backend | [`backend/services/engine_service.py`](../../backend/services/engine_service.py) — removed whisper / faster_whisper from **`ENGINE_FALLBACK_CHAIN`** |
| Unified config | [`backend/platform/config/unified_config.py`](../../backend/platform/config/unified_config.py), [`backend/services/unified_config.py`](../../backend/services/unified_config.py) — language mapping for **`get_engine_for_language`** applies to **TTS** only (not STT) |
| Policy ADR | [ADR-056](../../architecture/decisions/ADR-056-stt-router-no-silent-substitution.md) |

## Out of scope

- User-opt-in multi-engine STT discovery lists (would need named setting + ADR).
- **`real_whisper_cpp`** runtime transcript (Slice 27 / Task 32).

## Verification

| Command | Expected |
| --- | --- |
| `python -m pytest tests/unit/core/engines/test_router_stt_policy.py -q` | **0 failures** |
| `python scripts/run_verification.py` | **PASS** (gates incl. completion_guard when not skipped) |
| `.\scripts\verify.ps1 -Quick` | **VERIFICATION PASSED** — see **STATE** **Latest verify artifact**; Quick **skips** extended stages (do not oversell as full CI) |

## Artifacts

- Pin session report under `artifacts/verify/*/verification_report.md` in [.cursor/STATE.md](../../.cursor/STATE.md) **LATEST PROOF INDEX**.
- Bounded brief: [`docs/design/VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md`](../../design/VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md).

## Key files

- [`app/core/engines/router.py`](../../app/core/engines/router.py)
- [`tests/unit/core/engines/test_router_stt_policy.py`](../../tests/unit/core/engines/test_router_stt_policy.py)
