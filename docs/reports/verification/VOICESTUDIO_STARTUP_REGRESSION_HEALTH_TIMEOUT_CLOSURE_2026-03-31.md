# VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT — Lane closure (2026-03-31)

## 1. Summary

**Lane:** GOV-VOICESTUDIO-STARTUP-REGRESSION-HEALTH-TIMEOUT-01  

**Problem:** Desktop `BackendProcessManager` reported `health_timeout` because `/health` was not served until a long synchronous startup path completed.

**Resolution:** ASGI lifespan yields after `on_startup_prepare`; `on_startup_heavy` (engines, plugins, contract validation, route conflict log) runs concurrently with request handling. Default client URL uses `127.0.0.1` to match uvicorn bind.

---

## 2. Verification matrix

| Step | Command / check | Result |
|------|------------------|--------|
| Python main tests | `python -m pytest tests/unit/backend/api/test_main.py -q` | **PASS** (12 passed, 3 skipped) |
| CI gate | `python -m pytest tests/ci/ -q --randomly-seed=12345` | **PASS** (216 passed, 2 deselected) |
| C# build | `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **PASS** (warnings only, pre-existing nullable) |
| Startup MSTests | `dotnet test ... --filter "FullyQualifiedName~BackendProcessManager\|FullyQualifiedName~StartupGating\|FullyQualifiedName~StartupRetry\|FullyQualifiedName~StartupOverlay"` | **PASS** (28 tests) |
| Quick verify | `.\scripts\verify.ps1 -Quick` | **PASS** → `artifacts/verify/20260331_083518/` |
| Validator | `python scripts/run_verification.py` | **PASS** (`completion_guard` PASS, `.buildlogs/verification/last_run.json`) |
| Live uvicorn probe | Fresh `uvicorn` on `127.0.0.1:9876`; loop until `GET /health` returns 200 | **~13.9s** to first 200 on dev workstation (cold; demonstrates accept-before-heavy behavior) |

---

## 3. Honest limits

- Full WinUI cold-start was not automated in CI (existing `verify.ps1 -Quick` skips integration/UI); operator-class proof is the subprocess `/health` timing plus the architectural fix above.
- First requests that require fully loaded engines/plugins may still race deferred startup for a short window; engines were already best-effort at load time.

## 3.1 Follow-up gap closure (2026-03-31)

Review found **residual `http://localhost:8000` fallbacks** (wizard, diagnostics, env samples, pytest defaults) that could still mismatch uvicorn’s IPv4 bind. **Remediated** in the same closure window:

- **`BackendClientConfig.DefaultHttpBaseUrl` / `DefaultWebSocketUrl`** as the single C# default; call sites and **`FirstRunWizard`** use config + that constant.
- **`launchSettings.json`**, **`.vscode/launch.json`**, **`VOICESTUDIO_BACKEND_URL`** defaults aligned to `http://127.0.0.1:8000`.
- **Python** tests `tests/regression/test_audio_golden.py`, `tests/load/load_test.py`, `tests/e2e/test_primary_workflows.py`: default URL aligned with other integration tests.

---

## 4. References

- Execution row: [GOV_VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_STARTUP_REGRESSION_HEALTH_TIMEOUT_01_EXECUTION_ROW.md)
- Prior unified startup lane (MSTest-first closure): [VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md](VOICESTUDIO_UNIFIED_STARTUP_LANE_CLOSURE_2026-03-28.md)
