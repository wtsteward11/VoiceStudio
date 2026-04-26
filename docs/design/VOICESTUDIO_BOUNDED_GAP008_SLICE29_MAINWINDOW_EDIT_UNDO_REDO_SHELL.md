# GAP-008 Slice 29 — MainWindow Edit — Undo / Redo shell (bounded)

**Status:** Accepted and landed (Tasks 105–113 — Path G1)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from any other **“Slice 29”** in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (e.g. non–MainWindow bounded work).

## Path decision (one sentence) — Task 105

**GAP-008 continues with Slice 29** on seam **Edit → Undo / Redo** (menu + **the same** Ctrl+Z / Ctrl+Y shortcut handlers); **Path G1**; **umbrella not closed**.

## Goal

Move **`ExecuteUndo`**, **`ExecuteRedo`**, and the **inline** `edit.undo` / `edit.redo` keyboard registration bodies from [`MainWindow.xaml.cs`](../src/VoiceStudio.App/MainWindow.xaml.cs) into **`MainWindowEditUndoRedoShellBridge`**. [`MainWindow.Menu.cs`](../src/VoiceStudio.App/MainWindow.Menu.cs) **unchanged** (still `CreateMenuItem("Undo", ExecuteUndo)` / `ExecuteRedo` on `MainWindow` private methods that become one-line **forwards**).

**Deferred (explicit):** Deeper `UndoRedoService` implementation; **IUndoableAction** design; other Edit menu items; **transport** zoom/playback; **IBackendClient**; any **new** global shortcut beyond routing existing edit.undo/edit.redo through the bridge.

## IN / OUT

| IN | OUT |
|----|------|
| **`ExecuteUndo` / `ExecuteRedo`**: `ServiceProvider.GetUndoRedoService()` → `CanUndo` / `CanRedo` → `Undo()` / `Redo()`; `try`/`catch`; **`IErrorLoggingService.LogError`** on failure | **RHVoice** / `engines/audio/rhvoice/`; **synthesis**; **IBackendClient** / HTTP |
| **Keyboard `RegisterKeyboardShortcuts`:** **`edit.undo`**, **`edit.redo`** actions call **`MainWindowEditUndoRedoShellBridge`** (same as menu) — no duplicate `GetUndoRedoService` **bodies** in `MainWindow` | **CI verify-harness** GOV row rewrites, [closure-only narrative](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md) edits without fresh hosted **workflow_dispatch** evidence (Tasks **95–96**) |
| **One bridge:** `MainWindowEditUndoRedoShellBridge` | Merging with **`MainWindowKeyboardShortcutsShellBridge`** (Slice 21) **dialog** stack; **Help/About** (Slice 28); **menu tool activation** (Slice 20) |
| | **Task 103** (optional WinUI runtime report append) — **not** a merge gate; **not** a spine prerequisite |

## One bridge class name

**`MainWindowEditUndoRedoShellBridge`**

## Dependency map (Task 107)

| Symbol / surface | Role |
|------------------|------|
| [MainWindow.Menu.cs](../../src/VoiceStudio.App/MainWindow.Menu.cs) L62–63 | `CreateMenuItem("Undo", ExecuteUndo)`; `CreateMenuItem("Redo", ExecuteRedo)` — **wiring unchanged** |
| **`ExecuteUndo` / `ExecuteRedo`** ([MainWindow.xaml.cs](../../src/VoiceStudio.App/MainWindow.xaml.cs) — pre-slice) | **After:** one-line `MainWindow` **forwards** to `_editUndoRedoShellBridge.ExecuteUndo(...)` / `ExecuteRedo(...)` with **`getUndo`**: `() => ServiceProvider.GetUndoRedoService()`; **`logError`**: `ServiceProvider.TryGetErrorLoggingService()` → `LogError` (same shape as pre-extraction) |
| **`RegisterKeyboardShortcuts`**: **`edit.undo`**, **`edit.redo`** ([MainWindow.xaml.cs](../../src/VoiceStudio.App/MainWindow.xaml.cs) ~L979–1018) | **After:** `try` body replaced by **same** bridge calls as menu (no inline `GetUndoRedoService` **blocks**) |
| **Downstream** | `UndoRedoService` ([UndoRedoService.cs](../../src/VoiceStudio.App/Services/UndoRedoService.cs)) — `CanUndo`, `CanRedo`, `Undo()`, `Redo()`; `IErrorLoggingService` (optional) via `ServiceProvider` |
| **Async / UI** | **Sync**; no `XamlRoot`; no ctor fire-and-forget; shortcuts registered in ctor after bridge construction |
| **Side effects** | Mutates app undo stacks; may log to error service |
| **Must not name in bridge (anti-creep by type / surface):** | `MainWindowHelpAboutShellBridge`, `MainWindowKeyboardShortcutsShellBridge` (dialog), `MainWindowMenuToolActivationShellBridge`, `IBackendClient`, `GlobalTransport` |

| Overlap (other slices) | This slice does **not** re-touch Slice **28** Help, **21** keyboard-shortcuts *dialog*, **20** other Tools menu; **not** `ImportAudioFile` / project workflow. |
| **Deferred (explicit)** | Unifying *all* keyboard shortcuts to bridges; `ErrorLogger` vs `IErrorLoggingService` global policy; batch undo UI |

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** **Edit/undo-redo** shell story: menu + the two **named** `KeyboardShortcutService` **edit** routes only. **No** new shortcuts in this file without a new bounded brief.

## Acceptance

- `MainWindow.xaml.cs` contains **no** multi-line **`ExecuteUndo` / `ExecuteRedo`** and **no** **inline** `GetUndoRedoService` **+** `Undo`/`Redo` in **`edit.undo` / `edit.redo`** registrations; behavior stays **try/catch** + log on failure.
- **`Gap008Slice29Tests`** + **`MainWindowEditUndoRedoShellBridgeTests`**; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `dotnet test` … `Gap008Slice29Tests\|MainWindowEditUndoRedoShellBridgeTests` | **7/7** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **224/224** Passed, **listedTestCount** **224**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_123447.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` (optional) | Not run this session |

## RHVoice / CI freeze (Tasks 95–96, 104, 112)

No edits under **`engines/audio/rhvoice/`**; no **GOV** verify-harness **closure** churn without new **hosted** evidence. **Path B** RHVoice unchanged.

## Task 88 (Path G2)

**Not** selected — this slice is **G1** only.

## Task 113 (optional)

**Task 103** human WinUI / runtime report append: **out of scope** for this slice; **not** a spine or merge requirement unless product **explicitly** schedules it.

## Changelog

- **2026-04-26 — Accepted:** Edit Undo/Redo shell → `MainWindowEditUndoRedoShellBridge` (Tasks 105–108); **Task 105** continue sentence above (no “29+” mush without decision).
