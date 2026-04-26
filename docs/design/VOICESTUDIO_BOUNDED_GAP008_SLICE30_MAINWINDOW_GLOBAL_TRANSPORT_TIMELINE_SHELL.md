# GAP-008 Slice 30 — MainWindow global transport + timeline shell (bounded)

**Status:** Accepted (Tasks 114–123)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 30”** in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence) — Task 115 / continue cycle

**GAP-008 continues with Slice 30** on seam **global transport + timeline zoom + recording toggle**; **Path G1**; **umbrella not closed**.

## Goal

Move from [`MainWindow.xaml.cs`](../src/VoiceStudio.App/MainWindow.xaml.cs) into **`MainWindowGlobalTransportShellBridge`**: **`TogglePlayback`**, **`StopPlayback`**, **`OpenRecordingPanelFromTransportShortcut`**, **`ToggleRecording`**, **`ZoomIn`**, **`ZoomOut`**, **`ResetZoom`**. **`MainWindow`** keeps **one-line** forwards; **`GlobalTransportControl`** `Play`/`Stop` event handlers remain on `MainWindow` as **one-line** to the bridge. **`RegisterKeyboardShortcuts`** **zoom** registrations call the bridge (same as today’s behavior). **`TransportShortcutCoordinator.Attach`**, in Loaded bootstrap, uses a **forward** to the bridge for **open recording from transport shortcut** (closure signature unchanged).

**Deferred (explicit):** Deeper `IGlobalTransportOrchestrator` implementation; engine/audio graph; **IBackendClient** transport routes; **TimelineView** business logic; **GlobalTransportControl** XAML/controls; **PanelIds** / registry changes.

## IN / OUT

| IN | OUT |
|----|------|
| **Playback / stop:** `StartupGatingHelper.ShouldBlockTransportPlayback` + `IGlobalTransportOrchestrator` + toasts | **RHVoice** / `engines/audio/rhvoice/`; **synthesis** |
| **Recording panel shortcut:** `IStartupStateService.IsReady`, `IEventAggregator` + `NavigateToEvent` | **CI verify-harness** GOV closure rewrites without fresh hosted evidence (Tasks **95–96**) |
| **Toggle recording:** `PanelHost` / `RecordingView` + `OpenPanelByIdAsync` for `"Recording"` | **IBackendClient** as a **dependency of this bridge** (only via `MainWindow` DI delegates) — bridge does **not** name `IBackendClient` |
| **Zoom:** `CenterPanelHost` → `TimelineView` + `ITimelineTransportController` / `ZoomInCommand` / `ZoomOutCommand` / `TimelineZoom` | Merging with **`MainWindowMenuToolActivationShellBridge`**; **search overlay**; **Edit/Undo** (Slice 29) |
| **One bridge:** `MainWindowGlobalTransportShellBridge` | **Task 103** / **Task 113** (optional WinUI / runtime appendix) — **not** spine gates |

## One bridge class name

**`MainWindowGlobalTransportShellBridge`**

## Dependency map (Task 117)

| Symbol / surface | Role |
|------------------|------|
| **`TogglePlayback`** (MainWindow) | **After:** `await _globalTransportShellBridge.TogglePlaybackAsync(getStartup, getToast, getOrchestrator)` |
| **`StopPlayback`** | `StopPlayback(getStartup, getToast, getOrchestrator)` |
| **`OpenRecordingPanelFromTransportShortcut`** | Used by `TransportShortcutCoordinator.Attach` — `OpenRecordingFromTransportShortcut(getStartup, getToast, getEventAggregator)` |
| **`ToggleRecording`** | `ToggleRecordingAsync(getRightPanelHost, openPanelById, getStartup, getToast, logError)` |
| **`ZoomIn` / `ZoomOut` / `ResetZoom`** | `ZoomIn` / `ZoomOut` / `ResetZoom` with `Func<PanelHost?>` (center) to resolve `TimelineView` |
| **Downstream** | `IGlobalTransportOrchestrator`, `IStartupStateService`, `IToastNotificationService` / `ToastType`, `IEventAggregator` → `NavigateToEvent`, `RecordingView.ViewModel` commands, `TimelineViewModel` **zoom** |
| **Async / UI** | `TogglePlayback` / `ToggleRecording` are **async**; **no** new ctor fire-and-forget |
| **Side effects** | Transport state; panel navigation; recording; timeline zoom value |
| **Must not name in bridge (anti-creep):** | `MainWindowHelpAboutShellBridge`, `MainWindowEditUndoRedoShellBridge`, `IBackendClient`, `HttpClient` |

| Overlap (other slices) | This slice does **not** re-touch **Slice 29** Edit, **28** Help, **21** keyboard help dialog, **tool catalog** (Slice 10) beyond existing **`WireToolCatalogHandlers`**. |
| **Deferred (explicit)** | Rearchitecting `TransportShortcutCoordinator`; changing shortcut IDs |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** transport + timeline shell story. **No** opportunistic menu or startup edits in this slice.

## Acceptance

- `MainWindow.xaml.cs` contains **no** multi-line bodies for the seven **symbol group** above; `RegisterKeyboardShortcuts` zoom lambdas use **`_globalTransportShellBridge`**.**`ZoomIn`/`ZoomOut`/`ResetZoom`**.
- `Gap008Slice30Tests` + `MainWindowGlobalTransportShellBridgeTests`; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings elsewhere) |
| `dotnet test` … `Gap008Slice30\|MainWindowGlobalTransportShellBridge` | **7/7** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **231/231** Passed, **listedTestCount** **231**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_125336.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` (optional) | (not required this session) |

## RHVoice / CI freeze (Tasks 122, 95–96)

No **`engines/audio/rhvoice/`**; no **GOV** verify-harness closure churn. **Path B** RHVoice unchanged.

## Changelog

- 2026-04-26: **Tasks 114–123** — charter; bridge **`MainWindowGlobalTransportShellBridge`**.
