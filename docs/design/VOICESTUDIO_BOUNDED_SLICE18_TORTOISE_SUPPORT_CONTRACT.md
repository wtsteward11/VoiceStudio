# Bounded Slice 18 — Tortoise support contract (execution row)

**Status:** Accepted (2026-04-20)  
**Purpose:** Single source of truth for Tortoise TTS readiness, closure, and explicit non-claims for Slice 18.

## Engine id

**Authoritative engine id:** `tortoise` (see `engines/audio/tortoise/engine.manifest.json`).

## Readiness (what `checks.tortoise.ok == true` means)

**Readiness** means: `ensure_tortoise` in `backend/services/model_preflight.py` (and mirror `backend/ml/models/model_preflight.py`) reports **`ok: true`** — `from tortoise.api import TextToSpeech` succeeds in the **dedicated `venv_tortoise` interpreter** (subprocess probe; **not** the FastAPI backend `.venv`), and at least one cached weight file exists under `<tortoise cache>/tortoise_models` when `auto_download=False` (layout: `VOICESTUDIO_MODELS_PATH` or `%PROGRAMDATA%\VoiceStudio\models\tortoise`, then `tortoise_models`). See **ADR-052** (Slice 18B).

## Closure (matrix PASS)

**Closure** means: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md) **`tortoise` row PASS** only after: `GET /api/health/preflight` → `checks.tortoise.ok == true`; non-skipped `pytest -m real_tortoise`; C# live-backend stream + playback proofs per proof doc; real artifacts under `docs/reports/verification/slice18/tortoise/`; regression bar (`dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`) green — same honesty bar as Silero / Chatterbox.

## What this slice does not claim

- **Does not claim:** RHVoice, Chatterbox, or other engines; umbrella “all engines done.”
- **Does not claim:** Tortoise is installed in every CI image (heavy `torch` + `tortoise-tts` + weights).
- **Does not claim:** GPU is mandatory; CPU inference is valid when the stack supports it.
- **Does not claim:** Runtime parity if preflight is red — then Slice 18 documents the **first exact blocker** only (no fake PASS).

## Related artifacts

- Proof: [`PROOF_SLICE18_TORTOISE_AUDITION.md`](../reports/verification/PROOF_SLICE18_TORTOISE_AUDITION.md).
- Matrix: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md).
- Engine (routed): `app/core/engines/tortoise_subprocess_engine.py` (`TortoiseSubprocessEngine` → `app/cli/tortoise_worker_synthesize.py` in `venv_tortoise`). Legacy in-process `tortoise_engine.TortoiseEngine` is not used for API synthesis routing.

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-20 | **Slice 18A note:** [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](../reports/verification/PROOF_SLICE18A_TORTOISE_PROVISIONING.md) — on the proof host, **`tortoise-tts` cannot coexist** with **`coqui-tts`** in the same backend venv due to **`transformers` pins**; in-process closure may require a **dedicated venv + subprocess** (ADR) or upstream fix — not a contract change by this proof alone. |
| 2026-04-18 | **Slice 18B:** Readiness = **`venv_tortoise` subprocess** + `tortoise_models` cache; **ADR-052**; manifest `TortoiseSubprocessEngine`. |
| 2026-04-20 | Initial contract: in-process readiness vs closure, `tortoise_models` cache probe, explicit non-claims. |
