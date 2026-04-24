# Bounded Slice 19F — OpenVoice isolated venv + subprocess runtime

**Status:** Implementation landed (2026-04-21) — **matrix `openvoice` remains pending** until green preflight + live proofs.  
**Governance:** [ADR-054](../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) (**Accepted**); amends [ADR-053](../architecture/decisions/ADR-053-openvoice-advanced-tts-packaging-surface.md) runtime surface for OpenVoice.

## Goal

| Outcome | Meaning |
| --- | --- |
| **A** | `runtime/venvs/openvoice` exists; `myshell-openvoice` importable there; checkpoints under `<VOICESTUDIO_MODELS_PATH>/openvoice/{base_speakers,converter}`; **`checks.openvoice.ok == true`**; optional **`pytest -m real_openvoice`** + C# OpenVoice `LiveBackend` on one `VOICESTUDIO_REAL_XTTS_HTTP_BASE`. |
| **B** | Narrower blocker inside the **openvoice** venv (e.g. same **`av`** build on Windows cp311) — freeze verbatim evidence; matrix stays **pending**. |

## Non-goals

- No blind `pip install` churn into shared **`torch26`** for OpenVoice.
- No new OpenVoice pytest files or markers beyond existing **`real_openvoice`** harness.
- No mutation of backend **`.venv`** for engine stacks.
- No RHVoice scope.

## Runtime surface (authoritative)

| Component | Location |
| --- | --- |
| **Venv** | `VenvFamily.OPENVOICE` → `runtime/venvs/openvoice` (provision key **`openvoice`** in `scripts/engines/create_engine_venv.py`) |
| **Requirements** | [config/venv_families/requirements-openvoice.txt](../config/venv_families/requirements-openvoice.txt) |
| **Preflight** | [`ensure_openvoice`](../../backend/services/model_preflight.py) uses `_require_venv_openvoice_python_exe()` |
| **Synthesis** | Manifest `entry_point`: `app.core.engines.openvoice_subprocess_engine.OpenVoiceSubprocessEngine` → worker [`app/cli/openvoice_worker_synthesize.py`](../../app/cli/openvoice_worker_synthesize.py) |

## Operator provisioning

1. `python scripts/engines/create_engine_venv.py --family openvoice` (from repo root).
2. `runtime\venvs\openvoice\Scripts\pip.exe install -r config\venv_families\requirements-openvoice.txt` (Windows).
3. Import probe (matches preflight):  
   `runtime\venvs\openvoice\Scripts\python.exe -c "from openvoice.api import BaseSpeakerTTS, ToneColorConverter; print('ok')"`
4. Checkpoints: `<models>/openvoice/base_speakers` and `.../converter` — each tree must satisfy `_openvoice_has_checkpoints` in `model_preflight` (`config.json` + sibling `checkpoint.pth` / `checkpoint.ckpt` / `model.pth`).
5. Fresh Uvicorn + `GET /api/health/preflight` → **`checks.openvoice.ok === true`** before **`real_openvoice`** / C# live (same port discipline as [slice19b_proof_session.md](../reports/verification/slice19/openvoice/slice19b_proof_session.md)).

## Verification order

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. `python scripts/run_verification.py`
3. `.\scripts\verify.ps1 -Quick`
4. Preflight JSON artifact under `docs/reports/verification/slice19/openvoice/` when capturing proof.
5. `pytest -m real_openvoice` then `dotnet test` OpenVoice `LiveBackend` filter — **only** if step 4 is green.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-21 | Initial slice: `VenvFamily.OPENVOICE`, manifest `venv_openvoice`, subprocess engine + worker, preflight wiring, tests. |
