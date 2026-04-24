# Bounded Slice 19I — OpenVoice Strategy B (vendored / patched runtime)

**Status:** Closed — **Outcome B** for **live ladder** (2026-04-22): **Strategy B** + **ADR-055** green for **pip** / import / **`checks.openvoice`**; **`real_openvoice` + C# LiveBackend** **red** (synthesis **500** — placeholder weights). Matrix **`openvoice` pending**. See [PROOF §19I](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) and [session log](../reports/verification/slice19/openvoice/slice19i_proof_session.md).  
**Depends on:** [19H](../reports/verification/slice19/openvoice/slice19h_proof_session.md) (Strategy A **closed Outcome B** at **`av`/`faster-whisper`**) · [19G](VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md) · [ADR-054](../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md) · **[ADR-055](../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md)** (Strategy B decision)

## Outcomes

### Outcome A (runtime parity)

- **`runtime/venvs/openvoice`** provisioned with **vendored** [myshell-openvoice](../architecture/decisions/ADR-055-myshell-openvoice-vendored-patches.md) (not stock **git+**), **`pip` exit 0**.
- Import gates: `from openvoice.api import BaseSpeakerTTS, ToneColorConverter`; `from app.core.engines.openvoice_engine import OpenVoiceEngine` with `PYTHONPATH` = repo root; `from openvoice import se_extractor` without `ModuleNotFoundError` for default paths.
- Checkpoints under `<VOICESTUDIO_MODELS_PATH>/openvoice/{base_speakers,converter}` per `ensure_openvoice` / `_openvoice_has_checkpoints`.
- One fresh Uvicorn; **`GET /api/health/preflight`** → `checks.openvoice.ok == true` (artifact: [`slice19i_preflight_openvoice.json`](../reports/verification/slice19/openvoice/slice19i_preflight_openvoice.json)).
- `pytest -m real_openvoice` **2/2** + C# OpenVoice **LiveBackend 3/3** (same `VOICESTUDIO_REAL_XTTS_HTTP_BASE`).
- File contract: `OpenVoiceSubprocessEngine.synthesize` → `None` with file-ready path (Conclusion A or B with evidence).
- [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` → PASS** only if 8+9 are green.

### Outcome B (seam frozen)

- **Any** first failure: pip, import, checkpoints, preflight, HTTP, worker, or contract — **no** matrix PASS; document verbatim seam in [PROOF](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) **§19I** and [slice19i_proof_session.md](../reports/verification/slice19/openvoice/slice19i_proof_session.md).

## Non-goals

- No new `real_openvoice` tests.
- No backend **`.venv`** for engine wheels.
- No **RHVoice**; no other engine matrix row.
- No **matrix PASS** without live ladder evidence.
- **No** undocumented pip flags — install commands must match **ADR-055** + [requirements-openvoice.txt](../../config/venv_families/requirements-openvoice.txt) + [create_engine_venv.py](../../scripts/engines/create_engine_venv.py).

## Artifacts (this slice)

| Artifact | Role |
|----------|------|
| [slice19i_proof_session.md](../reports/verification/slice19/openvoice/slice19i_proof_session.md) | Operator log / commands / stderr |
| [slice19i_preflight_openvoice.json](../reports/verification/slice19/openvoice/slice19i_preflight_openvoice.json) | Verbatim preflight when backend run |

## Verification order (reference)

1. `python scripts/engines/create_engine_venv.py --family openvoice --force` (or `pip install -r config/venv_families/requirements-openvoice.txt` from **repo root** into the venv).
2. Import probes using **`runtime/venvs/openvoice/Scripts/python.exe`**.
3. Lay operator checkpoints; `ensure_openvoice` probe.
4. Fresh Uvicorn (dedicated port); save preflight JSON; gate **`checks.openvoice.ok`**.
5. If green: `pytest` then `dotnet test` (same base URL); then file-contract evidence; then matrix/PROOF/STATE; regression bar: `dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`.

## Changelog

| Date | Note |
|------|------|
| 2026-04-22 | Initial bounded brief — Strategy B; **ADR-055**; vendor path. |
