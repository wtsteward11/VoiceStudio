# Bounded Slice 24 — STT router fail-closed (no silent substitution)

**Status:** Closed (implementation)  
**Date:** 2026-04-23  

## Goal

Remediate the router-level STT trust leak after Slice 23: **no silent cross-engine STT substitution**; explicit engine requests stay fail-closed; align **`faster_whisper`** config token with manifest id **`whisper`**.

## Scope

- [`app/core/engines/router.py`](../../app/core/engines/router.py) — STT chain, load balancer, `select_engine_with_fallback`, `normalize_engine_request_id`
- [`config/engines.config.yaml`](../../config/engines.config.yaml) — STT fallback comment + single entry
- [`backend/services/engine_service.py`](../../backend/services/engine_service.py) — remove nonsensical TTS fallback keys for whisper ids
- [`backend/platform/config/unified_config.py`](../../backend/platform/config/unified_config.py), [`backend/services/unified_config.py`](../../backend/services/unified_config.py) — `get_engine_for_language` TTS-only mapping
- Tests: [`tests/unit/core/engines/test_router_stt_policy.py`](../../tests/unit/core/engines/test_router_stt_policy.py)

## Acceptance

- [x] STT `_get_fallback_chain` yields a **single** default id (from config `defaults.stt`)
- [x] `explicit_engine_id` path never walks a multi-engine list
- [x] `faster_whisper` resolves to **`whisper`** at `get_engine`
- [x] Load-based STT substitution disabled
- [x] Unit tests cover the above

## Verification

- `python -m pytest tests/unit/core/engines/test_router_stt_policy.py -q`
- `python scripts/run_verification.py`
- `.\scripts\verify.ps1 -Quick`

## Proof

[`docs/reports/verification/PROOF_SLICE24_STT_ROUTER_FAIL_CLOSED.md`](../reports/verification/PROOF_SLICE24_STT_ROUTER_FAIL_CLOSED.md)
