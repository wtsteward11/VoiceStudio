# GAP-008 Slice 40 — MainWindow window-activated exception logging shell (bounded)

**Status:** Accepted (Tasks 223–230)  
**Date:** 2026-04-27  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 40”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 40** moving the **`MainWindow_Activated`** **try/catch** + **`ErrorLogger.LogWarning`** wrapper out of **`MainWindow`** into **`MainWindowWindowActivatedLoggingShellBridge`**; **`MainWindowStartupWelcomeActivationShellBridge`** (**Slice 11**) **still owns** **`HandleActivatedAsync`** and welcome orchestration; **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 39** deferred **`MainWindow_Activated` policy** as explicit follow-up. Today **`MainWindow`** owns a **try/catch** that logs activation failures. **Slice 40** extracts **only** that shell so **`MainWindow_Activated`** is a **thin forward**; **no** change to **`HandleActivatedAsync`** semantics or **`WelcomeView`** wiring (**Slice 11** remains authoritative for those).

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowWindowActivatedLoggingShellBridge`** — **`RunActivatedAsync(Func<Task> inner)`** — **try/catch** + **`ErrorLogger.LogWarning`** with stable scope **`MainWindow.MainWindow_Activated`** | **RHVoice** / `engines/audio/rhvoice/` |
| **`MainWindow`** — **`readonly`** field **`_windowActivatedLoggingShellBridge`**; ctor **`new`** **immediately after** **`_startupWelcomeActivationShellBridge = ...`** / **before** **`_startupOverlayShellBridge = ...`**; **`MainWindow_Activated`** → **`await _windowActivatedLoggingShellBridge.RunActivatedAsync(() => _startupWelcomeActivationShellBridge.HandleActivatedAsync(this, e))`** | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence |
| | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn / matrix theater |
| | **Tasks 103 / 113 / 123 / 134 / 148 / … / 230** — optional runtime appendix; **not** spine gates |
| | **`MainWindowStartupWelcomeActivationShellBridge`** (**Slice 11**) — **no** edits to **`HandleActivatedAsync`**, **`WelcomeView`**, or welcome **orchestration** |
| | **`MainWindowSmokeStartupModeShellBridge`** (**Slice 39**) — **no** predicate / env logic merge |
| | **`MainWindowKeyboardShortcutKeyDispatchShellBridge`** (**Slice 38**) — **no** keyboard merge |
| | **Obsolete `SwitchToPanel`** **removal** — **OUT** (file-pin tests still expect method presence; future slice) |
| | **Menu/tool “remaining glue”** beyond this handler — **OUT** |

## One bridge class name

**`MainWindowWindowActivatedLoggingShellBridge`**

## Dependency map (Tasks 223–224)

| Bucket | Content |
|--------|---------|
| **MainWindow** | **`MainWindow_Activated`** — **delete** inline **try/catch** body; **thin** **`RunActivatedAsync`** lambda calling **`_startupWelcomeActivationShellBridge.HandleActivatedAsync(this, e)`**. **`MainWindow` partial** — **field** **`_windowActivatedLoggingShellBridge`** after **`_startupWelcomeActivationShellBridge`**. **Ctor:** **`_windowActivatedLoggingShellBridge = new MainWindowWindowActivatedLoggingShellBridge();`** after **`StartupWelcomeActivationShellBridge Created`** checkpoint / before **`_startupOverlayShellBridge = new ...`**. **`this.Activated += MainWindow_Activated`** unchanged (registration stays on **`MainWindow`**). |
| **Consumers** | **`ErrorLogger`** (static **`LogWarning`**) — called **only** from bridge. **`MainWindowStartupWelcomeActivationShellBridge`** — **unchanged** public surface; invoked via **`Func<Task>`** from **`MainWindow`**. |
| **Async / ADR-047** | **`MainWindow_Activated`** remains **`async void`** (WinUI event pattern); **inner** uses **`ConfigureAwait(true)`** inside bridge (**same** as prior **`MainWindow`** body). **No** new **`async`** from ctor. |
| **Side effects** | **Logging only** on failure path (**Warning**); **no** UI, **no** env mutation. |
| **Overlaps** | **Slice 11** — **orchestration** + **`HandleActivatedAsync`** stay on **`MainWindowStartupWelcomeActivationShellBridge`**. **Slice 40** — **exception boundary** + **log scope** only. **Slices 36–39** — **no** keyboard / smoke predicate changes. |
| **Deferred** | **Obsolete `SwitchToPanel`** removal; **menu/tool** alternate candidate from decomposition plan; **`MainWindow_Activated`** subscription site move (still **`MainWindow`**). |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 11** welcome flow, **Slice 39** smoke predicates, or **Slice 38** dispatch.

## Alternatives not Slice 40

- **Delete `SwitchToPanel` now** — breaks **`Gap008Slice24Tests`** file pins and **`[Obsolete(..., error: true)]`** contract without a chartered follow-up; **rejected** for **Slice 40**.
- **Move `HandleActivatedAsync` into this bridge** — **rejected**; **Slice 11** owns welcome activation.

## Acceptance

- **`MainWindow`**: **no** inline **try/catch** in **`MainWindow_Activated`**; **`_windowActivatedLoggingShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice40Tests` + `MainWindowWindowActivatedLoggingShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line 2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (Tasks 227–228)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (2026-04-27); warnings only in files outside this slice’s touched scope |
| `dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice40Tests\|FullyQualifiedName~MainWindowWindowActivatedLoggingShellBridgeTests"` | **9 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **302/302** Passed; **`listedTestCount`** **302**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_201018.trx`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → **`.buildlogs/verification/last_run.json`** |

## Changelog

- 2026-04-27: **Tasks 228** — Verification table filled from green run (**302/302** spine, TRX **`gap008_spine_20260426_201018.trx`**); fixture + reconciliation + STATE proof index aligned.
- 2026-04-27: **Tasks 223–224** — **Accepted** charter + dependency map; bridge name **`MainWindowWindowActivatedLoggingShellBridge`**.
