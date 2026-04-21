# ADR-052: Tortoise TTS Isolated Virtualenv and Subprocess Synthesis

**Status:** Accepted  
**Date:** 2026-04-18  
**Decision Makers:** Engineering (Bounded Slice 18B)

## Context

Slice 18A ([PROOF_SLICE18A_TORTOISE_PROVISIONING.md](../../reports/verification/PROOF_SLICE18A_TORTOISE_PROVISIONING.md)) closed **Outcome B**: `tortoise-tts` pins an older `transformers` stack than **Coqui / XTTS** in the backend `.venv`. Installing both into the FastAPI worker environment is not viable without breaking one side.

## Options Considered

1. **Keep Tortoise in-process in the backend `.venv`** — Rejected: dependency conflict is structural; “latest transformers” cannot satisfy both stacks.
2. **Dedicated `venv_tortoise` under `runtime/venvs/tortoise` + subprocess worker** — Same structural pattern as Chatterbox Model B (`venv_advanced_tts` + `app.cli.chatterbox_worker_synthesize`). FastAPI never imports `tortoise.api`; synthesis runs in the family interpreter via `python -m app.cli.tortoise_worker_synthesize`.
3. **Upstream pin alignment** — Out of scope for VoiceStudio; may change in the ecosystem later; does not unblock the product today.

## Decision

Adopt **option 2**:

- Add **`VenvFamily.TORTOISE`** in `app/core/runtime/venv_family_manager.py` with on-disk path **`runtime/venvs/tortoise`** (`TORTOISE_TTS_PROVISION_DIRNAME`).
- **Remove** `tortoise` from **`venv_core_tts`** engine membership so XTTS/Coqui remains authoritative in the backend `.venv`.
- **Manifest** `engines/audio/tortoise/engine.manifest.json`: `venv_family: "venv_tortoise"`, `entry_point` → `TortoiseSubprocessEngine`.
- **Preflight** `ensure_tortoise`: subprocess import probe + optional weight warm in the Tortoise interpreter only; report `python_exe` for diagnostics.

## Consequences

**Positive:** Coqui XTTS and Tortoise can coexist on one machine without a broken shared `.venv`; readiness checks match the runtime that actually performs synthesis.

**Negative:** Operators must provision and maintain a second large GPU-oriented venv; CI images may omit Tortoise (heavy) by design.

**Neutral:** In-process `TortoiseEngine` remains in the tree for benchmarks or manual use inside the Tortoise venv; routing uses the subprocess adapter only.

## Related

- [VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md](../../design/VOICESTUDIO_BOUNDED_SLICE18_TORTOISE_SUPPORT_CONTRACT.md)
- [PROOF_SLICE18A_TORTOISE_PROVISIONING.md](../../reports/verification/PROOF_SLICE18A_TORTOISE_PROVISIONING.md)
- `app/cli/tortoise_worker_synthesize.py`, `app/core/engines/tortoise_subprocess_engine.py`
- `config/venv_families/requirements-tortoise.txt`, `scripts/engines/create_engine_venv.py` (`--family tortoise`)
