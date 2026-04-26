# GAP-008 Slice 27 — MainWindow panel region focus + cycling shell (bounded)

**Status:** Accepted (Task 79 — Path G1)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP008` infix** and **“MainWindow”** in the filename distinguish this **WinUI MainWindow** slice from other repository “Slice 27” work (e.g. [whisper.cpp transcript harness](VOICESTUDIO_BOUNDED_SLICE27_WHISPER_CPP_TRANSCRIPT_PROOF.md) in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) *Bounded Slices* addendum).

## Task 79 / Path decision (one sentence)

**GAP-008 continues with Slice 27** on seam **panel region focus and keyboard cycling (historical GAP-E02)**: **Ctrl+Tab** / **Ctrl+Shift+Tab** cycle, **Ctrl+Alt+1–4** direct focus, **`PanelHost`** / hosted content **Focus**, optional quick-switch label via **injected callback**; **Path G1**; umbrella **not** closed.

## Goal

Move **`CyclePanelNext`**, **`CyclePanelPrevious`**, and **`FocusPanelRegion`** from **`MainWindow.xaml.cs`** into **`MainWindowPanelRegionFocusShellBridge`**. **`MainWindow`** keeps **`RegisterKeyboardShortcuts`** / **`RegisterPanelQuickSwitchShortcut`** as today but forwards **cycling and focus** to the bridge; the bridge receives **`Func<PanelRegion, PanelHost?>`** (same four hosts as other shells), **`Func<bool> isGateCSmokeMode`**, and **`Action<string, PanelRegion, PanelHost>`** for the IDEA 1 label ( **`MainWindow`** injects a lambda that calls the Slice 24 quick-switch path — **this bridge file does not name** that bridge type, per creep tests).

**Phase 0 (seam choice):** **Workspace splitter** / full **`InitializePanels`** migration remains **deferred** — this slice is **one story** (region focus + cycle + indicator hook), not **`MainWindow.Workspaces.cs`**.

## IN / OUT

| IN | OUT |
|----|-----|
| **Ctrl+Tab** / **Ctrl+Shift+Tab** / **Ctrl+Alt+1–4** → **`CyclePanelNext`** / **`CyclePanelPrevious`** / **`FocusPanelRegion`** | **IDEA 1** **`Popup`/`PanelQuickSwitchIndicator`/`DispatcherTimer` implementation** — **Slice 24** (`MainWindowPanelQuickSwitchShellBridge`); this slice **only** invokes the host callback |
| **Order + index** for cycling; **null** `PanelHost` = no-op | **Wholesale keyboard** table migration (`RegisterPanelQuickSwitchShortcut` **Ctrl+1–9** stays on **`MainWindow`**) |
| | **Panel dock** (**Slice 25**), **nav preview** (**Slice 23**), **startup overlay** (**Slice 26**), **search / palette** |
| **One bridge:** `MainWindowPanelRegionFocusShellBridge` | **RHVoice** / engines / preflight; **CI verify-harness GOV** row or **closure** narrative **edits** (Tasks 86–87) |
| | Absorbing **`MainWindowPanelQuickSwitchShellBridge`** or **`ShellNavigationCoordinator`** into this file |

## One bridge class name

**`MainWindowPanelRegionFocusShellBridge`**

## Dependency map (Task 81 — evidence from `MainWindow.xaml.cs` + new bridge)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | After **`new MainWindowPanelQuickSwitchShellBridge()`**: `new MainWindowPanelRegionFocusShellBridge( region => Left/Center/Right/Bottom PanelHost, IsGateCSmokeMode, (name, region, host) => _panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator(...) )` |
| **`RegisterKeyboardShortcuts`** (same method as other shortcuts) | **Thin:** `_panelRegionFocusShellBridge.CyclePanelNext` / `CyclePanelPrevious` / `FocusPanelRegion(Left|Center|Right|Bottom)` — **no** private **`CyclePanelNext`** / **`FocusPanelRegion`** on **`MainWindow`** |
| **`SwitchToPanel`** (obsolete) | Unchanged; still calls **`_panelQuickSwitchShellBridge.ShowPanelQuickSwitchIndicator`** directly (not this slice) |
| **Downstream** | WinUI **`Focus`**, **`PanelHost`**, **`FrameworkElement`**; callback may run Slice 24 indicator |
| **Async / UI** | Synchronous **UI-thread** **Focus**; **no** ctor fire-and-forget; **no** new **ContentDialog** |

**Must not call into (anti-creep — by type name in this file):** **`MainWindowPanelQuickSwitchShellBridge`**, **`MainWindowPanelDockShellBridge`**, **`MainWindowNavigationShellBridge`**, **`MainWindowStartupOverlayShellBridge`**, **`MainWindowSearchOverlayShellBridge`** — the indicator is **only** via the injected **`Action<…>`** from **`MainWindow`**.

**Explicitly deferred:** **Workspace** splitter / **restore** wholesale; broad **keyboard** registration refactors; **other** `MainWindow*ShellBridge` responsibilities.

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md): **Quick-switch popup** (Slice 24) and **focus/cycle behavior** (Slice 27) stay in **separate** types; **MainWindow** is the only composition point that **closes** over both.

## Acceptance

- **`MainWindow.xaml.cs`** has **no** GAP-E02 private **`CyclePanelNext` / `FocusPanelRegion`** **methods**; no **`GAP-E02`** **marker** in source (logic lives in the bridge + brief).
- **`Gap008Slice27Tests`** + **`MainWindowPanelRegionFocusShellBridgeTests`**; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings elsewhere) |
| `dotnet test` … `Gap008Slice27|MainWindowPanelRegionFocusShellBridge` | **6/6** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **212/212** Passed, `listedTestCount` **212**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_113330.trx`; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` | **exit 0** (same session, post–spine) |

## RHVoice / CI freeze (Tasks 86–87)

No edits under **`engines/audio/rhvoice/`**; no “optimistic” [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) theatre; no **GOV** verify-harness **closure** rewrites in this change set (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md)).

## Task 88 (Path G2)

**Not** applicable — this slice **is** G1; **Path G2** (umbrella closure) is out of scope for this charter.
