# Product Recovery Launch — 2026-04-16

## Summary

Three product recovery gates verified on local Windows machine.

## Gate 1: Windows Launch

- **Build**: `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` — 0 errors
- **Defect found**: `XamlParseException` — `VSQ.Button.NavToggle` style (TargetType=ToggleButton) applied to a `Button` in `MainWindow.xaml` line 47
- **Fix**: Changed to `VSQ.Button.Icon` style which targets `Button`
- **Result**: App shell renders — title bar, menu, toolbar, nav rail, panel layout, status bar
- **Proof**: `PRODUCT_RECOVERY_app_shell_screenshot.png`

## Gate 2: Backend Startup

- **Defect found**: `PydanticImportError` — `BaseSettings` moved to `pydantic-settings` package in Pydantic v2; fallback `except ImportError` didn't catch `PydanticImportError`
- **Fix**: Installed `pydantic-settings==2.13.1`; cleaned up `backend/core/settings.py` to use direct import
- **Standalone**: `uvicorn backend.api.main:app --host 127.0.0.1 --port 8000` — started, 62 engines loaded
- **Health**: `GET /health` → 200 `{"status":"ok","engines_ready":true}`
- **Health**: `GET /api/health` → 200 `{"status":"ok","version":"1.1.0","engines_ready":true}`
- **App-managed**: Backend starts via `BackendProcessManager`, health returns 200
- **Proof**: `PRODUCT_RECOVERY_app_backend_ready.png`

## Gate 3: Hero Workflow — Profiles CRUD

- **Create**: `POST /api/profiles` → 200, profile `13aae82b-f5f4-4a11-a05e-60a9061d087b` created
- **Read**: `GET /api/profiles/13aae82b-...` → 200
- **List**: `GET /api/profiles` → 200, 50 profiles returned
- **Persist**: JSON file exists at `%APPDATA%\VoiceStudio\data\profiles\13aae82b-...\profile.json`
- **UI**: Profiles panel loads and renders profile list with avatars, search, filtering
- **Proof**: `PRODUCT_RECOVERY_profiles_loaded.png`

## Code Changes

| File | Change |
|------|--------|
| `src/VoiceStudio.App/MainWindow.xaml` | Line 47: `VSQ.Button.NavToggle` → `VSQ.Button.Icon` (style TargetType mismatch fix) |
| `backend/core/settings.py` | Direct import from `pydantic_settings` instead of broken try/except fallback |

## Known Issues (Not Fixed — Out of Scope)

- `startup_decision.json` reports `spawn_failure` due to health probe racing backend startup; backend IS healthy
- Status bar shows "Starting..." even after backend is ready (false negative from startup decision)
- `aiosqlite` not installed (database init skipped; profile store uses JSON-on-disk, unaffected)
- `backend.ml.models.model_drift_detector` module missing (drift route fails to register; non-critical)
