# GOV-VOICESTUDIO-GAP070-USER-PREFERENCES-PERSISTENCE-BOUNDARY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP070_USER_PREFERENCES_PERSISTENCE_BOUNDARY_01`  
**Status:** **Closed** (closure 2026-04-07)  
**Tracker:** [GAP-070](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** GAP-014 **Closed** (single workspace authority); GAP-013 **Closed** (PanelHost lifecycle)

## Problem statement

After GAP-014, workspace **authority** is singular (`PanelStateService` + `MainWindow` orchestration), but **persistence semantics** can still drift: `SavePanelState` could leave `RegionState.ActivePanelId` stale while updating `PanelStates`; concurrent `SaveCurrentWorkspaceAsync` load-merge-save cycles can interleave; restore iteration order followed persisted JSON region order rather than a fixed shell order; legacy workspace DI must never relapse.

## Frozen architecture decisions

1. **Canonical workspace layout owner:** [PanelStateService.cs](../../src/VoiceStudio.App/Services/PanelStateService.cs) — in-memory `WorkspaceLayout`, merge into `SettingsData.WorkspaceLayout`, persist via `ISettingsService.SaveSettingsAsync`; profile files under `%LocalAppData%\VoiceStudio\WorkspaceProfiles\` for named profiles.
2. **Canonical shell apply/restore owner:** [MainWindow.Workspaces.cs](../../src/VoiceStudio.App/MainWindow.Workspaces.cs) — `RestorePanelsFromLayoutAsync`, `SaveWorkspaceLayout`, `OnWorkspaceProfileChanged` / `RestoreAfterProfileChangeAsync`.
3. **Settings transport:** [SettingsService.cs](../../src/VoiceStudio.App/Services/SettingsService.cs) — backend `/api/settings` + local mirror + in-memory cache; workspace saves must not lose merges under concurrent writes (**serialized** `SaveCurrentWorkspaceAsync`).
4. **Project autosave:** [SessionAutosaveOrchestrator.cs](../../src/VoiceStudio.App/Services/SessionAutosaveOrchestrator.cs) / `IProjectWorkflowCoordinator` — **project-scoped** only; **not** a second writer for `WorkspaceLayout` (see [GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_PERSISTENCE_FOUNDATION_01_EXECUTION_ROW.md)).
5. **GAP-014 / GAP-013:** No `IWorkspaceService`/`ILayoutService` DI; no PanelHost lifecycle contract rewrite.

## §0.1 Authority map (code-truth at lane open)

| Surface | Load / save role | Canonical? |
|--------|------------------|------------|
| `PanelStateService.LoadCurrentWorkspaceAsync` | Boot: load `SettingsData.WorkspaceLayout` or embedded studio; may persist first-run | **Yes** (load owner for layout DTO) |
| `PanelStateService.SaveCurrentWorkspaceAsync` | Merge `GetCurrentLayout()` → `settings.WorkspaceLayout` → `SaveSettingsAsync` | **Yes** (save owner for layout blob) |
| `PanelStateService.SaveRegionState` / `SavePanelState` / `SaveRegionCollapsedState` | Mutate layout + schedule save | **Yes** |
| `MainWindow.SaveWorkspaceLayout` | Snapshot grid + `PanelHost` → `SaveRegionState` / `SaveRegionCollapsedState` | **Yes** (orchestrated snapshot) |
| `PanelHost.HandleContentChangeAsync` | `SaveOutgoingPanelState` → `SavePanelState` | **Yes** (panel-local dict + active panel) |
| `MainWindow.InitializePanelsAsync` | `RestorePanelsFromLayoutAsync` then `StartAutosave` | **Yes** (restore orchestration + post-restore project autosave) |
| `SettingsService` | Transport + cache for full `SettingsData` | **Yes** (transport, not layout authority) |
| `SessionAutosaveOrchestrator` | Project dirty → `TryAutosaveProjectAsync` | **Adjacent** (must not write workspace layout) |
| `IWorkspaceService` / `ILayoutService` | Legacy types | **No** — not registered (GAP-014) |

### Load order (runtime shell path)

1. `PanelStateService` ctor → `_ = LoadCurrentWorkspaceAsync()` → `ISettingsService.LoadSettingsAsync` (or embedded layout + persist).
2. `MainWindow` Loaded → `RunPanelInitWhenReadyAsync` → `StartupGatingHelper.WaitForBackendReadyThenAsync` → `InitializePanelsAsync`.
3. `InitializePanelsAsync` → `RestorePanelsFromLayoutAsync` → per region `PanelHost.LoadPanelAsync` (**deterministic region order:** Left → Center → Right → Bottom → Floating) → `RestoreSplitterRatios`.
4. `InitializePanelsAsync` → `_sessionLifecycle.StartAutosave` (project scope).

### Save order (workspace shell path)

- Panel switches: `PanelHost` → `SavePanelState` → `SaveCurrentWorkspace` (async queue).
- Geometry: debounced / close `SaveWorkspaceLayout` → `SaveRegionState` / `SaveRegionCollapsedState`.
- Profile switch: `SwitchWorkspaceProfileAsync` (disk + settings).

## Contract (chosen)

- **Single merge-save pipeline:** all `SettingsData` workspace writes go through `PanelStateService.SaveCurrentWorkspaceAsync`, guarded by a **process-wide async lock** (per service instance) so load-merge-save cannot interleave.
- **Active panel correctness:** `SavePanelState` **always** sets `RegionState.ActivePanelId` to the panel whose state is being saved.
- **Deterministic restore:** `RestorePanelsFromLayoutAsync` sorts regions by `PanelRegion` (Left → Center → Right → Bottom → Floating) before restoring content, then applies splitter ratios.

## Acceptance contract (all required for Close)

- [x] `SavePanelState` keeps `ActivePanelId` aligned with persisted panel id when the region already exists.
- [x] `SaveCurrentWorkspaceAsync` is serialized (no interleaved load-merge-save).
- [x] `RestorePanelsFromLayoutAsync` restores regions in deterministic `PanelRegion` order; markers `GAP-070-order-1` / `GAP-070-order-2` remain in source for seam tests.
- [x] `WorkspaceAuthoritySeamTests` / `ShellPersistenceAuthoritySeamTests` prove GAP-014 DI boundary + GAP-070 contracts.
- [x] Closure matrix + post-commit `run_verification.py` **completion_guard** PASS; tracker + registry + STATE synced.

## Allowlist

`src/VoiceStudio.App/Services/PanelStateService.cs`, `src/VoiceStudio.App/MainWindow.Workspaces.cs`, `src/VoiceStudio.App.Tests/Services/PanelStateServiceTests.cs`, `src/VoiceStudio.App.Tests/Services/ShellPersistenceAuthoritySeamTests.cs` (new), `src/VoiceStudio.App.Tests/Services/WorkspaceAuthoritySeamTests.cs` (comment refresh optional), execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

Startup orchestrator redesign, MainWindow mega-refactor, settings schema redesign, Mica/title bar, inactive `app/ui` tree, reintroduction of `IWorkspaceService`/`ILayoutService` DI.

## Rollback

Revert GAP-070 scoped commits; restore prior `SavePanelState` / `RestorePanelsFromLayoutAsync` / `SaveCurrentWorkspaceAsync` behavior if required.

## Changelog

- **2026-04-08:** Row frozen (GAP-070 shell / user-preference persistence boundary).
- **2026-04-07:** Lane **Closed** — [VOICESTUDIO_GAP070_USER_PREFERENCES_PERSISTENCE_BOUNDARY_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP070_USER_PREFERENCES_PERSISTENCE_BOUNDARY_LANE_CLOSURE_2026-04-07.md); merge-save gate, `ActivePanelId` fix, deterministic restore order, MainWindow byte-budget compliance, seam tests.
