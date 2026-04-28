# TIMELINE_REFRESH_AFTER_GENERATED_AUDIO_INSERTION (2026-04-28)

## Scope

- **In scope:** `GeneratedAudioClipInsertedEvent` (`PanelEvents.cs`); publish from `GeneratedAudioTimelineService` after successful `CreateClipAsync`; `TimelineViewModel` subscribes and calls `LoadTracksForProject` when `SelectedProject.Id` matches `evt.ProjectId`; DI passes `IEventAggregator` into `GeneratedAudioTimelineService`; MSTest coverage (service publish + VM subscription behavior).
- **Out of scope / non-claims:** **not** GAP-008; **not** MainWindow or any `MainWindow*ShellBridge`; **not** Slice 46+; **not** RHVoice; **not** `ENGINE_PARITY_MATRIX` changes; **not** a manual in-app or runtime “full product” PASS; **not** shared OpenAPI/backend contract edits.

## Event and refresh path

1. `GeneratedAudioTimelineService.AddGeneratedClipAsync` persists a clip via `ITimelineClipService.CreateClipAsync`.
2. On success, `IEventAggregator.Publish(new GeneratedAudioClipInsertedEvent(PanelIds.VoiceSynthesis, …))`.
3. `TimelineViewModel` handles `GeneratedAudioClipInsertedEvent`; if the active `SelectedProject` matches `ProjectId`, it fire-and-forgets `LoadTracksForProject` (same continuation/logging pattern as `OnSelectedProjectChanged`).

## Files touched

| Area | File |
|------|------|
| Event | `src/VoiceStudio.Core/Events/PanelEvents.cs` |
| Service + publish | `src/VoiceStudio.App/Services/GeneratedAudioTimelineService.cs` |
| DI | `src/VoiceStudio.App/Services/AppServices.cs` |
| VM subscribe | `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` |
| Tests | `src/VoiceStudio.App.Tests/Services/GeneratedAudioTimelineServiceTests.cs`, `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelGeneratedAudioInsertedTests.cs` |

## Tests

- **Service:** `Success_PublishesGeneratedAudioClipInsertedEvent_WithMetadata`; `PlacementUnavailable_DoesNotPublishEvent`; `CreateClipThrows_DoesNotPublishEvent`; `NoActiveProject_DoesNotPublishEvent` (plus existing `GeneratedAudioTimelineServiceTests`).
- **VM:** `TimelineViewModelGeneratedAudioInsertedTests` — matching project refresh, wrong project ignored, no selection ignored, null optional fields, `Subscribe<GeneratedAudioClipInsertedEvent>` verification via optional `IEventAggregator` ctor injection.

## Commands (proof)

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test VoiceStudio.sln -c Debug -p:Platform=x64 --filter "FullyQualifiedName~GeneratedAudioTimelineServiceTests|FullyQualifiedName~TimelineViewModelGeneratedAudioInserted|FullyQualifiedName~TimelineViewModelTests"
python scripts/run_verification.py
.\scripts\verify.ps1 -Quick
```

## Artifacts

- **Quick verify:** `artifacts/verify/20260428_151820/verification_report.md`
- **Gate JSON:** `.buildlogs/verification/last_run.json`
