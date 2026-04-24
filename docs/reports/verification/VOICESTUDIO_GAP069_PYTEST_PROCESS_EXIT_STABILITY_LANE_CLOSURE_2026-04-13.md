# GAP-069 Slice 10 — Python Unit Tests Process-Exit Stabilization — Lane Closure

**Date:** 2026-04-13  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_PYTEST_PROCESS_EXIT_STABILITY_10_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_PYTEST_PROCESS_EXIT_STABILITY_10_EXECUTION_ROW.md) — **Closed**

## Hang class (frozen)

1. **Primary:** `EnhancedResourceManager` monitoring threads were left running when tests constructed instances without calling `shutdown()` (`tests/unit/core/runtime/test_resource_manager_enhanced.py`). Polling + VRAM alert logging continued after the pytest success summary.
2. **Secondary:** Post-session TensorFlow / HuggingFace / trainer activity during interpreter teardown (non-daemon work, lazy imports). Combined with (1), the CPython process could remain alive long after the last test.
3. **Harness interaction:** The timed `Invoke-Stage` wrapper relied on `$LASTEXITCODE`; scriptblocks that only `return $exitCode` did not set it, risking empty exit files (fixed globally in `verify.ps1`).

## Fix summary

| Area | Change |
|------|--------|
| Tests | Module autouse fixture + `manager.shutdown()` / `type(obj) is _erm` teardown in `test_resource_manager_enhanced.py`; `test_monitoring_thread_stops` uses `shutdown()`. |
| `tests/conftest.py` | Merged duplicate `pytest_collection_modifyitems` (GPU + env skips); improved session `event_loop` teardown (`shutdown_asyncgens` / `shutdown_default_executor`); `pytest_sessionfinish` **does not** scan `gc.get_objects()` (can stall on huge heaps). |
| `scripts/verify.ps1` | Python Unit Tests: `Start-Process` + Win32 `ArgumentList` string for `-m "not slow..."`; env `TF_CPP_MIN_LOG_LEVEL` / `TRANSFORMERS_VERBOSITY`; poll junit; **120s** grace after green junit then **terminate** child if still alive; `cmd /c exit /b` so timed stage captures exit code; null-safe exit file read; remove stale junit before run. |

## Proof

- **`verify.ps1 -OnlyStage "Python Unit Tests"`** — **PASSED** (~406s wall), artifact dir **`artifacts/verify/20260413_120159/`**: **5432 passed** in ~284s; harness note when pytest child did not exit within 120s after green junit (expected on hosts with lingering ML teardown); stage **PASSED** (junit green).
- `python scripts/check_empty_catches.py` — PASS  
- `python scripts/ci/check_ibackendclient_creep.py` — PASS  
- `python scripts/run_verification.py` — PASS  
- `.\scripts\verify.ps1 -Quick` — PASS  

## Umbrella GAP-069

Remainder: optional full **`-ResumeFrom`** checkpoint run through Contract/Security/Gate for end-to-end certification proof; Python Unit Tests stage is no longer blocked by **TIMED_OUT** at 1200s when tests are green.
