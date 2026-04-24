# GAP-069 — UI Smoke Tests timeout on resumed path (progress report)

**Date:** 2026-04-13  
**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_UI_SMOKE_TIMEOUT_12_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_UI_SMOKE_TIMEOUT_12_EXECUTION_ROW.md) — **Closed**  
**Umbrella:** **GAP-069** — **Open** (continuous track; **Slice 12** UI Smoke harness lane **Closed** — see resolution below)

## Observation

A post–Slice-11 **`verify.ps1 -ResumeFrom "Python Unit Tests"`** run (**`artifacts/verify/20260413_143616/`**) advanced past:

- **Python Unit Tests**  
- **Contract Tests**  
- **Security Tests**  
- **Backend Integration Tests**  

and **did not complete** **UI Smoke Tests** within the harness **600s** outer timeout (**TIMED_OUT** / stage failure per `summary.json` when present).

## What this implies

- The **Contract Tests** lane (Slice 11) can be closed separately with direct pytest / `-OnlyStage "Contract Tests"` proof; **umbrella GAP-069** still requires a green **UI Smoke** stage on the resumed or full non-Quick path.  
- Next work is **bounded** to the UI Smoke stage: FlaUI smoke harness, `--no-build` vs build artifacts, app process lifetime, and cleanup.

## Artifact index

| Artifact | Path |
|----------|------|
| Resumed run directory | `artifacts/verify/20260413_143616/` |
| Summary (when present) | `artifacts/verify/20260413_143616/summary.json` |
| UI Smoke stage log (when present) | `artifacts/verify/20260413_143616/logs/ui_smoke_tests.log` |
| UI Smoke TRX (when present) | `artifacts/verify/20260413_143616/test-results/ui_smoke_tests.trx` |

## Resolution (2026-04-13)

**Harness:** UI Smoke stage no longer hits the **600s** outer timeout on typical runs (`verify.ps1 -OnlyStage "UI Smoke Tests"` **PASS** `artifacts/verify/20260413_182528/`, ~182s). `dotnet test` filter scoped to FlaUI `SmokeTests`; `VOICESTUDIO_USE_REAL_UI_AUTOMATION=true` set for the stage; infinite FlaUI `GetMainWindow` wait removed; `Application.Attach` removed (child-exit correlation); first-run wizard skipped via `VOICE_STUDIO_FLAUI_AUTOMATION` in app; stray processes killed; Debug exe path resolution; inherited smoke env vars stripped from child.

**Resume:** `verify.ps1 -ResumeFrom "Python Unit Tests"` **PASS** `artifacts/verify/20260413_182858/` (checkpoint inherited prior UI Smoke completion).

**Integrity:** `python scripts/run_verification.py` **PASS** (`.buildlogs/verification/last_run.json`).

**TRX note:** Journeys may report **Skipped** / **Inconclusive** when WinUI HWNDs are not visible to automation from the test host (`visibleTitledWindowsForPid=0`); exit code may still be 0. Interactive desktop may be required for **Passed** journey rows.

## Next bounded work

1. Optional: full non-Quick re-certify with a **fresh** checkpoint (`-StopAfterStage "C# Unit Tests - Other"`) so `-ResumeFrom "Python Unit Tests"` **re-executes** downstream stages (not only inherited skips).  
2. Optional: TRX assertion for at least one **Passed** Smoke journey when strict UI proof is required.
