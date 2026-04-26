# GAP-008 Slice 12 — MainWindow jump list + taskbar progress shell (bounded)

**Status:** Accepted (Tasks 369–377 follow-on; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Prerequisite:** Startup truth recovery **closed** — [VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md](VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md); [operator visual proof](../reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md).

## First seam (exact)

**`MainWindowJumpListTaskbarProgressShellBridge`** owns **`MainWindowShellLoadedBootstrap`** hook bodies previously implemented as **`WireJumpListShell`** and **`WireTaskbarProgressShell`** on **`MainWindow`** (GAP-067 slices 2–3 — Loaded-only, ADR-047).

**`MainWindow`** passes **`Func<IntPtr>`** for **`WindowNative.GetWindowHandle(this)`** at **invoke** time (after **`Loaded`**), not at bridge construction time.

## Task 372 — Dependency / blast-radius map

| Responsibility | Pre-slice owner | Post-slice owner | Risk | Tests |
| -------------- | ---------------- | ---------------- | ---- | ----- |
| Jump list schedule (`ScheduleInitialRebuildAfterDelay`) | `MainWindow.WireJumpListShell` | **`MainWindowJumpListTaskbarProgressShellBridge.WireJumpList`** | L | Creep + forward pin |
| Taskbar progress HWND | `MainWindow.WireTaskbarProgressShell` | **`MainWindowJumpListTaskbarProgressShellBridge.WireTaskbarProgress`** | M if HWND wrong timing | Forward pin; ctor receives `Func<IntPtr>` |
| Pending jump-list dispatch | `MainWindow.TryDispatchPendingJumpListActivation` | **Out** — stays on **`MainWindow`** (coordinator + startup subscription) | H if merged | — |
| Pending file activation | `MainWindow.TryDispatchPendingFileActivation` | **Out** — future slice | H | — |
| Notification center wire | `MainWindow.WireNotificationCenter` | **Out** | — | — |
| Dispose **`JumpListService`** / **`TaskbarProgressService`** | `MainWindow` closing | **Out** — unchanged | L | — |

**Isolation stop rule:** If extraction requires **`TryDispatchPendingJumpListActivation`**, **`FileActivation`**, **`WireNotificationCenter`**, or any **Slice 1–11** bridge type inside this bridge file, **stop** — wrong slice.

## In scope (explicit)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`MainWindowJumpListTaskbarProgressShellBridge`** | **`WireJumpList`**, **`WireTaskbarProgress`** |
| **`MainWindow`** | Construct bridge; **`MainWindowLoadedBootstrapHooks`** assign **`WireJumpListShell`** / **`WireTaskbarProgressShell`** as one-line forwards |

## Explicitly NOT Slice 12

| Cluster | Deferred |
| ------- | -------- |
| **`TryDispatchPendingJumpListActivation`**, **`RunJumpListPendingAsync`** | Own brief (workflow + startup gate) |
| **`TryDispatchPendingFileActivation`**, **`RunFileActivationPendingAsync`** | Own brief |
| **`WireNotificationCenter`** | Own brief |
| Loaded bootstrap ordering inside **`MainWindowShellLoadedBootstrap.RunAsync`** | Change only via hooks wiring on **`MainWindow`**, not reorder inside runner without Slice 1 brief |
| **`engines/audio/rhvoice/`** | **Frozen** — Task 377 |

## RHVoice (Task 377)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid RHVoice path strings in bridge source.

## Not an extension bucket (Task 376)

**`MainWindowJumpListTaskbarProgressShellBridge`** is a **bounded seam owner** only for jump-list schedule + taskbar HWND wire. **Forbidden:** palette, tool catalog, search overlay, toolbar bridges, welcome bridge, navigation, project workflow, pending activation dispatch.

**Standing review (MAINWINDOW checklist 1–6):** This slice must **not** add unrelated behavior to any existing bridge; must **not** copy **`StatusBarCoordinator`** startup **`StateChanged`** pattern for unrelated UI (checklist item **6**); widened **`dotnet test`** filter must **extend** prior tokens only (item **4**).

## Acceptance criteria

1. **`MainWindowShellLoadedBootstrap`** still calls **`hooks.WireJumpListShell`** / **`hooks.WireTaskbarProgressShell`** in the same order; bodies run through the bridge.
2. **`Gap008Slice12Tests`** + **`MainWindowJumpListTaskbarProgressShellBridgeTests`** — delegation, creep, RHVoice forbidden substrings.
3. **`MainWindow.xaml.cs`** line count reduced only by removal of the two inlined **`Wire*`** method bodies (thin forwards + field).
4. No new **`engines/audio/rhvoice/`** references.

## Verification

**Canonical GAP-008 spine:** [`tools/gap008_mainwindow_regression_filter.txt`](../tools/gap008_mainwindow_regression_filter.txt); run [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../scripts/Run-Gap008MainWindowRegressionTests.ps1).

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Result (2026-04-25, post–Tasks 378–387):** `dotnet build` **0** errors; spine **Passed: 97**; `run_verification.py` **Overall: PASS**.

## Changelog

- 2026-04-25: Initial charter (Tasks 371–372, 376–377); bridge + tests + governance (Tasks 373–375).
