# ADR-053: OpenVoice advanced-TTS venv surface and packaging constraints

**Status:** Accepted — **runtime surface for OpenVoice superseded in part by [ADR-054](ADR-054-openvoice-isolated-venv-proposal.md) (Accepted, 2026-04-21 / Slice 19F)**; historical packaging analysis below remains valid.  
**Date:** 2026-04-20  
**Decision makers:** VoiceStudio bounded Slice 19D (packaging + runtime-surface governance)

## Context

OpenVoice is declared for **`VenvFamily.ADVANCED_TTS`** (`runtime/venvs/torch26`) in the engine manifest and probed by `ensure_openvoice` in `backend/services/model_preflight.py`. Slice 19C attempted `pip install` of the pinned **`myshell-openvoice`** Git URL from `requirements_engines.txt` into **`torch26`** and failed: **`faster-whisper==0.9.0`** → **`av==10.*`**, with PyAV **source** build **Cython `CompileError`** on Windows **cp311** (no suitable **`av` 10** wheel for this ABI).

Upstream **myshell-ai/OpenVoice** at the pinned commit uses **`setup.py`** with **`install_requires`** listing **`faster-whisper==0.9.0`** — it is **not** an optional extra; VoiceStudio’s engine adapter does not import `faster_whisper` directly, but **pip** must still satisfy upstream’s mandatory graph to install the package.

## Options considered

1. **Stay on shared `torch26` (current wiring)** — Keep manifest + `ensure_openvoice` on **`ADVANCED_TTS`**; fix provisioning via **compatible pins**, **prebuilt wheels**, **vendor/fork** of OpenVoice with relaxed or updated `faster-whisper`/`av` constraints, or **documented operator** supply of a compatible tree — all subject to **ADR-044** when `requirements_engines.txt` / hashes change.
2. **Isolated OpenVoice venv (ADR-052 pattern)** — New **`VenvFamily`** (or equivalent), dedicated `runtime/venvs/openvoice`, manifest + `ensure_openvoice` + worker alignment — decouples OpenVoice’s pins from Chatterbox/other **`torch26`** consumers but adds **seams**, disk, and maintenance cost.
3. **Defer OpenVoice** — Remove or gate the engine until packaging is tractable — rejected for this ADR: the decision here is **surface + honesty**, not product removal.

## Decision

- **Authoritative runtime surface (Slice 19D):** OpenVoice preflight and the declared manifest **`venv_family`** were **`venv_advanced_tts`** → **`runtime/venvs/torch26`**.
- **Amendment (Slice 19F / ADR-054 Accepted):** OpenVoice **`venv_family`** is **`venv_openvoice`** → **`runtime/venvs/openvoice`**; **`ensure_openvoice`** and **`OpenVoiceSubprocessEngine`** use that interpreter — **not** shared **`torch26`**.
- **Packaging truth:** Stock pinned **`myshell-openvoice`** is **not installable** on the documented Windows **cp311** host without further engineering (upstream mandatory **`faster-whisper`/`av`** chain vs wheel availability); see [slice19d_openvoice_package_provenance.md](../../reports/verification/slice19/openvoice/slice19d_openvoice_package_provenance.md) and proof §19D in [PROOF_SLICE19_OPENVOICE_AUDITION.md](../../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md).
- **Follow-up (Slice 19F):** Isolated OpenVoice interpreter is **implemented** under [ADR-054](ADR-054-openvoice-isolated-venv-proposal.md) (**Accepted**). Further **pin / fork / platform** work applies to **`runtime/venvs/openvoice`** without coupling to **Chatterbox**’s **`torch26`** stack.

## Consequences

**Positive**

- Historical clarity (19D): single **`torch26`** story for OpenVoice **before** ADR-054 amendment.
- Clear record that failure is **dependency / platform packaging**, not an optional “over-pull” myth.

**Negative**

- Matrix **`openvoice`** stays **pending** until install + checkpoints + green preflight + live proofs land.
- Unblocking **stock** `myshell-openvoice` may still require **fork**, **pin overrides**, or **Linux CI** inside **`openvoice`** venv — ADR-044 discipline when pins change.

**Neutral**

- Checkpoint layout under **`VOICESTUDIO_MODELS_PATH`** / **`openvoice/{base_speakers,converter}`** remains the operator contract from Slice 19; unchanged by this ADR.

## Related ADRs

- [ADR-044](ADR-044-supply-chain-integrity.md) — dependency pins and hashes when `requirements_engines.txt` changes.
- [ADR-052](ADR-052-tortoise-isolated-venv-subprocess.md) — precedent for isolated engine venv when shared venv is incompatible.
