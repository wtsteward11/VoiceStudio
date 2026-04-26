# GAP-008 Slice 32 — MainWindow shell chrome (Mica, title bar, theme) (bounded)

**Status:** Accepted (Tasks 135–148 Phase B)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from other **“Slice 32”** rows in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues with Slice 32** on seam **shell chrome** (Mica / acrylic, custom title bar, `AppWindow.TitleBar` colors, `IUnifiedThemeService.ThemeChanged`); **Path G1**; **umbrella GAP-008 is not closed**.

## Goal

Move the implementation previously in [`MainWindow.Shell.cs`](../../src/VoiceStudio.App/MainWindow.Shell.cs) (`ApplyMicaBackdrop`, `InitializeCustomTitleBar`, `UnsubscribeShellChromeEvents` and private theme/title-color helpers) into **`MainWindowShellChromeShellBridge`**.  
**`MainWindow`** / **`MainWindow.Shell` partial** keeps **expression-bodied** one-line forwards.  
**Loaded hook** still receives **`ApplyMicaBackdrop` / `InitializeCustomTitleBar`** from [`MainWindowShellLoadedBootstrap`](../../src/VoiceStudio.App/Services/MainWindowShellLoadedBootstrap.cs) (ADR-047). **Lifetime** cleanup still **`UnsubscribeShellChromeEvents`** via [`MainWindowLifetimeCleanupShellBridge`](../../src/VoiceStudio.App/Services/MainWindowLifetimeCleanupShellBridge.cs).

**Deferred (explicit):** `MaterialsHelper` / system material selection internals; `ThemeService` / `IUnifiedThemeService` **implementations**; **Slice 1** `MainWindowShellLoadedBootstrap` runner structure; **Task 103** / **113** / **123** / **134** optional appendices.

## IN / OUT

| IN | OUT |
|----|------|
| **`Window`**, **`RootGrid`**, **`AppTitleBar`** in ctor; **`ApplyMicaBackdrop`**, **`InitializeCustomTitleBar`**, **`UnsubscribeShellChromeEvents`** on bridge | **RHVoice** / `engines/audio/rhvoice/` |
| **`IUnifiedThemeService`** via `AppServices.TryGetThemeService()` only; **`ThemeChangedEventArgs`** from in-repo interface | **CI verify-harness** GOV row rewrites (Tasks **95–96** / **104** / **112** / **122** / **133**) — **out** of spine |
| **Overlap:** `MainWindowLifetimeCleanupShellBridge` **invokes** **unsubscribe** only — **no** new cleanup semantics | **IBackendClient**, **import**, **transport**, **undo**, **import workflow** — **out** |
| | **Task 103** / **113** / **123** / **134** — **not** spine gates |

## One bridge class name

**`MainWindowShellChromeShellBridge`**

## Dependency map

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | `new MainWindowShellChromeShellBridge(this, RootGrid, AppTitleBar)` **immediately after** `MainWindowImportWorkflowShellBridge` |
| **`MainWindowShellLoadedBootstrap` hooks** | `ApplyMicaBackdrop` / `InitializeCustomTitleBar` → one-line forwards to bridge (Loaded-only) |
| **`MainWindow` lifetime** | `UnsubscribeShellChromeEvents` → bridge **idempotent** unsubscribe of theme handler |
| **`MaterialsHelper`**, **`AppWindow.TitleBar`** | System material + **WinUI 3** caption color palette (dark/light) |
| **Must not name in bridge (anti-creep):** | `MainWindowImportWorkflowShellBridge`, `MainWindowNavigationShellBridge`, `IBackendClient` |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** shell-chrome story. **Do not** re-open **Slice 31** import/transport. **No** `MainWindow` inline backdrop/title color logic after land.

## Acceptance

- `MainWindow.Shell.cs` contains only **one-line** forwards to `_shellChromeShellBridge`.
- `Gap008Slice32Tests` + `MainWindowShellChromeShellBridgeTests`; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend** new tokens; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **242/242** Passed, **listedTestCount** **242**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_150316.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |

## Changelog

- 2026-04-26: **Tasks 135–148** Phase B — charter; bridge **`MainWindowShellChromeShellBridge`**.
