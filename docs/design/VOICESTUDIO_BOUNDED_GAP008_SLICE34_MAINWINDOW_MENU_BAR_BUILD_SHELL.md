# GAP-008 Slice 34 — MainWindow menu bar build shell (bounded)

**Status:** Accepted (Tasks 159–168)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from other **“Slice 34”** rows in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 34** on the **top-level `MenuBar` build** (Phase 0: menu created in code behind, not XAML); **umbrella GAP-008 is not closed** (not Path G2). The **next** seam after this slice = **35+** with an **Accepted** `VOICESTUDIO_BOUNDED_GAP008_SLICE35_*.md`.

## Goal

Move **`InitializeMenuBar`** and all **`Build*Menu` / `Create*MenuItem`** methods from the former [`MainWindow.Menu.cs`](../../src/VoiceStudio.App/) partial into **`MainWindowMenuBarShellBridge`**.  
**`MainWindow`** remains responsible for: constructing **pre-wired** flyout **menu item instances** (recent projects, customize toolbar, …), **`IPanelRegistry`** via `UnifiedPanelRegistry`, and **`CommandRouter`**. The bridge assembles the **`MenuBar`**, wires **`IPanelRegistry.GetAllDescriptors()`** for the **Modules** tree, and delegates all command paths via **`MainWindowMenuBarCommandCallbacks`**.

**Deferred (explicit):** `MainWindowRecentProjectsMenuPopulationShellBridge` **PopulateRecentProjectsMenu** (called from Loaded, not during initial bar build), **Slice 20** menu/tool **activation** handler *implementation* (stays in **`MainWindowMenuToolActivationShellBridge`**), **Slice 21** keyboard **shortcuts dialog** stack, **Task 103 / 113 / 123 / 134 / 148 / 168** optional appendices, **full workspace** init/restore (Slice 33 deferred items).

## IN / OUT

| IN | OUT |
|----|------|
| `Func<ContentControl?>` → **`MenuBarHost`**; `IPanelRegistry` for **Modules** menu; pre-built **`MenuFlyoutSubItem` / `MenuFlyoutItem`** for recent projects + Tools menu lines; `CommandRouter?` for `WireMenuItem` and playback/nav commands | **RHVoice** / `engines/audio/rhvoice/` |
| `MainWindowMenuBarCommandCallbacks` — all **File / Edit / View / AI / Help** actions, **`ExecuteNavCommand`**, `OpenPanelByIdAsync` | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence (Tasks **95–96** / **104** / **112** / **122** / **133** |
| `MainWindowMenuBarShellWire` — optional flyout item refs + router | **Task 103** / **113** / **123** / **134** / **148** / **168** — **not** spine gates |
| **Overlap:** **Slice 22** = **flyout** population; this slice = **static** `MenuBar` top-level + Modules registry sweep only | [VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md) **churn** in this batch |

## One bridge class name

**`MainWindowMenuBarShellBridge`**

## Dependency map

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** (after in-code `MenuFlyoutItem` + submenus, **“Menu Items Created”** checkpoint) | `new MainWindowMenuBarShellBridge(FindInContent, UnifiedPanelRegistry, wire, callbacks)` + **`InitializeMenuBar()`** immediately |
| **`IPanelRegistry`** | **`GetAllDescriptors()`** for **Modules** grouped by `MenuCategory` |
| **`CommandRouter?`** | **`WireMenuItem`** for View **Back/Forward**, **Playback** sub-commands; **null** → direct menu items + `Debug` fallback (unchanged) |
| **UI thread** | ctor-time menu build; **no** new async from ctor (**ADR-047**); all **`Click`** on UI thread |
| **Side effects** | **Assigns** `ContentControl.Content` = new **`MenuBar`**; no toasts; **no** `ContentDialog` from the bridge (Help/About stay on **`MainWindowHelpAboutShellBridge`**) |
| **Must not name in bridge (anti-creep):** | `MainWindowWorkspaceSplitterShellBridge`, `MainWindowImportWorkflowShellBridge` implementation, **`IBackendClient`**, `PopulateRecentProjectsMenu` (Loaded hook) |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** top-level **menu bar assembly** story. **Do not** merge **Slice 22** flyout population, **Slice 20** per-handler bodies, or **search overlay** into this file without a new bounded brief.

## Alternatives not Slice 34

- **Full `InitializePanelsAsync` / workspace restore** — already deferred across multiple slices.  
- **`MainWindow.Smoke.cs`** (Gate C) — different test harness story.

## Acceptance

- **`MainWindow.xaml.cs`**: `new` **`MainWindowMenuBarShellBridge`** + **`_menuBarShellBridge.InitializeMenuBar()`**; **no** `InitializeMenuBar` method in `MainWindow`.  
- **Former** `MainWindow.Menu.cs` **removed** (logic lives in **`Services/MainWindowMenuBarShellBridge.cs`**).  
- **`Gap008Slice34Tests` + `MainWindowMenuBarShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings only) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **255/255** Passed, **listedTestCount** **255**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_154959.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |

## Changelog

- 2026-04-26: **Tasks 159–168** — charter; bridge **`MainWindowMenuBarShellBridge`**; `MainWindow.Menu.cs` removed.
