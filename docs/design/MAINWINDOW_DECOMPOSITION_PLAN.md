# MainWindow Decomposition Plan

**Status:** Living  
**Last Updated:** 2026-03-15  
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

## Next Slice: Navigation-Shell Behavior

**Target:** Extract panel switching, nav buttons, `OpenPanelByIdAsync` into a dedicated coordinator or service.

### Future Slices (Not Yet Scoped)

| Slice | Description | Priority |
|-------|-------------|----------|
| Search overlay / toolbar glue | Global search, command palette, toolbar | Low |
| Open/save project workflows | Project open/save flows (distinct from import) | Medium |

---

## Changelog

- 2026-03-16: Transport Wave 4 complete. Marked Status Bar, Transport Shortcut, Import Workflow as done. Next slice = Navigation-Shell Behavior.
- 2026-03-15: Initial plan; next slice = Status Bar Orchestration (Transport Coherence Wave 2 Task 9).
