# GOV-VOICESTUDIO-BACKEND-READINESS-FAILURE-PATH-SMOKE-13 — Execution Row (GAP-069 Slice 13)

**Status:** **Closed** (2026-04-14) — [closure report](../reports/verification/VOICESTUDIO_GAP069_FAILURE_PATH_SMOKE_LANE_CLOSURE_2026-04-14.md)  
**Lane:** GAP-069 — **`verify.ps1`** stage **Failure-Path Smoke** (Stage 8.7) — port-occupied backend failure overlay proof  
**Date opened:** 2026-04-13

## Problem statement (frozen)

After a **fresh checkpoint** (`-StopAfterStage "C# Unit Tests - Other"`) and **honest downstream resume** (`-ResumeFrom "Python Unit Tests"`), the harness **failed** at **Failure-Path Smoke** with exit code **1**.

**Evidence run:** `artifacts/verify/20260413_192823/` — `overall_status: FAILED`; **Failure-Path Smoke** `status: FAILED` (exit **1**).  
**Checkpoint lineage:** `artifacts/verify/20260413_192153/` (16 stages **PASSED** through **C# Unit Tests - Other**).

**Harness script:** [icon-launch-failure-smoke.ps1](../../scripts/icon-launch-failure-smoke.ps1) — binds `127.0.0.1:VOICESTUDIO_API_PORT` (default **8000**), launches app with **`VOICE_STUDIO_SMOKE_FAILURE_PORT=1`**, polls for **`%LOCALAPPDATA%\VoiceStudio\crashes\failure_smoke_summary.json`** with **`status: PASS`**.

**Observed failure payload** (copied to `.buildlogs\verify\failure_smoke.json`):

- `status`: `FAIL`
- `error`: `failure_smoke_summary.json not found under LocalAppData VoiceStudio\crashes`

So the app did not emit the expected summary (or not in time) under the failure-port smoke mode.

## Scope

- App startup path when **port is occupied** + **`VOICE_STUDIO_SMOKE_FAILURE_PORT=1`**.
- Writing **`failure_smoke_summary.json`** with **`status: PASS`** when the BackendFailed overlay + Retry UX matches contract (see [STARTUP_ORCHESTRATION_HARDENING_PLAN.md](STARTUP_ORCHESTRATION_HARDENING_PLAN.md) Round 3 Task 2).
- **[verify.ps1](../../scripts/verify.ps1)** Stage 8.7 wiring only if harness bug is proven; default assumption is **app-side** contract.

## Hard OUT of scope

- FlaUI UI Smoke (Slice 12 — **Closed**).
- Contract/OpenAPI (Slice 11 — **Closed**).
- General “stability” or unrelated smoke refactors.

## Proof targets (closure)

1. **`verify.ps1 -OnlyStage "Failure-Path Smoke"`** **PASS** after fix, or  
2. Full **`verify.ps1 -StopAfterStage "C# Unit Tests - Other"`** then **`-ResumeFrom "Python Unit Tests"`** **PASS** through **Gate/Ledger Validation**.

## References

- [verify.ps1](../../scripts/verify.ps1) — `Failure-Path Smoke` block (~line 1985).
- [VOICESTUDIO_GAP069_FAILURE_PATH_SMOKE_2026-04-13.md](../reports/verification/VOICESTUDIO_GAP069_FAILURE_PATH_SMOKE_2026-04-13.md) — progress report.
