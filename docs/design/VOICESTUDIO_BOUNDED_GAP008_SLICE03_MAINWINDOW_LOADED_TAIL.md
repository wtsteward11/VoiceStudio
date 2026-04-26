# GAP-008 Slice 3 — MainWindow Loaded transport and panel-init tail (bounded)

**Status:** Accepted (slice 3 landed in repo)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 1](VOICESTUDIO_BOUNDED_GAP008_SLICE01_MAINWINDOW_LOADED_BOOTSTRAP.md); [Slice 2](VOICESTUDIO_BOUNDED_GAP008_SLICE02_MAINWINDOW_NAVIGATION_SHELL.md)

## First seam (exact)

**Post–bootstrap Loaded tail only** in [`MainWindow.xaml.cs`](../../src/VoiceStudio.App/MainWindow.xaml.cs) inside `contentFE.Loaded`, **after** `MainWindowShellLoadedBootstrap.RunAsync` and **after** the `#if DEBUG` … `#endif` block:

1. Assign **`_transportShortcutCoordinator`** from **`AppServices.GetService<TransportShortcutCoordinator>()`** and call **`Attach(_keyboardShortcutService, OpenRecordingPanelFromTransportShortcut)`**.
2. Fire-and-forget **`_ = RunPanelInitWhenReadyAsync(...)`** with the four **`FindNameOnContent("…PanelHost")`** panel hosts.

**Implementation:** [`MainWindowLoadedTailBootstrap.Run`](../../src/VoiceStudio.App/Services/MainWindowLoadedTailBootstrap.cs) + [`MainWindowLoadedTailHooks`](../../src/VoiceStudio.App/Services/MainWindowLoadedTailBootstrap.cs) — **`MainWindow`** supplies hooks (may close over **`AppServices`**); the bootstrap type performs **only** ordered invocation of those hooks (no extra service location inside the runner).

**Not moved:** [`RunPanelInitWhenReadyAsync`](../../src/VoiceStudio.App/MainWindow.Workspaces.cs) / **`InitializePanelsAsync`** bodies (remain on `MainWindow` partials).

## Ordering (ADR-047)

Mandatory order inside the Loaded lambda:

1. **`MainWindowShellLoadedBootstrap.RunAsync`** (Slice 1)
2. **`#if DEBUG`** diagnostics block (unchanged; not part of this slice)
3. **`MainWindowLoadedTailBootstrap.Run`** (Slice 3)
4. Close lambda

Panel init must **not** run from the constructor; only from this Loaded path.

## What stays in `MainWindow`

- **`_transportShortcutCoordinator`** field (assignment inside hook so **`Cleanup` → `Detach`** unchanged).
- **`RunPanelInitWhenReadyAsync`** implementation in **`MainWindow.Workspaces.cs`**.
- **`OpenRecordingPanelFromTransportShortcut`**, **`_keyboardShortcutService`**, **`FindNameOnContent`** (hook lambdas).

## What moves out

- **Orchestration** of the two tail steps into **`MainWindowLoadedTailBootstrap.Run`**.

## No-expansion rule (OUT OF SCOPE)

- DEBUG block logic changes (except line drift from call replacement).
- Slice 1 bootstrap hooks; navigation bridge / **`NavButtonActionSink`** behavior changes; startup overlay; dialogs; **`ProjectWorkflowCoordinator`** internals; **`InitializePanelsAsync`** extraction; **`engines/audio/rhvoice/`**; verify-bar / `engine_truth` churn.

## Dependency / blast-radius map (Task 270)

| Responsibility | Current owner (pre-slice) | Target after slice 3 | Risk | Tests |
|----------------|---------------------------|------------------------|------|--------|
| Resolve `TransportShortcutCoordinator` | Loaded inline | Hook from `MainWindow` | L | Order pin: attach after `#endif` |
| Assign field + `Attach` | Loaded inline | Same via hook | M | `Gap008Slice3Tests`; **`TransportShortcutCoordinator.Attach`** idempotency unchanged |
| `Detach` on exit | `MainWindow.Cleanup` | Unchanged | L | Pin **`_transportShortcutCoordinator?.Detach()`** in Cleanup |
| Panel-init trigger | `_ = RunPanelInitWhenReadyAsync(...)` | Hook `RunPanelInitFireAndForget` | M | Pin **`RunPanelInitWhenReadyAsync`** after **`Attach`**; preserve **`_ =`** (historical fire-and-forget) unless ADR + tests change |
| Downstream `InitializePanelsAsync` | `MainWindow.Workspaces` | Unchanged | — | — |

## Lifecycle symmetry (Task 273)

- **Attach:** still once per Loaded (coordinator **`Attach`** internal guard unchanged).
- **Detach:** only in **`Cleanup`**; no duplicate Loaded subscription.
- **Panel init:** single **`RunPanelInitWhenReadyAsync`** from Loaded; no ctor path.
- **No second** `contentFE.Loaded +=` for this pipeline.

## Slice 4 candidate (Task 276 — document only)

**Next bounded seam:** **project open/save workflow glue** in `MainWindow` / workflow services (behavior-heavy; distinct from import workflow). **Not** implemented in this slice. Search/toolbar overlay remains lower priority unless a defect drives it.

## Verification (Task 274)

Recorded **2026-04-25** (repo root `E:\VoiceStudio`):

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# Build succeeded. 0 Error(s). (Pre-existing warnings unchanged.)

dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~Gap008Slice3Tests" -v q
# Passed!  Failed: 0, Passed: 5, Skipped: 0, Total: 5

# Optional regression: same-session `Gap008Slice1Tests` — Passed: 4, Failed: 0.

python scripts\run_verification.py
# Overall: PASS — `.buildlogs\verification\last_run.json` (advisory stale-runtime rows unchanged)
```

## Changelog

- **2026-04-25:** Tasks 269–278 — bounded brief + `MainWindowLoadedTailBootstrap` + `Gap008Slice3Tests` + docs/STATE; NavButtonActionSink guardrail cross-ref Slice 2 / MAINWINDOW.
