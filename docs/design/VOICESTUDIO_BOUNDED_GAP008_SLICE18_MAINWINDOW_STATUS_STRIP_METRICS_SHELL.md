# GAP-008 Slice 18 — MainWindow **status strip metrics** shell (bounded)

**Status:** Accepted (Tasks 429–438)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **2-second `DispatcherTimer` CPU / RAM / GPU / latency strip** (`CpuText`, `GpuText`, `RamText`, `LatencyText`) plus **`UpdateGpuAndLatencyAsync`** (health + telemetry clients) — **not** **`ClockText`** (**[Slice 17](VOICESTUDIO_BOUNDED_GAP008_SLICE17_MAINWINDOW_STATUS_STRIP_CLOCK_SHELL.md)** — **`MainWindowStatusStripClockShellBridge`**), **not** **`StatusBarCoordinator`** attach/subscribe (**startup truth**), **not** **`GlobalTransportControl`** / **`TransportOrchestrationBootstrap`**, **not** menu/tool activation glue (**rejected:** broader than one status-chrome cluster; see table below).

## First seam (exact)

**`MainWindowStatusStripMetricsShellBridge`** owns:

- **`BeginMetricsTimer()`** — stop/dispose prior timer; create **`DispatcherTimer`** (2s); **`Tick`** → **`OnMetricsTick`**; **`Start()`**; call **`OnMetricsTick()`** once for immediate CPU/RAM lines (same as pre–Slice 18 **`StartStatusBarTimer`**).
- **`OnMetricsTick()`** — process CPU/RAM %, update **`CpuText`**, **`GpuText`**, **`RamText`**, **`LatencyText`** via injected accessors; **does not** write **`ClockText`** (clock-only **`MainWindowStatusStripClockShellBridge`**).
- **`StopMetricsTimer()`** — stop and null timer; **`MainWindowLifetimeCleanupShellBridge`** prelude channel **`StopStatusBarTimer`** forwards here (**one line** on **`MainWindow`**).

**Composition:** **`Func<TextBlock?>`** ×4 for metric labels; **`Func<IHealthVersionClient?>`**, **`Func<ITelemetryClient?>`** for backend ping/telemetry (resolved each tick like pre–Slice 18 **`ServiceProvider`** calls — **no** static **`ServiceProvider`** inside the bridge).

**`MainWindow`** removes **`MainWindow.StatusBar.cs`** partial (metrics-only file); calls **`_statusStripMetricsShellBridge.BeginMetricsTimer()`** where **`StartStatusBarTimer()`** ran; constructs bridge after **`MainWindowStatusStripClockShellBridge`**.

## Dependency / blast-radius map (Task 430)

| Responsibility | Former owner | Target owner | Services / deps | Async / UI thread | Side effects | Coupling to other bridges | Regression risks |
| -------------- | ------------ | ------------ | ----------------- | ----------------- | ------------ | ------------------------- | ------------------ |
| 2s metrics tick + CPU/RAM | **`MainWindow.StatusBar.cs`** **`_statusBarTimer`**, **`UpdateStatusBarMetrics`** | **`MainWindowStatusStripMetricsShellBridge`** | **`Process`**, **`GC.GetGCMemoryInfo`**, text accessors | **`DispatcherTimer.Tick`** (UI thread) | Text on **Cpu/Ram/Gpu/Latency** | **Slice 17** clock orthogonal | Removing **`ClockText`** write from tick avoids triple writer; clock bridge remains authority for **`h:mm tt`** |
| GPU + latency async | **`UpdateGpuAndLatencyAsync`** | **`MainWindowStatusStripMetricsShellBridge`** | **`IHealthVersionClient`**, **`ITelemetryClient`** via **`Func<>`** | **`async Task`** from tick (**fire-and-forget** `_ = …` preserved) | **`_lastLatencyMs`**, **`_lastGpuPercent`** fields on bridge | None | Network/backend down — same best-effort as before |
| Startup primary line | **`StatusBarCoordinator`** | **OUT** | — | — | — | **Slice 11** truth lane | Do not move **`Attach`/`Subscribe`** into this bridge |
| Wall clock | **`MainWindowStatusStripClockShellBridge`** | **OUT** | — | — | — | **Slice 17** | Metrics bridge must **not** import clock bridge |

## Mentor vs MAINWINDOW alternate candidate

| Option | Dependency sketch | Slice 18 decision |
| ------ | ----------------- | ----------------- |
| **Menu / tool activation glue** | **`MenuFlyout`**, recents, command routing; overlaps multiple bridges. | **Rejected** — not one status-chrome cluster; keep for a future **`VOICESTUDIO_BOUNDED_GAP008_SLICE19_*.md`** if chartered. |
| **Status strip metrics (this brief)** | Text **`Func<>`** + optional health/telemetry **`Func<>`**; same file as pre–Slice 18 partial; lifetime already **`StopStatusBarTimer`**. | **Selected** — adjacent to **Slice 17** clock; does not absorb **`StatusBarCoordinator`**. |

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`MainWindowStatusStripMetricsShellBridge`**, **`BeginMetricsTimer`**, **`StopMetricsTimer`**, **`OnMetricsTick`**, GPU/latency async | **IN** |
| **`MainWindowStatusStripClockShellBridge`**, **`ClockText`** | **OUT** |
| **`StatusBarCoordinator`**, **`GlobalTransportControl`**, notification / palette / toolbar / search | **OUT** |
| **`MainWindowLifetimeCleanupShellBridge`** implementation bodies | **OUT** — prelude forwards **`StopStatusBarTimer`** only |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 438) |

## RHVoice (Task 438)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid that path string and unrelated bridge type names in **`MainWindowStatusStripMetricsShellBridge.cs`**.

## Not an extension bucket + bridge accretion (Task 437)

**`MainWindowStatusStripMetricsShellBridge`** is a **bounded seam owner** for **status strip process/telemetry metrics timer** only. **Forbidden:** **`ClockText`**, transport play/stop, **`StatusBarCoordinator`** internals, notification center, menu builders.

**Distributed god object risk:** Each bridge remains a **single-story owner**. Routing unrelated shell features into this file recreates the monolith as **scatter**. Reject without a new brief + tests.

## Testing debt

MSTest host may not fully drive **`DispatcherTimer`** ticks without a WinUI window; constructor / **`StopMetricsTimer`** / source creep / **`MainWindow`** wiring pins cover the seam contract. Timer behavior remains **operator / integration** truth.

**Cross-ref (Slice 19 / Task 443):** proven vs not-proven and closure criteria are centralized in [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) § **Testing debt — DispatcherTimer / UI-host (status strip)**.

## Acceptance criteria

1. **`MainWindow`** does not define **`StartStatusBarTimer`** / **`UpdateStatusBarMetrics`** / **`UpdateGpuAndLatencyAsync`**; **`MainWindow.StatusBar.cs`** removed or empty of metrics (file removed).
2. **`Gap008Slice18Tests`** + **`MainWindowStatusStripMetricsShellBridgeTests`**.
3. Canonical spine filter extended (strict superset); **`Gap008StartupTruthTests`** unchanged — **`MainWindow`** still applies **`StatusBarCoordinator`** shell wiring after metrics bridge **`BeginMetricsTimer`** (ordering pin; **Slice 19** delegates **`Attach`/`Subscribe`** via **`MainWindowStatusBarCoordinatorShellBridge`**).

## Verification

Run **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`**, targeted `dotnet test` for **`Gap008Slice18Tests`** and **`MainWindowStatusStripMetricsShellBridgeTests`**, **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**, and **`python scripts\run_verification.py`**.

**Closure evidence (2026-04-25):** full spine **`Passed: 139`** / **`listedTestCount: 139`** via script → **`.buildlogs/gap008_spine/last_run_summary.json`**; `run_verification.py` **Overall: PASS**.

**Spine count authority:** [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) and **`.buildlogs/gap008_spine/last_run_summary.json`** (local regenerate; **`.buildlogs/`** is gitignored).

## Historical briefs (Task 436)

Older slice briefs are **not** rolling scoreboards for spine **N**; link reconciliation + run script instead of re-listing FQNs.
