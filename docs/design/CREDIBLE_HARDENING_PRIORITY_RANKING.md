# Credible Hardening Priority Ranking

**Date:** 2026-03-17  
**Source:** Release-Trust and Lifecycle Closure Plan Task 12  
**Purpose:** Re-rank hardening tasks by repo evidence for future prioritization.

**Status discipline:** Tasks completed (implementation) ≠ system proven (full verify.ps1). Release-Trust Plan 12 tasks = implementation-complete. Full lane = run verify.ps1.

---

## Method

Tasks are ranked by:
1. **Severity** (S0 > S1 > S2)
2. **Repo evidence** — what exists in code, tests, or artifacts
3. **Completion status** — done vs open

---

## Completed (Release-Trust Plan 2026-03-17)

| # | Task | Evidence |
|---|------|----------|
| 1 | Stage 13 (C# Services) timeout fix | `StartupRetryCoordinator` retryDelayOverride; tests pass in ~10s |
| 2 | STATE.md truth discipline | Blocker semantics; wave complete ≠ release ready |
| 3 | INavigatablePanel expansion | Library, Analyzer, ScriptEditor implement; ShellNavigationCoordinator alias |
| 4 | Search navigation typed contract | `SearchNavigationContext`, `SearchResultType`, `SearchResultTypeMapper` |
| 5 | Training panel unsubscribe | `_multiSelectService.SelectionChanged` unsubscribed in Dispose |
| 6 | Service tests background audit | `artifacts/verify/service_tests_background_audit.md` |
| 7 | Retry telemetry and UX | `StartupRetryProgress`, attempt count, no-retry explanations |
| 8 | Retry decision matrix tests | `StartupRetryCoordinatorTests` — InvalidAppRoot, SpawnFailure, HealthTimeout success, null fallback, progress |

---

## Re-Ranked Open Items (by priority)

### P0 — Before Release

| # | Task | Severity | Repo Evidence |
|---|------|----------|---------------|
| 9 | Run full verify.ps1 before release claim | S2 | STATE.md; verify.ps1 ~15+ min; Stage 13 now PASS |
| 10 | Release XAML smoke gate | S2 | RELEASE_XAML_SMOKE_GATE.md; manual |

### P1 — High (S1)

| # | Task | Severity | Repo Evidence |
|---|------|----------|---------------|
| 11 | Search overlay / toolbar glue in MainWindow | S1 | MainWindow.xaml.cs; extract to coordinator |
| 12 | Synthesis → timeline → playback E2E in CI | S1 | test_cross_panel_workflows.py; test_smoke_workflows.py |
| 13 | Error messages actionable | S1 | error_message_audit.md; implement recommendations |

### P2 — Medium (S2)

| # | Task | Severity | Repo Evidence |
|---|------|----------|---------------|
| 14 | Full verify.ps1 in CI or nightly | S2 | verify.ps1; ~15+ min |
| 15 | First-launch timeout UX (45–60s) | S2 | BackendProcessManager.WaitForHealthAsync |
| 16 | MainWindow decomposition | S2 | ~2400 lines; MAINWINDOW_DECOMPOSITION_PLAN.md |
| 17 | Throttle instrumentation in smoke | S2 | ThrottledEventPublisher.GetStats() |
| 18 | Naming: ABTestService, TimelineClipService → *Client | S2 | SEAM_MATURITY_AUDIT.md |
| 19 | Help overlay / error recovery integration | S2 | HelpOverlay, OnboardingHints |
| 20 | Feature islands documentation | S2 | ImageGen, VideoGen, DeepfakeCreator |
| 21 | Batch processing workflow documentation | S2 | PANEL_WIRING_CATALOG.md |

### P3 — Low / Deferred

| # | Task | Severity | Repo Evidence |
|---|------|----------|---------------|
| 22 | Timeline ownership on stop | S2 | Documented; user reclaims via Library |
| 23 | taskkill testhost safety net | S2 | verify.ps1; known WinUI issue |
| 24 | retained-async baseline | S2 | ADR-051; top 5 identified |

---

## Summary

- **Completed:** 8 tasks (Release-Trust Plan Phase 1–6)
- **P0 (release gate):** 2
- **P1 (S1):** 3
- **P2 (S2):** 8
- **P3 (deferred):** 3

**Total ranked:** 24 items.

---

## References

- [PREMIUM_SOFTWARE_COHERENCE_AUDIT](PREMIUM_SOFTWARE_COHERENCE_AUDIT.md)
- [.cursor/STATE.md](../../.cursor/STATE.md)
- [artifacts/verify/service_tests_background_audit.md](../../artifacts/verify/service_tests_background_audit.md)
