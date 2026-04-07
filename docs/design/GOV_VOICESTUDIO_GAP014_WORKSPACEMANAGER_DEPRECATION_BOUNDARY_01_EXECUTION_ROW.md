# GOV-VOICESTUDIO-GAP014-WORKSPACEMANAGER-DEPRECATION-BOUNDARY-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO_GAP014_WORKSPACEMANAGER_DEPRECATION_BOUNDARY_01`  
**Status:** **Closed** (2026-04-07)  
**Tracker:** [GAP-014](PROFESSIONAL_GAP_TRACKER.md)  
**Lane type:** **runtime-affecting** (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md))  
**Depends on:** GAP-013 **Closed** (PanelHost lifecycle)

## Problem statement

The shell still carried **parallel workspace stacks**: `PanelStateService` + `MainWindow` restore path (authoritative) versus **legacy** `IWorkspaceService`/`ILayoutService` DI registration (`WorkspaceService`/`LayoutService`) with **no live app consumers**, plus a deprecated file type `WorkspaceManager` under `Features/Workspaces` (empty catch blocks, alternate disk layout). That split is an authority footgun: future wiring could double-restore or diverge persistence from `PanelStateService`.

## Frozen architecture decisions

1. **Single runtime workspace authority:** [PanelStateService.cs](../../src/VoiceStudio.App/Services/PanelStateService.cs) (`IUnifiedWorkspaceService`) owns profile + `WorkspaceLayout` + disk/settings merge; [MainWindow.Workspaces.cs](../../src/VoiceStudio.App/MainWindow.Workspaces.cs) is the **shell orchestrator** that applies `GetCurrentLayout()` to `PanelHost` regions and persists geometry via `PanelStateService`.
2. **Legacy DI stack removed:** `ILayoutService` / `IWorkspaceService` are **not** registered in the app container; `AppServices` must not expose `GetWorkspaceService` / `GetLayoutService` entry points.
3. **Deprecated `WorkspaceManager` type removed:** Delete [WorkspaceManager.cs](../../src/VoiceStudio.App/Features/Workspaces/WorkspaceManager.cs); no shipping references (grep-verified). UI dialog [WorkspaceManagerDialog.cs](../../src/VoiceStudio.App/Views/Dialogs/WorkspaceManagerDialog.cs) remains; it delegates to `PanelStateService` (name collision only).
4. **Quarantine retained types:** `WorkspaceService` / `LayoutService` **source files remain** for `DefaultPresets` / test reflection ([PanelIdConsistencyTests.cs](../../src/VoiceStudio.App.Tests/Panels/PanelIdConsistencyTests.cs)); header comments state **not DI-registered** (no runtime authority).
5. **GAP-013:** No changes to `PanelHost` transition contract in this lane unless a restore bug forces an allowlisted fix (not expected).

## §0.1 Authority map (code-truth at lane open)

| Surface | Role | Runtime authority? |
|--------|------|-------------------|
| `PanelStateService` | Profile switch, layout DTO, persist flush, `WorkspaceProfileChanged` | **Yes** |
| `MainWindow` (Workspaces partial) | `RestorePanelsFromLayoutAsync`, `OnWorkspaceProfileChanged`, `SaveWorkspaceLayout` | Orchestrator only (applies authority) |
| `WorkspaceManager` (`Features/Workspaces`) | Legacy GUID JSON (type file) | **Removed** — file deleted; no type in app assembly |
| `IWorkspaceService` + `WorkspaceService` | Parallel `workspaces.json` model | **Not DI-registered** — source retained for presets/tests only |
| `ILayoutService` + `LayoutService` | In-memory layout helper for legacy workspace service | **Not DI-registered** — source retained for presets/tests only |
| `WorkspaceManagerDialog` | Manage workspaces UI | Delegates to `PanelStateService` |

## Deprecation strategy (chosen)

**Option C + partial removal:** Remove deprecated **`WorkspaceManager` type file** entirely. **Quarantine** `WorkspaceService`/`LayoutService` as non-DI legacy types for preset/test use. **Remove** parallel DI registration and static accessors so they cannot become a second runtime owner.

## Acceptance contract (all required for Close)

- [x] `AppServices` does not register `IWorkspaceService` or `ILayoutService`; no `GetWorkspaceService` / `GetLayoutService` public API on `AppServices`.
- [x] Deprecated `WorkspaceManager.cs` removed from `VoiceStudio.App`; no `Features.Workspaces.WorkspaceManager` type in app assembly.
- [x] `PanelStateService` remains registered and implements `IUnifiedWorkspaceService`; shell restore path unchanged in behavior (no intentional dual restore).
- [x] New/updated tests prove DI + source contract (see closure §2).
- [x] Closure matrix + `completion_guard` PASS post-commit; tracker + registry + STATE synced.

## Allowlist

`src/VoiceStudio.App/Services/AppServices.cs`, `src/VoiceStudio.App/Services/WorkspaceService.cs` (header only), `src/VoiceStudio.App/Services/LayoutService.cs` (header only), delete `src/VoiceStudio.App/Features/Workspaces/WorkspaceManager.cs`, `src/VoiceStudio.App/Utilities/LockOrderValidator.cs` (comment sync), `src/VoiceStudio.App/Services/IUnifiedWorkspaceService.cs` (comment sync), `src/VoiceStudio.App.Tests/Services/WorkspaceAuthoritySeamTests.cs` (new), execution row, closure report, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`.

## Hard OUT

Startup orchestrator changes, MainWindow mega-refactor, panel persistence schema redesign, tabbed workspace redesign, navigation/command rewiring, `app/ui` inactive trees, PanelHost lifecycle rewrite (GAP-013).

## Failure-path parity

- **Happy:** App starts with `AppServices.Initialize()`; workspace switch + restore still driven by `PanelStateService` + `MainWindow`.
- **Degraded:** If a future change reintroduces `AddSingleton<IWorkspaceService>`, source-contract test fails in CI.

## Rollback

Revert GAP-014 scoped commits; restore `WorkspaceManager.cs` and DI lines only if necessary.

## Changelog

- **2026-04-07:** Row frozen (GAP-014 workspace authority boundary).
- **2026-04-07:** Lane **Closed** — [VOICESTUDIO_GAP014_WORKSPACEMANAGER_DEPRECATION_BOUNDARY_LANE_CLOSURE_2026-04-07.md](../reports/verification/VOICESTUDIO_GAP014_WORKSPACEMANAGER_DEPRECATION_BOUNDARY_LANE_CLOSURE_2026-04-07.md); tracker **GAP-014** **Closed**.
