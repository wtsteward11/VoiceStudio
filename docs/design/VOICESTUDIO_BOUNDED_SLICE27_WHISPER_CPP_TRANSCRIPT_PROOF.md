# Bounded Slice 27 — whisper.cpp runtime transcript proof (harness)

**Status:** Harness landed; runtime PASS operator-gated  
**Date:** 2026-04-23  

## Goal

Opt-in live proof mirroring Slice 21A: `pytest -m real_whisper_cpp` + [`tests/integration/test_transcribe_whisper_cpp_real.py`](../../tests/integration/test_transcribe_whisper_cpp_real.py); same `VOICESTUDIO_REAL_XTTS_HTTP_BASE` discipline; preflight requires `checks.whisper_cpp.ok`.

## Preconditions

Green `ensure_whisper_cpp` (Slice 22), router fail-closed (Slice 24), trustworthy unit tests (Slice 25).

## Verification

- Default CI: test excluded via `pytest.ini` marker filter.
- Operator: `python -m pytest tests/integration/test_transcribe_whisper_cpp_real.py -m real_whisper_cpp --tb=short`
