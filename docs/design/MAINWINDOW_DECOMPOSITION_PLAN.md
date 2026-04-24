# MainWindow Decomposition Plan

**Status:** Living  
**Last Updated:** 2026-04-25  
**Related:** Transport Coherence Wave 2 Task 9, MainWindow god-object reduction

## Overview

MainWindow.xaml.cs is ~2450 lines across multiple partials. This plan identifies extraction targets to reduce the god-object status. Each slice is scoped and actionable.

## Current Structure

| Partial | Responsibility | Lines (approx) |
|---------|----------------|----------------|
| MainWindow.xaml.cs | Core shell, panel creation, Loaded, navigation, transport | ~2400 |
| MainWindow.StatusBar.cs | Status bar metrics (CPU, GPU, RAM, clock), latency | ~140 |
| MainWindow.Workspaces.cs | Workspace layout, splitter, restore | — |
| MainWindow.Menu.cs | Menu creation, recent projects | — |
| MainWindow.Smoke.cs | Smoke test orchestration | — |

## Already Extracted

- **Transport orchestration:** `GlobalTransportOrchestrator` + `TransportOrchestrationBootstrap` (Transport Coherence Wave 1)
- **Status Bar Orchestration:** `StatusBarCoordinator` (Transport Coherence Wave 3) — Attach/Subscribe/Unsubscribe; current media, reachability, degraded banner
- **Transport Shortcut Orchestration:** `TransportShortcutCoordinator` (Transport Coherence Wave 4 Phase 1) — Space/S/Ctrl+R via IGlobalTransportOrchestrator
- **Import Workflow:** `IImportWorkflowService` + `ImportWorkflowService` (Transport Coherence Wave 4 Phase 2) — picker, upload, AssetAddedEvent, SetCurrentPlayable, toast

## GAP-008 Slice 1 — landed 2026-04-24

**Loaded shell bootstrap orchestration** moved to `MainWindowShellLoadedBootstrap.RunAsync` with `MainWindowLoadedBootstrapHooks` (see [VOICESTUDIO_BOUNDED_GAP008_SLICE01_MAINWINDOW_LOADED_BOOTSTRAP.md](VOICESTUDIO_BOUNDED_GAP008_SLICE01_MAINWINDOW_LOADED_BOOTSTRAP.md)). DEBUG diagnostics, `TransportShortcutCoordinator` attach, and `RunPanelInitWhenReadyAsync` remain in `MainWindow` for the next extraction.

## GAP-008 Slice 2 — landed 2026-04-25

**Navigation-shell glue** moved to **`MainWindowNavigationShellBridge`** + **`NavButtonActionSink`** (rail toggles, `INavigationService` subscribe/unsubscribe, nav command forwards). **`MainWindow`** keeps one-line forwards for partial call sites. Bounded brief: [VOICESTUDIO_BOUNDED_GAP008_SLICE02_MAINWINDOW_NAVIGATION_SHELL.md](VOICESTUDIO_BOUNDED_GAP008_SLICE02_MAINWINDOW_NAVIGATION_SHELL.md); seam tests **`Gap008Slice2Tests`**.

## Next Slice: Loaded transport and panel-init tail (Slice 3)

**Target:** `contentFE.Loaded` tail only — **`TransportShortcutCoordinator.Attach`** + **`RunPanelInitWhenReadyAsync`** — chartered separately (ADR-047 ordering unchanged). **Not** navigation-shell or `NavigationViewModel` alias unification.

### Future Slices (Not Yet Scoped)

| Slice | Description | Priority |
|-------|-------------|----------|
| Search overlay / toolbar glue | Global search, command palette, toolbar | Low |
| Open/save project workflows | Project open/save flows (distinct from import) | Medium |

---

## Changelog

- 2026-04-25: **GAP-008 Slice 2** — `MainWindowNavigationShellBridge` + `NavButtonActionSink`; `NavigationService.NavigationChanged` lifecycle + nav rail `SetActiveNavButton` + command/nav forwards; `Gap008Slice2Tests` (6). **Next:** Slice 3 Loaded tail (`TransportShortcutCoordinator` + `RunPanelInitWhenReadyAsync`) — see Slice 2 brief §Slice 3 candidate.
- 2026-04-24: **GAP-008 Slice 1** — Loaded-time bootstrap block (ErrorDialogService root through title bar init) extracted to `MainWindowShellLoadedBootstrap`; seam tests `Gap008Slice1Tests`. Prior “Next Slice: Navigation-Shell” line referred to coordinator work already partially done; **navigation-shell polish** is the next chartered slice **after** this bootstrap cut (see bounded brief).
- 2026-03-16: Transport Wave 4 complete. Marked Status Bar, Transport Shortcut, Import Workflow as done. Next slice = Navigation-Shell Behavior.
- 2026-03-15: Initial plan; next slice = Status Bar Orchestration (Transport Coherence Wave 2 Task 9).
