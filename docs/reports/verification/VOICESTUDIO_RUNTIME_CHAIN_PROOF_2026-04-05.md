# VoiceStudio runtime chain proof — subprocess FastAPI health (2026-04-05)

**Purpose:** Record **live** evidence that `backend.api.main:app` binds and serves health endpoints after the closure-wave commits. This complements (does not replace) WinUI startup proofs (`BackendProcessManager`, `verify.ps1` failure smokes, icon-launch scripts).

## Scope and limits

| Claim | This proof | Not covered here |
|-------|------------|------------------|
| FastAPI app imports and listens on a chosen loopback port | Yes | Default port `8000` collision with other processes |
| `GET /health` returns HTTP 200 | Yes | Full engine/plugin readiness |
| `GET /api/health` returns HTTP 200 | Yes | Auth middleware on other routes |
| WinUI reaches `BackendReady` / tray overlay state | No | Use MSTest startup seams + `scripts/icon-launch-failure-smoke.ps1` + manual launch |
| WebSocket `GET /ws/realtime` upgrade | No (not exercised) | Route is registered in `backend/api/route_registry.py` (`@app.websocket("/ws/realtime")`) |

## Environment

- **Host:** Windows 10 (`win32`)
- **Repo root:** `E:\VoiceStudio` (working directory for uvicorn)
- **Interpreter:** `py -3.11` (Python **3.11.9**) — matches backend expectations better than system Python 3.9.
- **Port:** `127.0.0.1:8878` (non-default to avoid clashing with a dev backend on `8000`).

## Procedure

From PowerShell, repo root:

```powershell
Start-Process -FilePath py -ArgumentList @(
  "-3.11","-m","uvicorn","backend.api.main:app",
  "--host","127.0.0.1","--port","8878"
) -PassThru -WindowStyle Hidden -WorkingDirectory "E:\VoiceStudio" `
  -RedirectStandardOutput "$env:TEMP\vs_uvicorn_8878.log" `
  -RedirectStandardError  "$env:TEMP\vs_uvicorn_8878.err"
```

Poll `http://127.0.0.1:8878/health` until HTTP success or timeout (**first successful probe ~40s** after start in the recorded session — heavy import graph).

Then:

- `Invoke-WebRequest http://127.0.0.1:8878/health`
- `Invoke-WebRequest http://127.0.0.1:8878/api/health`

Stop the uvicorn process when finished.

## Recorded results (2026-04-05 session)

| Check | Result |
|-------|--------|
| Time to first `/health` 200 | ~40s (poll loop) |
| `GET /health` | **HTTP 200** |
| `GET /api/health` | **HTTP 200** |
| Backend PID (example session) | Non-stable — capture at runtime if forensic ID needed |

## Correlation with CI / closure wave

Closure-wave verification (same era) included: `dotnet build` 0 errors; App.Tests **3080** passed; `pytest tests/ci` **217** passed; `verify.ps1 -Quick` artifact `artifacts/verify/20260405_002104/`; `python scripts/run_verification.py` → `last_run.json` **20260405-002553** (**completion_guard** PASS); OnlyStage failure smokes `20260405_002027/` and `20260405_002049/`.

## Rollback / reproduction

No code rollback. To reproduce: use commands above; if connection refused, extend poll window or inspect `%TEMP%\vs_uvicorn_8878.err` for import failures.
