# VOICESTUDIO — Startup live runtime reopen lane closure (2026-04-05)

**Lane:** `GOV-VOICESTUDIO-STARTUP-LIVE-RUNTIME-REOPEN-01`  
**Execution row:** [GOV_VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_01_EXECUTION_ROW.md)  
**Status:** **Closed**

## 1. Goal

Restore **single backend URL authority** across DI, diagnostics, and UI self-test probes; align default loopback with **IPv4 `127.0.0.1`** (uvicorn bind); honor **`VOICESTUDIO_BACKEND_URL`** consistently. Restore **verify.ps1** Stage **8.7 / 8.8** script dependencies and make failure smokes **deterministic** when the app exits before the polling loop observes the summary file.

## 2. Verification matrix (closure-grade)

| Command | Result |
|---------|--------|
| `dotnet clean` + `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **PASS** (0 errors) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **PASS** — **3080** passed / **274** skipped |
| `dotnet test` filter `BackendClientConfigEnvironmentTests\|BackendProcessManagerDecisionTests\|StartupRetryCoordinatorTests\|StartupOverlayGatingTests` | **PASS** — **30** tests |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | **PASS** — **217** passed (**2** deselected) |
| `python scripts/validate_xaml_resources.py` | **PASS** (173 defined / 101 referenced / 0 missing) |
| `.\scripts\verify.ps1 -Quick` | **PASS** — `artifacts/verify/20260404_234142/` (UI failure stages **SKIPPED** in Quick; expected) |
| `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke" -SkipBuild` | **PASS** — `artifacts/verify/20260404_234738/` |
| `.\scripts\verify.ps1 -OnlyStage "Runtime-Missing Failure Smoke" -SkipBuild` | **PASS** — `artifacts/verify/20260404_234756/` |
| `python scripts/run_verification.py` | **PASS** — `.buildlogs/verification/last_run.json` **`timestamp_short` `20260404-235116`** (**9/9**; **completion_guard** PASS) |

### GAP-045 regression guard

- Filtered transcribe/coordinator tests (**77** total): one **timeout flake** on `ApplyEdit_Success_RecordsSucceededApplyJobStatusRow` under parallel load; **isolated re-run PASS** (no code change in GAP-045 seam for this lane).

## 3. Classification + root cause (branches D + E)

See [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_TAXONOMY_2026-04-05.md](VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_TAXONOMY_2026-04-05.md).

**Implemented fixes**

- **Branch D:** `BackendClientConfig.FromEnvironment()` + callers (`AppServices`, `App.xaml.cs` `GetBackendBaseUrl`, `StartupDiagnostics`, `AppConfig`). Default host **`127.0.0.1`**; `VOICESTUDIO_BACKEND_URL` wins when valid; `VOICESTUDIO_WS_PORT` optional WS-only override; `DefaultWebSocketUrl` path **`/ws/realtime`** (matches FastAPI registry).
- **Branch E:** Added `scripts/icon-launch-failure-smoke.ps1` and `scripts/runtime-missing-failure-smoke.ps1`; fixed race where **`WaitForExit` won before reading `failure_*_smoke_summary.json`**.

## 4. Baseline hygiene / XAML clue

- [VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_BASELINE_HYGIENE_2026-04-05.md](VOICESTUDIO_STARTUP_LIVE_RUNTIME_REOPEN_BASELINE_HYGIENE_2026-04-05.md) — **`selectedTextWithDiagnostics`** not found in `obj` generated `.cs` after rebuild.

## 5. Rollback

Revert `BackendClientConfig` / `AppServices` / `App.xaml.cs` / `AppConfig` / `StartupDiagnostics` / new tests / smoke scripts / governance artifacts together.
