# GOV-VOICESTUDIO-STARTUP-LIVE-RUNTIME-REOPEN-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_01`  
**Status:** **Closed** 2026-04-05 — [closure](../reports/verification/VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md).  
**Depends on:** [GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_UNIFIED_STARTUP_01_EXECUTION_ROW.md) (**Closed**), [GOV_VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_01_EXECUTION_ROW.md) (**Closed**), [GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP045_TRANSCRIPT_PERSISTENCE_01_EXECUTION_ROW.md) (**Closed**; regression guard only).

## Frozen architecture decisions

1. **Single backend URL authority:** `BackendClientConfig.FromEnvironment()` resolves `VOICESTUDIO_BACKEND_URL` (absolute http/https) when valid; otherwise `VOICESTUDIO_API_HOST` (default **`127.0.0.1`**) + `VOICESTUDIO_API_PORT` (default `8000`). Optional `VOICESTUDIO_WS_PORT` overrides WebSocket port only.
2. **WS path:** Client WebSocket URL uses `/ws/realtime` (matches `backend/api/route_registry.py`).
3. **No launch-profile drift:** `AppServices`, `App.GetBackendBaseUrl`, `StartupDiagnostics`, and `AppConfig` consume the same resolver (no split between `BACKEND_URL` and `API_HOST` at DI vs diagnostics).
4. **Failure-path smoke:** Stages 8.7 / 8.8 invoke `scripts/icon-launch-failure-smoke.ps1` and `scripts/runtime-missing-failure-smoke.ps1`; summaries are mirrored to `.buildlogs/verify/*.json` for `verify.ps1` reporting.

## Hard IN

- [x] Evidence-backed classification of startup failures (taxonomy A–E) before speculative code changes.
- [x] Root-cause fixes on matched branches only (D: authority/loopback; E: harness scripts).
- [x] `verify.ps1` Stage 8.7 / 8.8 executable (scripts present; `SkipUI` unchanged).
- [x] GAP-045 targeted regression tests pass after startup seam changes.
- [x] Full verification matrix + governance sync (STATE, gap tracker, canonical registry, closure report).

## Hard OUT

- Broad startup refactors unrelated to proven root causes.
- New product features outside startup/runtime + verification harness.
- PanelHost **GAP-007** or other Overseer-reprioritized lanes.

## Binary acceptance (closure gate)

- [x] `BackendClientConfig.FromEnvironment()` unit tests cover `BACKEND_URL`, defaults, and `WS_PORT` override.
- [x] `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` PASS.
- [x] Full `VoiceStudio.App.Tests` PASS; `pytest tests/ci` PASS; `validate_xaml_resources.py` PASS.
- [x] `.\scripts\verify.ps1 -Quick` PASS (including failure smokes when UI stages run).
- [x] `python scripts/run_verification.py` PASS (`completion_guard`).

## Proof

- [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md](../reports/verification/VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_CLOSURE_2026-04-05.md)

## Rollback

Revert `BackendClientConfig` / `AppServices` / `App.xaml.cs` / `AppConfig` / `StartupDiagnostics` / new tests / smoke scripts / governance-only edits in one revert set; preserve unrelated GAP-045 product code unless explicitly coupled.
