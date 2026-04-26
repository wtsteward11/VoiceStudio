# GAP-008 Slice 17 — MainWindow **status strip clock** shell (bounded)

**Status:** Accepted (Tasks 420–428)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **One-minute `ClockText` wall-clock** (`System.Threading.Timer` + `DispatcherQueue` enqueue + `DisposeClockTimer`) — **not** `DispatcherTimer` CPU/GPU/RAM metrics (**`MainWindow.StatusBar.cs`** / **`UpdateStatusBarMetrics`**), **not** **`StatusBarCoordinator`** attach/subscribe (startup truth lane), **not** **`GlobalTransportControl`** / **`TransportOrchestrationBootstrap`** (Slice 17 rejected: couples timeline + orchestrator bootstrap), **not** menu/tool activation glue (deferred — alternate candidate in MAINWINDOW plan; broader blast radius than this clock-only seam).

## First seam (exact)

**`MainWindowStatusStripClockShellBridge`** owns:

- **`BeginClockTimer()`** — dispose any prior timer; **`RefreshClockText()`** once; start **`System.Threading.Timer`** (one-minute period) that **`TryEnqueue`**’s **`RefreshClockText`** when **`getDisposed()`** is false.
- **`RefreshClockText()`** — resolve **`ClockText`** via injected accessor; set **`DateTime.Now.ToString("h:mm tt")`** (same behavior as pre–Slice 17 **`UpdateClock`** on **`MainWindow`** partial).
- **`DisposeClockTimer()`** — dispose + null timer; invoked from **`MainWindowLifetimeCleanupShellBridge`** channel (**one line** on **`MainWindow`**).

**`MainWindow`** removes **`_clockTimer`** and the Loaded-tail inline timer block; calls **`_statusStripClockShellBridge.BeginClockTimer()`** after **`StartStatusBarTimer()`** (metrics path unchanged). **`MainWindow.StatusBar.cs`** no longer defines **`private void UpdateClock`** — wall-clock formatting lives only on the bridge.

**Composition:** **`Func<TextBlock?>`** for **`ClockText`**, **`DispatcherQueue`**, **`Func<bool>`** for disposed guard — **no** **`AppServices`** / **`ServiceProvider`** inside the bridge.

## Dependency / blast-radius map (Task 421)

| Responsibility | Former owner (`MainWindow`) | Target owner | Services / deps | Async / UI thread | Side effects | Coupling to other bridges | Regression risks |
| ---------------- | --------------------------- | ------------ | ----------------- | ----------------- | ------------ | ------------------------- | ------------------ |
| 1-minute wall clock | **`_clockTimer`**, Loaded tail timer block, **`UpdateClock`** (partial) | **`MainWindowStatusStripClockShellBridge`** | **`Func<TextBlock?>`**, **`DispatcherQueue`**, **`Func<bool>`** | Timer callback → **`TryEnqueue`** | **`ClockText.Text`** | **Slice 13** cleanup channel calls **`DisposeClockTimer`** only | Double **`BeginClockTimer`** without dispose → duplicate timers (mitigated: **`BeginClockTimer`** disposes first) |
| CPU/GPU/RAM clock line | **`UpdateStatusBarMetrics`** | **unchanged** **`MainWindow.StatusBar.cs`** | **`FindNameOnContent`**, process telemetry | **`DispatcherTimer.Tick`** | Writes **`ClockText`** with **`HH:mm`** on 2s tick | Orthogonal to bridge | Two writers to **`ClockText`** (pre-existing); bridge uses **`h:mm tt` on 1 min** — unchanged product behavior vs pre–Slice 17 **`UpdateClock`** |
| Status primary line / startup | **`StatusBarCoordinator`** | **OUT** | — | — | — | Startup truth / **`IStartupStateService`** | Do not move coordinator logic here |

## Mentor vs MAINWINDOW “alternate candidate”

| Option | Dependency sketch | Slice 17 decision |
| ------ | ----------------- | ----------------- |
| **Status / transport (`GlobalTransportControl` + bootstrap)** | Requires stable **`CenterPanelHost`** / **`ITimelineTransportController`** resolution from **`MainWindow`**; overlaps Transport Coherence waves and **`TransportShortcutCoordinator`** tail. | **Rejected for Slice 17** — seam too wide for one bounded bridge + tests in this batch. |
| **Menu / tool activation glue** | Plan “alternate candidate” ([MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) §~150); mixes **`MenuFlyout`**, recents population, command routing. | **Deferred** — **not** Slice 17; needs its own charter. |
| **Status strip clock (this brief)** | **`Func<TextBlock?>`** + queue + timer only; cleanup already channelized in Slice 13. | **Selected** — smallest **status-chrome** cut that does not absorb **`StatusBarCoordinator`**. |

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`MainWindowStatusStripClockShellBridge`**, **`BeginClockTimer`**, **`RefreshClockText`**, **`DisposeClockTimer`** | **IN** |
| **`MainWindow.StatusBar.cs`** metrics timer, **`UpdateStatusBarMetrics`**, **`UpdateGpuAndLatencyAsync`** | **OUT** |
| **`StatusBarCoordinator`**, **`GlobalTransportControl`**, **`TransportOrchestrationBootstrap`**, **`OnPlayRequested` / `OnStopRequested`** | **OUT** |
| **`MainWindowNotificationCenterShellBridge`**, jump-list / file activation, palette/catalog/toolbar/search | **OUT** |
| **`MainWindowLifetimeCleanupShellBridge`** implementation bodies | **OUT** — channel invokes **`DisposeClockTimer`** one line only |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 428) |

## RHVoice (Task 428)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid that path string and unrelated bridge type names in **`MainWindowStatusStripClockShellBridge.cs`**.

## Not an extension bucket + bridge accretion (Task 427)

**`MainWindowStatusStripClockShellBridge`** is a **bounded seam owner** for **status strip wall-clock timer** only. **Forbidden:** transport play/stop, notification center, metrics **`DispatcherTimer`**, **`StatusBarCoordinator`** internals, menu builders.

**Distributed god object risk:** Each bridge remains a **single-story owner**. Routing unrelated shell features into this file recreates the monolith as **scatter**. Reject without a new brief + tests.

## Testing debt

Same as prior GAP-008 shell slices: MSTest host does not require a full WinUI tree; **`TextBlock`** + **`DispatcherQueueController`** suffice for **`RefreshClockText`**; timer paths use **`BeginClockTimer`** + **`DisposeClockTimer`** without hanging the suite.

## Acceptance criteria

1. **`MainWindow`** does not own **`_clockTimer`** or **`UpdateClock`** for **`h:mm tt`**; **`MainWindow.StatusBar.cs`** does not define **`UpdateClock`**; lifetime channel delegates **`DisposeClockTimer`** to the bridge.
2. **`Gap008Slice17Tests`** + **`MainWindowStatusStripClockShellBridgeTests`**.
3. Canonical spine filter extended (strict superset); script emits **`.buildlogs/gap008_spine/`** artifacts.

## Verification

Run **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`**, targeted `dotnet test` for **`Gap008Slice17Tests`** and **`MainWindowStatusStripClockShellBridgeTests`**, **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**, and **`python scripts\run_verification.py`**.

**Spine count authority:** do not hand-maintain cumulative **N** in this file — use [spine count reconciliation](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) and **`.buildlogs/gap008_spine/last_run_summary.json`** after **Task 419**.

## Historical briefs (Task 426)

Older slice briefs are **not** rolling scoreboards for spine **N**; link [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) from operational docs (**STATE**, **MAINWINDOW**, this brief §Verification) instead of re-listing FQNs across Slices 1–16.

## Changelog

- **2026-04-25 (Slice 18):** The 2s **CPU/GPU/RAM/latency** **`DispatcherTimer`** and **`UpdateGpuAndLatencyAsync`** moved to **[Slice 18 — status strip metrics shell](VOICESTUDIO_BOUNDED_GAP008_SLICE18_MAINWINDOW_STATUS_STRIP_METRICS_SHELL.md)**; **`MainWindow.StatusBar.cs`** was removed. Text in §First seam / §IN OUT above described **Slice 17 closure** (metrics still on the partial); treat this changelog as the superseding fact for repo layout after Slice 18.
