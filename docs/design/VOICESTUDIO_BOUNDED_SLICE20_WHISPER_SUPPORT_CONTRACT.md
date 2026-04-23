# Bounded Slice 20 — Whisper support contract (readiness)

**Status:** Accepted (2026-04-22)  
**Purpose:** Single source of truth for **`engine_id: whisper`** preflight: **`ensure_whisper`** and boolean **`checks.whisper`** on **`GET /api/health/preflight`**. Aligned with [`engines/audio/whisper/engine.manifest.json`](../../engines/audio/whisper/engine.manifest.json) (faster-whisper for inference).

## Engine id

**Authoritative engine id:** `whisper` (STT; not `whisper_cpp`).

## Runtime surface

The manifest names **`faster-whisper`** (CTranslate2) as the library surface. **Public preflight** uses **`ensure_whisper`** in `backend/services/model_preflight.py`, which **delegates to** **`ensure_faster_whisper`** — **no** automatic fallback to whisper.cpp, vosk, or another STT stack on failure (see `no-fallbacks.mdc`).

A separate key **`faster_whisper`** remains in `run_preflight` aggregates for diagnostics; **`checks["whisper"]`** is the engine-router-aligned boolean for matrix and probe.

## Readiness (what `checks.whisper.ok == true` means)

| Condition | |
| --- | --- |
| 1 | **`faster_whisper`** (package) is **importable** in the **FastAPI backend** Python environment used by the running Uvicorn process. |
| 2 | **Models root** is resolved: `VOICESTUDIO_MODELS_PATH` if set, else `%ProgramData%\VoiceStudio\models` (Windows). |
| 3 | **`{models_root}/whisper`** exists or can be **created** and is **writable** (cache for CTranslate2 / HF downloads on first use). |

**`ok: false`** must carry an **explicit** `message` (e.g. missing `pip install faster-whisper`).

**Readiness does not claim:** WER, transcript accuracy, or full **matrix STT “runtime PASS”** — that is a **future** bounded live slice (transcript JSON proof, not playback).

## venv family

Manifest declares **`venv_stt`**. This slice’s **readiness** check uses **whatever interpreter runs the API** and imports `faster_whisper` there. If a future split moves STT to an isolated venv, **`ensure_whisper`** must be updated in one place to subprocess-probe that venv (pattern: OpenVoice / Tortoise).

## Related artifacts

- Proof: [`PROOF_SLICE20_WHISPER_READINESS.md`](../reports/verification/PROOF_SLICE20_WHISPER_READINESS.md)  
- Matrix: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md) (STT section)  
- Code: `backend/api/routes/health.py` (`checks["whisper"]`), `backend/services/model_preflight.py` (`ensure_whisper`, `ensure_faster_whisper`), `scripts/engine_readiness_probe.py`

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-22 | Initial contract: `ensure_whisper` + boolean `checks.whisper`; faster-whisper authority; no `ok: null` for engine id `whisper` on preflight. |
