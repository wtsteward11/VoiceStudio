# GAP-008 Slice 11 — MainWindow startup / welcome activation shell (bounded)

**Status:** Accepted (Tasks 359–368 batch; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Prerequisite:** [Startup truth recovery](VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md) — **closed** (§A process cold start, §B pins, §C operator visual [PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md](../reports/verification/PROOF_GAP008_STARTUP_TRUTH_VISUAL_2026-04-25.md), §D coverage limits).  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

## First seam (exact)

**`MainWindowStartupWelcomeActivationShellBridge`** owns **`Window.Activated`**-time behavior previously inlined in **`MainWindow.MainWindow_Activated`**:

- **Keyboard attach (first activation path):** **`Content.KeyDown`** → **`MainWindow_KeyDown`** (same handler instance; bridge does not reimplement shortcuts).
- **Gates:** **`IsGateCSmokeMode`**, **`WindowActivationState.CodeActivated`**, **`IsSafeStartupMode`**.
- **Welcome one-shot:** **`_welcomeDialogShown`**, **`UnpackagedSettingsHelper`** + **`ShowWelcomeDialog`**, **`WelcomeView`** + **`XamlRoot`**, **`ShowAsync`** with **5s** timeout / **`Hide`**, **`SetValue`** for **`ShowOnStartup`**.

**`MainWindow`** retains **`MainWindow_Activated`** as a **thin** `await _startupWelcomeActivationShellBridge.HandleActivatedAsync(this, e)` forward and keeps **`MainWindow_KeyDown`** (private instance method passed into bridge ctor).

## Task 361 — KeyDown in/out decision

**Decision:** **In scope for Slice 11.** KeyDown wiring lives in the same **`Activated`** handler today; splitting it would leave **`MainWindow_Activated`** non-thin without a second slice. The bridge receives **`KeyEventHandler`** from **`MainWindow`** (no duplicate keyboard logic).

## Task 361 — Dependency / blast-radius map

| Responsibility | Current owner (pre-slice) | Target after Slice 11 | Risk | Tests |
| -------------- | ----------------------- | ---------------------- | ---- | ----- |
| Gate C smoke / exit | `MainWindow.IsGateCSmokeMode` | Same static on **`MainWindow`**; **`Func<bool>`** into bridge | L | Pin: bridge ctor receives gate |
| Safe startup env | `MainWindow.IsSafeStartupMode` | Same static; **`Func<bool>`** into bridge | L | Pin |
| KeyDown attach | `MainWindow_Activated` | **`MainWindowStartupWelcomeActivationShellBridge`** | M | Pin: **`HandleActivatedAsync`** |
| Welcome one-shot + settings | `MainWindow_Activated` | Bridge | M | **`Gap008Slice11Tests`** + creep |
| **`WelcomeView` lifecycle** | `MainWindow_Activated` | Bridge | M | No ctor fire-and-forget; async stays in **`HandleActivatedAsync`** |
| Startup overlay / status bar | `StartupState_*` / `StatusBarCoordinator` | **Out** — owned elsewhere | H if pulled in | Creep guards |

**Isolation stop rule:** If a change requires **`MainWindowShellLoadedBootstrap`**, tool catalog, palette, toolbar bridges, search overlay, or project workflow types in the Slice 11 bridge file, **stop** — wrong slice.

## In scope (explicit)

| Symbol / behavior | Role |
| ----------------- | ---- |
| **`MainWindowStartupWelcomeActivationShellBridge`** | **`HandleActivatedAsync(Window, WindowActivatedEventArgs)`** |
| **`MainWindow.MainWindow_Activated`** | Forwards to bridge; outer catch optional / minimal |
| **`_startupWelcomeActivationShellBridge`** field on **`MainWindow`** | Constructed in **`MainWindow`** ctor with **`MainWindow_KeyDown`** delegate |

## Explicitly NOT Slice 11

| Cluster | Deferred |
| ------- | -------- |
| Loaded bootstrap / tail (Slices 1, 3) | Own briefs |
| Tool catalog, command palette, toolbar routing, search overlay, project workflow, session lifecycle cleanup | Prior / other slices |
| **`engines/audio/rhvoice/`** | **Frozen** — operator gate only |

## RHVoice (Task 368)

**Zero** edits under **`engines/audio/rhvoice/`**; RHVoice remains frozen.

## Anti-pattern (Task 367)

**`StatusBarCoordinator`** startup **`StateChanged`** refresh is **status primary-line truth only** — not a template for unrelated shell refreshes. See [startup truth brief](VOICESTUDIO_BOUNDED_GAP008_STARTUP_TRUTH_RECOVERY.md) § Operator proof.

## Verification (canonical spine)

**Filter list:** [`tools/gap008_mainwindow_regression_filter.txt`](../tools/gap008_mainwindow_regression_filter.txt) — extend only (superset rule).

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python scripts\run_verification.py
```

**Result (2026-04-25, post–Tasks 378–387):** `dotnet build` **0** errors; **`Run-Gap008MainWindowRegressionTests.ps1`** **Passed: 97**; `run_verification.py` **Overall: PASS**. Verify bar unchanged (not `verify.ps1`-anchored batch).

## Changelog

- 2026-04-25: Initial Slice 11 charter + dependency map; bridge + tests landed per Tasks 362–365.
