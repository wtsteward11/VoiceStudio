# GAP-008 Slice 45 — MainWindow View → Toggle Mini Timeline menu item wiring shell (bounded)

**Status:** Accepted  
**Date:** 2026-04-27  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 45”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 45** moving **View → Toggle Mini Timeline** **`MenuFlyoutItem.Click`** wiring out of **`MainWindow`** into **`MainWindowToggleMiniTimelineMenuItemShellBridge`**; **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) **still owns** **`RunToggleMiniTimelineAsync`** (bottom panel + menu text + toast on failure); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 20** owns mini-timeline toggle flow. **`MainWindow`** declared **`private async void ToggleMiniTimelineMenuItem_Click`** forwarding to **`RunToggleMiniTimelineAsync`**. **Slice 45** extracts **only** that **menu item** **`Click`** wiring so **`MainWindow`** source has **no** **`ToggleMiniTimelineMenuItem_Click`**; **no** change to **`MainWindowMenuToolActivationShellBridge`** implementation beyond existing **`RunToggleMiniTimelineAsync`** contract.

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowToggleMiniTimelineMenuItemShellBridge`** — ctor captures **`MainWindowMenuToolActivationShellBridge`** plus **`Func`/`Action`** delegates matching **`RunToggleMiniTimelineAsync`**; **`OnToggleMiniTimelineMenuItemClick`** / **`RunFlowAsync`** delegate to **`RunToggleMiniTimelineAsync`** | **RHVoice** / `engines/audio/rhvoice/` matrix theater |
| **`MainWindow.xaml.cs`** — **`readonly`** field; ctor **`new`** **immediately after** **`MainWindowManageWorkspacesMenuItemShellBridge`** / **before** **`MainWindowKeyboardShortcutsShellBridge`**; **`_toggleMiniTimelineMenuItem.Click +=`** bridge handler | **CI verify-harness** GOV closure narrative edits |
| **Delete** **`ToggleMiniTimelineMenuItem_Click`** from **`MainWindow.xaml.cs`** | **Runtime-truth** lane doc edits unless product reopens |
| **`Gap008Slice45Tests`** + **`MainWindowToggleMiniTimelineMenuItemShellBridgeTests`**; prepend-only [filter](../../tools/gap008_mainwindow_regression_filter.txt) line 2 | **`MainWindowMenuToolActivationShellBridge`** (**Slice 20**) — **no** body edits |
| **`Gap008Slice20Tests`** pin — Toggle Mini Timeline delegation via menu item shell bridge, **not** `ToggleMiniTimelineMenuItem_Click` on **`MainWindow`** | **Collaboration** toggles, other menu items without brief |
| | **Engine / STT** numeric slice label collisions (registry disambiguation only) |

## One bridge class name

**`MainWindowToggleMiniTimelineMenuItemShellBridge`**

## Dependency map

| Bucket | Content |
|--------|---------|
| **Handlers / methods** | **`ToggleMiniTimelineMenuItem_Click`** (pre-slice): **`async void`** forwarding to **`RunToggleMiniTimelineAsync`** with visibility get/set, bottom **`PanelHost`**, **`OpenPanelByIdAsync`**, **`UpdateMiniTimelineMenuItem`**, toast service. **`RunToggleMiniTimelineAsync`** — **`MainWindowMenuToolActivationShellBridge.cs`**: existing public API unchanged. |
| **Wiring sites** | **`MainWindow.xaml.cs`** — **`_toggleMiniTimelineMenuItem`** in **Menu Items Created**; **`Click`** attaches to **`_toggleMiniTimelineMenuItemShellBridge.OnToggleMiniTimelineMenuItemClick`**. **`MainWindowMenuBarShellBridge`** passes flyout item only. |
| **Async boundaries** | **`OnToggleMiniTimelineMenuItemClick`** is **`async void`** (WinUI **`Click`**); forwards with **`ConfigureAwait(true)`** on **`RunFlowAsync`** (same pattern as **Slice 44**). |
| **Side effects** | Only those already inside **`RunToggleMiniTimelineAsync`**. |
| **Overlap** | **`Gap008Slice20Tests`** — update pin from **`ToggleMiniTimelineMenuItem_Click`** to shell-bridge wiring. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **`RunToggleMiniTimelineAsync`** into this bridge.

## Acceptance

- **`MainWindow`**: **no** **`ToggleMiniTimelineMenuItem_Click`**; **`_toggleMiniTimelineMenuItemShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice45Tests`** + **`MainWindowToggleMiniTimelineMenuItemShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line-2 prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.
- Reconciliation § *Spine size after Slice 45* + fixture **`listedTestCount`** / **`effectiveFilter`** match discovery.

## Verification

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (post-land) |
| `dotnet test … --filter "FullyQualifiedName~Gap008Slice45Tests\|FullyQualifiedName~MainWindowToggleMiniTimelineMenuItemShellBridgeTests"` | **13 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **354/354** Passed; TRX under **`.buildlogs/gap008_spine/`**; summary **`.buildlogs/gap008_spine/last_run_summary.json`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** → **`.buildlogs/verification/last_run.json`** |

Green coherence fixture: [`tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json`](../../tests/fixtures/gap008_spine/last_run_summary_green_listing_matches_trx.json) (`effectiveFilter` = [filter](../../tools/gap008_mainwindow_regression_filter.txt) line 2, character-for-character).

## Changelog

- 2026-04-27: **Accepted** charter (**Tasks 266** — STATE closure plan).
- 2026-04-27: **Landed** — `MainWindowToggleMiniTimelineMenuItemShellBridge` + tests + filter + fixture + reconciliation (**Task 267**).
