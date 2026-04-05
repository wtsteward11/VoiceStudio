# GOV-VOICESTUDIO-TRANSPORT-AUTHORITY-01 — Slice 1 proof (2026-03-28)

**Scope:** Slice 1 only — timeline transport bar honesty per [GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_TRANSPORT_AUTHORITY_01_EXECUTION_ROW.md). **Slice 2+ not claimed** (global bar / keyboard unification deferred).

## Code touched

| Area | File |
|------|------|
| VM | `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` — early `_eventAggregator` / `_contextManager`; `OpenRecordingFromTimelineCommand` → `NavigateToEvent(PanelIds.Timeline, PanelIds.Recording)`; `IsTimelineLoopEnabled` ↔ `IAudioPlayerService.IsLooping`; `TransportTimeDisplay` + `OnCurrentPlaybackPositionChanged` |
| XAML | `src/VoiceStudio.App/Views/Panels/TimelineView.xaml` — Record command, loop `ToggleSwitch` (bool `IsOn` TwoWay), time `TransportTimeDisplay`; per-track M/S/R/slider `IsEnabled="False"` + tooltips |

## XAML honesty notes

- **Loop control:** `ToggleButton.IsChecked` is `bool?`; x:Bind TwoWay to VM `bool` fails MarkupCompile. **Mitigation:** `ToggleSwitch` with `IsOn` TwoWay (same lane intent).
- **Per-track row:** `StackPanel` with `IsEnabled="False"` inside `ListView` `DataTemplate` caused **XamlCompiler exit 1** (WinUI 1.8). **Mitigation:** `IsEnabled="False"` on each child control; identical UX outcome.
- **Track controls:** No VM properties; disabled state is **view-only** (documented here; no fake VM test).

## Tests added (`TimelineViewModelTests`)

- `OpenRecordingFromTimelineCommand_PublishesNavigateToEvent_ToRecordingPanel` — `[TestCategory("SeamAware")]`, `TestAppServicesHelper`
- `IsTimelineLoopEnabled_Constructor_SyncsFromAudioPlayer`
- `IsTimelineLoopEnabled_WhenChanged_PropagatesToAudioPlayer`
- `TransportTimeDisplay_FormatsCurrentPlaybackPosition_Deterministically`
- `CurrentPlaybackPosition_WhenChanged_RaisesTransportTimeDisplayPropertyChanged`

Plus `OpenRecordingFromTimelineCommand` asserted in constructor test.

## Verification (executed)

| Step | Result |
|------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS (2798 passed, 274 skipped) |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS (216 passed, 2 deselected) |
| `.\scripts\verify.ps1 -Quick` | PASS → `artifacts/verify/20260328_044821/verification_report.md` |
| `python scripts/run_verification.py` | PASS (**completion_guard** PASS) |

## Next

Slice 2: unify timeline play/stop with `IGlobalTransportOrchestrator` / keyboard path — **do not start until this proof is accepted.**
