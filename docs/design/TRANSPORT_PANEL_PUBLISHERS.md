# Transport Panel Publishers Audit

**Status:** Canonical  
**Last Updated:** 2026-03-15  
**Related:** [Playback Entry Points](PLAYBACK_ENTRY_POINTS.md), Global Transport Hardening Plan, Transport Coherence Wave 2

## Overview

Panels that produce playable audio publish transport context via `IContextManager.SetCurrentPlayable(audioId, source, title)`. This document audits when each panel sets/clears ownership and whether behavior is intentional.

## Panel Publisher Summary

| Panel | When Sets | When Clears | Notes |
|-------|-----------|-------------|-------|
| Library | Selection (when `CanPlayAsset`) | Deselection / clear | Correct |
| Timeline | PlayAudio (synthesis), PlayProjectAudio (project) | Never on stop | Last-writer-wins; user selects Library to reclaim |
| Recording | Upload complete, PlayRecording | — | Correct |
| Synthesis | When LastSynthesizedAudioId set (PlayAudio path) | — | Correct |
| Analyzer | OnSelectedAudioIdChanged (when value set) | When value null | Correct |

## Detailed Audit

### Library (LibraryViewModel)

- **Location:** `src/VoiceStudio.App/ViewModels/LibraryViewModel.cs` (lines 666, 670, 681–682)
- **Sets:** On `SelectedAsset` change, when `CanPlayAsset(value)` → `SetCurrentPlayable(playbackId, TransportSource.Library, value.Name)`
- **Clears:** When selection cleared or asset not playable → `SetCurrentPlayable(null, null, null)`
- **Behavior:** Intentional. Selection drives ownership; clear on deselection.

### Timeline (TimelineViewModel)

- **Location:** `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` (lines 1231, 1801)
- **Sets:** 
  - On `PlayAudioCommand` (synthesis load) → `SetCurrentPlayable(LastSynthesizedAudioId ?? "timeline", TransportSource.Timeline, title)`
  - On `PlayProjectAudioCommand` (project audio load) → `SetCurrentPlayable(audioId, TransportSource.Timeline, title)`
- **Clears:** Does NOT clear on stop.
- **Behavior:** Intentional. Timeline owns while playing; when stopped, ownership remains until another panel sets. User can select Library to reclaim. Last-writer-wins.

### Recording (RecordingViewModel)

- **Location:** `src/VoiceStudio.App/ViewModels/RecordingViewModel.cs` (lines 263, 377)
- **Sets:** 
  - On upload complete → `SetCurrentPlayable(uploadResult.Id, TransportSource.Recording, "Recording")`
  - On `PlayRecordingCommand` → `SetCurrentPlayable(RecordedAudioId, TransportSource.Recording, "Recording")`
- **Clears:** Does not explicitly clear.
- **Behavior:** Intentional. Recording owns after upload and when playing.

### Synthesis (VoiceSynthesisViewModel)

- **Location:** `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` (line 1155)
- **Sets:** When `LastSynthesizedAudioId` is set (in PlayAudio path) → `SetCurrentPlayable(LastSynthesizedAudioId, TransportSource.Synthesis, SelectedProfile?.Name ?? "Synthesis")`
- **Clears:** Does not explicitly clear.
- **Behavior:** Intentional. Synthesis owns when user has synthesized audio and plays.

### Analyzer (AnalyzerViewModel)

- **Location:** `src/VoiceStudio.App/Views/Panels/AnalyzerViewModel.cs` (lines 197–201)
- **Sets:** In `OnSelectedAudioIdChanged` when `value` is non-empty → `SetCurrentPlayable(value, TransportSource.Analyzer, "Analyzer")`
- **Clears:** When `value` is null/empty → `SetCurrentPlayable(null, null, null)`
- **Behavior:** Intentional. Selection drives ownership; clear when nothing selected.

## Regression Test Coverage

`src/VoiceStudio.App.Tests/Services/ContextManagerTests.cs` (lines 74–175) contains 11 transport ownership tests:

| Test | Coverage |
|------|----------|
| SetCurrentPlayable_WithLibrary_UpdatesTransportContext | Library sets |
| SetCurrentPlayable_WithNull_ClearsTransportContext | Clear transport |
| SetCurrentPlayable_LibrarySelectionBeatsIdleTimeline_LastWriterWins | Last-writer-wins (Library beats Timeline) |
| SetCurrentPlayable_TimelineOverwritesLibrary_WhenTimelineSetsAfter | Timeline overwrites Library |
| SetCurrentPlayable_NoSelectedPlayable_ResultsInNullTransport | Null transport |
| SetCurrentPlayable_SameValues_DoesNotRaiseContextChanged | Same values → no event |
| SetCurrentPlayable_TimelineOwnsTransport_WhenTimelineSets | Timeline ownership |
| SetCurrentPlayable_StoppingTimelineDoesNotPermanentlySwallowLibrary_LibraryReclaimsAfterClear | Library reclaims after Timeline clear |
| SetCurrentPlayable_WithSynthesis_UpdatesTransportContext | Synthesis |
| SetCurrentPlayable_WithRecording_UpdatesTransportContext | Recording |
| SetCurrentPlayable_WithAnalyzer_UpdatesTransportContext | Analyzer |

## Ownership Rules

1. **Last-writer-wins:** No panel clears another's ownership. The last panel to call `SetCurrentPlayable` owns transport.
2. **Explicit reclaim:** User selects Library (or another panel) to reclaim ownership from Timeline/Recording/Synthesis.
3. **No stomping:** Panels do not incorrectly overwrite each other; each sets only when the user performs an action in that panel.

## Changelog

- 2026-03-16: Transport Coherence Wave 3 Task 6: Re-verified all call sites; ownership behavior matches doc; no changes.
- 2026-03-15: Transport Coherence Wave 2 Task 4: Verified all call sites; updated line numbers; no behavior changes.
- 2026-03-16: Initial audit; typed `TransportSource` migration complete.
