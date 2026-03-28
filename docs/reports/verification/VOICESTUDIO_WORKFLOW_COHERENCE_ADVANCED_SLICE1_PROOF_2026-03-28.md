# GOV-VOICESTUDIO-WORKFLOW-COHERENCE-ADVANCED-01 — Slice 1 Proof (Workflow A)

**Date:** 2026-03-28  
**Lane:** `GOV-VOICESTUDIO-WORKFLOW-COHERENCE-ADVANCED-01`  
**Slice:** 1 — Profile → Synthesis → Timeline continuity (deterministic VM proof)

## 1. Binary acceptance (execution row §6)

| ID | Criterion | Result |
| --- | --- | --- |
| A1 | `ProfileSelectedEvent` updates `VoiceSynthesisViewModel.SelectedProfile` when profile exists in list | **PASS** — `ProfileSelectedEvent_UpdatesVoiceSynthesisSelectedProfile_WhenProfileInList` |
| A2 | `AddToTimelineEvent` adds clip with `ProfileId` + `AudioId` when project + track exist | **PASS** — `AddToTimelineEvent_AddsClipWithProfileId_AndSelectsClip` |
| A3 | `SynthesisCompletedEvent` → `OnSynthesisCompleted` → same add path | **PASS** — `SynthesisCompletedEvent_AddsClipViaSamePath_AsAddToTimelineEvent` |
| A4 | New clip is selected via `MultiSelectService` / `IsClipSelected` | **PASS** — asserted in A2 test |
| A5 | Build + tests + CI on claim state | See §3 |

## 2. Repo path map (seam risks documented)

| Step | Location | Notes |
| --- | --- | --- |
| Profile broadcast | `ProfileSelectedEvent` (`VoiceStudio.Core.Events`) | `VoiceSynthesisViewModel.OnProfileSelected` (`Views/Panels/VoiceSynthesisViewModel.cs`); subscription in `OnActivatedAsync` |
| Synthesis → timeline manual | `AddSynthesizedAudioToTimeline` | Publishes `AddToTimelineEvent` + `AssetAddedEvent` via `AppServices.TryGetEventAggregator()` |
| Timeline handler | `TimelineViewModel.OnAddToTimeline` / `OnSynthesisCompleted` | `Views/Panels/TimelineViewModel.cs` |
| Clip + profile | `AddClipToTrack` | Requires non-empty `ProfileId` on event or `IContextManager.ActiveProfileId` fallback |
| Selection | `MultiSelectService.GetState(PanelIds.Timeline)` | Pass 01 selection-after-insert |

**Honesty (Premium audit alignment):** This slice proves **ViewModel + event** continuity, not full WinUI E2E or `PlayAudioAsync` HTTP playback in CI.

## 3. Tests

- **Class:** `VoiceStudio.App.Tests.ViewModels.WorkflowCoherenceAdvancedTests`
- **Filter:** `FullyQualifiedName~WorkflowCoherenceAdvancedTests`
- **Count:** 3 passed (2026-03-28)

## 4. Mandatory commands (slice claim)

Recorded in lane closure report §6 after full repo verification run.
