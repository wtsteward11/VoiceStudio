# GAP-008 Slice 38 — MainWindow keyboard shortcut key dispatch shell (bounded)

**Status:** Accepted (Tasks 201–210)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 38”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 38** moving **root `KeyDown` modifier assembly + `KeyboardShortcutService.TryHandleKeyDown`** out of **`MainWindow.MainWindow_KeyDown`** into **`MainWindowKeyboardShortcutKeyDispatchShellBridge`**; **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 36** registers shortcuts; **Slice 37** registers **Ctrl+1–9** panel row. **Runtime dispatch** (modifier bitmask + **`TryHandleKeyDown`**) remained on **`MainWindow`**. **Slice 38** removes that body so **`MainWindow_KeyDown`** is a **thin forward** only; **registration** stays in **Slice 36/37** bridges.

## IN / OUT

| IN | OUT |
|----|------|
| **`TryHandleKeyDown(KeyboardShortcutService, KeyRoutedEventArgs)`** — same modifier rules as prior **`MainWindow_KeyDown`** (**Control** / **Shift** / **Menu** via **`InputKeyboardSource`**) | **RHVoice** / `engines/audio/rhvoice/` |
| **Thin `MainWindow`** — field + ctor **`new`**; **`MainWindow_KeyDown`** → bridge only | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence |
| **`e.Handled = true`** when service handles the key | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn / matrix theater |
| | **Tasks 103 / 113 / 123 / 134 / 148 / 158 / 168 / 178 / 188** — optional runtime appendix; **not** spine gates |
| | **`MainWindowKeyboardShortcutRegistrationShellBridge`** (**Slice 36**) — **no** moving **`RegisterShortcut`** tables into this bridge |
| | **`MainWindowPanelQuickSwitchShortcutRegistrationShellBridge`** (**Slice 37**) — **no** registration merge |
| | **`MainWindowKeyboardShortcutsShellBridge`** (**Slice 21**) — **Help** dialog flow; **not** global **`KeyDown`** routing |

## One bridge class name

**`MainWindowKeyboardShortcutKeyDispatchShellBridge`**

## Dependency map (Task 204)

| Area | Detail |
|------|--------|
| **`MainWindow` partial** | **`MainWindow.xaml.cs`** — **field:** **`_keyboardShortcutKeyDispatchShellBridge`** (readonly; after **`_panelQuickSwitchShortcutRegistrationShellBridge`** / **Keyboard Shortcuts Registered** checkpoint). **Ctor:** `new MainWindowKeyboardShortcutKeyDispatchShellBridge()` after **`RegisterAll`** / before **Menu Items Created**. **`MainWindow_KeyDown`:** single forward to **`TryHandleKeyDown(_keyboardShortcutService, e)`**. **Removed:** inline **`VirtualKeyModifiers`** assembly + direct **`TryHandleKeyDown`** body. |
| **Injected / resolved services** | **`KeyboardShortcutService`** (existing **`_keyboardShortcutService`** instance). **No** new DI types. |
| **Events** | **`KeyDown`** still subscribed on **`root`** (existing Loaded wiring); handler target only changes implementation location. |
| **Async / ADR-047** | **Synchronous** dispatch on UI thread; **no** ctor async. |
| **Overlap / creep guards** | **Slice 36/37** = **registration** only. This bridge = **dispatch** only; source must not reference **`RegisterShortcut`** or **`MainWindowKeyboardShortcutRegistrationShellBridge`**. Tests: **`MainWindowKeyboardShortcutKeyDispatchShellBridgeTests`** file-text asserts. |
| **Explicitly deferred** | **`IsSafeStartupMode` / `IsGateCSmokeMode`** env probes; **`MainWindow_Activated`** try/catch; **obsolete `SwitchToPanel`**. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 36** bulk **`RegisterShortcut`** table, **Slice 37** Ctrl+1–9 registration, or **Slice 21** keyboard **Help** dialog.

## Alternatives not Slice 38

- **Fold into `MainWindowKeyboardShortcutRegistrationShellBridge`** — conflates ctor-time registration with per-key runtime dispatch.
- **Move `InitializeAsync`** for **`KeyboardShortcutService`** — different lifecycle seam.

## Acceptance

- **`MainWindow`**: **no** inline **`VirtualKeyModifiers`** block inside **`MainWindow_KeyDown`**; **`_keyboardShortcutKeyDispatchShellBridge`**; thin **`KeyDown`** handler.
- **`Gap008Slice38Tests` + `MainWindowKeyboardShortcutKeyDispatchShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line 2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 207–208)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing nullable warnings in other projects) |
| `dotnet test` … `--filter` `FullyQualifiedName~Gap008Slice38Tests\|FullyQualifiedName~MainWindowKeyboardShortcutKeyDispatchShellBridgeTests` | **7 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **282/282** Passed; **listedTestCount** 282; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_185722.trx`**; `effectiveFilter` = line 2 of [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → `.buildlogs/verification/last_run.json` |

## Changelog

- 2026-04-26: **Tasks 201–210** — charter; bridge **`MainWindowKeyboardShortcutKeyDispatchShellBridge`**; verification table completed post–Task 207.
