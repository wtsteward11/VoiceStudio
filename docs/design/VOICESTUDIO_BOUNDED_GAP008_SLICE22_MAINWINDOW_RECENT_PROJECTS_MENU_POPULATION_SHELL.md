# GAP-008 Slice 22 — MainWindow recent projects menu population shell (bounded)

**Status:** Accepted (Tasks 39–45)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 5](VOICESTUDIO_BOUNDED_GAP008_SLICE05_MAINWINDOW_RECENT_PROJECTS_MUTATION.md) (Option A superseded for **where** population lives — composition moves here; mutations stay in **`MainWindowRecentProjectsMutationBridge`**)

## Task 39 decision (one sentence)

**GAP-008 continues with Slice 22** on seam **recent projects submenu flyout construction and click wiring** — one coherent shell story; umbrella **not** closed (Path G1).

## Goal

Move **`PopulateRecentProjectsMenu`** UI composition (**`MenuFlyoutSubItem` / `MenuFlyoutItem` / separators**, empty state, pinned vs recent layout) into **`MainWindowRecentProjectsMenuPopulationShellBridge`** so **`MainWindow`** holds a **thin forward** only; **open / pin / unpin / remove / clear** execution remains **`MainWindowProjectWorkflowBridge`** + **`MainWindowRecentProjectsMutationBridge`** via injected **`Func`/`Task`** delegates (no new mutation surface).

## IN / OUT

| IN | OUT |
|----|-----|
| Build/clear **`MenuFlyoutSubItem.Items`** from **`RecentProjectsService`** lists (`PinnedProjects`, `RecentProjects`, `AllProjects`) | **`PinProjectAsync` / `UnpinProjectAsync` / `RemoveRecentProjectAsync` / `ClearRecentProjectsAsync`** implementation or toasts (Slice 5 **`MainWindowRecentProjectsMutationBridge`**) |
| Wire **`Click`** to delegates supplied at construction (same runtime behavior as pre-Slice-22 **`MainWindow`**) | **`OpenRecentProject`** body on **`MainWindow`** (stays **`MainWindowProjectWorkflowBridge`**); **File New/Open/Save** (Slice 4); **import / jump list / file activation** |
| **`null`** submenu or **`null`** service → immediate **return** (parity with **`MainWindow`** guards) | Absorbing **`PopulateRecentProjectsMenu`** into **`MainWindowRecentProjectsMutationBridge`** (Slice 5 Option B — still forbidden) |
| Emoji prefix **`📌`** on pinned entries (unchanged UX) | **RHVoice**, verify-harness GOV row edits |

## Dependency map (Task 41)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow.PopulateRecentProjectsMenu`** | Thin forward → **`_recentProjectsMenuPopulationBridge.Populate`** |
| **`MainWindow` ctor** | After **`_recentProjectsMutationBridge`**, construct population bridge with **`Func`** lambdas closing over **`_projectWorkflowCommandBridge`** and **`_recentProjectsMutationBridge`** |
| **`RecentProjectsService`** | Read **`AllProjects`**, **`PinnedProjects`**, **`RecentProjects`** (same as pre-Slice-22) |
| **`MainWindowProjectWorkflowBridge.OpenRecentProjectAsync`** | Open path from flyout |
| **`MainWindowRecentProjectsMutationBridge`** | Pin / Unpin / Remove / Clear only |
| **`PropertyChanged`** on service → **`EnqueueRecentProjectsMenuRefresh`** | **Unchanged** — bridge does **not** subscribe |

**Must not call into:** **`MainWindowSearchOverlayShellBridge`**, **`MainWindowMenuToolActivationShellBridge`**, **`MainWindowProjectWorkflowBridge`** for mutations, coordinators directly from population type.

**Async / UI:** **`Click`** handlers use **`async` lambda + `ConfigureAwait(true)`** for **`Task`**-returning delegates (UI thread).

## Anti-sprawl (guardrail alignment)

Matches [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) **`MainWindowRecentProjectsMutationBridge`** row: mutation bridge **forbidden** to add menu population — population lives in **this** new owner only.

## Acceptance

- **`MainWindow.PopulateRecentProjectsMenu`** is **≤ 5 lines** (forward + null guard optional in **`MainWindow`** or inside **`Populate`** only).
- Canonical spine gains **`Gap008Slice22Tests`** + **`MainWindowRecentProjectsMenuPopulationShellBridgeTests`**; filter **prepend-only**; **`Run-Gap008MainWindowRegressionTests.ps1`** green; **`test_gap008_spine_summary_shape.py`** green.
- **`MainWindowRecentProjectsMutationBridge`** file still contains **no** **`MenuFlyout`** / **`PopulateRecentProjectsMenu`** strings.

## Verification (post-merge — fill SHA in Task 45)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice22Tests|FullyQualifiedName~MainWindowRecentProjectsMenuPopulationShellBridgeTests|FullyQualifiedName~Gap008Slice5Tests" -v q
powershell -File scripts\Run-Gap008MainWindowRegressionTests.ps1
python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q
python scripts/run_verification.py
```

## RHVoice / CI freeze

Per **Tasks 46–47**: no **`engines/audio/rhvoice/`** changes; no closed verify-harness GOV narrative edits without new hosted dispatch.
