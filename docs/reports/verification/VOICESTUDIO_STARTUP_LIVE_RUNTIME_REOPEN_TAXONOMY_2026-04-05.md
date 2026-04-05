# Startup failure taxonomy — classification (pre-fix evidence)

**Lane:** GOV-VOICESTUDIO-STARTUP-LIVE-RUNTIME-REOPEN-01  
**Date:** 2026-04-05  
**Closure:** [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md](VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md)

## Observed contradictions (code + harness truth, not GUI-only)

| Branch | Signal | Evidence |
|--------|--------|----------|
| **D — wrong_host_or_port / split authority** | DI `BackendClientConfig` defaulted `VOICESTUDIO_API_HOST` to **`localhost`** while `BackendClientConfig.DefaultHttpBaseUrl` documented **`127.0.0.1`** for IPv4 uvicorn; `VOICESTUDIO_BACKEND_URL` used in `StartupDiagnostics` but **ignored** in `AppServices.RegisterCoreInfrastructure`. | `AppServices.cs` (pre-fix), `StartupDiagnostics.cs` (pre-fix), `App.xaml.cs` `GetBackendBaseUrl` (pre-fix). |
| **E — failure-smoke harness drift** | `verify.ps1` Stage 8.7 / 8.8 invoked **`icon-launch-failure-smoke.ps1`** and **`runtime-missing-failure-smoke.ps1`** missing from `scripts/`. | `scripts/verify.ps1` lines ~1486–1525; glob `scripts/*smoke*.ps1` showed only `smoke.ps1`. |

## Branches not primary for this closure

- **A / B / C:** No new subprocess or `/health` timing changes in this lane; prior **startup regression health-timeout** lane addressed ASGI deferral.
- **F — XAML / `selectedTextWithDiagnostics`:** Post–clean-build search in `obj/**/*.cs` found **no** `selectedTextWithDiagnostics`; no artifact-path fix applied.

## Live GUI capture

- Full WinUI icon-launch trace is **operator-class**; this closure relies on **unit tests + verify.ps1 failure smokes + matrix** per execution row. Optional: `%LOCALAPPDATA%\VoiceStudio\crashes\failure_*_smoke_summary.json` after Stage 8.7/8.8.
