# Profiles Truth Recovery — Bounded Slice 3 (2026-04-16)

## Goal

Restore correct Profiles panel population when navigating to the panel: API returns profiles but UI showed **0 of 0** because `IPanelLifecycle.OnActivatedAsync` was a no-op.

## Root cause

`ProfilesViewModel.OnActivatedAsync` returned `Task.CompletedTask` and never called `LoadProfilesAsync`. `PanelHost` invokes `OnActivatedAsync` on activation; no other path loaded data on first open.

## Fix (code)

| Area | Change |
|------|--------|
| `ProfilesViewModel.OnActivatedAsync` | If `Profiles.Count == 0`, await `LoadProfilesAsync(cancellationToken)`. |
| `LoadProfilesAsync` | `Debug.WriteLine` at entry, after list fetch (count), on `OperationCanceledException`, and on general exception; `ArgumentNullException.ThrowIfNull` on use-case list result. |
| `ApplyFilters` | `Debug.WriteLine` with `TotalProfiles` and `FilteredCount` after filter pass; `OnPropertyChanged(nameof(FooterSummary))`. |
| `FooterSummary` + footer XAML | Single `Text="{x:Bind ViewModel.FooterSummary}"` with `AutomationProperties.AutomationId="ProfilesView_FooterSummary"` (replaces Run-based footer for stable automation + accessibility). |

**DTO:** `VoiceProfile` (`src/VoiceStudio.App/Core/Models/VoiceProfile.cs`) aligns with `backend/api/models_additional.py` `VoiceProfile` (snake_case via shared JSON options). Extra backend field `reference_audio_bound` is ignored by `System.Text.Json` (no change required).

## Build proof (Slice 3 hardening)

- **Command:** `dotnet clean VoiceStudio.sln -c Debug -p:Platform=x64` then `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64 -m:1`
- **Result:** **Build succeeded** (exit 0). Pre-existing warnings in other projects unchanged; no new warnings required in `ProfilesViewModel` for this slice.
- **Lock mitigation:** `Stop-Process` on prior lock PID when needed; single-threaded `-m:1` build reduces copy races.

## Runtime API proof (backend healthy)

Probe artifact: [`PROOF_PROFILES_RUNTIME_API_2026-04-16.json`](PROOF_PROFILES_RUNTIME_API_2026-04-16.json)

- `GET http://127.0.0.1:8000/health` → `status: ok`
- `GET http://127.0.0.1:8000/api/profiles` → **`items_count`: 50** at probe time (catalog size varies by environment)

**Interpretation:** With backend up, the app’s `GET /api/profiles` path can return a non-zero list. **`TotalProfiles` in the ViewModel should match `items_count`** after load when no filters are applied. **`FilteredCount`** matches **`TotalProfiles`** on first open with default filters.

## Runtime UI proof artifact (Slice 3 final closure)

Structured proof: [`PROOF_PROFILES_UI_RUNTIME_2026-04-16.json`](PROOF_PROFILES_UI_RUNTIME_2026-04-16.json)

- **Classification:** **Case A** — API items count, ViewModel counts, and `FooterSummary` string match after `OnActivatedAsync` against a live backend (`127.0.0.1:8000`).
- **Live-backend test:** [`ProfilesRuntimeLiveBackendTests.cs`](../../src/VoiceStudio.App.Tests/ViewModels/ProfilesRuntimeLiveBackendTests.cs) `OnActivatedAsync_LiveBackend_ViewModelCountsAndFooterMatchApiItems` (skips with Inconclusive if backend down — CI-safe).
- **Slice 2:** Startup overlay / status **Ready** / Retry — recorded in JSON as `operator_interactive_recommended` when not captured in the same automated session.

## Automated proof (ViewModel + footer properties + filters)

**Seam tests — command:**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProfilesViewModelSeamTests"
```

**Live backend — command (backend must be running):**

```text
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ProfilesRuntimeLiveBackendTests"
```

**Tests in** [`ProfilesViewModelSeamTests.cs`](../../src/VoiceStudio.App.Tests/ViewModels/ProfilesViewModelSeamTests.cs) **(9 total):**

| Test | Intent |
|------|--------|
| `ProfileCreatedEvent_FromTraining_AfterReload_SelectsNewProfile` | Event-driven reload + selection |
| `ProfileCreatedEvent_WhenProfileAlreadyInList_SelectsWithoutSecondList` | No redundant list when profile exists |
| `OnActivatedAsync_WhenEmpty_LoadsProfilesOnce` | Activation loads when cache empty |
| `OnActivatedAsync_WhenAlreadyLoaded_SkipsSecondFetch` | No redundant fetch when cache warm |
| `OnActivatedAsync_AfterFailedLoad_RetriesOnNextActivation` | Failed load then second activation retries |
| `OnActivatedAsync_AfterLoad_SetsCountProperties` | `TotalProfiles` / `FilteredCount` / `Profiles.Count` / `FooterSummary` consistent |
| `OnActivatedAsync_EmptyResponse_ShowsEmptyState` | Empty API → `ShowEmptyState` + `FooterSummary` `0 of 0 profiles` |
| `ApplyFilters_WithSearchQuery_PreservesTotalCount` | Search reduces `FilteredCount`, preserves `TotalProfiles` |
| `ProfileUpdatedEvent_FromTraining_TriggersSecondListLoad` | GAP-028 metadata reload |

These tests run on the real test `DispatcherQueue` and exercise `ReplaceProfiles` → `UpdateAvailableFilters` → `ApplyFilters`, matching the XAML bindings for **`FilteredProfiles`**, **`FilteredCount`**, **`TotalProfiles`**.

## Runtime Diagnostic Capture

Expected `Debug.WriteLine` prefixes (see [`ProfilesViewModel.cs`](../../src/VoiceStudio.App/Views/Panels/ProfilesViewModel.cs)):  
`[ProfilesViewModel] LoadProfilesAsync: entering` → `received {N} profile(s) from use case` → `[ProfilesViewModel] ApplyFilters: TotalProfiles={N}, FilteredCount={N}`.  
Also mirrored in [`PROOF_PROFILES_UI_RUNTIME_2026-04-16.json`](PROOF_PROFILES_UI_RUNTIME_2026-04-16.json) under `diagnostics_expected`.

## Manual UI verification (recommended)

1. Start backend (`scripts/start_backend.ps1` or existing process on port 8000).
2. Launch `VoiceStudio.App.exe` from `.buildlogs\x64\Debug\net8.0-windows10.0.19041.0\`.
3. Open **Profiles** — footer **N of N** should match API items count when no search/filters are active.
4. Debug output (VS / DebugView):  
   - `[ProfilesViewModel] LoadProfilesAsync: entering`  
   - `received N profile(s) from use case`  
   - `[ProfilesViewModel] ApplyFilters: TotalProfiles=..., FilteredCount=...`

## Screenshots

Optional operator artifact: PNG of Profiles panel with non-zero footer and visible grid rows, plus status bar **Ready** (Slice 2 regression check). Automated tests above substitute for ViewModel/grid **data** proof when screenshot is not committed.

## Closure status

- **Activation seam:** fixed and tested.  
- **Presentation seam (counts + filtered collection + footer string):** covered by seam tests, `FooterSummary` binding, `ApplyFilters` diagnostic, and **live-backend** `ProfilesRuntimeLiveBackendTests` when API is reachable.  
- **Runtime API:** probed with JSON artifacts when backend available.  
- **Product truth (API ↔ ViewModel ↔ footer text):** **Case A** satisfied by live-backend test + proof JSON; **pixel** grid/footer PNG remains optional operator artifact for marketing/audit only.
