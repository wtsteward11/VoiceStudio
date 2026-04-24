# Slice 19G — OpenVoice isolated runtime proof (session log)

**Date:** 2026-04-22  
**Brief:** [VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19G_OPENVOICE_ISOLATED_RUNTIME_PROOF.md)

## Outcome

- [x] **Outcome B** — Primary seam: **`myshell-openvoice` / `faster-whisper` / `av`** install chain on Windows cp311 (same class as §19C/§19E) **plus** empty **`venv_openvoice`** after failed/partial install → **`ModuleNotFoundError: No module named 'openvoice'`** in `runtime\venvs\openvoice\Scripts\python.exe`. **`E:\VoiceStudio\models\openvoice\`** checkpoint trees **not** laid (would fail **424-class** after import even if pip succeeded).
- [ ] **Outcome A** — not satisfied; matrix **`openvoice`** remains **pending**.

## Canonical backend URL (this session)

| Field | Value |
| --- | --- |
| **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** | `http://127.0.0.1:8036` |
| **Uvicorn** | `E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8036` with **`PYTHONPATH=E:\VoiceStudio`**, cwd repo root. |
| **Stale-port warning** | Do not trust long-lived **8031/8032** without restart on current code. |

## Execution log

### 0. Preflight wiring fix (truth — same session)

`GET /api/health/preflight` and `scripts/engine_readiness_probe.py` still imported **`ensure_openvoice`** from **`backend.ml.models.model_preflight`** (stale **`venv_advanced_tts` / torch26** narrative). **Fixed** to **`backend.services.model_preflight`** (ADR-054 / Slice 19F authority) in:

- `backend/api/routes/health.py`
- `scripts/engine_readiness_probe.py`

After fix, preflight **`checks.openvoice`** reports **`python_exe`:** `E:\VoiceStudio\runtime\venvs\openvoice\Scripts\python.exe` and import failure in **that** venv (not `torch26`).

### 1. Venv provision (`runtime/venvs/openvoice`)

- Command: `python scripts/engines/create_engine_venv.py --family openvoice` (per bounded brief); then `pip install -r config/venv_families/requirements-openvoice.txt` into **that** venv.
- **Result:** Prior attempt did not complete **`myshell-openvoice`** install (PyAV **`av==10.*`** sdist / Cython **`CompileError`** class — see §19C/§19E). **`runtime\venvs\openvoice\Scripts\python.exe`** exists; **`import openvoice`** → **`ModuleNotFoundError`**.
- **Sanity:** Wrong ref `...@v2` on Git URL fails fast (`pathspec 'v2' did not match`); repo pins **`@74a1d147…`** in `requirements-openvoice.txt` — use that line for real retries.

### 2. Checkpoints

- **`VOICESTUDIO_MODELS_PATH`:** `E:\VoiceStudio\models` (from captured preflight env).
- **`openvoice/base_speakers`** and **`openvoice/converter`:** **not present** on this host under that root — not laid in this slice (blocked after import gate anyway).

### 3. Uvicorn + preflight

- Captured verbatim JSON: [`slice19g_preflight_openvoice.json`](slice19g_preflight_openvoice.json) (**timestamp ~2026-04-22T00:53:24Z**, port **8036**).
- **`checks.openvoice.ok`:** **`false`** — message includes **`OpenVoice import failed in venv_openvoice`** and **`ModuleNotFoundError: No module named 'openvoice'`**.

### 4. Python `real_openvoice`

- Command: `python -m pytest tests/integration/test_synthesis_openvoice_real.py -m real_openvoice -v --tb=short` with **`VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8036`** (backend up on **8036**).
- **Result:** **2 skipped** (preflight gate — `checks.openvoice.ok` not true).

### 5. C# OpenVoice LiveBackend

- Command: `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OpenVoice&TestCategory=LiveBackend"` with same **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`**.
- **Result:** **3 skipped** (same preflight gate).

### 6. File contract (`synthesize` returns `None`)

- **Not live-verified** in 19G — synthesis never ran (preflight red).
- **Design intent (unchanged):** `OpenVoiceSubprocessEngine.synthesize` returns **`None`** on worker success; **`SynthesisService.synthesize`** continues when **`result is None`** and **`_synth_output_file_ready(output_path)`** — see §19F in [PROOF_SLICE19_OPENVOICE_AUDITION.md](../../PROOF_SLICE19_OPENVOICE_AUDITION.md). **Slice 19G Outcome A** would require logs or debugger proof on a green stack.

## Regression bar (post-doc + import fix)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors** (warnings only).
- `python scripts/run_verification.py` → **PASS** (`.buildlogs/verification/last_run.json`).
- `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** — report `artifacts/verify/20260421_195838/verification_report.md`.
