# GAP-069 Slice 13 — Failure-Path Smoke — Lane closure

**Date:** 2026-04-14  
**Status:** **Closed**

## Root cause (verified)

1. **Startup ordering:** `failure_smoke_summary.json` was only written after `MainWindow` construction and `StartBackendWithTracking()`. On this machine, `MainWindow` threw `XamlParseException` (ToggleButton style on Button) during `InitializeComponent`, so the failure-port handler path never ran and no summary was emitted.
2. **Inherited env:** `icon-launch-failure-smoke.ps1` copied the full parent environment; `VOICE_STUDIO_SMOKE_UI` / `VOICE_STUDIO_SMOKE_EXIT` could force Gate C / smoke-exit routing and skip the normal backend path (mitigated in app + harness).
3. **Single-instance:** Stray `VoiceStudio.App` processes could cause `Program.cs` to exit before `App` (mitigated: harness kills processes before launch).
4. **Downstream Gate (separate):** Runtime-missing smoke left `startup_decision.json` with `decision=app_root_invalid`, causing `startup_artifact_check` to fail on the next Gate/Ledger — fixed by restoring/remediating that file in `runtime-missing-failure-smoke.ps1` plus `scripts/ci/startup_decision_success_template.json`.

## Fix summary

- **`App.xaml.cs`:** When `VOICE_STUDIO_SMOKE_FAILURE_PORT` or `VOICE_STUDIO_SMOKE_FAILURE_RUNTIME` is set, clear smoke-exit/ui-smoke routing; register failure handlers and call `StartBackendWithTracking()` **before** `MainWindow`; **return** without loading shell XAML for port/runtime failure proofs (contract is backend + JSON summary).
- **`icon-launch-failure-smoke.ps1`:** Strip conflicting smoke env vars; kill stray `VoiceStudio.App` before launch.
- **`runtime-missing-failure-smoke.ps1`:** Backup/restore `startup_decision.json` or apply neutral success template after run.
- **`tests/unit/test_failure_path_smoke_contract.py`:** Regression guard for harness + app markers.

## Proof

| Step | Artifact / command |
|------|-------------------|
| Isolated stage | `verify.ps1 -OnlyStage "Failure-Path Smoke"` → **PASS** `artifacts/verify/20260413_200926/` |
| Checkpoint | `verify.ps1 -StopAfterStage "C# Unit Tests - Other"` → **PASS** `artifacts/verify/20260413_200958/` |
| Honest resume | `artifacts/verify/latest` → `20260413_200958`; `verify.ps1 -ResumeFrom "Python Unit Tests"` → **PASS** `artifacts/verify/20260413_203523/` (**Failure-Path Smoke** ~11.3s; **Gate/Ledger** PASS, **startup_artifact_check** PASS) |

## Umbrella

**GAP-069** — **Closed** for bounded backend-readiness Failure-Path / resume chain per this lane; continuous CI items remain as listed in the roadmap.
