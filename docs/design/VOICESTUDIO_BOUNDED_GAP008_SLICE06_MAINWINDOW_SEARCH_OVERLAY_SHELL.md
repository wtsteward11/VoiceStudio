# GAP-008 Slice 6 — MainWindow global search overlay shell glue (bounded)

**Status:** Accepted (Tasks 299–308; implementation per verification section)  
**Date:** 2026-04-25  
**Parent:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-008**  
**Companion:** [MAINWINDOW_DECOMPOSITION_PLAN.md](MAINWINDOW_DECOMPOSITION_PLAN.md); [SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md](SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md); [Slice 5](VOICESTUDIO_BOUNDED_GAP008_SLICE05_MAINWINDOW_RECENT_PROJECTS_MUTATION.md)

## First seam (exact)

**Shell-only delegation** from **`MainWindow`** to existing **`ISearchOverlayCoordinator`** ([`SearchOverlayCoordinator`](../../src/VoiceStudio.App/Services/SearchOverlayCoordinator.cs)), via **`MainWindowSearchOverlayShellBridge`**:

- **Show overlay** — menu “Global Search”, Ctrl+K, and any path that called `ShowGlobalSearch()` → coordinator **`Show()`**.
- **Navigate from result** — `GlobalSearchView.NavigateRequested` handler → coordinator **`HandleNavigateRequestedAsync(result)`**.
- **Background tap dismiss** — `GlobalSearchOverlay_Tapped` (XAML entry stays on **`MainWindow`**) → coordinator **`Hide()`** only when **`OriginalSource`** is the overlay root (same **`ReferenceEquals`** rule as pre-Slice-6).
- **Startup visibility** — initial **`GlobalSearchOverlay.Visibility = Collapsed`** moved into bridge **`EnsureGlobalSearchOverlayCollapsed()`** (same timing: end of **`MainWindow`** ctor block).

**Types:** [`MainWindowSearchOverlayShellBridge`](../../src/VoiceStudio.App/Services/MainWindowSearchOverlayShellBridge.cs); coordinator implementation unchanged.

## Scope: search overlay shell only (not toolbar)

**Slice 6 includes:** wiring above only.

**Slice 7 (narrow — see [Slice 7 brief](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md)):** Toolbar **customization launcher** only on **`MainWindow`**; command palette / tool catalog deferred to **Slice 8+**; no toolbar identifiers in the Slice 6 bridge file.

## What stays in `MainWindow`

- **XAML** `Tapped="GlobalSearchOverlay_Tapped"` on **`GlobalSearchOverlay`** (code-behind name unchanged).
- **`NavigateRequested +=`** subscription site (ctor); handler is thin forward to bridge.
- **Construction** of **`SearchOverlayCoordinator`** (same lifetime as today); bridge receives **`ISearchOverlayCoordinator`** reference.
- **Collaboration panel** startup visibility and all non–global-search shell.

## What moves out

- **Orchestration of** show / navigate / dismiss / startup overlay collapse into **`MainWindowSearchOverlayShellBridge`** (header: GAP-008 Slice 6 only).

## No-expansion rule (OUT OF SCOPE)

- Toolbar, customize-toolbar dialog, tool catalog shortcuts beyond existing **`ShowGlobalSearch`** call sites in this slice; **recent** mutations bridge; **project workflow** bridge; import/jump/file/session; **`engines/audio/rhvoice/`**; verify-bar churn unless anchored to **`verify.ps1`**.

## Dependency / blast-radius map (Task 300)

| Responsibility | Current owner (pre–Slice-6) | Target after Slice 6 | Risk | Tests |
|----------------|----------------------------|----------------------|------|--------|
| Open overlay (menu / Ctrl+K) | `MainWindow.ShowGlobalSearch` → coordinator | `MainWindow` → **`MainWindowSearchOverlayShellBridge.Show`** → coordinator | L | Moq `Show` once |
| Navigate from result | `GlobalSearchView_NavigateRequested` → coordinator | Thin handler → **`OnNavigateRequestedAsync`** | M | Moq `HandleNavigateRequestedAsync` with expected **`SearchResultItem`** |
| Background tap dismiss | `GlobalSearchOverlay_Tapped` + `FindName` + `ReferenceEquals` | **`OnOverlayBackgroundTapDismiss`** (or tap entry + overload) | M | Static **`ShouldDismissSearchOverlayOnBackgroundTap`** + Moq `Hide` |
| Startup overlay collapsed | Ctor `FindName` + `Visibility` | **`EnsureGlobalSearchOverlayCollapsed`** | L | Ctor-order implicit via **`MainWindow`** + **`Gap008Slice6Tests`** |
| Panel routing / toasts inside navigation | **`SearchOverlayCoordinator`** | Unchanged | — | **`SearchOverlayCoordinatorTests`** (regression) |

## Contract alignment

[SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md](SEARCH_OVERLAY_OWNERSHIP_CONTRACT.md): coordinator owns show/hide/navigation; shell owns subscription and thin routing. Slice 6 **implements** thin routing in **`MainWindowSearchOverlayShellBridge`** while **handler entry points** remain on **`MainWindow`**.

## Follow-up: UI-host coverage (testing debt)

**What is missing:** A dedicated **`[UITestMethod]`** (or equivalent WinUI UI test) that asserts real **`FrameworkElement.Visibility`** on the global search overlay after **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** runs in a **live** WinUI visual tree.

**Why it was excluded:** The WinUI test host **crashed** in this repository’s default vstest configuration when that UI-level test was attempted (see [Slice 7 brief — Verification / Task 313 note](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md)). Collapse semantics are therefore covered by **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** unit tests + **`Gap008Slice6Tests`** text pins only.

**Definition of done for this debt:** One of: (1) a **stable** WinUI UI test process / harness where the visibility test runs green in CI, (2) a **single STA** integration test approved as the canonical host for this assertion, or (3) a **signed-off manual integration checklist** stored in canonical docs — whichever the release gate accepts. Until then, treat this as **known testing debt**, not “covered by UI tests.”

## Slice 7+ (post–Slice-6)

Toolbar customization shell is **[Slice 7](VOICESTUDIO_BOUNDED_GAP008_SLICE07_MAINWINDOW_TOOLBAR_SHELL.md)**. Command palette / tool catalog → **Slice 8+** planning in that brief chain; **not** part of Slice 6.

## Deferred: `IRecentProjectsMutationCommands` placement (Task 306)

**No move** during Slice 6. Interface remains declared alongside **`RecentProjectsService`** in [`RecentProjectsService.cs`](../../src/VoiceStudio.App/Services/RecentProjectsService.cs) until a later slice has a **technical** reason to relocate (not abstraction churn).

## RHVoice (Task 308)

**Zero** **`engines/audio/rhvoice/`** edits; RHVoice remains **frozen** / **operator-gated**.

## Verification (Task 304 — 2026-04-24)

**Commands (exact filter):**

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src\VoiceStudio.App.Tests\VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~MainWindowSearchOverlayShellBridgeTests|FullyQualifiedName~Gap008Slice6Tests|FullyQualifiedName~SearchOverlayCoordinatorTests|FullyQualifiedName~Gap008Slice5Tests" -v q
python scripts\run_verification.py
```

**Results:**

- **`dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`:** **0 Error(s)** (warnings only; pre-existing test-project warnings).
- **`dotnet test` (filter above):** **Passed: 33**, Failed: 0 — `MainWindowSearchOverlayShellBridgeTests`, `Gap008Slice6Tests`, `SearchOverlayCoordinatorTests`, `Gap008Slice5Tests`.
- **`python scripts/run_verification.py`:** **Overall: PASS** — `.buildlogs/verification/last_run.json`.

**Verify bar:** unchanged (no `verify.ps1` / `defaults.latest_verify_artifact` bump per slice policy).

## Changelog

- **2026-04-25 (Tasks 319–328):** Added **§ Follow-up: UI-host coverage (testing debt)** — documents missing **`[UITestMethod]`** for real overlay **`Visibility`** and closure criteria (WinUI host crash context); cross-links Slice 7 verification note and **MAINWINDOW_DECOMPOSITION_PLAN** testing-debt bullet.
- **2026-04-25 (Tasks 309–318 follow-on):** **`TryCollapseGlobalSearchOverlayIfFrameworkElement`** — explicit **`is not FrameworkElement`** gate for startup collapse; unit tests + **`Gap008Slice6Tests`** text pin (no flaky WinUI host test in default filter).
- **2026-04-25:** Tasks 299–308 — brief + **`MainWindowSearchOverlayShellBridge`** + tests + **`MainWindow`** wiring + docs/STATE/registry after green.
- **2026-04-24:** Verification recorded; tap-dismiss path uses **`FindName` root as `object?`** for **`ReferenceEquals`** with **`OriginalSource`** (headless-testable; visibility collapse uses **`FrameworkElement`** pattern only).
