# GAP-008 Slice 4 — MainWindow project workflow entry points (bounded)

**Status:** Accepted (slice 4 landed in repo)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 3](VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md)

## First seam (exact)

**Coordinator entry-point façade** for actions that only forward to **`IProjectWorkflowCoordinator`**:

- **New project** → `CreateNewProjectAsync`
- **Open project** (picker) → `OpenProjectAsync`
- **Save project** → `SaveProjectAsync`
- **Open recent** (path + display name) → `OpenRecentProjectAsync`

**Implementation:** [`MainWindowProjectWorkflowBridge`](../../src/VoiceStudio.App/Services/MainWindowProjectWorkflowBridge.cs) — resolves coordinator via **`Func<IProjectWorkflowCoordinator?>`** (same lifetime as today’s `_projectWorkflowCoordinator` field). **No** `IImportWorkflowService`, **no** `RecentProjectsService` mutation APIs on this type (Choice A).

## Scope decision: Choice A (frozen)

**Choice A (this slice):** Move **only** the four coordinator forwards above into the bridge. **`PopulateRecentProjectsMenu`** (flyout construction), **Pin / Unpin / Clear**, and **Remove from list** stay on **`MainWindow`** until **Slice 5** (recent menu mutation).

**Choice B (explicitly not taken):** Pulling all recent-menu glue into Slice 4 would mix UI construction, persistence, and coordinator paths — rejected for blast radius.

## What stays in `MainWindow`

- **`_projectWorkflowCoordinator`** field, **`CreateProjectWorkflowCoordinator`**, **`GetProjectWorkflowCoordinatorForSessionLifecycle()`** (session, jump list, file activation).
- **`PopulateRecentProjectsMenu`**, **`PinRecentProject`**, **`UnpinRecentProject`**, **`ClearRecentProjects`**, remove-from-list lambda.
- **`ImportAudioFile`**, **`RunJumpListPendingAsync`**, **`RunFileActivationPendingAsync`**.

## What moves out

- **Invocation** of the four coordinator methods through **`MainWindowProjectWorkflowBridge`** so **`MainWindow`** keeps **`async void`** menu handlers as thin **`await bridge.*`** lines.

## No-expansion rule (OUT OF SCOPE)

- Import workflow; jump list / file activation / session lifecycle coordinator dispatch; search overlay; transport; Loaded tail; navigation bridge internals; **`engines/audio/rhvoice/`**; verify-bar churn unless anchored to **`verify.ps1`**.

## Dependency / blast-radius map (Task 280)

| Responsibility | Current owner | Target after Slice 4 | Risk | Required tests |
|----------------|---------------|----------------------|------|----------------|
| File menu New/Open/Save | `MainWindow` → coordinator | `MainWindow` → bridge → coordinator | L | `MainWindowProjectWorkflowBridgeTests` mock |
| Open recent (handler) | `MainWindow` → coordinator | `MainWindow` → bridge → coordinator | L | Mock `OpenRecentProjectAsync` args |
| Coordinator composition | `ProjectWorkflowBootstrap` / ctor | Unchanged | — | — |
| Recents flyout UI | `PopulateRecentProjectsMenu` | Unchanged (MainWindow) | — | `Gap008Slice4Tests` pin |
| Pin/unpin/clear/remove | `MainWindow` + `RecentProjectsService` | Unchanged | — | — |
| Session / jump / file | `MainWindow` / `MainWindowSessionLifecycle` | Unchanged | M | Brief + grep; not in bridge file |

## Lifecycle

- Bridge holds **no** ownership of coordinator; **does not** replace **`GetProjectWorkflowCoordinatorForSessionLifecycle`**.
- Null coordinator: bridge methods complete without throwing (matches prior **`if (_projectWorkflowCoordinator != null)`** behavior).

## Slice 5 candidate (Task 286 — document only)

**Next bounded seam:** **`PopulateRecentProjectsMenu`** + pin/unpin/clear/remove + related toasts — optional alternate: search overlay / toolbar glue per decomposition plan.

## Related guardrails (Task 287)

- **`MainWindowLoadedTailBootstrap`** — Loaded transport + panel-init only ([Slice 3](VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md)).
- **`NavButtonActionSink`** — [Slice 2 §Guardrail](VOICESTUDIO_BOUNDED_GAP008_SLICE02_MAINWINDOW_NAVIGATION_SHELL.md).
- **`MainWindowProjectWorkflowBridge`** — coordinator entry points **only** (Choice A); not a menu builder or second coordinator.

## Verification (Task 284)

Recorded **2026-04-25** (repo root `E:\VoiceStudio`):

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# Build succeeded. 0 Error(s). (Pre-existing warnings unchanged.)

dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice4Tests|FullyQualifiedName~MainWindowProjectWorkflowBridgeTests" -v q
# Passed!  Failed: 0, Passed: 9, Skipped: 0, Total: 9

# Optional regression: `Gap008Slice1Tests` — Passed: 4, Failed: 0.

python scripts\run_verification.py
# Overall: PASS — `.buildlogs\verification\last_run.json` (advisory stale rows unchanged)
```

## Changelog

- **2026-04-25 (post–Slice 5):** Pin/Unpin/Clear/remove-from-list **orchestration** moved to **`MainWindowRecentProjectsMutationBridge`** ([Slice 5 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE05_MAINWINDOW_RECENT_PROJECTS_MUTATION.md)); **`MainWindow`** retains **`PopulateRecentProjectsMenu`** and thin **`async void`** handler entry points.
- **2026-04-25:** Tasks 279–288 — brief + `MainWindowProjectWorkflowBridge` + `Gap008Slice4Tests` + `MainWindowProjectWorkflowBridgeTests` + docs/STATE (after green).
