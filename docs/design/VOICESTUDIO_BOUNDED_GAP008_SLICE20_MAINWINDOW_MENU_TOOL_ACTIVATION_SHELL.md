# GAP-008 Slice 20 — MainWindow **menu / tool activation** shell (bounded)

**Status:** Accepted (Tasks 449–458)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

## First seam (exact)

**`MainWindowMenuToolActivationShellBridge`** owns **only** the **shell** path for **one** cluster of **menu / toolbar-adjacent activations** that today live as **`async void` / `void` handlers** on **`MainWindow`** (and **`ManageWorkspaces_Click`** on the **`MainWindow`** partial in **`MainWindow.Workspaces.cs`**):

| Handler (today) | Bridge entry |
| --- | --- |
| **`CheckForUpdatesMenuItem_Click`** | **`RunCheckForUpdatesAsync`** |
| **`ToggleMiniTimelineMenuItem_Click`** (+ **`UpdateMiniTimelineMenuItem`** still on **`MainWindow`** as UI text sync) | **`RunToggleMiniTimelineAsync`** |
| **`CollaboratorsToggleButton_Click`** | **`ToggleCollaborationPanelVisibility`** |
| **`CollaborationIndicator_CloseRequested`** | **`HideCollaborationPanel`** |
| **`ManageWorkspaces_Click`** | **`RunManageWorkspacesAsync`** |

**Explicit non-goals:** **`KeyboardShortcutsMenuItem_Click`** — **Slice 21** only ([`MainWindowKeyboardShortcutsShellBridge`](../../src/VoiceStudio.App/Services/MainWindowKeyboardShortcutsShellBridge.cs); [Slice 21 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md)). **`CustomizeToolbarMenuItem_Click`** (Slice 7 bridge). **`StartupRetryButton_Click`**, command palette, tool catalog, search overlay, status strip, notification center, project workflow commands, **`ImportAudioFile`**, palette/catalog glue, **`RHVoice`**.

## Rejected alternative (vs MAINWINDOW “Next Slice”)

| Option | Why rejected for Slice 20 |
| --- | --- |
| **Keyboard shortcuts dialog stack** | **`ContentDialog`**, **`KeyboardCustomizationViewModel`**, nested dialogs — separate bounded slice; would violate **one cluster** rule. |
| **Status bar / metrics / coordinator** | Owned by Slices **17–19**; no duplication. |

## `KeyboardShortcutsMenuItem_Click` — **Slice 21** (supersedes Task 461 deferral)

**Decision:** **OUT** of **`MainWindowMenuToolActivationShellBridge`**. The dialog chain is a **separate bounded seam** — landed as [Slice 21 — keyboard shortcuts shell](VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md) with **`MainWindowKeyboardShortcutsShellBridge`** + **`Gap008Slice21Tests`**.

## IN / OUT table

| Cluster | IN / OUT |
| --- | --- |
| **`MainWindowMenuToolActivationShellBridge`** and the five mappings above | **IN** |
| **`KeyboardShortcutsMenuItem_Click`** | **OUT** — **`MainWindowKeyboardShortcutsShellBridge`** ([Slice 21](VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md)); **`MainWindow`** thin forward only |
| **`CustomizeToolbarMenuItem_Click`**, **`MainWindowToolbarCustomizationShellBridge`** | **OUT** |
| **`StartupRetryButton_Click`**, startup overlay | **OUT** |
| Command palette, tool catalog, search overlay, jump list, file activation, workflow bridges | **OUT** |
| **`engines/audio/rhvoice/`** | **OUT** — **frozen**; no engine work without operator prerequisites |

## Dependency / blast-radius map (Tasks 460, 469)

| Responsibility | Current owner | Target owner | Services / deps | Async / UI | Overlap prior slices | Risks | Required tests |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Check for updates | **`MainWindow.CheckForUpdatesMenuItem_Click`** | **`RunCheckForUpdatesAsync`** | **`IViewModelContext`**, **`IUpdateService`**, **`UpdateViewModel`**, **`UpdateDialog`**, **`IErrorDialogService`** | UI thread **`ShowAsync`** | None | Missing error dialog path | **`Gap008Slice20Tests`**, **`MainWindowMenuToolActivationShellBridgeTests`** |
| Mini timeline toggle | **`MainWindow.ToggleMiniTimelineMenuItem_Click`** | **`RunToggleMiniTimelineAsync`** | **`OpenPanelByIdAsync`**, **`PanelHost`**, toast, **`_isMiniTimelineVisible`** (state stays on **`MainWindow`**) | **`async`** panel open | **5** nav bridge | Wrong panel id order | Source pins + delegation |
| Collaboration panel show/hide | **`MainWindow`** handlers | **`ToggleCollaborationPanelVisibility`** / **`HideCollaborationPanel`** | **`FindNameOnContent("CollaborationPanel")`** | Sync | None | Null host | Bridge null-safe + pins |
| Manage workspaces | **`MainWindow.Workspaces` `ManageWorkspaces_Click`** | **`RunManageWorkspacesAsync`** | **`WorkspaceManagerDialog`**, **`XamlRoot`**, toast | **`ShowAsync`** | None | **`XamlRoot`** null | Partial wiring pin |
| Menu item wire-up | **`MainWindow` ctor** (Click +=) | Unchanged targets; bodies forward | Same | — | **19** unrelated | Accidental re-wiring to wrong handler | **`Gap008Slice20Tests`** |

## RHVoice (Task 458)

**Zero** edits under **`engines/audio/rhvoice/`**. Creep tests: **`MainWindowMenuToolActivationShellBridge.cs`** must **not** contain the **`rhvoice`** path segment or unrelated **`MainWindow*ShellBridge`** type names (operator gate unchanged).

## Not an extension bucket (Task 457 / anti-sprawl)

This bridge is **not** a home for palette, catalog, shortcuts, startup retry, or status wiring. **Slice 19** warned against accretion into **`MainWindowStatusBarCoordinatorShellBridge`** (metrics, notification center, or unrelated menu glue creeping into coordinator shell wiring). The same discipline applies here: **one story** — **menu/tool activation shell** for the five handlers only. Reject shortcuts, status strip, or coordinator internals without a new bounded brief.

## Historical briefs

Slices **1–19** briefs are **not** rolling scoreboards for spine **N**. For membership and count, use **links only** (no full OR-filter duplication in briefs):

- Filter file: [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt)
- Script: [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1)
- Local summary: **`.buildlogs/gap008_spine/last_run_summary.json`**
- Reconciliation: [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md)

## Acceptance criteria

1. The five handlers delegate shell work through **`MainWindowMenuToolActivationShellBridge`**; **`KeyboardShortcutsMenuItem_Click`** forwards through **`MainWindowKeyboardShortcutsShellBridge`** only ([Slice 21](VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md)), not this bridge.
2. **`Gap008Slice20Tests`** + **`MainWindowMenuToolActivationShellBridgeTests`**; canonical filter strict superset (prepend Slice 20 tokens).
3. **`MainWindow`** shrinks **only** for this cluster; no palette/catalog/status/RHVoice creep.

## Verification

**Closure evidence (2026-04-26):** **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`** — **0 Error(s)**; targeted **`Gap008Slice20Tests`** + **`MainWindowMenuToolActivationShellBridgeTests`** — **Passed: 15** / **Failed: 0**; **`.\scripts\Run-Gap008MainWindowRegressionTests.ps1`** — **Passed: 161** / **`listedTestCount: 161`** → **`.buildlogs/gap008_spine/last_run_summary.json`** (timestamp **`2026-04-26T01:00:50Z`**; TRX **`gap008_spine_20260425_195929.trx`**); **`python scripts/run_verification.py`** — **Overall: PASS** (`.buildlogs/verification/last_run.json`). Spine membership and count authority: [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) § **Spine size after Slice 20**; filter [`tools/gap008_mainwindow_regression_filter.txt`](../../tools/gap008_mainwindow_regression_filter.txt); script [`scripts/Run-Gap008MainWindowRegressionTests.ps1`](../../scripts/Run-Gap008MainWindowRegressionTests.ps1). CI: green-contract coherence for `listedTestCount` vs TRX `passed` in [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) (Task 453).

## Slice 21 (landed)

[VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md](VOICESTUDIO_BOUNDED_GAP008_SLICE21_MAINWINDOW_KEYBOARD_SHORTCUTS_SHELL.md) — keyboard shortcuts menu flow; spine count post–Slice 21: [GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md](../reports/verification/GAP008_MAINWINDOW_SPINE_COUNT_RECONCILIATION.md) § **Spine size after Slice 21**.
