# Slice 19H — OpenVoice venv provisioning (session log)

**Date:** 2026-04-22  
**Brief:** [VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md](../../../design/VOICESTUDIO_BOUNDED_SLICE19H_OPENVOICE_VENV_PROVISIONING.md)

## Strategy

**Strategy A** (stock Git pin `74a1d147…`) — **failed** on this host at **`av==10.*`** sdist / **Cython `CompileError`** in **`av\logging.pyx`** (same class as §19E). **Strategy B** (fork / metadata / supply-chain change) is **mandatory next bounded work** under **ADR + ADR-044** — **not** executed in 19H.

## Outcome

- [x] **Outcome B** — Primary seam: **`pip install myshell-openvoice`** → **`faster-whisper==0.9.0`** → **`av==10.*`** wheel prep → **Cython `CompileError`**: `Cannot assign type ... log_context_name` / `log_callback` (**`av\logging.pyx`**). Verbatim capture: [`slice19h_pip_myshell_attempt.log.txt`](slice19h_pip_myshell_attempt.log.txt).
- [ ] **Outcome A** — not achieved; matrix **`openvoice` pending**.

## Canonical backend URL

| Field | Value |
| --- | --- |
| **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`** | `http://127.0.0.1:8037` (pytest + dotnet ephemeral backend) and **`http://127.0.0.1:8038`** (preflight JSON capture — same code path). |
| **Stale-port warning** | Do not trust **8031/8032/8036** without restart on current code. |

---

## 1. Provision command log

**Command (stock pin, existing `venv_openvoice`):**

```powershell
& "E:\VoiceStudio\runtime\venvs\openvoice\Scripts\pip.exe" install `
  "myshell-openvoice @ git+https://github.com/myshell-ai/OpenVoice.git@74a1d147b17a8c3092dd5430504bd83ef6c7eb23"
```

**Result:** Exit **1**. Full stdout/stderr: [`slice19h_pip_myshell_attempt.log.txt`](slice19h_pip_myshell_attempt.log.txt).

**Not re-run:** `create_engine_venv.py --force` (would re-download base torch stack); failure class already isolated at **`myshell-openvoice`** dependency resolution identical to staged script’s final line.

---

## 2. Import gates (`runtime\venvs\openvoice\Scripts\python.exe`)

### 2a. Preflight-equivalent

```text
ModuleNotFoundError: No module named 'openvoice'
```

### 2b. Worker path (`PYTHONPATH=E:\VoiceStudio`)

`from app.core.engines.openvoice_engine import OpenVoiceEngine` — **exits 0** with repo-side warnings; **OpenVoice package still absent** in venv (`WARNING:... OpenVoice not installed`). **Not** a substitute for **`import openvoice`**.

### 2c. `faster_whisper`

```text
ModuleNotFoundError: No module named 'faster_whisper'
```

---

## 3. Checkpoints

**Not executed** — import/install gate failed first. **`E:\VoiceStudio\models\openvoice\`** trees still absent (same as §19G). Laying weights is **blocked** until **`myshell-openvoice`** installs.

---

## 4. Uvicorn + preflight JSON

- Backend: `E:\VoiceStudio\.venv\Scripts\python.exe -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8038`, `PYTHONPATH=E:\VoiceStudio`.
- Verbatim JSON: [`slice19h_preflight_openvoice.json`](slice19h_preflight_openvoice.json) — **`checks.openvoice.ok: false`**, **`ModuleNotFoundError: No module named 'openvoice'`** in **`venv_openvoice`**.

---

## 5. Python `real_openvoice`

- **`VOICESTUDIO_REAL_XTTS_HTTP_BASE=http://127.0.0.1:8037`**, ephemeral Uvicorn on **8037**.
- **Result:** **2 skipped** (preflight gate).

---

## 6. C# OpenVoice `LiveBackend`

- Same **`VOICESTUDIO_REAL_XTTS_HTTP_BASE`**, ephemeral backend **8037**.
- **Result:** **3 skipped** (preflight gate).

---

## 7. File contract (`None` + file-ready)

**Not live-tested** (no synthesis). **Static conclusion (unchanged):** When `result` is **`None`** but **`_synth_output_file_ready(output_path)`** is true, [`SynthesisService.synthesize`](../../../backend/services/synthesis_service.py) does not raise at the early guard; **`audio`** is **`None`** and **`_extract_quality_metrics`** reads duration from **`output_path`** via **`wave.open`** (lines ~638–179). **Slice 19H** does not disprove the file-driven contract; green worker proof remains for a future slice after **Strategy B** or successful **Strategy A** on a capable host.

---

## Regression bar (post-doc)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` → **0 errors** (warnings only).
- `python scripts/run_verification.py` → **PASS**.
- `.\scripts\verify.ps1 -Quick` → **VERIFICATION PASSED** — [`artifacts/verify/20260421_203242/verification_report.md`](../../../../../artifacts/verify/20260421_203242/verification_report.md).
