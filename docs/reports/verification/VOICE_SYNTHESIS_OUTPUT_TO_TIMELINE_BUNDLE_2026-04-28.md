# VOICE_SYNTHESIS_OUTPUT_TO_TIMELINE_BUNDLE — Verification Report

**Date:** 2026-04-28  
**Scope:** Bundle **VOICE_SYNTHESIS_OUTPUT_TO_TIMELINE_BUNDLE** — insert synthesized voice audio into the active project timeline when backend clip APIs can persist a clip; preserve provenance metadata; surface actionable failure when project/track context is missing.

## Bundle Items Completed

1. **Timeline insertion:** `GeneratedAudioTimelineService` resolves active project (`IContextManager.ActiveProjectId`), target track via `RecordingTrackTargetResolver` + `ITimelineTrackService`, appends clip start time from existing track clips, calls `ITimelineClipService.CreateClipAsync`.
2. **Metadata:** `AudioClip` receives `AudioId`, `AudioUrl`, `ProfileId`, `Engine`, `Duration`, `QualityScore`, `DerivedFromClipId` (library asset id when present); human-readable `Name` includes **Voice Synthesis**, profile, timestamp, optional text preview.
3. **UI / state:** `VoiceSynthesisViewModel` — `CanAddGeneratedAudioToTimeline`, `IsGeneratedAudioAddedToTimeline`, `GeneratedAudioTimelineStatus`; `AddGeneratedAudioToTimelineCommand` (legacy `AddToTimelineCommand` alias); recent-result `IsAddedToTimeline` / `AddedToTimelineAtLocal`; JSON persistence fields on recent results.
4. **Unavailable vs failure:** Service returns `GeneratedAudioTimelineKind.Unavailable` (no project, no profile id, no tracks, resolver error) vs `Failed` (exceptions from backend clip create).

## Files Touched (implementation)

- `src/VoiceStudio.App/Services/IGeneratedAudioTimelineService.cs` (new)
- `src/VoiceStudio.App/Services/GeneratedAudioTimelineService.cs` (new)
- `src/VoiceStudio.App/Services/AppServices.cs` — DI registration
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs`
- `src/VoiceStudio.App/Views/Panels/VoiceSynthesisView.xaml`
- `src/VoiceStudio.App.Tests/Services/GeneratedAudioTimelineServiceTests.cs` (new)
- `src/VoiceStudio.App.Tests/ViewModels/VoiceSynthesisViewModelTests.cs` — Timeline output tests region
- `docs/developer/AUTOMATION_ID_REGISTRY.md`
- `tests/ui/fixtures/automation_ids.py`

## Insertion Path (Architecture Note)

- **Seam:** `IGeneratedAudioTimelineService` → `ITimelineTrackService.GetTracksAsync` / `ITimelineClipService.CreateClipAsync`.
- **Not used for persistence:** `AddToTimelineEvent` / `TimelineViewModel` in-process clip path (previous toolbar published events). The new flow persists clips through the same backend API as `TimelineClipService` without `MainWindow` coupling.
- **Limitation:** Open Timeline panel may require refresh/load to show the new clip immediately if it was not subscribed to backend mutations; clip is still created server-side.

## Tests

- **Filters:** `VoiceSynthesisViewModelTests` + `GeneratedAudioTimelineServiceTests`
- **Result:** `dotnet test` — **155 passed** (focused filter run during implementation).
- **Repo gates:** `python scripts/run_verification.py` — **PASS** (`.buildlogs/verification/last_run.json`); `.\scripts\verify.ps1 -Quick` — **PASS** — `artifacts/verify/20260428_130733/verification_report.md`

## Explicit Non-Claims

- **Not** GAP-008 / **not** Slice 46 / **not** new `MainWindow*ShellBridge`.
- **Not** RHVoice / **not** edits to `ENGINE_PARITY_MATRIX.md`.
- **Not** runtime **FULL PASS** / **not** human in-app synthesis attestation.

## Known Limitations

- Command enablement does not require an active project id in the VM; missing project is reported on execute via timeline status + service `Unavailable` result.
- Append placement uses track clips returned by `GetTracksAsync`; if the API omits clip payloads, start time may default to `0` (potential overlap — backend/track authority dependent).
