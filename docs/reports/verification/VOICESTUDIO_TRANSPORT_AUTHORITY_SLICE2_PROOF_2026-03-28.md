# GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01 — Slice 2 proof (2026-03-28)

**Scope:** Slice 2 only — global / timeline / keyboard command-path convergence per [GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md) §6. **Slices 3–4 not claimed** (playhead/seek truth, full lane closure).

## Code touched

| Area | File | Change summary |
|------|------|----------------|
| Timeline VM | `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` | `PlayAudioCommand` resumes when `IAudioPlayerService.IsPaused`; `ITimelineTransportController.PlayAsync()` resumes before starting a new `PlayAudioAsync` pipeline |
| Orchestrator | `src/VoiceStudio.App/Services/GlobalTransportOrchestrator.cs` | Timeline branch fallback when `GetTimelineController()` is null: `IAudioPlayerService` pause/resume/stop; `PausePlayback()` for unified pause entry |
| Orchestrator API | `src/VoiceStudio.App/Services/IGlobalTransportOrchestrator.cs` | `PausePlayback()` |
| Shortcuts | `src/VoiceStudio.App/Services/TransportShortcutCoordinator.cs` | Ctrl+R invokes caller `openRecordingPanel` (navigate policy); XML docs |
| Shell | `src/VoiceStudio.App/MainWindow.xaml.cs` | `OpenRecordingPanelFromTransportShortcut()` → `NavigateToEvent(Timeline → Recording)`; attach coordinator to that action (menu `ToggleRecording` unchanged) |
| Command palette | `src/VoiceStudio.App/Commands/PlaybackOperationsHandler.cs` | With orchestrator: `PauseAsync` → `PausePlayback()`; `TogglePlayPauseAsync` → `TogglePlaybackAsync()` |
| Lane doc | `docs/design/GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md` | §6 Slice 2 frozen contract + changelog |

## Honesty / out of scope

- **`playback.record` command registry** still uses `ToggleRecordAsync` (microphone path). Slice 2 row explicitly scopes **Ctrl+R shortcut** parity with timeline Record; command-palette record is unchanged.
- **Slice 1** timeline bar honesty (Record/Loop/time, disabled track chrome) preserved.

## Tests added

| Class | File |
|-------|------|
| `GlobalTransportOrchestratorTests` | `src/VoiceStudio.App.Tests/Services/GlobalTransportOrchestratorTests.cs` — timeline toggle/stop/pause routing; controller-null fallbacks; sequential play→pause→play |
| `TransportShortcutCoordinatorTests` | `src/VoiceStudio.App.Tests/Services/TransportShortcutCoordinatorTests.cs` — `playback.play` / `playback.stop` / `playback.record` → orchestrator or navigate callback |
| Timeline | `src/VoiceStudio.App.Tests/ViewModels/TimelineViewModelTests.cs` — `PlayAudioCommand_WhenPlayerPaused_CallsResumeOnly`; `TimelineTransportController_PlayAsync_WhenPaused_CallsResume` |

## Verification (executed)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS (2812 passed, 274 skipped after final rebuild) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (216 passed, 2 deselected) |
| `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260328_052954/verification_report.md` |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |

## Next

**Slice 3:** playhead / seek / single canonical time source vs context (`SetCurrentPlayable`) — per execution row. **Lane remains Open** until Slices 3–4 + closure report.
