# GAP-008 Slice 36 — MainWindow keyboard shortcut registration shell (bounded)

**Status:** Accepted (Tasks 179–188)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 36”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 36** on **bulk `KeyboardShortcutService.RegisterShortcut` wiring** (file / edit / nav / zoom / help **F1** that forwards to the existing **Help → Keyboard Shortcuts** menu handler, plus **Ctrl+1–9** quick-switch and **Ctrl+Alt+1–4** region focus) moved out of **`MainWindow.RegisterKeyboardShortcuts`**; **umbrella GAP-008 is not closed** (not Path G2). The **next** seam after this lands = **Slice 37+** with an **Accepted** `VOICESTUDIO_BOUNDED_GAP008_SLICE37_*.md`.

## Goal

**`RegisterKeyboardShortcuts`** (large method on **`MainWindow`**) only orchestrated registration of many shortcuts. **`MainWindowKeyboardShortcutsShellBridge` (Slice 21)** remains the **Help → Keyboard Shortcuts** **dialog** flow — this slice does **not** replace it; it only moves **registration** of global shortcuts to **`MainWindowKeyboardShortcutRegistrationShellBridge`**.

**`RegisterPanelQuickSwitchShortcut` (Slice 27)** stays on **`MainWindow`** per prior charter; the registration bridge calls a **single** delegate **`registerPanelQuickSwitchGroup`** that **`MainWindow`** implements (nine `RegisterPanelQuickSwitchShortcut` calls).

## IN / OUT

| IN | OUT |
|----|------|
| **`Register(KeyboardShortcutService, MainWindowKeyboardShortcutRegistrationDependencies)`** — all **`RegisterShortcut`** lines that lived in **`MainWindow.RegisterKeyboardShortcuts`** | **RHVoice** / `engines/audio/rhvoice/` |
| **Dependencies** — undo/redo shell, search overlay, global transport zoom, panel region focus, command palette + tool catalog **actions** supplied by **`MainWindow`** | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence (Tasks **95–96** / **104** / **112** / **122** / **133** |
| **`registerPanelQuickSwitchGroup`** — **`Action`** implemented in **`MainWindow`** (invokes nine **`RegisterPanelQuickSwitchShortcut`** only) | **Task 103** / **113** / **123** / **134** / **148** / **158** / **168** / **178** — not spine gates (optional runtime / appendix) |
| **`triggerHelpKeyboardShortcutsFromShortcut`** — **best-effort**; may no-op if menu item not yet assigned (ctor order) | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn in this batch |
| **Out:** | **Re-open** **Slice 34** menu bar, **Slice 33** splitters, **Slice 35** tool-catalog host chrome, **Slice 10** catalog **dialog** body — not this slice. |

## One bridge class name

**`MainWindowKeyboardShortcutRegistrationShellBridge`**

## Dependency map (Task 182)

| Area | Detail |
|------|--------|
| **Entrypoint** | **`MainWindow` ctor** — after **`MainWindowKeyboardShortcutsShellBridge`** and **recent projects** bridges; **before** in-code **Menu Items Created** — `new MainWindowKeyboardShortcutRegistrationShellBridge()` + **`Register(...)`** (replaces **`RegisterKeyboardShortcuts()`**). |
| **Downstream** | **`KeyboardShortcutService`** (injected from **`ServiceProvider`** in **`MainWindow`**) — still owned by window; bridge only **registers** shortcuts. **`MainWindowEditUndoRedoShellBridge`**, **`MainWindowSearchOverlayShellBridge`**, **`MainWindowGlobalTransportShellBridge`**, **`MainWindowPanelRegionFocusShellBridge`** — passed by reference in **`MainWindowKeyboardShortcutRegistrationDependencies`**. |
| **Async / ADR-047** | Synchronous registration in ctor; **no** new async from ctor. **Tool catalog** path uses **fire-and-forget** `Task` on shortcut (`_ = ShowToolCatalogAsync()`) — unchanged from prior **`MainWindow`** behavior. |
| **Side effects** | Populates **global shortcut** table; **no** I/O. Help shortcut may invoke **`KeyboardShortcutsMenuItem_Click`** (dialog) when menu wired. |
| **Overlap** | **Slice 21** **`MainWindowKeyboardShortcutsShellBridge`**: **dialog** only; this slice is **registration** only. **Slice 27/30**: panel focus / quick-switch / zoom still delegate to existing bridges; no duplicate logic. |
| **Explicitly OUT** | Moving **`RegisterPanelQuickSwitchShortcut` method** into the bridge. Moving **`IsGateCSmokeMode` / `IsSafeStartupMode`** (used elsewhere). **Transport** recording shortcuts in **Loaded** — not here. |
| **Rejected alternatives** | **Merge with Slice 21 class** — blurs “Help dialog” vs “ctor registration” and breaks test filter naming. **Extract only zoom** — too small vs remaining blob. **Session lifecycle** cluster — different seam (`MainWindowSessionLifecycle` already). |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — this slice **only** moves **shortcut registration** lines. **Do not** absorb **menu bar** build, **splitter** pointers, or **tool catalog** chrome.

## Alternatives not Slice 36

- **`IsGateCSmokeMode` static** extraction — used by **multiple** subsystems; separate brief.
- **Full `MainWindow` ctor split** — out of scope (umbrella / Path G2 product call).

## Acceptance

- **`MainWindow`**: no **`private void RegisterKeyboardShortcuts`**. **`_keyboardShortcutRegistrationShellBridge`** field; ctor calls **`Register`**.  
- **`Gap008Slice36Tests` + `MainWindowKeyboardShortcutRegistrationShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 185–186 — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (host run) |
| `dotnet test` … `--filter` with `FullyQualifiedName~Gap008Slice36Tests` OR `FullyQualifiedName~MainWindowKeyboardShortcutRegistrationShellBridgeTests` (same tokens as filter file line 2) | **6 passed** (directed slice tests) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **268/268** Passed; **listedTestCount** 268; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_173120.trx`**; `effectiveFilter` = line 2 of [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** (green fixture vs TRX coherence) |
| `python scripts/run_verification.py` | **Overall: PASS** → `.buildlogs/verification/last_run.json` |

## Changelog

- 2026-04-26: **Tasks 185–186** — verification table filled; spine **N=268**; reconciliation § *Spine size after Slice 36*.
- 2026-04-26: **Tasks 179–188** — charter; bridge **`MainWindowKeyboardShortcutRegistrationShellBridge`**.
