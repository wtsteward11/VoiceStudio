# GAP-008 Slice 26 — MainWindow startup backend overlay shell (bounded)

**Status:** Accepted (Tasks 69–75)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP008` infix** distinguishes this **WinUI MainWindow** slice from other “Slice 26” documents in the repository (e.g. [Vosk STT](../governance/CANONICAL_REGISTRY.md) bounded work). See [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Task 39 / Path decision (Task 69 — one sentence)

**GAP-008 continues with Slice 26** on seam **startup backend overlay (cold-start)**: `IStartupStateService` state transitions, `StartupOverlay` border visibility, message and progress, retry; **Path G1**; umbrella **not** closed.

## Goal

Move **`UpdateStartupOverlay`**, **`StartupState_StateChanged`** handling, and **`StartupRetryButton_Click`** from **`MainWindow.xaml.cs`** into **`MainWindowStartupOverlayShellBridge`**. **`MainWindow`** provides **`FindInContent` lambdas per named element**, **`DispatcherQueue`**, a single **idempotent** `Action` for shell-interactive cold-start timing (owns **`_recordedShellInteractiveTiming`** + **`ColdStartTimingCollector`**, **not** the bridge), and **`AppServices.GetService<StartupRetryCoordinator>()`** as **`Func<StartupRetryCoordinator?>`** only.

**Phase 0:** Slice 25 **Deferred (not this slice):** [startup overlay beyond existing wiring](VOICESTUDIO_BOUNDED_GAP008_SLICE25_MAINWINDOW_PANEL_DOCKING_SHELL.md); this slice is that one seam (no junk-drawer Path G2).

## IN / OUT

| IN | OUT |
|----|-----|
| `StartupState_StateChanged` forward; initial `ApplyStartupOverlay` on subscribe; `StartupRetryButton` async retry + progress on dispatcher | **Welcome** dialog / first-run / **`MainWindowStartupWelcomeActivationShellBridge`** (Slice 11) |
| `StartupState` + failure message text for backend failed | **Nav rail**, **search**, **panel** bridges; **`MainWindowNavigationShellBridge`**, file activation, jumplists |
| **One bridge:** `MainWindowStartupOverlayShellBridge` | **RHVoice** / engines / preflight; **CI verify-harness GOV** row or **closure** narrative **edits** (Tasks 76–77) |
| | Absorbing **wholesale keyboard** or **transport** or **status bar** into this type — **other bridges stay separate** |

## One bridge class name

**`MainWindowStartupOverlayShellBridge`**

## Dependency map (Task 71 — enumerated, evidence from `MainWindow.xaml.cs`)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | After **`MainWindowStartupWelcomeActivationShellBridge`**: `new MainWindowStartupOverlayShellBridge( () => FindInContent<Border>(\"StartupOverlay\"), () => FindInContent<TextBlock>(\"StartupOverlayMessage\"), () => FindInContent<ProgressRing>(\"StartupProgressRing\"), () => FindInContent<Button>(\"StartupRetryButton\"), DispatcherQueue, onShellInteractiveTiming, () => AppServices.GetService<StartupRetryCoordinator>() )` where `onShellInteractiveTiming` enforces **once** and calls **`ColdStartTimingCollector.RecordShellInteractive()`** |
| **`MainWindow` — startup try block** | `StateChanged += StartupState_StateChanged`; `ApplyStartupOverlay(CurrentState, FailureMessage)` for initial paint |
| **`MainWindow` — `StartupState_StateChanged`** | Thin: `_startupOverlayShellBridge.OnStartupStateChanged(e)` (bridge re-enqueues to **`DispatcherQueue`**) |
| **`MainWindow` — `StartupRetryButton_Click`** (XAML `Click=`) | Thin: `await _startupOverlayShellBridge.OnRetryButtonClickAsync()` |
| **`MainWindowLifetimeCleanup` — `UnsubscribeStartupOverlay`** | Unsubscribes **`StateChanged`**; nulls `_startupStateService` — unchanged contract |
| **Downstream** | **`IStartupStateService`**, **`StartupRetryCoordinator`**, **WinUI** `Border` / `TextBlock` / `ProgressRing` / `Button` / `Visibility` |
| **Async / UI** | **`ConfigureAwait(true)`** on retry; **`TryEnqueue`** for progress reports; no ctor fire-and-forget from **`MainWindow` ctor** beyond existing subscription pattern; **no new `ContentDialog` / popup** in this bridge |

**Must not call into (anti-creep):** **`MainWindowPanelDockShellBridge`**, **`MainWindowPanelPreviewShellBridge`**, **`MainWindowSearchOverlayShellBridge`**, **`MainWindowNavigationShellBridge`**, `IShellNavigationCoordinator` — **not** in this file.

**Async / UI / side effects:** UI thread for overlay element mutation; `StartupRetryCoordinator.RetryAsync` may use backend/timeout paths — no new `Thread` creation in the bridge; no file I/O in the bridge.

**Explicitly deferred:** **Workspace splitter** layout, **wholesale** `InitializePanels` / transport shell, **other** `MainWindow*ShellBridge` refactors not listed IN.

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md): **Welcome activation** and **backend overlay** remain **separate** shell stories. Do not merge.

## Acceptance

- **`MainWindow.xaml.cs`** has **no** private `UpdateStartupOverlay` **method**; **no** “Starting VoiceStudio services…” string (UX copy lives in the bridge).
- **`Gap008Slice26Tests`** + **`MainWindowStartupOverlayShellBridgeTests`**; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings elsewhere) |
| `dotnet test` … `Gap008Slice26|MainWindowStartupOverlayShellBridge` | **7/7** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **206/206** Passed, `listedTestCount` **206**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_110349.trx`; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` | **exit 0** (if run in same session) |

## RHVoice / CI freeze (Tasks 76–77)

No edits under **`engines/audio/rhvoice/`**; no “optimistic” [ENGINE_PARITY_MATRIX.md](../reports/verification/ENGINE_PARITY_MATRIX.md) theatre; no **GOV** verify-harness **closure** rewrites in this change set (see [EXECUTION_ROW_DISCIPLINE.md](../governance/EXECUTION_ROW_DISCIPLINE.md)).

## Task 78 (Path G2)

**Not** applicable — this slice **is** G1; Path G2 umbrella closure is **out of scope** for this charter.
