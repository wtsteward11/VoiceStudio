# GAP-008 Slice 28 — MainWindow Help / About shell (bounded)

**Status:** Accepted (Tasks 97–98 — Path G1)  
**Date:** 2026-04-26  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md)

> **Disambiguation:** The **`GAP008` infix** and **“MainWindow”** in the filename distinguish this **WinUI MainWindow** slice from other repository “Slice 28” work (e.g. [Parakeet TTS bounded Slice 28](VOICESTUDIO_BOUNDED_SLICE28_PARAKEET_READINESS.md) in [CANONICAL_REGISTRY.md](../governance/CANONICAL_REGISTRY.md) *Bounded Slices*).

## Path decision (one sentence)

**GAP-008 continues with Slice 28** on seam **Help menu: open local documentation folder in Explorer** and **About VoiceStudio** `ContentDialog` (version + third-party license link); **Path G1**; umbrella **not** closed.

## Goal

Move **`OpenDocumentationFolder`** and **`ShowAboutDialog`** implementation from [`MainWindow.xaml.cs`](../src/VoiceStudio.App/MainWindow.xaml.cs) into **`MainWindowHelpAboutShellBridge`**. [`MainWindow.Menu.cs`](../src/VoiceStudio.App/MainWindow.Menu.cs) continues to wire **Help** → the same `MainWindow` private methods, which become **one-line** forwards to the bridge. **Edit** menu **Undo/Redo** (`ExecuteUndo` / `ExecuteRedo`) **remain on `MainWindow`** for this slice — not Help/About.

**Deferred:** Third-party license **content** changes; any **backend** or **synthesis** path; **IBackendClient** surface; **menu bar structure** beyond thin forwards.

## IN / OUT

| IN | OUT |
|----|-----|
| **`OpenDocumentationFolder`**: resolve `docs` path from `VOICESTUDIO_REPO_ROOT` or `AppContext.BaseDirectory`; `explorer.exe` launch; user toasts on missing folder / failure | **RHVoice** / `engines/audio/rhvoice/`; **IBackendClient** / HTTP; **synthesis** |
| **`ShowAboutDialog`**: `Package` version string; `ContentDialog` + `StackPanel` + `HyperlinkButton` to repo **THIRD_PARTY_LICENSES.md**; **XamlRoot** from host `Content` | **CI verify-harness** GOV row or [closure narrative](../reports/verification/VOICESTUDIO_CI_VERIFY_HARNESS_FIRST_RUN_2026-04-14.md) **edits** (Task 95); **KeyTip** / accelerator work |
| **One bridge:** `MainWindowHelpAboutShellBridge` | **MainWindowMenuToolActivationShellBridge** (Slice 20), **MainWindowKeyboardShortcutsShellBridge** (Slice 21) **logic moves** — this slice is **only** Help folder + About |
| | `ExecuteUndo` / `ExecuteRedo` (Edit); **panel / nav / search** shells |

## One bridge class name

**`MainWindowHelpAboutShellBridge`**

## Dependency map (Task 98 — evidence from `MainWindow` + `MainWindow.Menu.cs`)

| Symbol / surface | Role |
|------------------|------|
| [`MainWindow.Menu.cs`](../../src/VoiceStudio.App/MainWindow.Menu.cs) L187–188 | `CreateMenuItem("Documentation Folder", OpenDocumentationFolder)`; `CreateMenuItem("About VoiceStudio", ShowAboutDialog)` — **wiring unchanged** (still private methods on `MainWindow`) |
| **`OpenDocumentationFolder`** (was in `MainWindow.xaml.cs`) | **Now:** `MainWindow` → `_helpAboutShellBridge.OpenDocumentationFolder(...)` with **lambdas** for env, `AppContext.BaseDirectory`, toasts, `Directory.Exists`, `Process.Start` |
| **`ShowAboutDialog`** (async `void`, was in `MainWindow.xaml.cs`) | **Now:** `await _helpAboutShellBridge.ShowAboutDialogAsync(getXamlRoot, ...)` — `XamlRoot` from `(Content as FrameworkElement)?.XamlRoot` |
| **Downstream** | `IErrorLoggingService` (optional log); `IToastNotificationService` (user messages); `System.Diagnostics.Process` (Explorer); `Windows.ApplicationModel.Package` (version) |
| **Async / UI** | `ShowAboutDialogAsync` is **async**; must run when **`XamlRoot` non-null** (post-`Loaded` policy unchanged); **no** ctor fire-and-forget |
| **Must not call into (anti-creep — by type name in bridge file):** | `MainWindowMenuToolActivationShellBridge`, `MainWindowNavigationShellBridge`, `MainWindowSearchOverlayShellBridge`, `IBackendClient` |

| Overlap (Slices 22–27) | This slice does **not** re-touch panel preview, dock, quick-switch, region focus, recent projects menu, or startup overlay — only Help entries above. |
| **Deferred (explicit)** | Undo/Redo; transport playback; import; project workflow; keyboard shortcuts table; check-for-updates (Slice 20) |

## Anti-sprawl (guardrail alignment)

[MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md): one **Help** story (folder + About) only; `MainWindow` remains the **composition** point for `ServiceProvider` and `XamlRoot` capture.

## Acceptance

- `MainWindow.xaml.cs` contains **no** multi-line **Help** / **About** business logic; **no** `Process.Start` for docs path **in `MainWindow`**; **no** `ContentDialog` construction for About **in `MainWindow`**.
- **`Gap008Slice28Tests`** + **`MainWindowHelpAboutShellBridgeTests`**; [filter](../../tools/gap008_mainwindow_regression_filter.txt) **prepend-only**; full spine **green**; **`tests/ci/test_gap008_spine_summary_shape.py`** **green**.

## Verification (observed green — 2026-04-26)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | **0 errors** (pre-existing warnings elsewhere) |
| `dotnet test` … filter `Gap008Slice28` + `MainWindowHelpAboutShellBridge` | **5/5** Passed |
| `.\scripts\Run-Gap008MainWindowRegressionTests.ps1` | **217/217** Passed, `listedTestCount` **217**; TRX `.buildlogs/gap008_spine/gap008_spine_20260426_121432.trx`; `last_run_summary.json` |
| `python -m pytest tests/ci/test_gap008_spine_summary_shape.py -q` | **4 passed** |
| `python scripts/run_verification.py` | **Overall: PASS** (`.buildlogs/verification/last_run.json`) |
| `.\scripts\verify.ps1 -Quick` (optional) | Not run this session (spine + `run_verification.py` sufficient) |

## RHVoice / CI freeze (Tasks 95–96, 104)

No edits under **`engines/audio/rhvoice/`**; no GOV **verify-harness** closure rewrites in this change set; no **engine** “Slice 28 / Parakeet” brief (registry disambiguation).

## Task 88 (Path G2)

**Not** applicable — this slice is **G1**; **Path G2** (umbrella close) is out of scope for this charter.

## Changelog

- **2026-04-26 — Accepted:** Help menu documentation folder + About `ContentDialog` → `MainWindowHelpAboutShellBridge` (Task 97–98); Task 99 tests before production bridge (Task 100).
