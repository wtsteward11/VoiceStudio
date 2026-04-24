# Bounded Slice 19C — OpenVoice advanced-venv provisioning

**Status:** Living operator slice (provisioning + re-proof; no new harness code)  
**Date:** 2026-04-21  
**Supersedes:** Nothing — extends [VOICESTUDIO_BOUNDED_SLICE19_OPENVOICE_SUPPORT_CONTRACT.md](VOICESTUDIO_BOUNDED_SLICE19_OPENVOICE_SUPPORT_CONTRACT.md) and [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) §19B.

## Goal

Move OpenVoice from **Slice 19B Branch B** (`ModuleNotFoundError: No module named 'openvoice'` in `runtime/venvs/torch26`) to either:

- **Outcome A:** `myshell-openvoice` is importable in **`venv_advanced_tts`** (resolved to **`runtime/venvs/torch26`** on this repo), checkpoint trees under `<models>/openvoice/{base_speakers,converter}` satisfy `ensure_openvoice`, **`checks.openvoice.ok == true`** on a fresh backend, then optional **Python `real_openvoice` 2/2** + **C# OpenVoice `LiveBackend` 3/3** on one `VOICESTUDIO_REAL_XTTS_HTTP_BASE`.
- **Outcome B:** A single documented next blocker (pip conflict, wrong models root, checkpoint layout, synthesis/timeout, etc.) — matrix **`openvoice`** stays **pending**.

## Authoritative runtime surface (no drift)

| Surface | Role |
| --- | --- |
| **`VenvFamily.ADVANCED_TTS` / `venv_advanced_tts`** | Family resolved to **`runtime/venvs/torch26`** per `venv_family_manager` — **this** Python runs `ensure_openvoice` subprocess import probe. |
| **FastAPI worker `.venv`** | **Not** the OpenVoice import authority for preflight. Do not install OpenVoice only there and expect green `checks.openvoice`. |
| **`VOICESTUDIO_MODELS_PATH`** (or ProgramData default) | Checkpoint root for `openvoice/base_speakers` and `openvoice/converter` per `ensure_openvoice` in [`backend/services/model_preflight.py`](../../backend/services/model_preflight.py). |

## Provisioning steps (operator)

1. **Interpreter:** `runtime\venvs\torch26\Scripts\python.exe` (Windows).
2. **Package:** Install pinned MyShell OpenVoice from [`requirements_engines.txt`](../../requirements_engines.txt) (line with `myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147...`).
3. **Sanity probe (matches backend):**  
   `runtime\venvs\torch26\Scripts\python.exe -c "from openvoice.api import BaseSpeakerTTS, ToneColorConverter; print('ok')"`
4. **Conflict watch:** Same file documents NumPy / PyTorch tension with OpenVoice — if `pip` fails, capture verbatim output for §19C Branch B; do not claim matrix PASS.

## Checkpoints

Under resolved models root, each of **`openvoice/base_speakers`** and **`openvoice/converter`** must pass `_openvoice_has_checkpoints`: at least one `config.json` with a sibling `checkpoint.pth`, `checkpoint.ckpt`, or `model.pth` (possibly nested; see `rglob` in code).

## Re-proof sequence (after import + checkpoints)

1. Free port + **one** fresh Uvicorn (`127.0.0.1:<port>`); avoid trusting stale **8031** without restart — see [slice19b_proof_session.md](../reports/verification/slice19/openvoice/slice19b_proof_session.md).
2. `VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:<port>` in the same shell for all commands.
3. `GET .../api/health/preflight` → **`checks.openvoice.ok === true`** (hard stop if false).
4. `pytest -m real_openvoice` then `dotnet test` OpenVoice `LiveBackend` filter.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial bounded provisioning slice doc (post–19B Branch B). |
| 2026-04-21 | **Branch B session:** `pip install` pinned OpenVoice failed on **`av`** / PyAV wheel build (**Cython**); host lacked **`models/openvoice/`** trees — evidence [`slice19c_proof_session.md`](../reports/verification/slice19/openvoice/slice19c_proof_session.md). |
