# GAP-008 Slice 1 — MainWindow Loaded shell bootstrap (bounded)

**Status:** Accepted (slice 1 landed in repo)  
**Date:** 2026-04-24  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008** — MainWindow decomposition  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md) (changelog + next-slice wording aligned with this brief)

## First seam (exact)

**Loaded-time shell bootstrap orchestration** — the ordered block that ran inline in [`MainWindow.xaml.cs`](../../src/VoiceStudio.App/MainWindow.xaml.cs) inside `contentFE.Loaded` from **`ErrorDialogService.Root = contentFE.XamlRoot`** through **`InitializeCustomTitleBar()`** (inclusive), **excluding**:

- `#if DEBUG` diagnostics and pointer handler (remain in `MainWindow` — local diagnostic coupling).
- **`_transportShortcutCoordinator` attach** and **`RunPanelInitWhenReadyAsync`** — [GAP-008 Slice 3](VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md) (`MainWindowLoadedTailBootstrap`); not part of this cut.

**Implementation:** [`MainWindowShellLoadedBootstrap`](../../src/VoiceStudio.App/Services/MainWindowShellLoadedBootstrap.cs) + [`MainWindowLoadedBootstrapHooks`](../../src/VoiceStudio.App/Services/MainWindowShellLoadedBootstrap.cs) — `RunAsync` is **only** legal from **`FrameworkElement.Loaded`** (ADR-047).

## What stays in `MainWindow`

- XAML and **window** chrome: `MainWindow.xaml`, title bar, Mica **entrypoints** (`ApplyMicaBackdrop` / `InitializeCustomTitleBar` **methods** stay as partial members; **invocation order** is delegated).
- Constructor: service fields, coordinator construction, `Activated`, menu/splitter, `StartupState` overlay subscription **before** Loaded.
- DEBUG-only Loaded tail; post-DEBUG transport attach + `RunPanelInitWhenReadyAsync` trigger ([Slice 3](VOICESTUDIO_BOUNDED_GAP008_SLICE03_MAINWINDOW_LOADED_TAIL.md)).
- All **private** `Wire*` / `TryDispatch*` **implementations** (hooks call them via delegates constructed in `MainWindow`).

## What moves out

Only the **orchestration sequence** (call order + `await` boundaries) into `MainWindowShellLoadedBootstrap.RunAsync`:

1. Set `ErrorDialogService.Root` from `contentFE.XamlRoot`
2. `WireNotificationCenter`
3. `WireJumpListShell` / `WireTaskbarProgressShell`
4. `TryDispatchPendingJumpListActivation` / `TryDispatchPendingFileActivation`
5. `StartBackendHealthMonitoring` on status bar coordinator
6. Low-priority dispatcher enqueue: recent projects load + menu populate
7. `_sessionLifecycle.AttachRecoveryHandlers`
8. Theme `InitializeAsync(contentFE)`
9. Keyboard shortcut `InitializeAsync`
10. `ApplyMicaBackdrop` + `InitializeCustomTitleBar`

## No-expansion rule (OUT OF SCOPE for slice 1)

- Navigation graph / `OpenPanelByIdAsync` refactors (slice 2 candidate per decomposition plan).
- Dialog service redesign, backend routes, engine manifests, **`engines/audio/rhvoice/`**, verify/`engine_truth` churn, Slice 27 / `whisper_cpp` proof edits.
- Merging DEBUG diagnostics into the bootstrap class.
- Changing **relative order** of the ten steps without ADR + test update.

## Supersedes stale decomposition “next slice” line

[`MAINWINDOW_DECOMPOSITION_PLAN.md`](MAINWINDOW_DECOMPOSITION_PLAN.md) listed **“Next Slice: Navigation-Shell Behavior.”** **ShellNavigationCoordinator** already exists; the **Loaded** lambda remained a high-coupling orchestration hotspot. Slice 1 **cuts that orchestration** first; **navigation-shell extraction** remains the **next planned slice** after slice 1 is **merged** (see decomposition plan changelog).

## Dependency / blast-radius map (Task 254)

| Responsibility | Current owner | Target owner after slice 1 | Risk |
|----------------|---------------|------------------------------|------|
| Error dialog XamlRoot | Loaded inline | `MainWindowShellLoadedBootstrap` | L |
| Notification / jump list / taskbar / file activation wiring | Loaded inline | Same (via hooks) | M |
| Status bar backend monitoring start | Loaded inline | Same | M |
| Recent projects menu refresh | Loaded inline | Same | L |
| Crash recovery attach | Loaded inline | Same (calls existing `MainWindowSessionLifecycle`) | M |
| Theme init (async) | Loaded inline | Same | L |
| Keyboard shortcuts init | Loaded inline | Same | L |
| Mica + custom title bar | Loaded inline | Same | M |
| ctor / Activated / startup overlay | `MainWindow` ctor | Unchanged | — |
| Coordinators construction | ctor / Loaded | Unchanged this slice | — |

## Slice 2 candidate (explicitly NOT slice 1)

- **Navigation-shell polish:** panel switching glue still in `MainWindow` beyond coordinator construction; `OpenPanelByIdAsync` ownership.
- **Loaded tail:** `TransportShortcutCoordinator.Attach` + `RunPanelInitWhenReadyAsync` — separate bounded slice with its own ADR-047 note if ordering changes.

## Acceptance criteria (Task 255 freeze)

1. `MainWindow.xaml.cs` **net** reduction of the inlined Loaded orchestration (replaced by one `RunAsync` call + hooks object).
2. **No reorder** of the ten steps vs pre-refactor without ADR + seam test update.
3. **ADR-047:** no new async UI work from `MainWindow` **constructor**; bootstrap **only** from Loaded.
4. No RHVoice / STT proof / verify-bar edits in this slice.

## Verification (Task 258)

Recorded **2026-04-24** (host: repo root `E:\VoiceStudio`):

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# Build succeeded. 0 Error(s). 5 Warning(s) — pre-existing nullability in unrelated files (not this slice).

dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice1Tests" --no-build -v q
# Passed!  Failed: 0, Passed: 4, Skipped: 0, Total: 4

python scripts\run_verification.py
# Overall: PASS (gates + completion_guard; advisories only for proof freshness)
```

## Task 259 (STATE)

Update [`.cursor/STATE.md`](../../.cursor/STATE.md) **ACTIVE WINDOW** **only after** this slice is committed and verification is green — note **landed** class `MainWindowShellLoadedBootstrap` and **next seam** (navigation-shell / Loaded tail).

## Changelog

- **2026-04-24:** Initial bounded brief + bootstrap extraction (Tasks 253–258).
