# GAP-008 Slice 21 — MainWindow **keyboard shortcuts** shell (bounded)

**Status:** Accepted  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)  
**Prior:** [Slice 20 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE20_MAINWINDOW_MENU_TOOL_ACTIVATION_SHELL.md) (**`KeyboardShortcutsMenuItem_Click`** was **DEFERRED** there).

## First seam (exact)

**`MainWindowKeyboardShortcutsShellBridge`** owns **only** the **`KeyboardShortcutsMenuItem_Click`** flow: show **`KeyboardShortcutsView`** in a **`ContentDialog`**; on **Primary** (“Customize…”), resolve **`KeyboardCustomizationViewModel`** via DI, show **`KeyboardCustomizationView`** in a second **`ContentDialog`**, **`Dispose`** the VM in **`finally`**; on failure, **`IToastNotificationService.ShowToast`** (same messages as before). **`MainWindow`** keeps a **one-line** `KeyboardShortcutsMenuItem_Click` forward that supplies **`XamlRoot`** (`this.Content?.XamlRoot`), VM factory, and toast accessor.

## ADR-047 (WinUI / XamlRoot)

Dialogs are shown **only** from the **menu click handler** (user gesture after window content is loaded), not from **`MainWindow`** constructor. The bridge does **not** change timing: callers pass a **`Func<XamlRoot?>`** evaluated at invoke time (same as prior **`this.Content.XamlRoot`**). If **`XamlRoot`** is null, the bridge throws **`InvalidOperationException`** with an explicit message (fail-fast); the existing **`catch`** surfaces a toast — no silent skip.

## IN / OUT table

| Cluster | IN / OUT |
| --- | --- |
| **`MainWindowKeyboardShortcutsShellBridge`**, **`RunKeyboardShortcutsMenuFlowAsync`** | **IN** |
| **`MainWindowMenuToolActivationShellBridge`**, check-for-updates, mini-timeline, collaboration, workspaces | **OUT** |
| Toolbar customization, command palette, tool catalog, search overlay | **OUT** |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen** |

## Dependency map

| Item | Owner | Notes |
| --- | --- | --- |
| **`ContentDialog`**, **`KeyboardShortcutsView`**, **`KeyboardCustomizationView`** | Bridge | WinUI types |
| **`KeyboardCustomizationViewModel`**, **`RefreshShortcuts`**, **`Dispose`** | Bridge via injected **`Func<KeyboardCustomizationViewModel>`** | Same lifecycle as before |
| **`IToastNotificationService`**, **`ToastType.Error`** | Bridge via injected getter | Error path only |

## RHVoice

**Zero** edits under **`engines/audio/rhvoice/`**. Creep tests on **`MainWindowKeyboardShortcutsShellBridge.cs`**.

## Anti-sprawl

Single-story owner for **keyboard shortcuts menu → dialogs** only. **Forbidden:** routing other menu items, transport, or status logic into this bridge.

## Spine count authority (SSOT)

Do not paste the full MSTest OR filter here. Use: [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt), [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1), **`.buildlogs/gap008_spine/last_run_summary.json`**, [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md).

## Acceptance criteria

1. **`KeyboardShortcutsMenuItem_Click`** body lives only as a thin forward through **`MainWindowKeyboardShortcutsShellBridge`**.
2. **`Gap008Slice21Tests`** + **`MainWindowKeyboardShortcutsShellBridgeTests`**; filter prepend; no reference from this bridge to **`MainWindowMenuToolActivationShellBridge`**.
3. **`Gap008Slice20Tests`** still assert keyboard path does **not** use **`_menuToolActivationShellBridge`**.

## Verification

**Closed 2026-04-26 (local):** `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` **0 errors**; `dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice21Tests|FullyQualifiedName~MainWindowKeyboardShortcutsShellBridgeTests|FullyQualifiedName~Gap008Slice20Tests|FullyQualifiedName~MainWindowMenuToolActivationShellBridgeTests"` **Passed: 24**; `scripts\Run-Gap008MainWindowRegressionTests.ps1` → **`listedTestCount` 170**, **`passed` 170**, **`failed` 0** (`.buildlogs/gap008_spine/last_run_summary.json`); `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` **4 passed**; `python scripts/run_verification.py` **Overall PASS**; optional `.\scripts\verify.ps1 -Quick` **exit 0**. CI green fixture: `tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json` updated to **170** / **170**.

## Slice 22+ (planning only)

Next seam requires a new **`VOICESTUDIO_BOUNDED_GAP008_SLICE22_*.md`**.
