# GAP-008 Slice 13 — MainWindow lifetime / shutdown cleanup shell (bounded)

**Status:** Accepted (Tasks 378–387)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Seam choice:** **Option D** — window **Closed prelude** + idempotent **`RunCleanupCore`** body previously inline on **`MainWindow`**; **not** pending jump-list/file activation (**Slice 14+**) and **not** notification center Loaded wire (**Slice 14+**).

## First seam (exact)

**`MainWindowLifetimeCleanupShellBridge`** owns:

- **`OnClosedPrelude()`** — **`DispatcherTimer`** status metrics stop, layout debouncer cancel, **`SaveWorkspaceLayout()`**, **`MainWindowSessionLifecycle.TryMarkCleanShutdown()`** (same order as former **`MainWindow_Closed`** prefix).
- **`RunCleanupCore()`** — former **`Cleanup()`** body: **`_disposed`** guard, temp-audio cleanup, clock/preview timers, debounced save, event teardown, navigation detach, startup overlay unsubscribe, session lifecycle dispose, transport/status detach, jump list + taskbar **`AppServices`** dispose, notification VM + global transport teardown, **`UnsubscribeShellChromeEvents`**.

**`MainWindow`** keeps **`MainWindow_Closed`** / **`Cleanup`** / finalizer as **one-line forwards**; **`Closed`** subscription remains on **`MainWindow`**.

**Jump list / taskbar dispose on shutdown** moves with this slice (shutdown side); **Slice 12** remains **Loaded** schedule + HWND only ([Slice 12 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE12_MAINWINDOW_JUMPLIST_TASKBAR_PROGRESS_SHELL.md)).

## Task 379 — Dependency / blast-radius map

| Responsibility | Pre-slice owner | Post-slice owner | Risk | Tests |
| -------------- | ---------------- | ---------------- | ---- | ----- |
| Status bar metrics timer stop on close | `MainWindow_Closed` | **`MainWindowLifetimeCleanupShellBridge.OnClosedPrelude`** | L | Prelude invokes channel |
| Layout debouncer cancel + save + clean shutdown mark | `MainWindow_Closed` | **`OnClosedPrelude`** | M if order wrong | Order pin / prelude test |
| Temp WAV cleanup (`%TEMP%` patterns) | `MainWindow.CleanupTempAudioFiles` (static) | **`MainWindowLifetimeCleanupShellBridge`** (private static) | L | Static moved; behavior unchanged |
| Clock / preview timer dispose | `MainWindow.Cleanup` | **`RunCleanupCore`** via channels | L | Idempotent double-`RunCleanupCore` |
| KeyDown / Activated / Closed unsubscribe | `MainWindow.Cleanup` | channels | M | — |
| Workspace profile changed unsubscribe | `MainWindow.Cleanup` | channels | L | — |
| **`DetachNavigationService`** | `MainWindow.Cleanup` | channels | M | — |
| Startup **`StateChanged`** unsubscribe | `MainWindow.Cleanup` | channels | L | — |
| **`MainWindowSessionLifecycle.Dispose`** | `MainWindow.Cleanup` | channels | H | Single dispose |
| Transport shortcut detach + status unsubscribe | `MainWindow.Cleanup` | channels | M | — |
| **`JumpListService`** / **`TaskbarProgressService`** dispose | `MainWindow.Cleanup` | channels (try/catch preserved) | M | Same as pre-slice |
| Notification VM + global transport cleanup | `MainWindow.Cleanup` | channels | M | — |
| **`UnsubscribeShellChromeEvents`** | `MainWindow.Cleanup` | channel | L | — |
| Pending **`TryDispatchPendingJumpListActivation`** | `MainWindow` | **Slice 15** — [`MainWindowJumpListDispatchShellBridge`](../../src/VoiceStudio.App/Services/MainWindowJumpListDispatchShellBridge.cs) ([Slice 15 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE15_MAINWINDOW_JUMPLIST_DISPATCH_SHELL.md)) | H if merged into wrong bridge | — |
| **`WireNotificationCenter`** | `MainWindow` | **Out** | — | — |

**Isolation stop rule:** If extraction pulls **`TryDispatchPendingJumpListActivation`**, **`RunJumpListPendingAsync`**, **`WireNotificationCenter`**, or any **Slice 1–12** bridge **implementation** into this file, **stop** — wrong slice.

## In scope (explicit)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`MainWindowLifetimeCleanupShellBridge`** | **`OnClosedPrelude`**, **`RunCleanupCore`**, static temp-audio cleanup |
| **`MainWindowClosedPreludeChannels`** / **`MainWindowLifetimeCleanupCoreChannels`** | Channel records (**`required`** delegates); constructed in **`MainWindow`** ctor |
| **`MainWindow`** | Field + ctor channel wiring; **`MainWindow_Closed`** / **`Cleanup`** forward; **`~MainWindow`** forward |

## Explicitly NOT Slice 13

| Cluster | Deferred |
| ------- | -------- |
| **`TryDispatchPendingJumpListActivation`**, **`RunJumpListPendingAsync`** | Slice 15+ (charter) |
| **`TryDispatchPendingFileActivation`**, **`RunFileActivationPendingAsync`** | **Slice 14** — [`MainWindowFileActivationShellBridge`](../../src/VoiceStudio.App/Services/MainWindowFileActivationShellBridge.cs) ([Slice 14 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE14_MAINWINDOW_FILE_ACTIVATION_SHELL.md)) |
| **`WireNotificationCenter`** | Slice 14+ |
| **`MainWindowJumpListTaskbarProgressShellBridge.WireJumpList` / `WireTaskbarProgress`** | Slice 12 only |
| **`engines/audio/rhvoice/`** | **Frozen** (Task 387) |

## RHVoice (Task 387)

**Zero** edits under **`engines/audio/rhvoice/`**; creep tests forbid RHVoice path strings in bridge source.

## Closed-only prelude vs `Cleanup` / finalizer asymmetry (Tasks 393 / Slice 13 pin)

**`MainWindow_Closed`** is the **only** user-driven window teardown path that must run **window-order prelude** work (metrics timer stop, layout debouncer cancel, workspace save, clean-shutdown mark). It calls **`_lifetimeCleanupShellBridge.OnClosedPrelude()`** then **`Cleanup()`**.

**`Cleanup()`** forwards **only** to **`_lifetimeCleanupShellBridge.RunCleanupCore()`** — the **idempotent** resource teardown used from **`MainWindow_Closed`**, from **`~MainWindow()`**, or from any direct **`Cleanup()`** call. **`RunCleanupCore`** must remain safe when invoked **without** a preceding prelude (e.g. GC finalizer path where **`Window.Closed`** may not have run).

**Do not** merge **`OnClosedPrelude`** into **`RunCleanupCore`** or the finalizer without a **new bounded slice**, explicit acceptance criteria, and tests — prelude is **Closed-only** by design; duplicating it on GC/finalizer paths risks **double** layout save / shutdown marks or ordering violations.

## Not an extension bucket (Tasks 376 / 386)

**`MainWindowLifetimeCleanupShellBridge`** is a **bounded seam owner** for **shutdown prelude + idempotent teardown** only. **Forbidden:** palette, tool catalog, search overlay, toolbar bridges, welcome **`Activated`** path, navigation **routing**, project workflow, pending activation dispatch, Loaded bootstrap hooks.

**Standing review (MAINWINDOW checklist 1–6):** No unrelated behavior added to existing bridges; widened regression spine extends only via [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) (item **4**); no **`StatusBarCoordinator`** startup pattern reuse for unrelated UI (item **6**).

**PR checklist:** Ran **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`**; filter tokens extended only in **`tools/gap008_mainwindow_regression_filter.txt`**.

## Testing debt

**WinUI window host:** Full **`Window.Closed`** integration is **not** instantiated in unit tests; coverage is **delegate wiring pins** + **`MainWindowLifetimeCleanupShellBridge`** behavioral tests with **fake channels** (idempotent **`RunCleanupCore`**, prelude invokes all four actions).

## Acceptance criteria

1. **`MainWindow_Closed`** and **`Cleanup`** delegate to **`MainWindowLifetimeCleanupShellBridge`** without re-embedding teardown bodies.
2. **`Gap008Slice13Tests`** + **`MainWindowLifetimeCleanupShellBridgeTests`** — delegation, creep, RHVoice forbidden substrings, null-channel fail-closed, double-**`RunCleanupCore`** idempotency.
3. **`MainWindow.xaml.cs`** shrinks only by moving **`Cleanup`** / prelude / static temp cleanup into the bridge file.
4. Regression spine: run **[`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)** (reads canonical filter).

## Verification

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Regression filter source of truth:** [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) — do not duplicate the full `--filter` string in this brief.

**Result (2026-04-25):** `dotnet build` **0** errors; **`Run-Gap008MainWindowRegressionTests.ps1`** **Passed: 97** at Slice **13** land; `run_verification.py` **Overall: PASS**. **Current cumulative spine** **`Passed: N`:** authoritative source is [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt) + script output — see [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) or **`.cursor/STATE.md`**; do not edit this brief only to bump **N** when later slices widen the filter.

## Changelog

- 2026-04-25: Tasks **393** (with **388–398**) — §**Closed-only prelude vs `Cleanup` / finalizer asymmetry**; extended **`Gap008Slice13Tests`** (`MainWindow_Closed`, **`Cleanup`**, **`~MainWindow`** pins).
- 2026-04-25: Initial charter (Tasks 378–379, 386–387); implementation Tasks 380–384; canonical filter Task 382; proof policy Task 385.
