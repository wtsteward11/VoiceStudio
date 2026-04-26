# GAP-008 Slice 19 — MainWindow **StatusBarCoordinator** shell wiring (bounded)

**Status:** Accepted (Tasks 439–448)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

## First seam (exact)

**`MainWindowStatusBarCoordinatorShellBridge`** owns **only** the **shell** path from **`MainWindow`** into the already-extracted **`StatusBarCoordinator`** ([`StatusBarCoordinator.cs`](../../src/VoiceStudio.App/Services/StatusBarCoordinator.cs)): **resolve** from DI, **`Attach(DispatcherQueue, FindNameOnContent)`**, **`Subscribe(IContextManager, StatusBarActivityService?, GracefulDegradationService?)`**, and **`StartBackendHealthMonitoring()`** invoked from **`MainWindowShellLoadedBootstrap`** via the prelude hook **`StartBackendHealthMonitoring`** (same timing as today: after shell visible / post-bootstrap). **`MainWindowLifetimeCleanupShellBridge`** continues to call **`Unsubscribe()`** and null the coordinator field on **`MainWindow`** — **not** moved into this bridge (lifetime channel stays centralized).

**Explicit non-goals:** No duplication of **`StatusBarCoordinator`** event handlers, **`UpdateActivityIndicators`**, reachability wiring, or **`IStartupStateService`** subscription (those remain inside **`StatusBarCoordinator`**).

## Rejected alternative (vs MAINWINDOW “Next Slice”)

| Option | Why rejected for Slice 19 |
| ------ | ------------------------- |
| **Menu / tool activation glue** | Touches **`MenuFlyout`**, recents, command routing, multiple bridges — not one **status coordinator wiring** cluster. Charter stays **Attach / Subscribe / health-monitoring prelude hook** only. |

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`MainWindowStatusBarCoordinatorShellBridge`**, **`ResolveAttachSubscribe`**, **`StartBackendHealthMonitoring(StatusBarCoordinator?)`** | **IN** |
| **`MainWindowStatusStripClockShellBridge`**, **`MainWindowStatusStripMetricsShellBridge`** | **OUT** |
| **`StatusBarCoordinator`** implementation (handlers, **`ApplyPrimaryStatusText`**, etc.) | **OUT** |
| Notification center / palette / toolbar / workflow / search | **OUT** |
| **`MainWindowLifetimeCleanupShellBridge`** body except existing **`UnsubscribeStatusBarCoordinator`** channel | **OUT** |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 448); no engine work without operator prerequisites |

## Dependency / blast-radius map (Task 440)

| Responsibility | Current owner (`MainWindow`) | Target owner | Services / deps | Async / UI | Overlap Slices 17–18 | Startup-truth | Risks | Required tests |
| ---------------- | ---------------------------- | ------------ | ----------------- | ---------- | --------------------- | --------------- | ----- | ---------------- |
| Resolve + Attach + Subscribe | **`Loaded`**: **`AppServices.GetService<StatusBarCoordinator>()`** + **`Attach`** + **`Subscribe`** (~623–632) | **`MainWindowStatusBarCoordinatorShellBridge.ResolveAttachSubscribe`** | **`StatusBarCoordinator`**, **`IContextManager`**, **`StatusBarActivityService?`**, **`GracefulDegradationService?`**, **`DispatcherQueue`**, **`FindNameOnContent`** | Sync on UI thread after **`BeginMetricsTimer`** | **18** metrics timer must still run **before** bridge wire; **17** clock orthogonal | **Ordering:** **`BeginMetricsTimer()`** then coordinator shell wire (preserved) | Creep into coordinator body in bridge file | **`Gap008Slice19Tests`**, **`MainWindowStatusBarCoordinatorShellBridgeTests`**, **`Gap008Slice18Tests`** ordering pin (bridge call after metrics) |
| Post-loaded backend monitoring prelude | **`MainWindowShellLoadedBootstrap`**: **`StartBackendHealthMonitoring = () => _statusBarCoordinator?.StartBackendHealthMonitoring()`** (~434) | Same hook → **`_statusBarCoordinatorShellBridge.StartBackendHealthMonitoring(_statusBarCoordinator)`** | **`StatusBarCoordinator`** (delegates to **`ErrorPresentationService`**) | Runs after bootstrap | None | Must run after **`Subscribe`** completed in **`Loaded`** | Calling before **`Subscribe`** is no-op inside coordinator | Source pin on hook + bridge method |
| Lifetime unsubscribe | **`MainWindowLifetimeCleanupShellBridge`** **`UnsubscribeStatusBarCoordinator`** (~709–713) | **Unchanged** on **`MainWindow`** | **`StatusBarCoordinator.Unsubscribe`** | Cleanup | **12** lifetime bridge | N/A | Forgetting unsubscribe — unchanged risk | **`Gap008Slice12Tests`** / existing lifetime pins |

## RHVoice (Task 448)

**Zero** edits under **`engines/audio/rhvoice/`**. Creep tests: new bridge source must **not** contain the **`rhvoice`** path segment or unrelated bridge type names.

## Not an extension bucket + bridge accretion (Task 447)

**`MainWindowStatusBarCoordinatorShellBridge`** is a **single-story owner** for **StatusBarCoordinator shell wiring** only. **Forbidden:** metrics/clock timers, notification center, menu builders, duplicating **`StatusBarCoordinator`** internals.

**Distributed god object risk:** Routing unrelated shell features here recreates the monolith as **scatter**. Reject without a new brief + tests.

## Testing debt

MSTest pins constructor / **`ResolveAttachSubscribe`** / source creep / **`MainWindow`** hook and ordering vs **`BeginMetricsTimer`** cover the seam contract. Full **`DispatcherTimer`** and compositor timing under a real WinUI host remain **documented debt** in [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) (DispatcherTimer / UI-host subsection; cross-ref from [Slice 18](VOICESTUDIO_BOUNDED_GAP008_SLICE18_MAINWINDOW_STATUS_STRIP_METRICS_SHELL.md) §Testing debt).

## Historical briefs (Task 446)

Slices **1–18** briefs are **not** rolling scoreboards for spine **N**. For current spine count use [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) and **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`** output.

## Acceptance criteria

1. **`MainWindow`** does not inline **`Attach`/`Subscribe`** on **`StatusBarCoordinator`** except via **`MainWindowStatusBarCoordinatorShellBridge`**.
2. **`Gap008Slice19Tests`** + **`MainWindowStatusBarCoordinatorShellBridgeTests`**; canonical filter strict superset.
3. **`BeginMetricsTimer()`** remains **before** coordinator shell wire (startup-truth ordering).
4. **`StartBackendHealthMonitoring`** prelude uses bridge; **`Unsubscribe`** path unchanged.

## Verification

Run **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`**, targeted `dotnet test` for **`Gap008Slice19Tests`** and **`MainWindowStatusBarCoordinatorShellBridgeTests`**, **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**, and **`python scripts\run_verification.py`**.

**Closure evidence (2026-04-25):** targeted **`Passed: 7`** / **`Failed: 0`**; full spine **`Passed: 146`** / **`listedTestCount: 146`** → **`.buildlogs/gap008_spine/last_run_summary.json`** (timestamp **`2026-04-25T23:05:15Z`**; TRX **`gap008_spine_20260425_180329.trx`**); **`python scripts/run_verification.py`** **Overall: PASS** (`.buildlogs/verification/last_run.json`). Count reconciliation: [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) § **Spine size after Slice 19**.
