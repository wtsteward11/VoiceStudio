# GAP-008 Slice 23 — MainWindow nav rail panel preview shell (bounded)

**Status:** Accepted (Tasks 39–45)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

## Task 39 decision (one sentence)

**GAP-008 continues with Slice 23** on seam **nav rail pointer hover → `PanelPreviewPopup` + static preview copy** — one shell story; **Path G1**; umbrella **not** closed.

## Goal

Move **nav button pointer enter/exit**, **`PanelPreviewPopup`** lazy creation, hide **timer**, **`GetPanelInfoForButton`**, and **`CreatePreviewContent`** from **`MainWindow.xaml.cs`** into **`MainWindowPanelPreviewShellBridge`** so **`MainWindow`** only forwards **`NavButton_PointerEntered`** / **`NavButton_PointerExited`**; **`DisposePreviewHideTimer`** is invoked from **`MainWindowLifetimeCleanupShellBridge`** (unchanged channel, bridge target).

## IN / OUT

| IN | OUT |
|----|-----|
| Show/hide **`PanelPreviewPopup`** on **`ToggleButton`** hover with 300ms delayed hide on exit | **Execute navigation** / **`ExecuteNavCommand`** (stays **`MainWindowNavigationShellBridge`**) |
| Map **`NavStudio`** … **`NavLogs`** to panel metadata and bullet preview **`StackPanel`** (parity) | **Panel quick switch** popups, **keyboard** routing, **search overlay** |
| **`DispatcherQueue.TryEnqueue`** for timer callback (UI thread) | **RHVoice**, engine preflight, verify-harness GOV row or closure **edits** (Tasks **46–47**) |
| **CI / hosted dispatch** — **no** changes to frozen GOV row without new evidence | Absorbing **`MainWindowNavigationShellBridge`** or **`NavButtonActionSink`** into this type |

## Dependency map (Task 41)

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow.NavButton_PointerEntered` / `Exited`** | Thin forward → **`_panelPreviewShellBridge`** |
| **`MainWindow` ctor** | After **`_navShellBridge`**, **`new MainWindowPanelPreviewShellBridge(DispatcherQueue)`** |
| **`MainWindowClosedPreludeChannels.DisposePreviewHideTimer`** | **`() => _panelPreviewShellBridge.DisposePreviewHideTimer()`** |
| **`PanelPreviewPopup`** | Owned lazily by bridge |
| **`MainWindow.xaml`** | `PointerEntered` / `Exited` on nav toggles (unchanged) |

**Must not call into:** **`IShellNavigationCoordinator`**, **`OpenPanelByIdAsync`**, **`SearchOverlayCoordinator`**, **`CommandPaletteService`**.

**Async / UI:** Pointer hooks are **synchronous**; timer uses **`DispatcherQueue`** for **`Hide`**.

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) **`MainWindowNavigationShellBridge`** is **nav command + highlight** only — **panel preview** is a **separate** bounded type (no accretion into nav bridge).

## Acceptance

- **`MainWindow`** region **Panel Preview** contains **only** two one-line event forwards + **no** **`PanelPreviewPopup`** / **`GetPanelInfoForButton`** / **`CreatePreviewContent`**.
- **`Gap008Slice23Tests`** + **`MainWindowPanelPreviewShellBridgeTests`**; filter **prepend-only**; full spine green; **`test_gap008_spine_summary_shape.py`** green.

## Verification (post-merge)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
.\scripts\Run-Gap008MainWindowRegressionTests.ps1
python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q
python scripts/run_verification.py
```

## RHVoice / CI freeze

Per **Tasks 46–47**: no **`engines/audio/rhvoice/`** matrix theater; no closed verify-harness GOV / **STATE** churn for that row without new hosted **`workflow_dispatch`** + **`run_full_chain: true`**.
