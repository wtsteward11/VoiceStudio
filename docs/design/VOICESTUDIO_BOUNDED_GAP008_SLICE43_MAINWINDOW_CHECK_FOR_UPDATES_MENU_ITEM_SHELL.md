# GAP-008 Slice 43 — MainWindow View → Check for Updates… menu item wiring shell (bounded)

**Status:** Accepted  
**Date:** 2026-04-27  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 43”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 43** moving **View → Check for Updates…** **`MenuFlyoutItem.Click`** wiring out of **`MainWindow`** into **`MainWindowCheckForUpdatesMenuItemShellBridge`**; **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) **still owns** **`RunCheckForUpdatesAsync`** (dialog + errors); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 20** owns update dialog flow. **`MainWindow`** still declared **`private async void CheckForUpdatesMenuItem_Click`** forwarding to **`_menuToolActivationShellBridge.RunCheckForUpdatesAsync`**. **Slice 43** extracts **only** that **menu item** wiring so **`MainWindow`** has **no** named handler for this item; **no** change to **`MainWindowMenuToolActivationShellBridge`** behavior.

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowCheckForUpdatesMenuItemShellBridge`** — ctor captures **`MainWindowMenuToolActivationShellBridge`**, **`Func<IViewModelContext>`**, **`IUpdateService`**, **`Func<IErrorDialogService>`**; **`OnCheckForUpdatesMenuItemClick`** and **`RunFlowAsync`** delegate to **`RunCheckForUpdatesAsync`** | **RHVoice** / `engines/audio/rhvoice/` |
| **`MainWindow`** — **`readonly`** field; ctor **`new`** **immediately after** **`MainWindowMenuToolActivationShellBridge Created`** / **before** **`MainWindowKeyboardShortcutsShellBridge Created`**; **`_checkForUpdatesMenuItem.Click +=`** bridge handler; **delete** **`CheckForUpdatesMenuItem_Click`** | **CI verify-harness** GOV row rewrites |
| **`Gap008Slice43Tests`** + **`MainWindowCheckForUpdatesMenuItemShellBridgeTests`**; prepend-only [filter](../../tools/gap008_mainwindow_regression_filter.txt) line 2 | **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) — **no** body edits |
| | **Mini Timeline** / **Manage Workspaces** — **OUT** |
| | **Customize Toolbar** (**Slice 42**) — **no** merge |

## One bridge class name

**`MainWindowCheckForUpdatesMenuItemShellBridge`**

## Dependency map

| Bucket | Content |
|--------|---------|
| **MainWindow** | **Delete** **`CheckForUpdatesMenuItem_Click`**. **`_checkForUpdatesMenuItem.Click +=`** → **`_checkForUpdatesMenuItemShellBridge.OnCheckForUpdatesMenuItemClick`**. **Ctor:** **`_checkForUpdatesMenuItemShellBridge = new MainWindowCheckForUpdatesMenuItemShellBridge(_menuToolActivationShellBridge, () => ServiceProvider.GetViewModelContext(), _updateService, () => ServiceProvider.GetErrorDialogService())`** immediately after **`_menuToolActivationShellBridge`** / **before** **`_keyboardShortcutsShellBridge`**. |
| **Consumers** | **`MainWindowMenuToolActivationShellBridge`** — sole update UI path (**Slice 20**). |
| **Async / ADR-047** | **`OnCheckForUpdatesMenuItemClick`** remains **`async void`**; **`ConfigureAwait(true)`** on **`RunFlowAsync`** chain. |
| **Overlaps** | **Slice 20** — `UpdateDialog` + error surface. **Slice 43** — **menu `Click`** wiring only. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 20** `RunCheckForUpdatesAsync` implementation.

## Acceptance

- **`MainWindow`**: **no** **`CheckForUpdatesMenuItem_Click`**; **`_checkForUpdatesMenuItemShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice43Tests` + `MainWindowCheckForUpdatesMenuItemShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line-2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `dotnet test … --filter "FullyQualifiedName~Gap008Slice43Tests\|FullyQualifiedName~MainWindowCheckForUpdatesMenuItemShellBridgeTests"` | **pass** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **listedTestCount == passed**; **`failed == 0`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |

## Changelog

- 2026-04-27: **Accepted** charter + land **`MainWindowCheckForUpdatesMenuItemShellBridge`** (Task plan Truth sync + Slice 43).
