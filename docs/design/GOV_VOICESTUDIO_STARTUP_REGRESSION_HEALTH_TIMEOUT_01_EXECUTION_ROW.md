# GOV-VOICESTUDIO-STARTUP-REGRESSION-HEALTH-TIMEOUT-01 — Startup regression (live health)

## 0. Status

- **State:** **Closed** (2026-03-31) — closure [VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_CLOSURE_2026-03-31.md)
- **Opened:** 2026-03-31
- **Supersedes for live-start trust:** [GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md) closure did not require a live uvicorn + desktop health probe; this lane restores trust against `Backend started but did not become healthy within timeout`.

---

## 1. Objective

Diagnose and fix persistent desktop health-timeout failures by proving machine-truth behavior and removing unnecessary blocking work before the ASGI server accepts `GET /health`.

---

## 2. Root cause (frozen)

1. **Blocking startup before accept:** Starlette/FastAPI lifespan runs the pre-yield phase before the server handles requests. The previous `on_startup` path included **engine manifest load**, **route conflict scan**, **OpenAPI contract validation**, and **plugin load** in that blocking phase, so `/health` could not succeed until all of that finished—often beyond the desktop health budget on cold machines (historical note: **45s** at time of this lane; **current** `StartupReadinessTimeoutSeconds` **60** + UI boundary proof [VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md](../reports/verification/VOICESTUDIO_UI_STARTUP_BOUNDARY_2026-04-05.md)).
2. **Loopback mismatch risk:** Uvicorn is spawned with `--host 127.0.0.1` while defaults used `http://localhost:8000`, which can misbehave on Windows IPv6 resolution. Defaults now align to `127.0.0.1`.

---

## 3. Targeted fix (frozen)

- **`backend/api/main.py`:** ASGI `lifespan` runs `on_startup_prepare`, yields (server accepts traffic), schedules `on_startup_heavy` as a background task, awaits it on shutdown before `on_shutdown`.
- **`backend/api/lifecycle.py`:** Split into `on_startup_prepare` (DB, security, scheduler, route registration hook, training broadcaster) and `on_startup_heavy` (sanity checks, engines, route validator, contract validation, plugins). `on_startup` remains sequential for manual/test use.
- **`BackendClientConfig`:** `DefaultHttpBaseUrl` / `DefaultWebSocketUrl` constants; **`SettingsService`**, **`BackendProcessManager`**, and **all previous `localhost:8000` fallbacks** in the app project now use the constant or env `VOICESTUDIO_BACKEND_URL` ( **`launchSettings`** / **`.vscode`** parity).

---

## 4. Verification

See closure report §2 for the full matrix (pytest, dotnet startup tests, `verify.ps1 -Quick`, `run_verification.py`, live uvicorn `/health` timing).

---

## 5. Out of scope

Installer, commercial packaging, broad route registry cleanup (e.g. optional drift/search import noise), unrelated GAP rows.
