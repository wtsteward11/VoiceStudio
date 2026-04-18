# PROOF — Slice 11: No automatic utility TTS fallback

**Status:** Closed (Option B)  
**Date:** 2026-04-18

## Summary

- Removed `_try_utility_tts_fallback` from `backend/services/synthesis_service.py` and duplicate logic from `backend/api/routes/voice/_helpers.py`.
- Retained explicit `synthesize_with_utility` / `tests/integration/test_tts_utilities.py` (no dependency removal).
- Discovery: [PROOF_SLICE11_FALLBACK_REMOVAL_DISCOVERY.md](PROOF_SLICE11_FALLBACK_REMOVAL_DISCOVERY.md)  
- Brief: [VOICESTUDIO_BOUNDED_SLICE11_NO_UTILITY_FALLBACK_PLAN.md](../../design/VOICESTUDIO_BOUNDED_SLICE11_NO_UTILITY_FALLBACK_PLAN.md)

## Automated proof

| Gate | Command | Result |
| ---- | --------- | ------ |
| Unit (no utility in error path) | `python -m pytest tests/unit/backend/services/test_synthesis_no_silent_fallback.py -q` | **1 passed** |
| Live HTTP markers (invalid engine) | `dotnet test ... --filter FullyQualifiedName~EngineFailureTruthfulnessLiveBackendTests` (`VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8030`) | **Passed: 1** |
| Verification | `python scripts/run_verification.py` | **Overall: PASS**; **completion_guard PASS** (commits `4db6a234`+ on `main`) |

## Changelog

| Date | Change |
| ---- | ------ |
| 2026-04-18 | Initial proof — Option B closure. |
