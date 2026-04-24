# Bounded Slice 25 — whisper.cpp unit test trust repair

**Status:** Closed  
**Date:** 2026-04-23  

## Goal

Remove `except Exception: pytest.skip` patterns that laundered assertion failures in [`tests/unit/core/engines/test_whisper_cpp_engine.py`](../../tests/unit/core/engines/test_whisper_cpp_engine.py); align assertions with the real `WhisperCPPEngine` surface (`_caching_enabled`, `batch_transcribe`, LRU cache, `batch_size`).

## Verification

`python -m pytest tests/unit/core/engines/test_whisper_cpp_engine.py -q`
