# GAP-008 Slice 33 — MainWindow workspace grid splitter shell (bounded)

**Status:** Accepted (Tasks 149–158)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from other **“Slice 33”** rows in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 33** on seam **workspace grid splitters** (pointer capture + star column/row resize + debounced layout save on release); **umbrella GAP-008 is not closed** (not Path G2).

## Goal

Move **`WorkspaceSplitter_PointerPressed` / `PointerMoved` / `PointerReleased`** implementation from [`MainWindow.Workspaces.cs`](../../src/VoiceStudio.App/MainWindow.Workspaces.cs) into **`MainWindowWorkspaceSplitterShellBridge`**.  
**`MainWindow`** keeps **expression-bodied** one-line forwards.  
**`SaveWorkspaceLayout`** and **`Debouncer`** remain on **`MainWindow`**; the bridge receives **`Func<string, object?> findNameOnContent`** and **`Action` → `_layoutSaveDebouncer?.Invoke()`** on pointer release (same debounce story as pre-slice).

**Deferred (explicit):** **`InitializePanelsAsync`**, **`ResetToStudioWorkspace`**, **`RestorePanelsFromLayoutAsync`**, full workspace **profile** orchestration, **Slice 25** panel dock, **Slice 27** region focus, **Slice 1** bootstrap, **Task 103** / **113** / **123** / **134** / **148** optional appendices.

## IN / OUT

| IN | OUT |
|----|------|
| **`Func<string, object?>`** name resolution for **`WorkspaceGrid`**, **`LeftColumn`**, **`CenterColumn`**, **`RightColumn`**, **`TopRow`**, **`BottomRow`**; **`Action`** debounced save on **pointer release** | **RHVoice** / `engines/audio/rhvoice/` |
| **Splitters** **`VerticalSplitter1`**, **`VerticalSplitter2`**, **`HorizontalSplitter`** (name match) + **star** math **`MinStarValue` 0.5** | **CI verify-harness** GOV row rewrites (Tasks **95–96** / **104** / **112** / **122** / **133**) — **out** of spine |
| **Overlap:** **`MainWindowPanelDockShellBridge`** **layout save** on dock remains separate — this slice **only** pointer splitter drag | **IBackendClient**, import, transport, shell chrome (**Slice 32**), Mica/title bar |
| | **Task 103** / **113** / **123** / **134** / **148** — **not** spine gates |

## One bridge class name

**`MainWindowWorkspaceSplitterShellBridge`**

## Dependency map

| Symbol / surface | Role |
|------------------|------|
| **`MainWindow` ctor** | **`new MainWindowWorkspaceSplitterShellBridge(FindNameOnContent, () => _layoutSaveDebouncer?.Invoke())`** immediately after **`_layoutSaveDebouncer = new Debouncer(...)`** |
| **`MainWindow.Workspaces.cs`** | **`WorkspaceSplitter_*`** → one-line **`_workspaceSplitterShellBridge.OnPointer*`** only |
| **XAML** | Unchanged: splitter **`PointerPressed` / `PointerMoved` / `PointerReleased`** on **`VerticalSplitter1`**, **`VerticalSplitter2`**, **`HorizontalSplitter`** (wired in `MainWindow.xaml`) |
| **UI thread** | Handlers run on **UI** thread (WinUI input); **no** new async from ctor (**ADR-047**) |
| **Side effects** | Updates **`ColumnDefinition` / `RowDefinition`** **GridLength** (star); **`CapturePointer` / `ReleasePointerCapture`**; **debounced** **`SaveWorkspaceLayout`** on release only |
| **Must not name in bridge (anti-creep):** | `MainWindowShellChromeShellBridge`, `MainWindowImportWorkflowShellBridge`, `IBackendClient` |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** workspace **splitter pointer** story. **Do not** re-open **Slice 32** chrome, **Slice 31** import, or move **panel init** / **workspace reset** into this bridge without a new bounded brief.

## Alternatives not Slice 33

- **`MainWindow.Smoke.cs`** (Gate C) — different product story; larger blast radius.  
- **Full `MainWindow.Workspaces.cs` extraction** (restore/save/panels) — multiple seams; not one bounded slice.

## Acceptance

- **`MainWindow.Workspaces.cs`**: **only** thin **`WorkspaceSplitter_*`** forwards to **`_workspaceSplitterShellBridge`**.  
- **`Gap008Slice33Tests` + `MainWindowWorkspaceSplitterShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend** new tokens, full spine **green**, **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings only) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **247/247** Passed, **listedTestCount** **247**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_152834.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |

## Changelog

- 2026-04-26: **Tasks 149–158** — charter; bridge **`MainWindowWorkspaceSplitterShellBridge`**.
