# GAP-008 Slice 5 — MainWindow recent-project mutation actions (bounded)

**Status:** Accepted (Tasks 289–298 landed in repo)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [Slice 4](VOICESTUDIO_BOUNDED_GAP008_SLICE04_MAINWINDOW_PROJECT_WORKFLOW.md) (Choice A)

## First seam (exact)

**Recent-project mutation orchestration** from `MainWindow` Pin/Unpin/Clear handlers and the **Remove from list** flyout path into **`MainWindowRecentProjectsMutationBridge`**, using **`IRecentProjectsMutationCommands`** (implemented by **`RecentProjectsService`**) and **`IToastNotificationService`**.

- **Pin** → `PinProjectAsync` + success/error toasts (same copy as pre-Slice-5 `MainWindow`).
- **Unpin** → `UnpinProjectAsync` + success/error toasts.
- **Clear** → `ClearRecentProjectsAsync` + success/error toasts.
- **Remove from list** → `RemoveRecentProjectAsync` only (**no** success toast, **no** try/catch in bridge — parity with pre-Slice-5 inline lambda).

**Types:** [`MainWindowRecentProjectsMutationBridge`](../../src/VoiceStudio.App/Services/MainWindowRecentProjectsMutationBridge.cs); **`IRecentProjectsMutationCommands`** lives in [`RecentProjectsService.cs`](../../src/VoiceStudio.App/Services/RecentProjectsService.cs) (implemented by **`RecentProjectsService`**).

## Scope decision: Option A — `PopulateRecentProjectsMenu` stays on `MainWindow` (frozen)

**Option A (this slice):** **`PopulateRecentProjectsMenu`** remains fully on **`MainWindow`** — all `MenuFlyout*` construction, empty-state item, separators, and click **wiring** only. Handlers for Pin/Unpin/Clear become thin `await _recentProjectsMutationBridge.…` forwards; the Remove lambda calls the bridge instead of `_recentProjectsService` directly.

**Option B (explicitly not taken):** Moving menu population into the same type as mutation orchestration mixes UI composition with action glue — rejected (blob refactor).

## What stays in `MainWindow`

- **`PopulateRecentProjectsMenu`** body and **`_recentProjectsSubMenu`** wiring.
- **`OpenRecentProject`** → **`_projectWorkflowCommandBridge`** (Slice 4).
- **`_recentProjectsService`** field (bridge resolves mutations through **`Func<IRecentProjectsMutationCommands?>`** — typically the same instance).
- **`GetProjectWorkflowCoordinatorForSessionLifecycle`**, import/jump/file/session paths.

## What moves out

- **Try/catch + toasts + service calls** for Pin, Unpin, Clear, Remove into **`MainWindowRecentProjectsMutationBridge`** (header: GAP-008 Slice 5 only; no menu building, no open-project, no import).

## No-expansion rule (OUT OF SCOPE)

- **`PopulateRecentProjectsMenu`** implementation file relocation; **`OpenRecentProject`** / **`MainWindowProjectWorkflowBridge`**; import workflow; jump list / file activation; **search overlay / toolbar** (Slice 6 candidate); **`engines/audio/rhvoice/`**; verify-bar churn unless anchored to **`verify.ps1`** / intentional proof batch.

## Dependency / blast-radius map (Task 290)

| Path | Caller today | Service | Toast | Menu refresh | Collections / side effects | Notes | Risk | Required tests |
|------|----------------|---------|-------|----------------|-----------------------------|-------|------|------------------|
| Pin | `PopulateRecentProjectsMenu` → `PinRecentProject` | `PinProjectAsync` | `ShowToast(Success, …)` / `ShowToast(Error, …)` on failure | `RecentProjectsService` raises `PropertyChanged` for `PinnedProjects` / `RecentProjects` / `AllProjects` → **`MainWindow`** subscriber (~553–562) calls **`PopulateRecentProjectsMenu()`** synchronously | Cap **`MaxPinnedProjects`**; may move item recent→pinned | `async void` handler | Null service → no-op (silent) matches pre-Slice-5 | Bridge Moq + null no-op |
| Unpin | same | `UnpinProjectAsync` | same | same | Unpins; re-inserts to recent; trims | same | same | same |
| Clear | Clear flyout item → `ClearRecentProjects` | `ClearRecentProjectsAsync` | same | same | Clears **`_recentProjects`** only (pinned unchanged per service impl) | same | same | same |
| Remove | `PopulateRecentProjectsMenu` inline lambda | `RemoveRecentProjectAsync` | **None** | same | Removes one recent entry | **Asymmetry:** no try/catch vs Pin/Unpin/Clear — preserved unless brief amended | Unhandled exception still possible from service (unchanged) | Bridge delegates; **no** toast assert on success |

**Menu refresh policy:** Bridge **does not** call **`PopulateRecentProjectsMenu`**. Refresh stays **event-driven** via **`PropertyChanged`** on **`RecentProjectsService`** plus **`EnqueueRecentProjectsMenuRefresh`** on Loaded bootstrap ([`MainWindow.xaml.cs`](../../src/VoiceStudio.App/MainWindow.xaml.cs) ~351–356).

## Lifecycle

- Bridge holds **no** ownership of **`RecentProjectsService`**; **`Func<IRecentProjectsMutationCommands?>`** mirrors Slice 4’s lazy resolver pattern.
- **Null** mutation service: Pin/Unpin/Clear **return** without throwing (matches **`if (_recentProjectsService != null)`** guards). Remove: **no-op** when null (matches guarded **`PopulateRecentProjectsMenu`** + null check in lambda).

## Slice 4 regression

**`MainWindowProjectWorkflowBridge`** remains **coordinator forwards only** — do not route Pin/Unpin/Clear/Remove through it.

## Slice 6 candidate (Task 296 — document only here; no code)

**Next bounded seam:** **Search overlay / toolbar glue** (`GlobalSearchView`, `GlobalSearchOverlay`, transport-adjacent toolbar) — **not** started in this batch.

## Narrow-seam guardrail (Task 297)

**`MainWindowRecentProjectsMutationBridge`** — **Pin / Unpin / Clear / Remove mutation + toasts only**; not a flyout builder, not a second **`MainWindowProjectWorkflowBridge`**.

## RHVoice (Task 298)

**Zero** **`engines/audio/rhvoice/`** edits; RHVoice remains **frozen** / **operator-gated** — not this product lane.

## Verification (Task 294)

Recorded **2026-04-25** (repo root `E:\VoiceStudio`):

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
# Build succeeded. 0 Error(s). (Pre-existing warnings unchanged.)

dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowRecentProjectsMutationBridgeTests|FullyQualifiedName~Gap008Slice5Tests|FullyQualifiedName~Gap008Slice4Tests" -v q
# Passed!  Failed: 0, Passed: 14, Skipped: 0, Total: 14

python scripts\run_verification.py
# Overall: PASS — `.buildlogs\verification\last_run.json`
```

Verify bar unchanged (no `verify.ps1` anchor bump).

## Changelog

- **2026-04-25:** Tasks 289–298 — bounded brief; **`IRecentProjectsMutationCommands`** + **`MainWindowRecentProjectsMutationBridge`**; **`MainWindowRecentProjectsMutationBridgeTests`** (7) + **`Gap008Slice5Tests`** (4) + **`Gap008Slice4Tests`** regression (3); **`MainWindow`** thin wiring; docs/STATE/registry after green.
