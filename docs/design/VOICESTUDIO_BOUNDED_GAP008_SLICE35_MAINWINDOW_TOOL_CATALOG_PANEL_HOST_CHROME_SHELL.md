# GAP-008 Slice 35 — MainWindow tool catalog panel host chrome shell (bounded)

**Status:** Accepted (Tasks 169–178)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 35”** row in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (non–MainWindow numeric slices).

## Path decision (one sentence)

**GAP-008 continues on Path G1** with **Slice 35** on the **tool catalog → panel host chrome** application (title + icon on the target **`PanelHost`** after **`MainWindowToolCatalogShellBridge.RunShowAsync`** opens a panel); **umbrella GAP-008 is not closed** (not Path G2). The **next** seam after this = **36+** with an **Accepted** `VOICESTUDIO_BOUNDED_GAP008_SLICE36_*.md`.

## Goal

**`ApplyToolCatalogPanelHostChrome`** lived on **`MainWindow`** as the **`applyPanelHostChrome`** callback wired into **`MainWindowToolCatalogShellBridge.WireToolCatalogHandlers`**. It only resolves **`LeftPanelHost` / `CenterPanelHost` / `RightPanelHost` / `BottomPanelHost`** by **`PanelRegion`** and sets **`PanelTitle`** / **`PanelIcon`**.

Move that logic to **`MainWindowToolCatalogPanelHostChromeShellBridge`**. **`MainWindowToolCatalogShellBridge` (Slice 10)** keeps **`RunShowAsync`**, **`WireToolCatalogHandlers`**, and catalog dialog ownership — this slice only owns **which host** gets the chrome and **the assignment** (narrow seam).

**Deferred (explicit):** full **`RegisterKeyboardShortcuts`** (would pull half the shell), **Slice 34** menu bar, **Slice 33** splitters, **import** / **Mica** / **transport** — **out**.

## IN / OUT

| IN | OUT |
|----|------|
| **`Apply(PanelRegion, string title, string? icon, Func<string, object?> findNameOnContent)`** — host resolution + title/icon | **RHVoice** / `engines/audio/rhvoice/` |
| **`findNameOnContent`** same contract as **`MainWindow.FindNameOnContent`** (Slice composition) | **CI verify-harness** GOV row rewrites without new hosted `workflow_dispatch` + evidence (Tasks **95–96** / **104** / **112** / **122** / **133** |
| **Wired from** **`MainWindow` ctor** immediately before/after existing **`_toolCatalogShellBridge.WireToolCatalogHandlers`** (order: construct chrome bridge → pass **`(r,t,i) => _toolCatalogPanelHostChromeShellBridge.Apply(r,t,i, FindNameOnContent)`**) | **Task 103** / **113** / **123** / **134** / **148** / **158** / **168** — not spine gates |
| **Slice 10** remains owner of **tool catalog dialog** and **open panel** orchestration | **[VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md](../reports/verification/VOICESTUDIO_RUNTIME_TRUTH_LANE_2026-04-26.md)** churn in this batch |

## One bridge class name

**`MainWindowToolCatalogPanelHostChromeShellBridge`**

## Dependency map (Task 172)

| Area | Detail |
|------|--------|
| **Entrypoint** | **`MainWindow` ctor** — `new MainWindowToolCatalogPanelHostChromeShellBridge()`; **`WireToolCatalogHandlers` second arg** from **`ApplyToolCatalogPanelHostChrome` method** → **lambda** calling **`Apply`**. |
| **Services** | None. **`Func<string, object?>`** injected per call ( **`FindNameOnContent`** ). |
| **Async / ADR-047** | Synchronous; runs on call stack of **`RunShowAsync`** (UI thread after dialog). **No** new ctor async. |
| **Side effects** | Mutates **`PanelTitle`** / **`PanelIcon`** on a **`PanelHost`**; **no** toasts, **no** new dialogs. |
| **Overlap with existing bridges** | **`MainWindowToolCatalogShellBridge`**: still calls the injected apply delegate — implementation moves out of **`MainWindow`**, not out of the Slice 10 class. **`MainWindowNavigationShellBridge`**: **no** `OpenPanelById` change. |
| **Explicitly OUT** | Adding **`Apply`** body **into** **`MainWindowToolCatalogShellBridge.cs`** in this batch (separate new bridge file per GAP-008 one-slice / one-brief pattern). Revisit only if a later ADR unifies "tool catalog" as one class (not this slice). |
| **Rejected alternatives (not Slice 35)** | **`RegisterKeyboardShortcut` mass extraction** (crosses 12+ bridges). **`SwitchToPanel` obsolete** path — dead; no revival. **Smoke env helpers** (`IsGateCSmokeMode`) — different concern. |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — this file **only** implements **PanelHost** title/icon for the **tool catalog** completion path. **Do not** absorb **command palette** chrome, **search overlay**, or **arbitrary** panel open paths.

## Alternatives not Slice 35

- **Merge into `MainWindowToolCatalogShellBridge` only** — rejected: blurs **Slice 10** charter; new class keeps reconciliation/test tokens explicit.

## Acceptance

- **`MainWindow`**: no **`private void ApplyToolCatalogPanelHostChrome`**; **`_toolCatalogPanelHostChromeShellBridge`** field; **`WireToolCatalogHandlers`** uses **`Apply`**.  
- **`Gap008Slice35Tests` + `MainWindowToolCatalogPanelHostChromeShellBridgeTests`**, [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend**; full spine **green**; [`tests/ci/test_gap008_spine_summary_shape.py`](../../tests/ci/test_gap008_spine_summary_shape.py) **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings only) |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **262/262** Passed, **listedTestCount** **262**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_161143.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |

## Changelog

- 2026-04-26: **Tasks 169–178** — charter; bridge **`MainWindowToolCatalogPanelHostChromeShellBridge`**.
