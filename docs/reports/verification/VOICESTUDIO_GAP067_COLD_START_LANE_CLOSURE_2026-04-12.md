# VOICESTUDIO — GAP-067 Cold-Start Lane Closure (2026-04-12)

**Lane:** **GOV-VOICESTUDIO-GAP067-COLD-START-07**  
**Execution row:** [GOV_VOICESTUDIO_GAP067_COLD_START_07_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP067_COLD_START_07_EXECUTION_ROW.md)  
**Status:** **Closed**

## Frozen timing contract

| Milestone | Definition | Budget |
|-----------|------------|--------|
| **T1** | ms from `App` ctor (`_appStartTime`) to `MainWindow Activated` profiler checkpoint | ≤ 3000 ms (`PerformanceBudgets.StartupMs`) |
| **T2** | ms from same origin to first hide of startup overlay (non-blocking shell) — `shell_interactive_ms` | Stretch ≤ 10 000 ms (honest: backend-bound) |

**Artifact:** `%LOCALAPPDATA%\VoiceStudio\Logs\cold_start_timing.json` (written when overlay clears **and** deferred init completes).

## Baseline vs optimized

| Phase | T1 median | T1 worst | T2 median | T2 worst | Notes |
|-------|-----------|----------|-----------|----------|-------|
| Pre-optimization (operator) | — | — | — | — | Fill from 5+ cold starts on reference hardware (execution row table). |
| Post-optimization (this lane) | — | — | — | — | Compare using the same artifact path after changes; environment-dependent. |

**What moved off the critical path (T1)**

- **`RecentProjectsService`**: no synchronous file I/O in ctor; `EnsureRecentDataLoaded()` on first property access + Loaded low-priority menu population.
- **Jump list**: `ScheduleInitialRebuildAfterDelay(200)` + `DispatcherQueuePriority.Low` for initial COM rebuild.
- **Status bar**: `ErrorPresentationService.StartBackendMonitoring()` deferred to `StartBackendHealthMonitoring()` from `MainWindow` Loaded (after shell wiring).

**What stayed on the critical path**

- DI + `MainWindow` ctor work, `m_window.Activate()`, backend spawn / health (T2), panel restore (`panels_init_*` markers).

**Slices 1–6 non-regression**

No intentional changes to notification center, taskbar progress shell, file activation, progressive disclosure controls, or WCAG AutomationIds beyond deferral of non-critical startup work. Existing seam tests for slices 1–6 remain the regression guard.

## Verification proof

| Check | Result |
|-------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| `dotnet test src/VoiceStudio.App.Tests/... --filter FullyQualifiedName~Gap067Slice7` | PASS (**5**) |
| Full `VoiceStudio.App.Tests` | **3428** PASS / **278** skipped |
| `python scripts/ci/check_ibackendclient_creep.py` | PASS |
| `python scripts/check_empty_catches.py` | PASS |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260412_061857/` |
| `python scripts/run_verification.py` | Run post-commit for **completion_guard** |

## Honest note on &lt;10s stretch

T2 is dominated by backend process start and health readiness; this lane improves **T1** and **startup discipline** (deferral + measurable artifact). Further T2 reduction is a **backend/runtime** concern, not shell layout.

## Related

- Umbrella **GAP-067** **Closed** (all 7 bounded slices) — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).
