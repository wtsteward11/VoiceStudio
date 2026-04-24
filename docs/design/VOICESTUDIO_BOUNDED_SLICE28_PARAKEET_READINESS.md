# Bounded Slice 28 — Parakeet readiness (boolean preflight)

**Status:** Closed (implementation)  
**Date:** 2026-04-23  

**Engine kind (Task 31):** VoiceStudio’s **`engine_id: parakeet`** is **PaddleSpeech Parakeet TTS** (`engines/audio/parakeet/engine.manifest.json`, **`subtype: tts`**). This is **not** NVIDIA Parakeet ASR and **not** STT matrix work — it shipped in the same bounded **batch** as STT slices 24–27 for preflight consistency only.

## Goal

`ensure_parakeet` validates `paddle` + `paddlespeech` imports and non-empty `models/parakeet/checkpoints`; boolean `checks.parakeet`; probe branch for `parakeet`.

## Verification

`python -m pytest tests/unit/backend/services/test_model_preflight.py::test_ensure_parakeet_raises_without_checkpoints -q`
