# GAP-008 Slice 31 — MainWindow import-audio workflow shell (bounded)

**Status:** Accepted (Tasks 124–134)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP-008` / `MAINWINDOW` infix** distinguishes this **WinUI `MainWindow`** slice from other **“Slice 31”** rows in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) (e.g. non–MainWindow bounded work).

## Path decision (one sentence) — Task 126 / continue cycle

**GAP-008 continues with Slice 31** on seam **import-audio shell gating**; **Path G1**; **umbrella GAP-008 is not closed**.

## Goal

Move **`ImportAudioFile`** from a multi-line body in [`MainWindow.xaml.cs`](../../src/VoiceStudio.App/MainWindow.xaml.cs) (public method used by **File** menu, **`file.import`** shortcut, and **Slice 9** `WireImportAudioHandler`) into **`MainWindowImportWorkflowShellBridge`**: startup readiness gate, resolve **`IImportWorkflowService`**, fire-and-forget **`ImportAudioFileAsync(IntPtr)`** with **`WindowNative.GetWindowHandle`**. **`MainWindow`** keeps a **one-line** expression-bodied forward with **`ServiceProvider` / `AppServices`** lambdas.

**Deferred (explicit):** `IImportWorkflowService` implementation (library upload, events, toasts inside service); **toolbar** XAML; **Slice 4** `MainWindowProjectWorkflowBridge` (New/Open/Save); **global transport** (**Slice 30**).

## IN / OUT

| IN | OUT |
|----|------|
| **Gate:** `IStartupStateService.IsReady`; if not ready → **info** toast (same strings as pre-slice) | **RHVoice** / `engines/audio/rhvoice/`; **synthesis** engines |
| **Invoke:** `AppServices.GetService<IImportWorkflowService>()`; null = no-op (same as pre-slice) | **CI verify-harness** GOV closure rewrites (Tasks **95–96** / **133**) |
| **Window:** `WinRT.Interop.WindowNative.GetWindowHandle` for file picker parent | **IBackendClient** as a **static dependency of the bridge** — bridge takes **func** delegates from `MainWindow` only |
| **Callers unchanged:** `MainWindow.Menu.cs` **Import Audio…**; `RegisterKeyboardShortcuts` **`file.import`**; **`MainWindowToolbarCommandShellBridge.WireImportAudioHandler(ImportAudioFile)`** | **Task 103** / **113** / **123** / **134** (optional WinUI / runtime appendix) — **not** spine gates |

## One bridge class name

**`MainWindowImportWorkflowShellBridge`**

## Dependency map (Task 128)

| Symbol / surface | Role |
|------------------|------|
| **`ImportAudioFile`** (`MainWindow`) | **After:** `_importWorkflowShellBridge.ImportAudioFile(getStartup, getImport, showInfo, getHwnd)` |
| **`IStartupStateService`** | **IN** via `Func<IStartupStateService>` — **IsReady** only for gate |
| **`IImportWorkflowService`** | **IN** via `Func<IImportWorkflowService?>` — **null** returns without call |
| **`IToastNotificationService`** | **IN** via `Action<string, string> showInfo` for “Starting…” path only |
| **Overlap** | **Slice 9** `MainWindowToolbarCommandShellBridge` still calls **`ImportAudioFile`** on `MainWindow` — **unchanged** contract; **Slice 4** `MainWindowProjectWorkflowBridge` — **OUT** (New/Open/Save) |
| **Async** | Bridge is **void**; service call **`_ = ImportAudioFileAsync`** — same as pre-slice (no new async state in bridge) |
| **Must not name in bridge (anti-creep):** | `MainWindowGlobalTransportShellBridge`, `MainWindowHelpAboutShellBridge`, `IBackendClient`, `HttpClient` |

## Anti-sprawl

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) — **one** import shell story. **No** opportunistic workspace, menu, or **Slice 30** transport edits in this slice.

## Acceptance

- `MainWindow.ImportAudioFile` is **expression-bodied** to the bridge only.
- `Gap008Slice31Tests` + `MainWindowImportWorkflowShellBridgeTests`; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** |
| `dotnet test` … `Gap008Slice31\|MainWindowImportWorkflowShellBridge` | **6/6** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **237/237** Passed, **listedTestCount** **237**; TRX **`.buildlogs/gap008_spine/gap008_spine_20260426_133328.trx`**; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` (Task 124) | **exit 0**; **Report:** `artifacts/verify/20260426_132526/verification_report.md` |

## RHVoice / CI freeze (Tasks 133, 95–96)

No **`engines/audio/rhvoice/`**; no **GOV** verify-harness closure churn. **Path B** RHVoice unchanged.

## Changelog

- 2026-04-26: **Tasks 124–134** — charter; bridge **`MainWindowImportWorkflowShellBridge`**. **Task 124** — Quick verify completed (`artifacts/verify/20260426_132526/`). **Task 125** — `HEAD` = `origin/main` = `756f3d712cfeeff53619abfb391896218725feb1` (reconciled live).
