# GAP-008 Slice 14 — MainWindow file activation shell (bounded)

**Status:** Accepted (Tasks 388–398)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **File activation only** — consume **`FileActivation.TryConsumePending()`** after **`IStartupStateService.IsReady`**, dispatch via **`IProjectWorkflowCoordinator`** + toasts + **`IShellNavigationCoordinator`** for profile path; **not** jump-list dispatch (**[`MainWindowJumpListDispatchShellBridge`](../../src/VoiceStudio.App/Services/MainWindowJumpListDispatchShellBridge.cs)** — Slice **15**), **not** notification center (**[`MainWindowNotificationCenterShellBridge`](../../src/VoiceStudio.App/Services/MainWindowNotificationCenterShellBridge.cs)** — Slice **16**).

## First seam (exact)

**`MainWindowFileActivationShellBridge`** owns:

- **`TryDispatchPendingFileActivation()`** — pending consume, coordinator null-guard, startup gate + **`StateChanged`** subscription (same pattern as pre-slice **`MainWindow`**; **`_ = RunFileActivationPendingAsync`** remains **same-path** async continuation after ready — no new constructor fire-and-forget; ADR-047: Loaded hook only invokes this delegate).
- **`RunFileActivationPendingAsync`** (private) — **`FileActivationKind`** switch: open project by path; import project toast + open dialog; import profile toast + **`OpenPanelByIdAsync("Profiles", …)`**; errors → **`Debug.WriteLine`** + toast.

**`MainWindow`** delegates jump-list **pending** dispatch via **`MainWindowJumpListDispatchShellBridge`** (Slice **15**); **`MainWindowShellLoadedBootstrap`** hook **`TryDispatchPendingFileActivation`** passes **`() => _fileActivationShellBridge.TryDispatchPendingFileActivation()`** (or equivalent one-line forward).

**Composition:** Bridge receives **`Func<IProjectWorkflowCoordinator?>`**, **`Func<IStartupStateService>`**, **`Func<IToastNotificationService?>`**, **`Func<IShellNavigationCoordinator?>`** from **`MainWindow`** ctor (no new **`ServiceProvider`** usage inside bridge).

## Task 389 — Dependency / blast-radius map

| Responsibility | Current owner (`MainWindow`) | Target owner | Services / deps | Async / F&F | Side effects | Coupling to **`MainWindowProjectWorkflowBridge`** | Regression risks |
| ---------------- | ---------------------------- | ------------ | --------------- | ----------- | ------------ | -------------------------------------------------- | ------------------ |
| Consume pending argv / association | **`TryDispatchPendingFileActivation`** | **`MainWindowFileActivationShellBridge.TryDispatchPendingFileActivation`** | **`FileActivation`**, **`IStartupStateService`**, **`IProjectWorkflowCoordinator?`** | **`_ = RunFileActivationPendingAsync`** after ready (Loaded hook path only) | None until **`Run`** | Coordinator instance same as workflow bridge target; **not** menu builders | Startup never ready → handler must unsubscribe on ready |
| Open project by path | **`RunFileActivationPendingAsync`** | bridge (private) | **`IProjectWorkflowCoordinator`** | **`await`** on UI context | Opens project | Uses coordinator from **`MainWindow`** field via **`Func`** | Wrong path handling unchanged (coordinator) |
| Import project / profile UX | same | bridge | **`IToastNotificationService?`**, **`IShellNavigationCoordinator?`** | **`await`** | Toasts, optional panel nav | Profile path uses shell nav only | Null **`IShellNavigationCoordinator`** on import profile → skip panel open (preserved) |

**Jump-list relationship:** **`TryDispatchPendingJumpListActivation`** and **`TryDispatchPendingFileActivation`** share only the **template** (consume static pending → require coordinator → read **`GetStartupStateService()`** → if **`IsReady`** run immediately else subscribe **`StateChanged`** and unsubscribe on first ready). **They do not** share implementation, types (**`JumpListPendingAction`** vs **`FileActivationPendingAction`**), or routing. Splitting them is **not** artificial duplication of a single dispatcher type.

## Jump-list dispatch: OUT (Task 390)

**Decision:** **OUT** of Slice 14. **`MainWindowJumpListTaskbarProgressShellBridge`** owns Loaded-time jump list + taskbar shell; pending **jump-list activation dispatch** is **[`MainWindowJumpListDispatchShellBridge`](../../src/VoiceStudio.App/Services/MainWindowJumpListDispatchShellBridge.cs)** — [Slice 15 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE15_MAINWINDOW_JUMPLIST_DISPATCH_SHELL.md).

**If merged later:** Treat as **scope change** — requires a **new** brief (e.g. “pending activation dispatcher”) plus creep-test updates and explicit removal of jump-list methods from **`MainWindow`** in that slice’s acceptance criteria. **Reject** silent expansion of **`MainWindowFileActivationShellBridge`** without brief amendment.

## IN / OUT table

| Cluster | IN / OUT |
| ------- | -------- |
| **`TryDispatchPendingFileActivation`**, **`RunFileActivationPendingAsync`** | **IN** (bridge) |
| **`TryDispatchPendingJumpListActivation`**, **`RunJumpListPendingAsync`** | **OUT** of this slice — **Slice 15** (**`MainWindowJumpListDispatchShellBridge`**) |
| Notification center **`MainWindowNotificationCenterShellBridge`** | **OUT** — [Slice 16 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE16_MAINWINDOW_NOTIFICATION_CENTER_SHELL.md) |
| Startup / welcome **`Activated`** path | **OUT** (Slice 11) |
| Loaded bootstrap / tail orchestration | **OUT** (Slices 1 / 3) — hook **assigns** delegate only |
| Tool catalog, palette, toolbar, search bridges | **OUT** |
| **`MainWindowLifetimeCleanupShellBridge`** | **OUT** (Slice 13) |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** (Task 398) |

## RHVoice (Task 398)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid **`engines/audio/rhvoice/`** substrings in **`MainWindowFileActivationShellBridge.cs`** source.

## Not an extension bucket (Tasks 397 / anti-sprawl)

**`MainWindowFileActivationShellBridge`** is a **bounded seam owner** for **shell file-association / argv activation dispatch** only. **Forbidden:** jump-list dispatch, notification center, palette, tool catalog, toolbar/search bridges, welcome **`Activated`** path, lifetime cleanup, **`MainWindowProjectWorkflowBridge`** implementation (coordinator **reference** via **`Func`** only).

**Standing review (MAINWINDOW checklist 1–6):** No unrelated behavior added to existing bridges; widened regression spine extends only via [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt); no **`StatusBarCoordinator`** startup pattern reuse for unrelated UI.

**PR checklist:** Ran **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**; filter tokens extended only in **`tools/gap008_mainwindow_regression_filter.txt`**.

## Testing debt

Full **`FileActivation.TryConsumePending`** integration with real argv is **not** required in unit tests; coverage is **text pins** on **`MainWindow.xaml.cs`**, **creep** bans, and **`MainWindowFileActivationShellBridge`** ctor null-guards + source creep.

### Host runtime proof (Slice 14 debt — Tasks 403 / 399–408)

| Proven today | Not proven | Closure bar |
| ------------ | ---------- | ----------- |
| MSTest **text pins** + bridge **ctor** null-guards + **creep** bans; inclusion in **canonical GAP-008** filter spine | End-to-end **WinUI** process: shell hands off argv / file association → **`App`** → **`FileActivation`** → **`MainWindowShellLoadedBootstrap`** → **`MainWindowFileActivationShellBridge`** → real **`IProjectWorkflowCoordinator`** on a user session | **Either:** (a) **WinAppDriver** (or equivalent UI automation) scenario using stable **`AutomationId`** values from [`docs/developer/AUTOMATION_ID_REGISTRY.md`](../../docs/developer/AUTOMATION_ID_REGISTRY.md) that launches with a temp `.vsproj`/association path and asserts project UI state **or** (b) **signed operator runbook** with reproducible argv + screenshot/log capture linked from this brief’s **Verification** row — **one** chosen path documented when closed |

Until closure, treat file-activation behavior as **structurally** covered by Slice **14** tests, **not** fully **runtime-host** certified.

## Acceptance criteria

1. **`MainWindow`** does not embed **`TryDispatchPendingFileActivation`** / **`RunFileActivationPendingAsync`** bodies; **`MainWindowLoadedBootstrapHooks.TryDispatchPendingFileActivation`** targets the bridge.
2. **`Gap008Slice14Tests`** + **`MainWindowFileActivationShellBridgeTests`** — delegation, construction order vs coordinator, creep (jump-list names **out** of bridge source when jump-list OUT; RHVoice path forbidden).
3. Regression spine: run **[`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)** (reads canonical filter; count **strict superset** of pre–Slice 14).

## Verification

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Regression filter source of truth:** [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) — do not duplicate the full `--filter` string in this brief.

**Result (2026-04-25):** `dotnet build` **0** errors; **`Run-Gap008MainWindowRegressionTests.ps1`** **Passed: 106** at Slice **14** land; `run_verification.py` **Overall: PASS**. **Current cumulative spine count** is **not** maintained in historical slice briefs — authoritative membership and count come from [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) + [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1) output **`Passed: N`** (see [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) or **`.cursor/STATE.md`** for latest operational **N**).

## Changelog

- **2026-04-25:** Tasks **388–398** — Slice 14 chartered; **`MainWindowFileActivationShellBridge`**; **`Gap008Slice14Tests`** + **`MainWindowFileActivationShellBridgeTests`**; filter superset; jump-list dispatch **OUT**; Slice 15 **planning only**; Slice 13 **Closed vs finalizer** asymmetry documented + tests.
