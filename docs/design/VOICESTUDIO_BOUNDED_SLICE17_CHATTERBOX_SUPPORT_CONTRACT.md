# Bounded Slice 17 — Chatterbox support contract (execution row)

**Status:** Accepted (2026-04-20)  
**Purpose:** Single source of truth for Chatterbox TTS readiness, closure, and explicit non-claims for Slice 17.

## Engine id

**Authoritative engine id:** `chatterbox` (see `engines/audio/chatterbox/engine.manifest.json`).

## Readiness (what `checks.chatterbox.ok == true` means)

**Readiness** means: `ensure_chatterbox` in `backend/services/model_preflight.py` (and mirror `backend/ml/models/model_preflight.py`) reports **`ok: true`** — PyTorch importable, `chatterbox-tts` importable, `huggingface_hub` usable, and the Hugging Face repo **`ResembleAI/chatterbox`** has required weights available under preflight policy (`auto_download=False` implies local cache only; no silent network fetch in preflight).

## Closure (matrix PASS)

**Closure** means: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md) **`chatterbox` row PASS** only after: `GET /api/health/preflight` → `checks.chatterbox.ok == true`; non-skipped `pytest -m real_chatterbox`; C# live-backend stream + playback proofs per proof doc; real artifacts under `docs/reports/verification/slice17/chatterbox/`; regression bar (`dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`) green — same honesty bar as Silero (Slice 15).

## What this slice does not claim

- **Does not claim:** RHVoice, other TTS engines, or umbrella “synthesis works everywhere.”
- **Does not claim:** Chatterbox is installed in every default CI venv (manifest `venv_family`: `venv_advanced_tts`; operator may need that environment).
- **Does not claim:** GPU is mandatory; CPU inference is valid when supported by the installed stack.
- **Does not claim:** Runtime parity if preflight is red — then Slice 17 documents the **first exact blocker** only (no fake PASS).

## Related artifacts

- Proof: [`PROOF_SLICE17_CHATTERBOX_AUDITION.md`](../reports/verification/PROOF_SLICE17_CHATTERBOX_AUDITION.md) (created in Slice 17).
- Matrix: [`ENGINE_PARITY_MATRIX.md`](../reports/verification/ENGINE_PARITY_MATRIX.md).
- Engine: `app/core/engines/chatterbox_engine.py` (`ChatterboxTTS.from_pretrained`).

## Changelog

| Date | Change |
| --- | --- |
| 2026-04-20 | Initial contract: readiness vs closure, HF repo `ResembleAI/chatterbox`, explicit non-claims. |
