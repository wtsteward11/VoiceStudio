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

**DTO:** `VoiceProfile` (`src/VoiceStudio.App/Core/Models/VoiceProfile.cs`) aligns with `backend/api/models_additional.py` `VoiceProfile` (snake_case via shared JSON options). Extra backend field `reference_audio_bound` is ignored by `System.Text.Json` (no change required).

## Automated proof

- **Seam tests** (`ProfilesViewModelSeamTests.cs`):
  - `OnActivatedAsync_WhenEmpty_LoadsProfilesOnce`
  - `OnActivatedAsync_WhenAlreadyLoaded_SkipsSecondFetch`
- **Command:**  
  `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~OnActivatedAsync"`

## Manual verification (recommended)

1. Start backend; launch app.
2. Open **Profiles** — footer should show **N of N** matching API (`GET /api/profiles`).
3. Debug output should include `[ProfilesViewModel] LoadProfilesAsync: entering` and `received N profile(s)`.

## Screenshots

UI screenshots of a populated grid are optional follow-up on a machine without file-lock contention during build; functional behavior is covered by seam tests above.
