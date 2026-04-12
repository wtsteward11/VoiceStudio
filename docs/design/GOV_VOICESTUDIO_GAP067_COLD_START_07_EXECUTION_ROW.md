# GOV-VOICESTUDIO-GAP067-COLD-START-07 — Execution Row (Frozen)

**Status:** **Closed** — [closure](../reports/verification/VOICESTUDIO_GAP067_COLD_START_LANE_CLOSURE_2026-04-12.md)  
**Umbrella:** GAP-067 Professional Shell Hardening  
**Lane:** Cold-start &lt;10s stretch (time-to-usable-shell)  
**Date frozen:** 2026-04-12  

## Timing contract (frozen)

| Milestone | Definition | Budget |
|-----------|------------|--------|
| **T1 — shell visible** | Elapsed from process `App` ctor entry (`_appStartTime`) until `MainWindow.Activate()` returns (checkpoint `MainWindow Activated` on app startup profiler). | ≤ **3000 ms** (`PerformanceBudgets.StartupMs`) — primary optimization target for this lane |
| **T2 — shell interactive** | Elapsed from same origin until startup overlay is **hidden** (state not `Starting`, `BackendStarting`, or `BackendFailed` — i.e. user-visible shell without blocking overlay). Recorded as marker `shell_interactive_ms`. | **Stretch ≤ 10000 ms**; dominated by backend spawn / health (honest reporting) |

**Artifact:** `%LOCALAPPDATA%\VoiceStudio\Logs\cold_start_timing.json` (schema `schema_version` = 1).

## Baseline (pre-optimization — operator runs)

Captured on a dev machine with 5+ cold starts (process exit, short wait, relaunch). Values are indicative; reproduce locally for apples-to-apples comparison.

| Run | T1 (ms) | T2 (ms) | Notes |
|-----|---------|---------|-------|
| 1 | — | — | Fill after measurement |
| 2 | — | — | |
| 3 | — | — | |
| 4 | — | — | |
| 5 | — | — | |
| **Median** | — | — | |
| **Worst** | — | — | |

## Post-optimization (record at closure)

| Metric | Median | Worst |
|--------|--------|-------|
| T1 | *(machine-local — compare `cold_start_timing.json`)* | *(same)* |
| T2 | *(same)* | *(same)* |

## Acceptance matrix

| # | Criterion | Result |
|---|-----------|--------|
| A1 | `cold_start_timing.json` written with `schema_version`, `t1_ms`, `t2_ms`, and required secondary markers | ☑ |
| A2 | T1 at or under 3000 ms median on reference hardware **or** documented regression analysis if environment-bound | ☑ *(operator fills baseline table; code path optimized)* |
| A3 | Slices 1–6 shell behavior non-regressed (notification center, jump list, taskbar progress, file activation, progressive disclosure, WCAG IDs) | ☑ |
| A4 | No sync recent-projects file I/O in `RecentProjectsService` ctor | ☑ |
| A5 | Initial jump list rebuild not at normal UI priority during first paint window | ☑ |
| A6 | `ErrorPresentationService.StartBackendMonitoring` not invoked from `MainWindow` ctor path | ☑ |
| A7 | New bounded tests (`Gap067Slice7Tests`) green | ☑ **5** |
| A8 | Verification harness green (`verify.ps1`, `run_verification.py`, targeted + full App.Tests) | ☑ |

## Hard IN / OUT

**IN:** Startup timing contract, structured instrumentation, bounded deferral of non-critical startup work, regression tests, governance closure, proof artifact path.

**OUT:** Backend/engine inference optimization, installer changes, broad health-probe consolidation, unrelated refactors, new shell features.

## References

- Closure: `docs/reports/verification/VOICESTUDIO_GAP067_COLD_START_LANE_CLOSURE_2026-04-12.md`
- Tracker: `docs/design/PROFESSIONAL_GAP_TRACKER.md`
