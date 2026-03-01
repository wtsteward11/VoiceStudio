# CI Guardrails (Milestone 1)

This document describes the CI guardrails that prevent "prototype soup" from regrowing in the VoiceStudio codebase.

## What Is Banned

### 1. Route-to-Route Imports

- `from ..routes import ...`
- `from .voice import synthesize`, `from .audio import ...`, etc.
- Routes must not import other routes for functionality. Use services instead.

### 2. sys.path.insert

- Any `sys.path.insert(...)` in `backend/api/routes/**/*.py`
- Use proper module dependencies; no runtime path surgery.

### 3. Repo-Relative Persistent Writes

- `Path("backups")`, `Path("data")`, `Path("data/...")`
- `os.path.join("data", ...)`
- `open("data/...", ...)`
- `os.makedirs("data"...)`
- Use `backend.config.path_config.get_path(...)` instead.

## Why

- **Path spine**: All persistent writes must go through `get_path(...)` so files live outside the repo.
- **Artifact spine**: Audio and model artifacts must go through registry/store services.
- **Cursor brick prevention**: Payload dirs (backups, data/audio_uploads, installer/runtime) and oversized files cause IDE indexing failures.

## How to Run Locally

```powershell
python scripts/ci/check_route_boundaries.py
python scripts/ci/check_repo_payloads.py
```

Migration bypass (local only; CI does not set this):

```powershell
$env:VOICESTUDIO_SKIP_ROUTE_BOUNDARIES = "1"
python scripts/ci/check_route_boundaries.py  # exits 0
```

## Allowlist (check_repo_payloads)

To grandfather existing payload files:

```powershell
python scripts/ci/check_repo_payloads.py --update-allowlist
```

- Use only when intentionally allowing existing debris (e.g., after a one-time migration).
- Do not abuse: new payloads should be moved outside the repo, not allowlisted.
- CI runs in strict mode (no updates).

## Example Failure Output

### check_route_boundaries.py

```
backend/api/routes/training.py:680: sys.path.insert (use proper module imports): sys.path.insert(0, str(app_path))
backend/api/routes/search.py:33: route-to-route import (from ..routes): from ..routes.markers import _markers as _m
```

### check_repo_payloads.py

```
NEW PAYLOAD: backups/backup-xxx.zip (add to allowlist or remove)
OVERSIZED: some_file.bin (30MB > 25MB)
ALLOWLIST GROWTH: data/voicestudio_state.db grew beyond +5MB (was 1024, now 6291456)
```

## Known Violations (Initial)

The following route files contain `sys.path.insert` and require remediation:

- `models.py`
- `training.py`
- `waveform.py`
- `ssml.py`
- `voice.py`
- `voice/synthesis.py`
- `voice/processing.py`
- `voice/analysis.py`
- `voice/_helpers.py`
- `dubbing.py`
- `sonography.py`
- `realtime_visualizer.py`
- `model_inspect.py`
- `profiles.py`
- `text_speech_editor.py` (route-to-route: from .voice)
- `voice_cloning_wizard.py` (route-to-route: from .voice)
- `quality.py` (route-to-route: from ..routes.profiles)
- `search.py` (route-to-route: from ..routes.markers, profiles, script_editor)
- `voice.py` (route-to-route: from ..routes.profiles)

Remediation: Move engine imports to backend services; use `EngineService` and proper module structure.
