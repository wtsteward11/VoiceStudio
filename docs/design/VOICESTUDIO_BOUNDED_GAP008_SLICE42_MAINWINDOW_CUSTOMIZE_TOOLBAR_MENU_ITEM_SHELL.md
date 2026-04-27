# GAP-008 Slice 42 — MainWindow View → Customize Toolbar… menu item wiring shell (bounded)

**Status:** Accepted (Task 246)  
**Date:** 2026-04-27  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 42”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 42** moving **View → Customize Toolbar…** **`MenuFlyoutItem.Click`** wiring out of **`MainWindow`** into **`MainWindowCustomizeToolbarMenuItemShellBridge`**; **`MainWindowToolbarCustomizationShellBridge`** (**Slice 7**) **still owns** **`ShowCustomizationDialogAsync`** (dialogs / ADR-047); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

**Slice 7** owns toolbar customization dialog launch. **`MainWindow`** still declared **`private async void CustomizeToolbarMenuItem_Click`** forwarding to **`_toolbarCustomizationShellBridge.ShowCustomizationDialogAsync`**. **Slice 42** extracts **only** that **menu item** wiring so **`MainWindow`** has **no** named handler for this item; **no** change to **`MainWindowToolbarCustomizationShellBridge`** behavior.

## IN / OUT

| IN | OUT |
|----|------|
| **`MainWindowCustomizeToolbarMenuItemShellBridge`** — ctor captures **`MainWindowToolbarCustomizationShellBridge`**; **`OnCustomizeToolbarMenuItemClick`** (async void for **`RoutedEventHandler`**) and **`RunFlowAsync`** delegate to **`ShowCustomizationDialogAsync`** | **RHVoice** / `engines/audio/rhvoice/` |
| **`MainWindow`** — **`readonly`** field **`_customizeToolbarMenuItemShellBridge`**; ctor **`new`** **immediately after** **`MainWindowToolbarCustomizationShellBridge Created`** / **before** **`MainWindowCommandPaletteShellBridge`**; **`_customizeToolbarMenuItem.Click +=`** bridge handler; **delete** **`CustomizeToolbarMenuItem_Click`** | **CI verify-harness** GOV row rewrites |
| **`Gap008Slice7Tests`** — file pins updated so delegation is asserted via menu-item bridge + **`MainWindowCustomizeToolbarMenuItemShellBridge`** source | **`MainWindowToolbarCustomizationShellBridge`** (**Slice 7**) — **no** body edits |
| | **Keyboard shortcuts** (**Slice 41**) — **no** merge |
| | **Other menu items** — **OUT** |

## One bridge class name

**`MainWindowCustomizeToolbarMenuItemShellBridge`**

## Dependency map

| Bucket | Content |
|--------|---------|
| **MainWindow** | **Delete** **`CustomizeToolbarMenuItem_Click`**. **`_customizeToolbarMenuItem.Click +=`** → **`_customizeToolbarMenuItemShellBridge.OnCustomizeToolbarMenuItemClick`**. **Field** after **`_toolbarCustomizationShellBridge`**. **Ctor:** **`_customizeToolbarMenuItemShellBridge = new MainWindowCustomizeToolbarMenuItemShellBridge(_toolbarCustomizationShellBridge)`** after toolbar customization bridge / **before** command palette. |
| **Consumers** | **`MainWindowToolbarCustomizationShellBridge`** — sole dialog path (**Slice 7**). |
| **Async / ADR-047** | **`OnCustomizeToolbarMenuItemClick`** remains **`async void`**; **`ConfigureAwait(true)`** on **`RunFlowAsync`** chain. |
| **Overlaps** | **Slice 7** — dialog + toast on failure. **Slice 42** — **menu `Click`** wiring only. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one seam per brief**. **Do not** absorb **Slice 7** dialog implementation.

## Acceptance

- **`MainWindow`**: **no** **`CustomizeToolbarMenuItem_Click`**; **`_customizeToolbarMenuItemShellBridge`**; ctor ordering per dependency map.
- **`Gap008Slice42Tests` + `MainWindowCustomizeToolbarMenuItemShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **line-2 prepend**; **`Gap008Slice7Tests`** pins aligned; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `dotnet test … --filter "FullyQualifiedName~Gap008Slice42Tests\|FullyQualifiedName~MainWindowCustomizeToolbarMenuItemShellBridgeTests"` | **9 passed** |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **320/320** Passed; TRX **`.buildlogs/gap008_spine/gap008_spine_20260427_094459.trx`** |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |

## Changelog

- 2026-04-27: **Task 246** — **Accepted** charter + land **`MainWindowCustomizeToolbarMenuItemShellBridge`**; spine **320/320**; **`Gap008Slice7Tests`** delegation pins updated.
