# GAP-008 Slice 44 — MainWindow File → Manage Workspaces… menu item wiring shell (bounded)

**Status:** Accepted  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 44”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 44** moving **File → Manage Workspaces…** **`MenuFlyoutItem.Click`** wiring out of **`MainWindow`** partials into **`MainWindowManageWorkspacesMenuItemShellBridge`**; **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) **still owns** **`RunManageWorkspacesAsync`** (dialog + toast on failure); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 20** owns workspace manager dialog flow. **`MainWindow.Workspaces`** declared **`private async void ManageWorkspaces_Click`** forwarding to **`_menuToolActivationShellBridge.RunManageWorkspacesAsync`**. **Slice 44** extracts **only** that **menu item** **`Click`** wiring so **`MainWindow`** source has **no** **`ManageWorkspaces_Click`**; **no** change to **`MainWindowMenuToolActivationShellBridge`** implementation beyond existing **`RunManageWorkspacesAsync`** contract.

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowManageWorkspacesMenuItemShellBridge`** — ctor captures **`MainWindowMenuToolActivationShellBridge`**, **`Func<XamlRoot?>`**, **`Func<IToastNotificationService?>`**; **`OnManageWorkspacesMenuItemClick`** / **`RunFlowAsync`** delegate to **`RunManageWorkspacesAsync`** | **RHVoice** / `engines/audio/rhvoice/` matrix theater |
| **`MainWindow.xaml.cs`** — **`readonly`** field; ctor **`new`** **immediately after** **`MainWindowCheckForUpdatesMenuItemShellBridge`** / **before** **`MainWindowKeyboardShortcutsShellBridge`**; **`_manageWorkspacesMenuItem.Click +=`** bridge handler | **CI verify-harness** GOV closure narrative edits |
| **Delete** **`ManageWorkspaces_Click`** from **`MainWindow.Workspaces.cs`** | **Runtime-truth** lane doc edits unless product reopens |
| **`Gap008Slice44Tests`** + **`MainWindowManageWorkspacesMenuItemShellBridgeTests`**; prepend-only [filter](../../tools/gap008_mainwindow_regression_filter.txt) line 2 | **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) — **no** body edits |
| **`Gap008Slice20Tests`** pin — Manage Workspaces delegation via menu item shell bridge, **not** `ManageWorkspaces_Click` on **`MainWindow.Workspaces`** | **Mini Timeline**, **collaboration** toggles, other menu items |
| | **Engine / STT** numeric slice label collisions (registry disambiguation only) |

## One bridge class name

**`MainWindowManageWorkspacesMenuItemShellBridge`**

## Dependency map

| Bucket | Content |
|--------|---------|
| **Handlers / methods** | **`ManageWorkspaces_Click`** — **`src/VoiceStudio.App/MainWindow.Workspaces.cs`** (lines ~193–198 pre-slice): **`async void`** forwarding to **`RunManageWorkspacesAsync`** with **`() => (Content as FrameworkElement)?.XamlRoot`** and **`() => ServiceProvider.TryGetToastNotificationService()`**. **`RunManageWorkspacesAsync`** — **`src/VoiceStudio.App/Services/MainWindowMenuToolActivationShellBridge.cs`**: **`public async Task RunManageWorkspacesAsync(Func<XamlRoot?> getXamlRoot, Func<IToastNotificationService?> tryGetToast)`** — **`ArgumentNullException.ThrowIfNull`** on both **`Func`** parameters; **`WorkspaceManagerDialog`** construction + **`ShowAsync`**; catch shows toast title **`"Workspace Management"`** via **`tryGetToast()`**. |
| **Wiring sites** | **`MainWindow.xaml.cs`** — **`_manageWorkspacesMenuItem`** created in ctor **Menu Items Created** block; **`_manageWorkspacesMenuItem.Click`** must attach to **`_manageWorkspacesMenuItemShellBridge.OnManageWorkspacesMenuItemClick`**. **`MainWindowMenuBarShellBridge`** — **`MainWindowMenuBarShellWire.ManageWorkspacesMenuItem`** (**`src/VoiceStudio.App/Services/MainWindowMenuBarShellBridge.cs`**) passes the flyout item into menu build only; **no** ctor ordering dependency on the new bridge (bridge instantiates **before** **`MainWindowMenuBarShellBridge`**). |
| **Async boundaries** | **`OnManageWorkspacesMenuItemClick`** is **`async void`** (WinUI **`Click`** handler); forwards with **`ConfigureAwait(true)`** on **`RunFlowAsync`** (same pattern as **`MainWindowCheckForUpdatesMenuItemShellBridge`**). |
| **Side effects** | Only those already inside **`RunManageWorkspacesAsync`** (dialog + error toast). The menu-item bridge **must not** add behavior. |
| **Overlap** | **`Gap008Slice20Tests`** / **`Gap008Slice21Tests`** — update pins that still assert **`ManageWorkspaces_Click`** on **`MainWindow.Workspaces`**. |
| **Deferred** | Workspace grid splitter (**Slice 33**), layout restore, other **`MainWindow.Workspaces`** methods; any further menu items without their own bounded brief. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **`RunManageWorkspacesAsync`** implementation into this bridge.

## Acceptance

- **`MainWindow`**: **no** **`ManageWorkspaces_Click`** on **`MainWindow.xaml.cs`** or **`MainWindow.Workspaces.cs`**; **`_manageWorkspacesMenuItemShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice44Tests`** + **`MainWindowManageWorkspacesMenuItemShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line-2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.
- Reconciliation § *Spine size after Slice 44* + fixture **`listedTestCount`** / **`effectiveFilter`** match discovery (no hand-pasted **N**).

## Verification

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (2026-04-27) |
| `dotnet test … --filter "FullyQualifiedName~Gap008Slice44Tests\|FullyQualifiedName~MainWindowManageWorkspacesMenuItemShellBridgeTests"` | **10 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **341/341** Passed; **listedTestCount** 341; TRX **`.buildlogs/gap008_spine/gap008_spine_20260427_111924.trx`**; summary **`.buildlogs/gap008_spine/last_run_summary.json`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → **`.buildlogs/verification/last_run.json`** |

Green coherence fixture: [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) (`effectiveFilter` = [filter](../../tools/gap008_mainwindow_regression_filter.txt) line 2, character-for-character).

## Changelog

- 2026-04-26: **Accepted** charter (**Tasks 252–253**).
- 2026-04-27: **Landed** — `MainWindowManageWorkspacesMenuItemShellBridge` + tests + filter + fixture + reconciliation (**Tasks 254–257**).
