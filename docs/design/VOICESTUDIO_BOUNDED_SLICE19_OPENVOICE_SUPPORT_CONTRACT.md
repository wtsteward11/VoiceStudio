# Bounded Slice 19 — OpenVoice support contract (readiness only)

**Status:** Accepted (2026-04-21)  
**Purpose:** Single source of truth for **OpenVoice readiness** (`ensure_openvoice` + `checks.openvoice`). **Runtime parity** (matrix PASS, live synthesis proofs) is **explicitly out of scope** for this slice.

## Engine id

**Authoritative engine id:** `openvoice` (see `engines/audio/openvoice/engine.manifest.json`).

## Venv authority

Manifest declares **`venv_family`: `venv_openvoice`** (dedicated OpenVoice tree; **Slice 19F / ADR-054**). Preflight **must** probe imports in that family’s `python.exe`, not the FastAPI backend `.venv` — same subprocess posture as `ensure_tortoise` / Chatterbox worker paths (API worker does not load OpenVoice for synthesis).

## Readiness (what `checks.openvoice.ok == true` means)

**Readiness** means: `ensure_openvoice` in `backend/services/model_preflight.py` (and mirror `backend/ml/models/model_preflight.py`) returns **`ok: true`** when:

1. `from openvoice.api import BaseSpeakerTTS, ToneColorConverter` succeeds in the **`venv_openvoice`** interpreter (subprocess probe), and  
2. Under `<models root>/openvoice/base_speakers` and `<models root>/openvoice/converter`, the tree contains at least one `config.json` with a sibling `checkpoint.pth`, `checkpoint.ckpt`, or `model.pth` (rglob discovery; matches engine loader expectations).

`<models root>` is `VOICESTUDIO_MODELS_PATH` when set, otherwise `%PROGRAMDATA%\VoiceStudio\models` (Windows layout aligned with manifest `model_paths`).

## No silent download

The `auto_download` parameter is accepted for API symmetry but **ignored** for OpenVoice preflight: there is **no** automatic weight fetch. Operators must place checkpoints locally or see an explicit **`ok: false`** message (aligned with `no-fallbacks.mdc` — fail explicit).

## Closure (matrix PASS) — not this slice

**Closure** means: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS** only after a **future** bounded live slice documents: green preflight, `pytest` / C# live-backend proofs, artifacts, and regression bar — same honesty bar as Tortoise 18D / Chatterbox 17D.

## What this slice does not claim

- **Does not claim:** OpenVoice runtime parity or matrix PASS.  
- **Does not claim:** Chatterbox / Tortoise / RHVoice state.  
- **Does not claim:** OpenVoice is installed in every CI image.

## Related artifacts

- Proof (readiness truth): [`PROOF_SLICE19_OPENVOICE_AUDITION.md`](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md).  
- Matrix: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md).  
- Engine (worker / in-venv): `app/core/engines/openvoice_engine.py`; manifest **`entry_point`**: `OpenVoiceSubprocessEngine` (`app/core/engines/openvoice_subprocess_engine.py`) + CLI `app/cli/openvoice_worker_synthesize.py`.

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-21 | Initial contract: `ensure_openvoice`, `checks.openvoice` boolean, `venv_advanced_tts`, local checkpoints only, readiness vs runtime boundary. |
| 2026-04-21 | **19F:** `venv_openvoice` authority, subprocess engine + worker CLI; amends venv section only — matrix PASS still future operator work. |
