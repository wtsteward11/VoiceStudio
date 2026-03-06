# Golden Path Engine Blocker: Diagnosis and Fix

## Critical: Backend Failed to Start

Your last run showed:
```
ERROR: [Errno 10048] error while attempting to bind on address ('127.0.0.1', 8000): only one usage of each socket address is normally permitted
```

**The backend never started.** The E2E tests ran against an **old backend** already on port 8000 (likely without `VOICESTUDIO_MODELS_PATH` set).

**First step: kill the existing process and restart.**

```powershell
# Find and kill process on port 8000
Get-NetTCPConnection -LocalPort 8000 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
# Or: netstat -ano | findstr :8000  then taskkill /PID <pid> /F
```

---

## Root Cause 1: Path Mismatch

Preconditions found whisper at `E:\VoiceStudio\models\whisper\` (project root fallback). Backend defaults to `C:\ProgramData\VoiceStudio\models`. Align them:

```powershell
$env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000
```

---

## Root Cause 2: Whisper Engines Not Installed

Startup logs show:
- `whisper-cpp-python not installed` → whisper_cpp fails
- Test falls back to `whisper` (faster_whisper) → also 503

faster_whisper is installed, but `get_whisper_engine()` may fail if the engine router or service path is broken. Install whisper-cpp-python for the primary path:

```powershell
pip install whisper-cpp-python
```

---

## Root Cause 3: XTTS + PyTorch/CUDA Mismatch

```
NVIDIA GeForce RTX 5070 Ti with CUDA capability sm_120 is not compatible with the current PyTorch installation.
The current PyTorch install supports sm_50 sm_60 sm_61 sm_70 sm_75 sm_80 sm_86 sm_90.
```

**RTX 5070 Ti (Blackwell, sm_120)** is not supported by your PyTorch. XTTS may fail when initializing on GPU.

**Workaround: force CPU for inference** (slower but may work):

```powershell
$env:CUDA_VISIBLE_DEVICES = ""
$env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000
```

---

## Root Cause 4: Engine Router Warning (Non-blocking)

```
WARNING:app.core.audio.enhanced_ensemble_router:Failed to load engine router: 'NoneType' object has no attribute '__dict__'
```

This comes from `enhanced_ensemble_router.py` (a separate code path). The main synthesis uses `_helpers._ensure_engine_router()` which gets the router from `get_engine_service().get_engine_router()`. The enhanced_ensemble_router warning may be a red herring if synthesis works.

---

## Full Startup Sequence

```powershell
# 1. Kill existing backend
Get-NetTCPConnection -LocalPort 8000 -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

# 2. Set env and start backend (Terminal 1)
cd E:\VoiceStudio
.\.venv\Scripts\Activate.ps1
$env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
$env:CUDA_VISIBLE_DEVICES = ""   # Force CPU if XTTS fails on RTX 5070 Ti
python -m uvicorn backend.api.main:app --host 127.0.0.1 --port 8000
```

Wait for "Application startup complete" and **no** "error while attempting to bind".

```powershell
# 3. Run golden path (Terminal 2)
$env:VOICESTUDIO_MODELS_PATH = "E:\VoiceStudio\models"
python -m pytest tests/e2e/test_golden_path.py -v -p no:randomly -o addopts=

# 4. If E2E passes, regenerate proof
python scripts/ci/write_golden_path_real_proof.py
```

---

## If XTTS Still Fails After CPU Forcing

Check backend logs for the exact XTTS init error. Options:
- Upgrade PyTorch to a build that supports sm_120 (when available)
- Ensure XTTS model files exist at `E:\VoiceStudio\models\xtts\`
- Verify `COQUI_TOS_AGREED=1` is set (main.py sets it)
