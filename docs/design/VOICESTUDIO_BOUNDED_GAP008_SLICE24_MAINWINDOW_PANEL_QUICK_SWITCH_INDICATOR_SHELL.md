# GAP-008 Slice 24 — MainWindow panel quick-switch visual indicator shell (bounded)

**Status:** Accepted (Tasks 49–58)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP008` infix** distinguishes this **WinUI MainWindow** slice from other repo “Slice 24” documents (e.g. STT router — [VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED](VOICESTUDIO_BOUNDED_SLICE24_STT_ROUTER_FAIL_CLOSED.md) in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md)).

## Task 39 decision (one sentence)

**GAP-008 continues with Slice 24** on seam **panel quick-switch `Popup` + `PanelQuickSwitchIndicator` + hide timers (IDEA 1)** — one shell story; **Path G1**; umbrella **not** closed.

## Goal

Move **lazy `Popup`**, **`PanelQuickSwitchIndicator`**, **`DispatcherTimer` hide/slide window**, **fade storyboards**, and **`ShowPanelQuickSwitchIndicator` / `HidePanelQuickSwitchIndicator`** from **`MainWindow.xaml.cs`** into **`MainWindowPanelQuickSwitchShellBridge`**; **`MainWindow`** delegates **`ShowPanelQuickSwitchIndicator`** to the bridge, passes **bridge method** into **`ShellNavigationCoordinator`**, and wires **`DisposeQuickSwitchHideTimer`** on **`MainWindowLifetimeCleanupCoreChannels`**.

## IN / OUT

| IN | OUT |
|----|-----|
| `Popup` + `PanelQuickSwitchIndicator` + primary hide timer + `HidePanelQuickSwitchIndicator` animation close timer | **Nav rail `PanelPreviewPopup`** / **`MainWindowPanelPreviewShellBridge`** (Slice 23) |
| `ShowPanelQuickSwitchIndicator` used by `ShellNavigationCoordinator` + `MainWindow.SwitchToPanel` + `MainWindow.FocusPanelRegion` (gate: smoke mode skips) | **Execute** `OpenPanelByIdAsync` / `SwitchToPanel` body (stays `MainWindow` + coordinator) |
| `DisposeQuickSwitchHideTimer` for lifetime teardown (stop main hide timer) | **RHVoice** / engines / matrix / preflight; **CI verify-harness GOV** row or closure **edits** (Tasks 57–58) |
| **MainWindow** constructs **`MainWindowPanelQuickSwitchShellBridge`** before **`ShellNavigationCoordinator`** | Absorbing **`MainWindowNavigationShellBridge`**, **search/palette** bridges, or **nav preview** into this type |

## Dependency map (Task 52)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | After **`navButtonSink`**, **`_panelQuickSwitchShellBridge = new MainWindowPanelQuickSwitchShellBridge()`**; then **`new ShellNavigationCoordinator(..., _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator, ...)`** |
| **`MainWindow.SwitchToPanel`** (obsolete) | **Gate** `IsGateCSmokeMode` — then **`_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator(...)`** |
| **`MainWindow.FocusPanelRegion`** | **Gate** smoke — then **`_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator(...)`** |
| **`ShellNavigationCoordinator`**, **`SwitchToPanelByIdAsync`** | Already **gates** with **`_isGateCSmokeMode()`** before **`_showQuickSwitchIndicator`**; delegate targets bridge method |
| **`MainWindowPanelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator`** | Creates/positions **`Popup`**, **fade-in** storyboard, **1.5s** `DispatcherTimer` → `HidePanelQuickSwitchIndicator` |
| **`MainWindowPanelQuickSwitchShellBridge.HidePanelQuickSwitchIndicator`** (private) | **FadeOutThemeAnimation** + **200ms** `DispatcherTimer` to **`IsOpen = false`** |
| **`MainWindowLifetimeCleanupCoreChannels.DisposeQuickSwitchHideTimer`** | **`() => _panelQuickSwitchShellBridge.DisposeQuickSwitchHideTimer()`** |
| **`MainWindow.xaml` / `MainWindow.Shell.xaml.cs`** | **No** XAML change required for this slice (indicator is code-created) |

**Must not call into (anti-creep):** **`OpenPanelByIdAsync`**, **`IProjectWorkflowCoordinator`**, **`ISearchOverlayCoordinator`**, **`MainWindowPanelPreviewShellBridge`** (nav preview is separate), **`IBackendClient`**.

**Async / UI / side effects:** All UI on **UI thread**; **`DispatcherTimer`** for hide; nested timer for post-fade **close**; no file I/O; no COM except WinUI; **no** `Task.Run` for the indicator path.

**Deferred (not this slice):** full **IDEA 14** panel docking, wholesale **keyboard registration** refactors, **startup overlay** beyond existing gates, **`RegisterPanelQuickSwitchShortcut`** / **`OpenPanelByIdAsync`** orchestration (coordination remains **`ShellNavigationCoordinator` + `MainWindow`** as today).

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) **`MainWindowPanelPreviewShellBridge`** (Slice 23) and **`MainWindowPanelQuickSwitchShellBridge`** (this slice) are **separate** popups: **hover preview** vs **focus/switch** indicator — do not merge without a new brief.

## Acceptance

- **`MainWindow.xaml.cs`** has **no** `new Popup` / `PanelQuickSwitchIndicator` / `DispatcherTimer` for quick switch (moved to bridge); **`ShellNavigationCoordinator`** receives **`_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator`**.
- **`MainWindowLifetimeCleanupCoreChannels`** includes **`DisposeQuickSwitchHideTimer`**; **`RunCleanupCore`** invokes it after preview hide timer.
- **`Gap008Slice24Tests`** + **`MainWindowPanelQuickSwitchShellBridgeTests`**; filter [prepend only](../../tools/gap008_mainwindow_regression_filter.txt); full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | 0 errors (pre-existing nullability warnings in other files) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **192/192** Passed, `listedTestCount` **192**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_101924.trx`; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` (optional) | **exit 0** — `artifacts/verify/20260426_102238/verification_report.md` |

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q
python scripts/run_verification.py
```

## RHVoice / CI freeze

Per **Tasks 57–58**: no **`engines/audio/rhvoice/`** matrix theater; no closed verify-harness GOV / **STATE** churn for that row without new hosted **`workflow_dispatch`** + **`run_full_chain: true`**.
