# GAP-008 Slice 25 — MainWindow panel docking / cross-region swap shell (bounded)

**Status:** Accepted (Tasks 59–65)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP008` infix** distinguishes this **WinUI MainWindow** slice from any other “Slice 25” documents in the repository (e.g. non–MainWindow bounded work). See [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Task 39 / Path decision (one sentence)

**GAP-008 continues with Slice 25** on seam **panel region dock / cross-region swap (IDEA 14)**: fade storyboard, unload/reload by panel id, `PanelStateService.MigratePanelState`, layout save debounce, toasts; **Path G1**; umbrella **not** closed.

## Goal

Move **`PanelHost_OnPanelDockRequested`**, **`AnimatePanelDock`**, and **`CompletePanelDockAsync`** from **`MainWindow.xaml.cs`** into **`MainWindowPanelDockShellBridge`**; **`MainWindow`** wires **`OnPanelDockRequested`**, provides **`getPanelHostByRegion`**, **`OpenPanelByIdAsync`**, **`PanelStateService`**, layout save, and toast accessor.

**Phase 0:** Slice 24 explicitly **deferred** full IDEA 14; this slice implements that single seam (no “junk drawer” Path G2).

## IN / OUT

| IN | OUT |
|----|-----|
| `OnPanelDockRequested` handler, `DoubleAnimation`+`Storyboard` fade, `CompletePanelDockAsync` (unload, migrate state, `OpenPanelByIdAsync`, debounced layout save, toasts) | **Nav rail** `PanelPreviewPopup` / **`MainWindowPanelPreviewShellBridge`** (Slice 23) |
| | **Panel quick-switch** `Popup` / **`MainWindowPanelQuickSwitchShellBridge`** (Slice 24) — separate visual story |
| | **ShellNavigationCoordinator** / **`MainWindowNavigationShellBridge` internals** (bridge only **calls** `OpenPanelByIdAsync` as injected delegate) |
| | **RHVoice** / engines / matrix / preflight; **CI verify-harness GOV** row or **closure** narrative **edits** (Tasks 66–67) |
| **One bridge:** `MainWindowPanelDockShellBridge` | Absorbing **preview** or **quick-switch** or **search** into this type |

## One bridge class name

**`MainWindowPanelDockShellBridge`**

## Dependency map (Task 61 — enumerated)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | After **`MainWindowNavigationShellBridge`**: **`_panelDockShellBridge = new MainWindowPanelDockShellBridge( getHostByRegion, OpenPanelByIdAsync, _panelStateService, () => _layoutSaveDebouncer?.Invoke(), () => ServiceProvider.TryGetToastNotificationService() )`**. `getHostByRegion` = same `PanelRegion` → named `PanelHost` resolution as `ShellNavigationCoordinator` region map. |
| **`MainWindow`** (post-`InitializeComponent`, `PanelRegion` set on hosts) | **`OnPanelDockRequested += _panelDockShellBridge.OnPanelDockRequested`** on each of Left/Center/Right/Bottom `PanelHost` |
| **`MainWindowPanelDockShellBridge.OnPanelDockRequested`** | Resolves target `PanelHost` from **`e.TargetRegion`**, no-op if missing or self; calls **`AnimatePanelDock`** |
| **`MainWindowPanelDockShellBridge.AnimatePanelDock`** (private) | `DoubleAnimation` 200ms fade out/in, **`Storyboard.Completed`** → opacities reset → **`_ = CompletePanelDockAsync(...)`** |
| **`MainWindowPanelDockShellBridge.CompletePanelDockAsync`** (private) | **`UnloadPanelAsync`**, **`MigratePanelState`**, **`_openPanelByIdAsync`**, **`_invokeLayoutSave`**, **`_getToast()`** success toasts |
| **`PanelHost` / `PanelDockEventArgs`** | Fires from **`OnPanelDockRequested`** in `PanelHost` (control layer unchanged) |
| **`PanelStateService`** | Injected; **`MigratePanelState`** only; no new coordinator |

**Must not call into (anti-creep):** **`MainWindowNavigationShellBridge`**, **`MainWindowPanelPreviewShellBridge`**, **`MainWindowPanelQuickSwitchShellBridge`**, **`ISearchOverlayCoordinator`**, **`IProjectWorkflowCoordinator`**, **direct** `ServiceProvider` / `AppServices` (toast via ctor **`Func` only** from MainWindow).

**Async / UI / side effects:** UI thread for animation completion path; `ConfigureAwait(true)` on async continuations; no new background threads; no file I/O in bridge.

**Deferred (not this slice):** **workspace splitter** drag, wholesale **InitializePanels** / **RestorePanelsFromLayout** refactors. **Follow-up:** **startup overlay** extraction → [Slice 26](VOICESTUDIO_BOUNDED_GAP008_SLICE26_MAINWINDOW_STARTUP_OVERLAY_SHELL.md) (`MainWindowStartupOverlayShellBridge`).

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md): **hover preview** (23), **quick-switch label** (24), and **dock/swap** (this slice) remain **separate** WinUI surface stories. Do not merge.

## Acceptance

- **`MainWindow.xaml.cs`** has **no** `AnimatePanelDock`, `CompletePanelDockAsync`, or inline `PanelHost_OnPanelDockRequested` **body** (only bridge field, ctor, **`+=` wiring**).
- **`Gap008Slice25Tests`** + **`MainWindowPanelDockShellBridgeTests`**; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings elsewhere) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **199/199** Passed, `listedTestCount` **199**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_104323.trx`; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |

## RHVoice / CI freeze (Tasks 66–67)

No edits under **`engines/audio/rhvoice/`** for matrix narrative; no closed **verify-harness GOV** row rewrites in this change set.
