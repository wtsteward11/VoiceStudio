# Bounded Slice 19H — OpenVoice isolated-venv provisioning unblocker

**Status:** Closed — **Outcome B** (2026-04-22) — **Strategy A** stock **`pip install myshell-openvoice`** fails on Windows cp311 at **`av==10.*`** / **PyAV** **Cython `CompileError`** (`av\logging.pyx`); **`openvoice`** not importable in **`venv_openvoice`**; checkpoints not laid; preflight red; **`real_openvoice` 2 skipped**, C# **3 skipped**. **Strategy B** (fork / ADR / ADR-044) is the **mandatory** next lane — not executed here.  
**Depends on:** [VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md](VOICESTUDIO_BOUNDED_SLICE19F_OPENVOICE_ISOLATED_VENV.md), [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md) (**19G** closed Outcome B), [ADR-054](../architecture/decisions/ADR-054-openvoice-isolated-venv-proposal.md).

## Goal

Move OpenVoice from **correct preflight wiring + empty/failed `venv_openvoice`** to one honest outcome:

### Outcome A

- `runtime/venvs/openvoice` contains a **complete** install: **`import openvoice`** and worker-heavy imports succeed in that interpreter (see import gates below).
- Checkpoint trees under **`{VOICESTUDIO_MODELS_PATH}/openvoice/base_speakers`** and **`.../openvoice/converter`** satisfy [`_openvoice_has_checkpoints`](../../backend/services/model_preflight.py).
- Fresh Uvicorn; **`GET /api/health/preflight`** → **`checks.openvoice.ok == true`**.
- **`pytest -m real_openvoice` 2/2 PASS** and C# OpenVoice **`LiveBackend` 3/3 PASS** on one **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`**.
- **File contract:** explicit verification that **`result is None`** + on-disk WAV is handled by [`SynthesisService.synthesize`](../../backend/services/synthesis_service.py) (see §19F / `_extract_quality_metrics` wave path when `audio` is `None` but file exists).
- Then: matrix **`openvoice` → PASS**, PROOF §19H, STATE — **only** with regression bar green.

### Outcome B

- **One** primary seam frozen with verbatim evidence (pip/`av`, resolver pins, import stack, checkpoint **424**, etc.).
- **No** matrix PASS; PROOF §19H + [`slice19h_proof_session.md`](../reports/verification/slice19/openvoice/slice19h_proof_session.md).

## Non-goals

- No new `real_openvoice` tests or markers.
- No backend **`.venv`** mutation for engine stacks.
- No RHVoice; no other engine slice.
- No matrix PASS without live Python + C# PASS lines.

---

## Phase 1 — Frozen authoritative install path (copy-paste)

| Element | Value |
| --- | --- |
| **Venv directory** | `<repo>/runtime/venvs/openvoice` |
| **Python version (family)** | **3.11** (`VENV_FAMILIES["openvoice"].python_version` in [`scripts/engines/create_engine_venv.py`](../../scripts/engines/create_engine_venv.py)) |
| **Create / recreate venv** | From repo root: `python scripts/engines/create_engine_venv.py --family openvoice` — add **`--force`** to delete and recreate an existing tree. |
| **What the script does** | Staged install: all lines of `family.requirements` **except the last** via a temp `-base.txt`, then **`pip install <last line>`** alone for **`myshell-openvoice @ git+...`**. |
| **Git pin (authoritative)** | `myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23` — same commit as [`config/venv_families/requirements-openvoice.txt`](../../config/venv_families/requirements-openvoice.txt) line 10. |
| **Manual install (equivalent)** | `runtime\venvs\openvoice\Scripts\pip.exe install -r config\venv_families\requirements-openvoice.txt` (Windows) after venv exists — **order differs** from script staging; prefer **`create_engine_venv`** for parity with CI/docs. |
| **Preflight import probe** | `from openvoice.api import BaseSpeakerTTS, ToneColorConverter` in **`runtime\venvs\openvoice\Scripts\python.exe`** (matches `ensure_openvoice`). |
| **Known failure class (§19E)** | Upstream **`install_requires`** includes **`faster-whisper==0.9.0`** → resolver **`av==10.*`**; Windows **cp311** often hits **sdist** build → **Cython `CompileError`** in PyAV — see [`slice19e_openvoice_dependency_graph.md`](../reports/verification/slice19/openvoice/slice19e_openvoice_dependency_graph.md). |
| **Heavier than preflight** | Worker [`app/cli/openvoice_worker_synthesize.py`](../../app/cli/openvoice_worker_synthesize.py) → [`OpenVoiceEngine`](../../app/core/engines/openvoice_engine.py) → **`se_extractor`** / **`faster_whisper`**. Green preflight **does not** guarantee green worker until that chain imports in **`venv_openvoice`** with **`PYTHONPATH=<repo>`**. |

---

## Phase 2 — Provisioning strategy (single choice for this slice)

| Strategy | Definition |
| --- | --- |
| **A** | **Stock** MyShell-OpenVoice at pinned Git commit in **`venv_openvoice`** only — resolve **`av`** / pins on Windows within [free-only](../../.cursor/rules/core/free-only.mdc) constraints (prebuilt wheels, documented build prereqs, constrained pip flags). **No** silent engine fallback. |
| **B** | **Controlled fork or patched distribution** (relax `install_requires`, vendor `se_extractor`, or replace faster-whisper chain) — requires **new ADR**, [ADR-044](../architecture/decisions/ADR-044-supply-chain-hashes.md) hash discipline if requirements change — **out of scope for 19H unless 19H explicitly adopts B** in a follow-up commit within the same bounded slice. |

**Selection for Slice 19H execution:** **Strategy A** — attempt stock provision first. If the host reproducibly fails at the **`av`** seam after an honest staged install, **close 19H as Outcome B** with verbatim logs and record **Strategy B** as the **mandatory next bounded slice** (ADR-first), without mixing undocumented local hacks into `main`.

---

## Verification order

1. Run import gates in **`venv_openvoice`** (preflight + engine + optional `faster_whisper`).
2. Lay checkpoints (operator weights) under **`VOICESTUDIO_MODELS_PATH`**.
3. Fresh Uvicorn + capture **`slice19h_preflight_openvoice.json`**.
4. If **`checks.openvoice.ok`**: `pytest -m real_openvoice` then `dotnet test` OpenVoice `LiveBackend`.
5. Document **file-return** seam (code path citation or live log).
6. Regression bar: `dotnet build`, `python scripts/run_verification.py`, `.\scripts\verify.ps1 -Quick`.

## References

- Dependency truth: [slice19e_openvoice_dependency_graph.md](../reports/verification/slice19/openvoice/slice19e_openvoice_dependency_graph.md).
- Session log: [`slice19h_proof_session.md`](../reports/verification/slice19/openvoice/slice19h_proof_session.md).
- Preflight capture: [`slice19h_preflight_openvoice.json`](../reports/verification/slice19/openvoice/slice19h_preflight_openvoice.json).

## Closure (this session)

- **Evidence:** [`slice19h_proof_session.md`](../reports/verification/slice19/openvoice/slice19h_proof_session.md); pip log [`slice19h_pip_myshell_attempt.log.txt`](../reports/verification/slice19/openvoice/slice19h_pip_myshell_attempt.log.txt); preflight [`slice19h_preflight_openvoice.json`](../reports/verification/slice19/openvoice/slice19h_preflight_openvoice.json).
- **PROOF / matrix / STATE:** [PROOF_SLICE19_OPENVOICE_AUDITION.md](../reports/verification/PROOF_SLICE19_OPENVOICE_AUDITION.md) §19H; [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) **`openvoice` pending**; [.cursor/STATE.md](../../.cursor/STATE.md) updated.

## Changelog

| Date | Note |
| --- | --- |
| 2026-04-22 | Initial bounded brief: frozen install table, Strategy A vs B, Outcome A/B, verification order. |
| 2026-04-22 | **Closed Outcome B** — Strategy A fails at **`av`** build; Strategy B deferred to ADR-led slice; session + artifacts. |
