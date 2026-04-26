# GAP-008 Slice 2 — MainWindow navigation shell glue (bounded)

**Status:** Accepted (slice 2 landed in repo)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008** — MainWindow decomposition  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 1 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE01_MAINWINDOW_LOADED_BOOTSTRAP.md)

## First seam (exact)

**Navigation-shell glue** around the existing [`ShellNavigationCoordinator`](../../src/VoiceStudio.App/Services/ShellNavigationCoordinator.cs), moved into [`MainWindowNavigationShellBridge`](../../src/VoiceStudio.App/Services/MainWindowNavigationShellBridge.cs):

1. **`SetActiveNavButton`** — `FindNameOnContent` for eight rail `ToggleButton`s and `IsChecked` mutation (was ~646–669 in `MainWindow.xaml.cs`).
2. **`OnNavigationChanged`** / **`OnNavigationChangedCoreAsync`** — `INavigationService.NavigationChanged`: dispatcher enqueue, `ResolvePanelIdAlias`, `OpenPanelByIdAsync`; unknown panel → `Debug.WriteLine` only (**current behavior**, not upgraded to UI fail-closed in this slice).
3. **`ExecuteNavCommand`** / **`ExecuteNavCommandAsync`** — forwards to `_shellNavigationCoordinator.ExecuteNavCommandAsync` (call sites: menu, smoke; remain invokable via thin `MainWindow` partial forwards where convenient).

**Included with bridge (avoid dangling façade):** **`GetPanelRegion`**, **`GetPanelTitle`**, **`OpenPanelByIdAsync`** as **public** one-line forwards to the coordinator (same contract as previous `MainWindow` private methods). **`MainWindow`** keeps **one-line** `private` wrappers so other partials (`MainWindow.Workspaces.cs`, `MainWindow.Smoke.cs`, `MainWindow.Menu.cs`) continue to call `OpenPanelByIdAsync` / `GetPanelTitle` / `SetActiveNavButton` without churn.

**Lifecycle:** `AttachNavigationService` / `DetachNavigationService` own `NavigationChanged` subscription; **`MainWindow.Cleanup`** calls **`DetachNavigationService`** (fixes prior gap: subscription was never unsubscribed).

## What stays in `MainWindow`

- **`FindNameOnContent` / `FindInContent`** (tree ownership).
- **Construction order:** `NavButtonSink` + `ShellNavigationCoordinator` + bridge + `CreateProjectWorkflowCoordinator` (workflow still receives `Action<string>` that resolves to the same nav-button path via sink).
- **Thin forwards** (one line each) for partial ergonomics: `ExecuteNavCommand`, `ExecuteNavCommandAsync`, `OpenPanelByIdAsync`, `GetPanelRegion`, `GetPanelTitle`, optional `SetActiveNavButton` → bridge.
- **`NavButton_PointerEntered`**, **`PanelPreviewPopup`**, keyboard shortcut registration that calls `GetPanelTitle` / `OpenPanelByIdAsync` (unchanged behavior via forwards).
- **`ShellNavigationCoordinator`** construction and all non-navigation shell regions.

## What moves out

- **Substantive** logic listed under “First seam” into **`MainWindowNavigationShellBridge`**.
- **NavigationService** subscribe/unsubscribe **surface** on the bridge.

## No-expansion rule (OUT OF SCOPE)

- **NavigationViewModel** alias map unification with `ResolvePanelIdAlias` (cross-component; ADR-level if done).
- Loaded tail: **`TransportShortcutCoordinator.Attach`**, **`RunPanelInitWhenReadyAsync`** (Slice 3).
- Dialog stack, project workflow **internals** (only the **callback target** for nav highlight may point at bridge), backend routes, **`engines/audio/rhvoice/`**, Slice 27 / verify-bar / `engine_truth` churn.
- Changing **`ShellNavigationCoordinator`** behavior (safe startup, startup toast, `SwitchToPanelByIdAsync`).

## Regression surface

| Surface | Risk |
|---------|------|
| Menu `CreateNavMenuItem` | `ExecuteNavCommand` path |
| Smoke nav lambdas | `ExecuteNavCommandAsync` |
| `INavigationService` command-driven navigation | `OnNavigationChanged` → open panel |
| `ProjectWorkflowCoordinator` | `SetActiveNavButton` via same sink as coordinator |
| Rail `ToggleButton` names | `NavStudio` … `NavLogs` — brittle; seam tests pin names |

## Dependency / blast-radius map (Task 262)

| Responsibility | Current owner (pre-slice) | Target owner after slice 2 | Risk | Tests |
|----------------|-------------------------|-----------------------------|------|--------|
| Nav toggle `IsChecked` | `MainWindow.SetActiveNavButton` | `MainWindowNavigationShellBridge` | M | `Gap008Slice2Tests` pin eight names |
| `NavigationChanged` handler | `MainWindow` | Bridge; **unsub** in `DetachNavigationService` | M | Pin subscribe string + `ResolvePanelIdAlias` + `OpenPanelByIdAsync` order |
| `ExecuteNavCommandAsync` | `MainWindow` → coordinator | Bridge → coordinator (MainWindow may 1-line forward) | L | Pin coordinator path |
| `OpenPanelByIdAsync` / region/title | `MainWindow` → coordinator | Bridge → coordinator; MainWindow 1-line | L | Pin `MainWindowNavigationShellBridge` symbol |
| `ShellNavigationCoordinator` ctor `Action<string>` | `SetActiveNavButton` method | `NavButtonSink.Forward` → bridge | M | Sink wired in bridge ctor |
| Unknown panel id | `Debug.WriteLine` only | Unchanged | L | Document in tests / brief |

## Slice 3 candidate (explicitly NOT slice 2)

- **Landed 2026-04-25** — [VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md](VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md): `MainWindowLoadedTailBootstrap` + `Gap008Slice3Tests`.

## Guardrail — `NavButtonActionSink` contract (Tasks 277 / GAP-008)

**`NavButtonActionSink` exists only** to resolve **`ShellNavigationCoordinator`** constructor ordering vs **`MainWindowNavigationShellBridge.SetActiveNavButton`** (rail highlight after navigation commands). It is **not** a general-purpose event bus or pub/sub surface. **No new responsibilities** may be routed through it without a **new bounded slice** and **ADR** if the change alters architectural meaning.

## Acceptance criteria

1. **`MainWindow.xaml.cs`** net reduction: removed **substantive** blocks for `SetActiveNavButton` + navigation event handlers; subscription cleanup added.
2. **No behavior reorder** for nav command path, alias resolution, or toggle updates without brief + test update.
3. **No** `NavigationViewModel` edits in this slice.
4. **No** RHVoice / engine proof churn.

## Verification (Task 265)

Recorded **2026-04-25** (repo root `E:\VoiceStudio`):

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# Build succeeded. 0 Error(s). (Pre-existing test-project warnings unchanged.)

dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice2Tests" -v q
# Passed!  Failed: 0, Passed: 6, Skipped: 0, Total: 6

python scripts\run_verification.py
# Overall: PASS — `.buildlogs\verification\last_run.json`
```

## Changelog

- **2026-04-25 (post–Tasks 269–278):** §Guardrail — **`NavButtonActionSink`** narrow contract (ordering bridge only; not an event bus); Slice 3 Loaded tail **landed** (cross-ref Slice 3 brief).
- **2026-04-25:** Tasks 261–268 — bounded brief + bridge + seam tests + verification + doc/STATE sync.
