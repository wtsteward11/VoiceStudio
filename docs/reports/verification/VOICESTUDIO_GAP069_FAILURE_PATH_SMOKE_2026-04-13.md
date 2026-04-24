# GAP-069 — Failure-Path Smoke (Slice 13) — Progress Report

**Date:** 2026-04-13  
**Umbrella:** **GAP-069** — **Open** (blocked by this stage until fixed)  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_FAILURE_PATH_SMOKE_13_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_FAILURE_PATH_SMOKE_13_EXECUTION_ROW.md)

## What was attempted (fresh certification)

Per **GAP-069 Fresh Downstream Certification** plan:

1. **Task 1 — Fresh checkpoint**  
   - Command: `.\scripts\verify.ps1 -StopAfterStage "C# Unit Tests - Other"`  
   - **Result:** **PASS** — artifact `artifacts/verify/20260413_192153/`  
   - Stages through **C# Unit Tests - Other** executed (no **INHERITED** rows in this run).

2. **Task 2 — Fresh resumed downstream**  
   - Command: `.\scripts\verify.ps1 -ResumeFrom "Python Unit Tests"`  
   - **Result:** **FAIL** — artifact `artifacts/verify/20260413_192823/`  
   - **Checkpoint source:** `20260413_192153` (16 stages **INHERITED** in resume summary).

## Downstream stages actually executed (resume run)

| Stage | Status |
|-------|--------|
| Python Unit Tests | PASSED |
| Contract Tests | PASSED |
| Security Tests | PASSED |
| Backend Integration | PASSED |
| UI Smoke Tests | PASSED |
| UI Self-Test | PASSED |
| Icon-Launch Smoke | PASSED |
| **Failure-Path Smoke** | **FAILED** (exit 1) |

Stages after **Failure-Path Smoke** did not run (verify **fail-fast**).

## Failure details

- **Stage:** Failure-Path Smoke  
- **Harness:** [scripts/icon-launch-failure-smoke.ps1](../../scripts/icon-launch-failure-smoke.ps1)  
- **Stage log:** `artifacts/verify/20260413_192823/logs/failure-path_smoke.log` (minimal; script exit **1**)  
- **Report:** `.buildlogs/verify/failure_smoke.json` — `status: FAIL`, `error`: **`failure_smoke_summary.json not found under LocalAppData VoiceStudio\crashes`**

Interpretation: under port-occupied + `VOICE_STUDIO_SMOKE_FAILURE_PORT=1`, the app did not produce **`%LOCALAPPDATA%\VoiceStudio\crashes\failure_smoke_summary.json`** with **`status: PASS`** within the script’s deadline (45s poll), so the harness wrote a FAIL payload and exited **1**.

## Prior truth (not regressed)

- **Slice 12** (UI Smoke timeout): **Closed** — unrelated to this blocker.  
- **Fresh checkpoint** succeeded — the blocker is **only** Failure-Path Smoke on the new lineage.

## Next steps (bounded)

1. Reproduce locally: `.\scripts\verify.ps1 -OnlyStage "Failure-Path Smoke"` (requires built exe at `.buildlogs\x64\Debug\...\VoiceStudio.App.exe`).  
2. Trace app code that should emit **`failure_smoke_summary.json`** when **`VOICE_STUDIO_SMOKE_FAILURE_PORT=1`** and backend bind fails.  
3. Fix at the correct layer; re-run **OnlyStage** then full **StopAfter + Resume** proof.

## Summary.json reference

- Failed run: `artifacts/verify/20260413_192823/summary.json` — `overall_status: FAILED`, **Failure-Path Smoke** `status: FAILED`.
