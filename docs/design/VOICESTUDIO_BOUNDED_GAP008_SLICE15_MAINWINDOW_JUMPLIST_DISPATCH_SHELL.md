# GAP-008 Slice 15 — MainWindow jump-list **pending dispatch** shell (bounded)

**Status:** Accepted (Tasks 399–408)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **Pending jump-list dispatch only** — consume **`JumpListActivation.TryConsumePending()`** after **`IStartupStateService.IsReady`**, dispatch via **`IProjectWorkflowCoordinator`** + toast on failure; **not** Loaded **`WireJumpList` / `WireTaskbarProgress`** (**Slice 12**), **not** file activation (**Slice 14**), **not** notification center (**Slice 16+** candidate).

## First seam (exact)

**`MainWindowJumpListDispatchShellBridge`** owns:

- **`TryDispatchPendingJumpListActivation()`** — pending consume, coordinator null-guard, startup gate + **`StateChanged`** subscription; **`_ = RunJumpListPendingAsync`** after ready (Loaded hook path only; ADR-047).
- **`RunJumpListPendingAsync`** (private) — **`JumpListPendingKind`** switch: **`NewProject`**, **`OpenDialog`**, **`OpenProject`** (recent path); errors → **`Debug.WriteLine`** + toast.

**`MainWindow`** removes private **`TryDispatchPendingJumpListActivation`** / **`RunJumpListPendingAsync`** bodies; **`MainWindowShellLoadedBootstrap`** hook passes **`() => _jumpListDispatchShellBridge.TryDispatchPendingJumpListActivation()`**.

**Composition:** **`Func<IProjectWorkflowCoordinator?>`**, **`Func<IStartupStateService>`**, **`Func<IToastNotificationService?>`** from **`MainWindow`** ctor — **no** **`ServiceProvider`** inside the bridge.

## Relationship to Slice 12 (mandatory boundary)

| Slice | Type | Responsibility |
| ----- | ---- | ---------------- |
| **12** | **`MainWindowJumpListTaskbarProgressShellBridge`** | **`WireJumpList`** (schedule rebuild delay) + **`WireTaskbarProgress`** (**`HWND`**); **no** coordinator, **no** **`JumpListActivation.TryConsumePending`**. |
| **15** | **`MainWindowJumpListDispatchShellBridge`** | **Pending** action dispatch after startup ready; **no** **`ScheduleInitialRebuildAfterDelay`**, **no** **`SetWindowHandle`**. |

**Do not** merge Slice 12 implementation into this bridge without a **new bounded brief** and acceptance-criteria rewrite.

## Task 400 — Dependency / blast-radius map

| Responsibility | Current owner (`MainWindow`) | Target owner | Services / deps | Async / F&F | Side effects | Coupling to **`MainWindowProjectWorkflowBridge`** | Regression risks |
| ---------------- | ---------------------------- | ------------ | --------------- | ----------- | ------------ | -------------------------------------------------- | ------------------ |
| Consume pending jump-list argv | **`TryDispatchPendingJumpListActivation`** | **`MainWindowJumpListDispatchShellBridge`** | **`JumpListActivation`**, **`IStartupStateService`**, **`IProjectWorkflowCoordinator?`** | **`_ = RunJumpListPendingAsync`** (Loaded hook) | None until **`Run`** | Same coordinator instance as workflow bridge; **not** menu builders | Startup never ready → unsubscribe on ready |
| New / open dialog / open recent | **`RunJumpListPendingAsync`** | bridge (private) | **`IProjectWorkflowCoordinator`**, **`IToastNotificationService?`** | **`await`** on UI context | New project, file dialog, open path | Coordinator only | Empty **`OpenProject`** path unchanged (no-op branch) |

**Overlap with file activation:** Only the **startup-gate template** (consume pending → coordinator → **`IsReady`** or **`StateChanged`**). **Different** static holders (**`JumpListActivation`** vs **`FileActivation`**), **different** pending types, **different** **`Run*Async`** bodies — **no** shared implementation or hidden static coupling beyond **`IStartupStateService`**.

**`JumpListPendingKind`:** Enum covers **`NewProject`**, **`OpenDialog`**, **`OpenProject`** only — **no** “unknown” dispatch arm; behavioral tests use **`SetPendingIfParsed`** / **`TryParse`** with real tokens (**`JumpListArgs`**); **no** invented unknown-kind test (N/A).

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`TryDispatchPendingJumpListActivation`**, **`RunJumpListPendingAsync`** | **IN** (this bridge) |
| **`MainWindowJumpListTaskbarProgressShellBridge`** (`WireJumpList`, `WireTaskbarProgress`) | **OUT** (Slice 12) |
| **`MainWindowFileActivationShellBridge`**, file activation hooks | **OUT** (Slice 14) |
| Notification center **`MainWindowNotificationCenterShellBridge`** | **OUT** — [Slice 16 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE16_MAINWINDOW_NOTIFICATION_CENTER_SHELL.md) |
| Startup / welcome **`Activated`** | **OUT** (Slice 11) |
| Loaded bootstrap / tail orchestration | **OUT** (Slices 1 / 3) — hook assigns delegate only |
| Tool catalog, palette, toolbar, search, lifetime cleanup bridges | **OUT** |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 408) |

## File activation host runtime proof (related debt — Task 403)

Full **WinUI** shell argv → **`FileActivation`** → coordinator path is **not** host-proven in this slice; see [Slice 14 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE14_MAINWINDOW_FILE_ACTIVATION_SHELL.md) §Testing debt — **Host runtime proof**.

## RHVoice (Task 408)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid that path in **`MainWindowJumpListDispatchShellBridge.cs`**.

## Not an extension bucket + bridge accretion (Task 407)

**`MainWindowJumpListDispatchShellBridge`** is a **bounded seam owner** for **pending jump-list dispatch** only. **Forbidden:** file activation, notification center, Slice 12 HWND/schedule wiring, palette/catalog/toolbar/search, lifetime cleanup.

**Distributed god object risk:** Each new bridge must remain a **single-story owner**. Adding “misc activation” or second shell features into this file recreates the monolith as **scatter**. Reject without a new brief + tests.

**Standing review (MAINWINDOW checklist 1–6):** Unchanged; spine extends only via [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**PR checklist:** Ran **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**; extended **only** **`tools/gap008_mainwindow_regression_filter.txt`**.

## Testing debt

Unit tests: ctor null-guards, **creep** substring bans, **`MainWindow.xaml.cs`** text pins, **behavioral** dispatch when **`JumpListActivation`** has a parsed pending and **`IStartupStateService.IsReady`** is **true** (coordinator **`Verify`**). Full jump-list + WinUI integration not in scope for MSTest spine.

## Acceptance criteria

1. **`MainWindow`** does not embed jump-list pending dispatch bodies; Loaded hook delegates to **`MainWindowJumpListDispatchShellBridge`**.
2. **`Gap008Slice15Tests`** + **`MainWindowJumpListDispatchShellBridgeTests`**.
3. Regression spine strict superset of pre–Slice 15 count.

## Verification

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Regression filter source of truth:** [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt).

**Spine count arithmetic (historical briefs):** authoritative explanation + script artifacts — [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) and `.buildlogs/gap008_spine/` (Tasks **418–419**); do not re-edit this brief only to chase cumulative **N**.

**Result (2026-04-26):** `dotnet build` **0** errors; **`Run-Gap008MainWindowRegressionTests.ps1`** **Passed: 114** at Slice **15** land; `run_verification.py` **Overall: PASS**. **Later cumulative** **`Passed: N`** values: run [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1) against [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) — do not edit this brief only to bump **N**.

## Changelog

- **2026-04-26:** Tasks **399–408** — Slice 15 chartered and landed; **`MainWindowJumpListDispatchShellBridge`**; **`Gap008Slice15Tests`** + **`MainWindowJumpListDispatchShellBridgeTests`**; filter superset; Slice 14 host-proof debt cross-link; Slice **16+** planning only.
