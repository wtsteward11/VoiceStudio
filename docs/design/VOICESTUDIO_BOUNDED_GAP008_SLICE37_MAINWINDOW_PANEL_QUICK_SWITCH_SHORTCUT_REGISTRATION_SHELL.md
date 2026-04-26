# GAP-008 Slice 37 — MainWindow panel quick-switch shortcut registration shell (bounded)

**Status:** Accepted (Tasks 193–200)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 37”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 37** moving **Ctrl+1–9** **`nav.panel.{n}`** **`KeyboardShortcutService.RegisterShortcut`** wiring (nine panels) out of **`MainWindow.RegisterPanelQuickSwitchShortcut`** into **`MainWindowPanelQuickSwitchShortcutRegistrationShellBridge`**; **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 36** left **`RegisterPanelQuickSwitchShortcut`** on **`MainWindow`** and passed a **`registerPanelQuickSwitchGroup`** **`Action`** into **`MainWindowKeyboardShortcutRegistrationDependencies`**. **Slice 37** removes that coupling: **`MainWindowKeyboardShortcutRegistrationShellBridge`** no longer invokes **`RegisterPanelQuickSwitchGroup`**; **`MainWindow`** ctor calls **`RegisterAll`** on the new bridge **immediately after** **`_keyboardShortcutRegistrationShellBridge.Register`** (same **`KeyboardShortcutService`** instance; **`GetPanelTitle`** and **`OpenPanelByIdAsync`** remain **`MainWindow`**-owned delegates).

## IN / OUT

| IN | OUT |
|----|------|
| **`RegisterAll(KeyboardShortcutService, Func<string, string> getPanelTitle, Func<string, PanelRegion?, Task<bool>> openPanelByIdAsync)`** — nine **`nav.panel.{1..9}`** registrations (same panel IDs / regions as prior **`MainWindow`**) | **RHVoice** / `engines/audio/rhvoice/` |
| **Thin `MainWindow`** — field + ctor **`RegisterAll`**; **no** **`private void RegisterPanelQuickSwitchShortcut`** | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence |
| **Delegates from `MainWindow`** — **`GetPanelTitle`** → **`_navShellBridge`**, **`OpenPanelByIdAsync`** → **`_navShellBridge`** | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn in this batch |
| **Out:** | **`MainWindowPanelQuickSwitchShellBridge`** (**Slice 24**) **Popup** / indicator / timers — not this slice |
| | **`MainWindowPanelRegionFocusShellBridge`** (**Slice 27**) **Ctrl+Tab** / **Ctrl+Alt+1–4** — not this slice |
| | **Re-open** **Slice 36** bulk registration table (file/edit/nav/zoom/help/panel focus) — not this slice |

## One bridge class name

**`MainWindowPanelQuickSwitchShortcutRegistrationShellBridge`**

## Dependency map (Task 195)

| Area | Detail |
|------|--------|
| **`MainWindow` partial** | **`MainWindow.xaml.cs`** — **fields:** **`_panelQuickSwitchShortcutRegistrationShellBridge`** (readonly, adjacent to **`_keyboardShortcutRegistrationShellBridge`**). **Ctor cluster:** after **`_keyboardShortcutRegistrationShellBridge.Register(...)`** / **“Keyboard Shortcuts Registered”** checkpoint; **before** **“Menu Items Created”**. **Removed:** **`private void RegisterPanelQuickSwitchShortcut`** and the **nine-line** **`registerPanelQuickSwitchGroup`** lambda inside **`MainWindowKeyboardShortcutRegistrationDependencies`**. **Retained:** **`GetPanelTitle`**, **`OpenPanelByIdAsync`** (unchanged signatures). |
| **Injected / resolved services** | **`KeyboardShortcutService`** (same instance as Slice 36 registration). **No** new DI types. |
| **Events / coordinators** | **None** in the bridge — only **`RegisterShortcut`** calls. |
| **Async / ADR-047** | **Synchronous** **`RegisterAll`** in ctor after Slice 36 **`Register`**; shortcut callbacks use **`() => { _ = openPanelByIdAsync(...); }`** — **same** fire-and-forget pattern as pre–Slice 37 **`MainWindow`**. **No** ctor **`async void`**. |
| **Overlap / creep guards** | **Slice 24** **`MainWindowPanelQuickSwitchShellBridge`** = **IDEA 1** visual indicator — **must not** be referenced in this bridge file. **Slice 36** bridge = bulk registration **without** Ctrl+1–9 table. **Slice 27** = region focus shortcuts — **must not** move here. Tests: **`MainWindowPanelQuickSwitchShortcutRegistrationShellBridgeTests`** file-text asserts no **`MainWindowPanelQuickSwitchShellBridge`** type name in the new bridge source. |
| **Explicitly deferred** | **`SwitchToPanel`** (**obsolete**, error) — unchanged. **Panel preview** (**Slice 23**), **dock** (**Slice 25**). **Merging** quick-switch **indicator** with **registration** — rejected (different bounded seams). |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — this slice **only** moves **Ctrl+1–9** **registration** lines. **Do not** absorb **Slice 24** **`Popup`**, **Slice 27** focus cycling, or **Slice 36** file/menu/zoom table.

## Alternatives not Slice 37

- **Fold into `MainWindowKeyboardShortcutRegistrationShellBridge`** — blurs Slice 36 “bulk table” vs “numeric panel row” and widens **`MainWindowKeyboardShortcutRegistrationDependencies`** again.
- **Move `GetPanelTitle` into bridge** — would pull **`MainWindowNavigationShellBridge`** / nav surface into a service-type seam without a charter.

## Acceptance

- **`MainWindow`**: **no** **`private void RegisterPanelQuickSwitchShortcut`**. **`_panelQuickSwitchShortcutRegistrationShellBridge`**; ctor **`RegisterAll`** after **`Register`**. **`MainWindowKeyboardShortcutRegistrationDependencies`**: **no** **`registerPanelQuickSwitchGroup`** parameter; **`MainWindowKeyboardShortcutRegistrationShellBridge`**: **no** **`RegisterPanelQuickSwitchGroup`** call.
- **`Gap008Slice37Tests` + `MainWindowPanelQuickSwitchShortcutRegistrationShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line 2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 198–199 — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (host run) |
| `dotnet test` … `--filter` `FullyQualifiedName~Gap008Slice37Tests|FullyQualifiedName~MainWindowPanelQuickSwitchShortcutRegistrationShellBridgeTests` | **7 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **275/275** Passed; **listedTestCount** 275; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_175934.trx`**; `effectiveFilter` = line 2 of [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → `.buildlogs/verification/last_run.json` |

## Changelog

- 2026-04-26: **Tasks 193–200** — charter; bridge **`MainWindowPanelQuickSwitchShortcutRegistrationShellBridge`**; verification table completed post–Task 198.
