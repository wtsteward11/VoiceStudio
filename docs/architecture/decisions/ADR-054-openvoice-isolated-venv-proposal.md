# ADR-054: OpenVoice isolated venv (proposal)

**Status:** Accepted  
**Date:** 2026-04-21  
**Decision makers:** Bounded Slice 19E evidence bundle (dependency graph + dry-run failure + runtime import analysis); **implementation:** Slice 19F (2026-04-21)

## Context

[ADR-053](ADR-053-openvoice-advanced-tts-packaging-surface.md) **Accepted** that OpenVoice preflight uses **`VenvFamily.ADVANCED_TTS` → `runtime/venvs/torch26`**. Slice **19C** and **19E** reproduce that **stock** `myshell-openvoice` at the pinned Git commit **cannot be installed** into **`torch26`** on **Windows cp311** because **`faster-whisper==0.9.0`** requires **`av==10.*`**, which has **no** suitable binary wheel and **fails** Cython compilation from sdist (see [slice19e_openvoice_dependency_graph.md](../../reports/verification/slice19/openvoice/slice19e_openvoice_dependency_graph.md)).

Upstream **`openvoice/se_extractor.py`** imports **`faster_whisper`** at **module import time**; VoiceStudio’s [openvoice_engine.py](../../../app/core/engines/openvoice_engine.py) imports **`se_extractor`** for **`get_se`**. Therefore **Branch B** (“drop whisper from install metadata only”) is **insufficient** without a **fork** that refactors `se_extractor` lazy loading or optional pipelines.

## Options considered

1. **Stay on shared `torch26` with stock package** — Blocked on this platform without vendor wheels or toolchain hacks not captured in repo (19E **Outcome B** evidence).
2. **Fork MyShell-OpenVoice** — Relax `install_requires` **and** refactor `se_extractor` — valid **Branch B** under ADR-044; ongoing fork maintenance cost.
3. **Isolated OpenVoice venv** — Same **class** as [ADR-052](ADR-052-tortoise-isolated-venv-subprocess.md): dedicated `runtime/venvs/openvoice` (name TBD), new **`VenvFamily`**, manifest `venv_family` for OpenVoice only, `ensure_openvoice` + worker/subprocess alignment, preflight probes the OpenVoice interpreter.

## Decision

**Accepted Option 3:** OpenVoice uses **`VenvFamily.OPENVOICE` → `runtime/venvs/openvoice`**, manifest **`venv_family: venv_openvoice`**, **`ensure_openvoice`** subprocess-probes that interpreter, and synthesis uses **`OpenVoiceSubprocessEngine`** + **`app.cli.openvoice_worker_synthesize`** (same class of seam as [ADR-052](ADR-052-tortoise-isolated-venv-subprocess.md)). [ADR-053](ADR-053-openvoice-advanced-tts-packaging-surface.md) is **amended** for OpenVoice runtime surface (shared **`torch26`** is no longer the OpenVoice authority).

## Consequences

**Positive**

- Decouples OpenVoice’s **numpy/librosa/gradio/av** pin stack from **Chatterbox**-critical `torch26` contents.
- Allows a **separate** pin strategy (e.g. Linux-only CI vs Windows operator doc) per venv.

**Negative**

- More disk, more provisioning scripts, more CI/cache complexity.
- Duplicated PyTorch stack risk unless carefully pinned.

**Neutral**

- Matrix **`openvoice`** remains **pending** until import + checkpoints + live proofs after implementation.

## Related

- [ADR-053](ADR-053-openvoice-advanced-tts-packaging-surface.md) — current `torch26` surface (may be superseded in part).
- [ADR-052](ADR-052-tortoise-isolated-venv-subprocess.md) — precedent.
- [ADR-044](ADR-044-supply-chain-integrity.md) — hashes when `requirements_engines.txt` changes.
