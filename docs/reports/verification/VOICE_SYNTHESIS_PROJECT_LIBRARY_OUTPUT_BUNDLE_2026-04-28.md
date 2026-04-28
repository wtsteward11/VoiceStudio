# Voice Synthesis Project/Library Output Bundle — Verification Report

**Date:** 2026-04-28  
**Lane:** `VOICE_SYNTHESIS_PROJECT_LIBRARY_OUTPUT_BUNDLE` (product; **not** GAP-008)

## Scope

Bundled Voice Synthesis output workflow:

1. Add generated voice audio to the project/library surface via **`IGeneratedAudioLibraryService`** (event-bus integration).
2. Show saved/unsaved state on the active generated-audio result (**`IsGeneratedAudioSaved`**, **`GeneratedAudioSaveStatus`**, **`CanAddGeneratedAudioToLibrary`**).
3. Mark the matching recent-result row when save succeeds; persist **`IsSavedToLibrary`** / **`SavedAtLocal`** in panel **`CustomData`** when recent-results persistence is used.

## Bundle items completed

| Item | Status |
|------|--------|
| Add generated audio to library workflow | Done — `AddGeneratedAudioToLibraryCommand` → `GeneratedAudioLibraryService` publishes `AssetAddedEvent` |
| Saved/unsaved UI state | Done — status text + command gating |
| Recent result saved marker + persistence | Done — `VoiceSynthesisRecentResult` + DTO round-trip |

## Service path used

- **`IGeneratedAudioLibraryService`** / **`GeneratedAudioLibraryService`** (`src/VoiceStudio.App/Services/`): publishes **`AssetAddedEvent`** via **`IEventAggregator`** (same family as “Library knows” from timeline flow).
- **Limitation (documented):** no direct **`ILibraryClient.UploadLibraryAssetAsync`** or **`IProjectAudioClient.SaveAudioToProjectAsync`** call — those require local paths or a **project id**, which this panel does not own.

## Files changed (summary)

| Path | Change |
|------|--------|
| `src/VoiceStudio.App/Services/IGeneratedAudioLibraryService.cs` | New — interface + request/result records |
| `src/VoiceStudio.App/Services/GeneratedAudioLibraryService.cs` | New — `AssetAddedEvent` publisher |
| `src/VoiceStudio.App/Services/AppServices.cs` | Register `IGeneratedAudioLibraryService` |
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` | Save command/state, recent-result saved flags, persistence |
| `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml` | Add to Library + status + recent badge |
| `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` | Library output tests (+ mock service in setup) |
| `docs/developer/AUTOMATION_ID_REGISTRY.md` | Three new AutomationIds |

## Saved/unsaved behavior

- New successful synthesis clears **`IsGeneratedAudioSaved`** (via **`ResetLastSynthesisOutput`** at synthesis start).
- Successful **`SaveAsync`** sets **`IsGeneratedAudioSaved`** and **`GeneratedAudioSaveStatus`** (“Saved to library”).
- Failed save does not set saved; generated **`AudioId`/`Url`** and **`CanPlayAudio`** unchanged.

## Recent-result saved marker

- **`VoiceSynthesisRecentResult`** exposes **`IsSavedToLibrary`** / **`SavedAtLocal`** ( **`INotifyPropertyChanged`** for UI).
- **`MarkMatchingRecentResultSaved`** updates the row matching active **`AudioId`** or **`AudioReference`**.
- **`RestoreRecentResult`** restores VM saved state from the selected row.
- **`VoiceSynthesis_RecentResults`** JSON includes **`IsSavedToLibrary`** and **`SavedAtUtc`**.

## Tests run

```powershell
dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~VoiceSynthesis|FullyQualifiedName~Library"
```

**Result:** Passed (282 passed, 42 skipped — skipped UI/E2E-style tests).

## Verification artifacts

| Artifact | Path |
|----------|------|
| Automated verification JSON | `.buildlogs/verification/last_run.json` |
| Quick verify report | `artifacts/verify/20260428_115125/verification_report.md` |

## Known limitations

- Library integration is **event-bus notification** only until a project-bound or file-backed pipeline is wired from this panel.
- No backend artifact mutation beyond existing **`AssetAddedEvent`** subscribers.

## Explicit non-claims

- **Not** GAP-008 / **no** `MainWindow*ShellBridge` / **no** Slice 46.
- **Not** RHVoice work.
- **Not** `ENGINE_PARITY_MATRIX` edits.
- **Not** claiming **runtime FULL PASS** or human in-app attestation — harness **PASS** only for scoped commands above.
