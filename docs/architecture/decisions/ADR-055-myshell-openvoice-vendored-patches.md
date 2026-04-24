# ADR-055: MyShell OpenVoice vendored patches (Strategy B / Windows)

**Status:** Accepted  
**Date:** 2026-04-22  
**Decision makers:** System architecture + supply chain (ADR-044)  
**Related:** [ADR-054](ADR-054-openvoice-isolated-venv-proposal.md) (isolated venv), [ADR-044](ADR-044-supply-chain-integrity.md) (hashes, pins), [VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md](../design/VOICESTUDIO_BOUNDED_SLICE19I_OPENVOICE_STRATEGY_B_RUNTIME.md)

## Context

Stock `myshell-openvoice` from `git+https://github.com/myshell-ai/OpenVoice.git@74a1d147` declares `faster-whisper==0.9.0`, which pulls **PyAV (`av`)**; on Windows cp311 that chain fails to build (Cython `CompileError` in `av/logging.pyx` — see §19H evidence). [slice19e_openvoice_dependency_graph.md](../reports/verification/slice19/openvoice/slice19e_openvoice_dependency_graph.md) documents that `openvoice.se_extractor` also imported `faster_whisper` at **module** import time, so any install failure or missing `av` blocked `import openvoice` before runtime could branch.

## Options considered

1. **Fork on GitHub + git URL in requirements** — Traceable, but this repo was not required to add a new remote; an **in-repo vendor** is equally reproducible with a pinned commit SHA and documented refresh steps.
2. **Vendored subtree + `pip install -e`** — `runtime/vendor/myshell-openvoice` at upstream commit `74a1d147b17a8c3092dd5430504bd83ef6c7eb23`, with minimal patches (chosen).
3. **Replace faster-whisper stack** with an alternate API — Rejected for this slice: would require re-validating `se_extractor.get_se` and Whisper code paths; out of scope.

## Decision

- **Vendor** the upstream OpenVoice tree at **commit `74a1d147b17a8c3092dd5430504bd83ef6c7eb23`** under **`runtime/vendor/myshell-openvoice/`**.
- **Patch `setup.py`:** remove **`faster-whisper`** from `install_requires`; add **`extras_require['whisper'] = ['faster-whisper==0.9.0']`** for optional full parity. Relax **numpy** and **librosa** lower bounds to align with VoiceStudio’s torch/venv stack (`numpy>=1.24.0,<2.0`, `librosa>=0.10.0`).
- **Patch `openvoice/se_extractor.py`:** **lazy-import** `WhisperModel` inside `split_audio_whisper` only; no top-level `faster_whisper` import. Default **`get_se(..., vad=True)`** uses VAD and does not require `faster_whisper` on import. **`vad=False`** still requires the optional extra if `faster_whisper` is to run.
- **Provisioning:** `config/venv_families/requirements-openvoice.txt` and `scripts/engines/create_engine_venv.py` use **`-e <repo>/runtime/vendor/myshell-openvoice`** (absolute path in the script) instead of the stock **git+** line.
- **Re-apply script:** `scripts/engines/apply_myshell_openvoice_vendor_patches.py` re-applies edits if the vendor tree is refreshed from upstream.

## Consequences

**Positive**

- `import openvoice` and `import openvoice.se_extractor` succeed in `venv_openvoice` without building **PyAV** for the default code paths.
- Reproducible: commit SHA + vendor path + patch script; ADR-044 still governs any change to the requirements **lines** and CI hashes when applicable.

**Negative / operations**

- **Upstream updates** are manual: refresh vendor, re-run **apply_myshell_openvoice_vendor_patches.py**, re-verify imports and tests.
- Operators who need **Whisper** segmentation (`vad=False`) must install **`MyShell-OpenVoice[whisper]`** into the same venv (brings `faster-whisper` and its stack); that path may still fail on hosts without a working **av** build — documented, not a silent fallback.

**Neutral**

- [openvoice_engine.py](../../app/core/engines/openvoice_engine.py) can keep `from openvoice import se_extractor` at module level once **se_extractor** no longer forces **faster_whisper** at import time.

## Appendix: Patch surface checklist

| # | File | Change |
|---|------|--------|
| 1 | `runtime/vendor/myshell-openvoice/setup.py` | Drop mandatory `faster-whisper`; optional extra; relax numpy/librosa. |
| 2 | `runtime/vendor/myshell-openvoice/openvoice/se_extractor.py` | Lazy `faster_whisper.WhisperModel` inside `split_audio_whisper`. |
| 3 | `config/venv_families/requirements-openvoice.txt` | Point to **`-e ./runtime/vendor/myshell-openvoice`** (run pip from repo root). |
| 4 | `scripts/engines/create_engine_venv.py` | Same intent via `OPENVOICE_VENDOR_SRC` for staged install. |
| 5 | `scripts/engines/apply_myshell_openvoice_vendor_patches.py` | Re-apply 1–2 after vendor refresh. |

**VoiceStudio** engine file changes were **not** required once **se_extractor** is lazy at import time.

## Related ADRs

- [ADR-054](ADR-054-openvoice-isolated-venv-proposal.md)  
- [ADR-044](ADR-044-supply-chain-integrity.md)  
- [ADR-053](ADR-053-openvoice-advanced-tts-packaging-surface.md) (packaging / capability context)
