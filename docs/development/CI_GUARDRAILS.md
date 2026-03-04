# CI Guardrails (Milestone 1)

This document describes the CI guardrails that prevent "prototype soup" from regrowing in the VoiceStudio codebase.

## What Is Banned

### 1. Route-to-Route Imports

The checker flags ANY import from one route module to another, including:

- `from ..routes import ...` / `from ..routes.<module> import ...`
- `from .<module> import ...` (e.g. `from .profiles import ...`, `from .voice import ...`)
- `from backend.api.routes.<module> import ...`
- `import backend.api.routes.<module>`

**Allowed:** Only underscore-prefixed internal modules: `_persistent_store`, `_engine_shared`, `_shared`, `_helpers`, etc. These are shared infrastructure, not route handlers.

Routes must not import other routes for functionality. Use services instead.

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

## Artifact Spine Compliance (M6)

`scripts/ci/check_artifact_spine_compliance.py` and `tests/guardrails/test_route_table_and_spine.py` enforce that route files do not reintroduce manual artifact creation patterns.

### Forbidden Patterns

- **`_register_audio_file`** (def or call) — use `create_audio_artifact_from_wav_array` / `create_audio_artifact_from_file` instead.
- **`_audio_storage`** — use `AudioRegistry` / spine.
- **`sf.write(path, ...)`** in routes — use spine helpers (which write to BytesIO internally).
- **`tempfile.mktemp(...)`** — use `tempfile.NamedTemporaryFile` or spine.
- **`open(path, "wb").write(...)`** for final audio output — use spine.

### Why

Ensures all audio-producing routes use the artifact spine; prevents manual tempfile+sf.write+register patterns from returning.

### How to Run Locally

```powershell
python scripts/ci/check_artifact_spine_compliance.py
python -m pytest tests/guardrails/test_route_table_and_spine.py -q
```

## Route Enumeration Tests (M6)

`tests/contract/test_route_enumeration.py` enumerates routes whose response model contains `audio_id` and verifies:

1. **Enumeration** — At least one route returns `audio_id` (e.g. `/api/voice/synthesize`).
2. **Compliance** — Route source files pass artifact spine compliance.
3. **Resolution** — `AudioRegistry.get_path(audio_id)` resolves for registered artifacts.

### How to Run Locally

```powershell
python -m pytest tests/contract/test_route_enumeration.py -q
```

## How to Run Locally (Route Boundaries + Repo Payloads)

```powershell
python scripts/ci/check_route_boundaries.py
python scripts/ci/check_repo_payloads.py
```

Migration bypass (local only; CI does not set this):

```powershell
$env:VOICESTUDIO_SKIP_ROUTE_BOUNDARIES = "1"
python scripts/ci/check_route_boundaries.py  # exits 0
```

## Repo Payload Policy (check_repo_payloads)

Uses `.ci/repo_payload_policy.json` — **not** the legacy allowlist. The policy enforces two invariants:

1. **Payload dirs** (backups/, data/audio_uploads/, data/recordings/, installer/runtime/, installer/runtime__DISABLED/) must not grow unnoticed.
2. **Large files** (>25MB) must not be auto-allowlisted. Any exception must be explicit and justified.

### Policy Schema

- **settings**: `max_file_mb` (default 25), `growth_threshold_mb` (default 5)
- **payload_dir_baselines**: Per-dir config with `path`, `mode` ("forbidden" | "baseline"), `baseline_file_count`, `baseline_total_bytes`, `max_growth_bytes`. Small dirs (backups, data/audio_uploads, data/recordings) may include a `manifest` of allowed file paths.
- **large_file_exceptions**: Explicit list of paths allowed to exceed max size. Each entry requires `path`, `stored_size_bytes`, and `justification`.

### Why Auto-Allowlist Is Banned

The legacy `--update-allowlist` mode auto-added every payload and large file, turning the allowlist into a landfill. The new policy forbids that: every large-file exception must be added manually with a justification. CI fails if a >25MB file exists and is not in the list.

### How to Update Baselines Safely

- **`--update-baselines`**: Updates `payload_dir_baselines` (counts, bytes, manifests for small dirs). Does **not** add new large-file exceptions.
- **`--refresh-large-file-sizes`**: Updates `stored_size_bytes` only for paths already in `large_file_exceptions`. Does **not** add new paths.

```powershell
python scripts/ci/check_repo_payloads.py --update-baselines
python scripts/ci/check_repo_payloads.py --refresh-large-file-sizes
```

### installer/runtime

- `installer/runtime` is gitignored.
- CI never sets `VOICESTUDIO_ALLOW_REPO_RUNTIME`; the dir must be empty in CI.
- Local dev: run `prepare-runtime.ps1` only if needed; set `VOICESTUDIO_ALLOW_REPO_RUNTIME=1` before `check_repo_payloads.py` if the dir is populated.
- Do not commit `installer/runtime` contents.

### installer/runtime__DISABLED

Must not grow. Cleanup is a separate manual milestone. The policy baselines it; any growth fails CI.

### Adding a New Large-File Exception

Edit `.ci/repo_payload_policy.json` manually. Add an entry to `large_file_exceptions` with `path`, `stored_size_bytes`, and `justification`. There is no automated "add everything big" mode.

### Post-M8: large_file_exceptions Expected to Be Empty

After Milestone 8 (Repo Payload Detox), `large_file_exceptions` must be empty. Heavyweight binaries are migrated to `VOICESTUDIO_PAYLOADS_ROOT` and replaced with pointer files. See [PAYLOADS.md](PAYLOADS.md) for migration and restore scripts.

## Example Failure Output

### check_route_boundaries.py

```
backend/api/routes/training.py:680: sys.path.insert (use proper module imports): sys.path.insert(0, str(app_path))
backend/api/routes/search.py:33: route-to-route import (from ..routes): from ..routes.markers import _markers as _m
```

### check_repo_payloads.py

```
PAYLOAD GROWTH: backups file count 8 > baseline 7
NEW FILE IN PAYLOAD DIR: backups/backup-new.zip (not in manifest)
OVERSIZED: some_file.bin (30MB > 25MB, add to large_file_exceptions with justification)
EXCEPTION GROWTH: tests/assets/foo.wav grew beyond +5MB (was 1000000, now 7000000)
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
