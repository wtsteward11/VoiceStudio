# PROOF — Slice 28 — Parakeet readiness (TTS preflight)

**Status:** **PASS** (wiring + unit negative path). **Task 31:** **`parakeet`** is **PaddleSpeech Parakeet TTS** — manifest [`engines/audio/parakeet/engine.manifest.json`](../../engines/audio/parakeet/engine.manifest.json) **`subtype: tts`**. This slice is **not** STT lane work; it shipped in the same bounded **batch** as STT hardening for preflight consistency.

**Date:** 2026-04-23  

## What changed

| Surface | Responsibility |
| --- | --- |
| [`backend/services/model_preflight.py`](../../backend/services/model_preflight.py) | **`ensure_parakeet`** — docstring: PaddleSpeech TTS; checkpoints under `models/parakeet/checkpoints` |
| [`backend/api/routes/health.py`](../../backend/api/routes/health.py) | **`checks.parakeet`** boolean |
| Probe | [`scripts/engine_readiness_probe.py`](../../scripts/engine_readiness_probe.py) — `parakeet` branch |
| Registry | [`backend/services/preflight_registry.py`](../../backend/services/preflight_registry.py) — `parakeet` → **`ensure_parakeet`** |

## Verification

```powershell
python -m pytest tests/unit/backend/services/test_model_preflight.py::test_ensure_parakeet_raises_without_checkpoints -q
```

**Expected:** exit code **0** — raises or red dict path per contract when checkpoints missing.

## Out of scope

- NVIDIA Parakeet ASR (would be a **different** `engine_id`).
- TTS synthesis runtime PASS / `real_parakeet` (readiness-only until a future slice).

## Artifacts

- Brief: [`docs/design/VOICESTUDIO_BOUNDED_SLICE28_PARAKEET_READINESS.md`](../../design/VOICESTUDIO_BOUNDED_SLICE28_PARAKEET_READINESS.md).
