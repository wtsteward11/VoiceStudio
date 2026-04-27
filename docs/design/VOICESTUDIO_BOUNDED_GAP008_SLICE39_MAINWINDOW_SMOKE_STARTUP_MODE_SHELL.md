# GAP-008 Slice 39 — MainWindow smoke / safe-startup mode shell (bounded)

**Status:** Accepted (Tasks 211–220)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 39”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 39** moving **`VOICESTUDIO_SAFE_STARTUP`** / Gate-C **smoke** environment and command-line classification out of **`MainWindow`** into **`MainWindowSmokeStartupModeShellBridge`**; **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 38** deferred **`IsSafeStartupMode` / `IsGateCSmokeMode`** as explicit follow-up. Those probes lived as **`private static`** methods on **`MainWindow`** and duplicated **`IsSafeStartupMode`** on **`ShellNavigationCoordinator`**. **Slice 39** centralizes the rules in **one** bridge, removes **`MainWindow`** static bodies, and removes the coordinator duplicate so **safe-startup** has a **single** implementation.

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowSmokeStartupModeShellBridge`** — **`IsSafeStartupMode()`** / **`IsGateCSmokeMode()`** (instance; same rules as prior **`MainWindow`** statics) | **RHVoice** / `engines/audio/rhvoice/` |
| **`EvaluateSafeStartup()`** / **`EvaluateGateCSmoke()`** — **public static** for **`ShellNavigationCoordinator`** (no duplicate private safe-mode) | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence |
| **`MainWindow`** — **`readonly`** field; ctor **`new`** before **`MainWindowPanelRegionFocusShellBridge`**; **`Func<bool>`** delegates use **`_smokeStartupModeShellBridge.IsGateCSmokeMode`** / **`IsSafeStartupMode`**; ctor tail **`if (_smokeStartupModeShellBridge.IsSafeStartupMode())`**; **`SwitchToPanel`** uses **`_smokeStartupModeShellBridge.IsGateCSmokeMode()`** | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn / matrix theater |
| | **Tasks 103 / 113 / 123 / 134 / 148 / 158 / 168 / 178 / 188 / 210** — optional runtime appendix; **not** spine gates |
| | **`MainWindowKeyboardShortcutKeyDispatchShellBridge`** (**Slice 38**) — **no** **`KeyDown`** / **`TryHandleKeyDown`** merge |
| | **`MainWindowKeyboardShortcutRegistrationShellBridge`** / **`MainWindowPanelQuickSwitchShortcutRegistrationShellBridge`** (**Slices 36–37**) — **no** registration merge |
| | **`MainWindowStartupWelcomeActivationShellBridge`** (**Slice 11**) — **no** moving **`HandleActivatedAsync`**, **`WelcomeView`**, or **`MainWindow_Activated`** try/catch; **predicates only** stay shared via **`Func<bool>`** |
| | **Obsolete `SwitchToPanel`** removal — **OUT** (delegate source update only if body remains) |

## One bridge class name

**`MainWindowSmokeStartupModeShellBridge`**

## Dependency map (Task 214)

| Area | Detail |
|------|--------|
| **`MainWindow` partial** | **`MainWindow.xaml.cs`** — **field:** **`_smokeStartupModeShellBridge`** (readonly). **Ctor:** assign **`new MainWindowSmokeStartupModeShellBridge()`** immediately **before** **`_panelQuickSwitchShellBridge = new ...`** so **`MainWindowPanelRegionFocusShellBridge`**, **`ShellNavigationCoordinator`**, and **`MainWindowStartupWelcomeActivationShellBridge`** receive **`Func<bool>`** from the instance. **Remove:** **`private static bool IsSafeStartupMode`** / **`IsGateCSmokeMode`**. **Replace:** ctor tail **`IsSafeStartupMode()`** → **`_smokeStartupModeShellBridge.IsSafeStartupMode()`**; **`SwitchToPanel`** **`IsGateCSmokeMode()`** → **`_smokeStartupModeShellBridge.IsGateCSmokeMode()`**. |
| **`ShellNavigationCoordinator`** | **`ExecuteNavCommandAsync`** — replace private **`IsSafeStartupMode`** with **`MainWindowSmokeStartupModeShellBridge.EvaluateSafeStartup()`** (same semantics as instance **`IsSafeStartupMode`**). **Delete** duplicate private static method. |
| **Injected / resolved services** | **None** — env / **`Environment.CommandLine`** only. |
| **Async / ADR-047** | **Synchronous** probes; **no** ctor async. |
| **Side effects** | **Read-only** process environment and command line; **no** UI. |
| **Overlap / creep guards** | **Slice 11** = **activated** welcome **orchestration** only. **Slice 38** = **KeyDown** dispatch only. This bridge = **mode flags** only; source must not reference keyboard dispatch or registration bridge **type names**. Tests: file-text asserts. |
| **Explicitly deferred** | **`MainWindow_Activated`** logging policy-only extraction; **obsolete `SwitchToPanel`** removal; menu/tool “remaining glue” per decomposition plan alternate candidate. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 38** dispatch, **Slice 36/37** registration, **Slice 11** welcome UI, or **ShellNavigationCoordinator** navigation execution logic—**only** shared predicate implementation + coordinator duplicate removal.

## Alternatives not Slice 39

- **`MainWindow_Activated` try/catch shell** — overlaps **Slice 11**; lower value unless chartered narrowly.
- **Leave duplicate `IsSafeStartupMode` on coordinator** — rejected; **one source of truth** for safe-startup rules.

## Acceptance

- **`MainWindow`**: **no** **`private static bool IsSafeStartupMode` / `IsGateCSmokeMode`**; **`_smokeStartupModeShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice39Tests` + `MainWindowSmokeStartupModeShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line 2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 217–218)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `dotnet test` … `--filter` `FullyQualifiedName~Gap008Slice39Tests\|FullyQualifiedName~MainWindowSmokeStartupModeShellBridgeTests` | **11 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **293/293** Passed; **listedTestCount** **293**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_194918.trx` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** |

## Changelog

- 2026-04-27: **Tasks 218** — verification table filled from Task **217** outputs (build, **11** targeted MSTest, **293/293** spine, pytest shape, `run_verification.py` **PASS**).
- 2026-04-26: **Tasks 211–220** — charter; bridge **`MainWindowSmokeStartupModeShellBridge`**; coordinator **`EvaluateSafeStartup`** dedupe; land + verify (Task **217**).
