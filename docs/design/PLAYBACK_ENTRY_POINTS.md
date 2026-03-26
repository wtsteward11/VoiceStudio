# Playback Entry Points Map

**Date:** 2026-03-16  
**Purpose:** Document who owns playback today and where it diverges from a global transport model.  
**Related:** [Global Transport UX Plan](.cursor/plans/global_transport_ux_plan_69a81ee5.plan.md)

---

## Summary

| Entry Point | Owner | Divergence from Global Transport |
|-------------|-------|-----------------------------------|
| Main Play (toolbar/menu) | MainWindow.TogglePlayback | **Global transport.** Uses IContextManager.CurrentPlayableAudioId/SourcePanel. Routes to Library, Timeline, Synthesis, Recording, Analyzer. |
| Library Play | LibraryViewModel.PlayAsset | Panel-local. Publishes SetCurrentPlayable on selection. Main Play targets Library when source is Library. |
| Timeline Play | TimelineViewModel.PlayAudioCommand | Panel-local. Publishes SetCurrentPlayable. Main Play delegates when source is Timeline. |
| Synthesis Play | VoiceSynthesisViewModel.PlayAudioCommand | Panel-local. Publishes SetCurrentPlayable. Main Play targets Synthesis when source is Synthesis. |
| Recording Play | RecordingViewModel | Panel-local. Publishes SetCurrentPlayable. Main Play targets Recording when source is Recording. |
| Analyzer | AnalyzerViewModel | Publishes SetCurrentPlayable on SelectedAudioId. Main Play targets Analyzer when source is Analyzer. |
| VoiceBrowser | VoiceBrowserViewModel | Panel-local. Plays voice preview. |
| SSML Control | SSMLControlViewModel | Panel-local. Plays preview/synthesis. |
| TextSpeechEditor | TextSpeechEditorViewModel | Panel-local. Plays preview. |
| PronunciationLexicon | PronunciationLexiconViewModel | Panel-local. Plays test audio. |
| Toolbar Play | CustomizableToolbar → TogglePlayback | Invokes MainWindow.TogglePlayback (global transport). |
| Menu Play/Pause | MainWindow.Menu → TogglePlayback | Same as toolbar. |
| PlaybackRequestedEvent | AudioPlayerService | Subscribes to PlaybackRequestedEvent; Library fallback path publishes this when IAudioPlayerService unavailable. |

---

## Detailed Entry Points

### 1. Main Play (MainWindow.TogglePlayback)

- **File:** `src/VoiceStudio.App/MainWindow.xaml.cs` lines 2530-2551
- **Trigger:** Toolbar Play button, Menu "Play/Pause", keyboard shortcut (Space)
- **Behavior:** Only works when `CenterPanelHost.Content is TimelineView`. Calls `TimelineViewModel.PlayAudioCommand` or `PauseAudioCommand`.
- **Divergence:** No global transport. Does not check ContextManager. Does not play Library/imported audio.

### 2. Main Stop (MainWindow.StopPlayback)

- **File:** `src/VoiceStudio.App/MainWindow.xaml.cs` lines 2553-2564
- **Behavior:** Same as TogglePlayback — Timeline-only.
- **Divergence:** No global transport.

### 3. Library Play (LibraryViewModel.PlayAsset)

- **File:** `src/VoiceStudio.App/ViewModels/LibraryViewModel.cs` lines 798-845
- **Trigger:** LibraryView double-click or Play button on asset
- **Behavior:** Uses `IAudioPlayerService.PlayBackendAudioIdAsync(playbackId, baseUrl)` or `PlayFileAsync(path)`. Calls `GetPlaybackAudioId(asset)` for backend ID.
- **Divergence:** Panel-local. Does not set global transport context. Main Play cannot target Library selection.

### 4. Timeline Play (TimelineViewModel.PlayAudioCommand)

- **File:** `src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs` line 416
- **Trigger:** TimelineView Play button, Space key in Timeline
- **Behavior:** Timeline-specific playback.
- **Divergence:** Main Play delegates here when center is Timeline. No transport ownership concept.

### 5. Synthesis Play (VoiceSynthesisViewModel.PlayAudioCommand)

- **File:** `src/VoiceStudio.App/Views/Panels/VoiceSynthesisViewModel.cs` line 301
- **Trigger:** Synthesis panel Play button
- **Behavior:** Plays synthesis output via IAudioPlayerService.
- **Divergence:** Panel-local. Main Play cannot target synthesis output.

### 6. Recording Play (RecordingViewModel)

- **File:** `src/VoiceStudio.App/ViewModels/RecordingViewModel.cs` line 373
- **Behavior:** `PlayBackendAudioIdAsync(RecordedAudioId, baseUrl)`
- **Divergence:** Panel-local.

### 7. Import Flow (MainWindow.ImportAudioFile)

- **File:** `src/VoiceStudio.App/MainWindow.xaml.cs` lines 2040-2165
- **Behavior:** Uploads file, publishes `AssetAddedEvent`, calls `SetCurrentPlayable(uploadResult.Id, "Library", fileName)` and `SetActiveAsset`.
- **Status:** Import establishes transport context. Main Play plays imported file immediately.

### 8. ContextManager (Transport Context)

- **File:** `src/VoiceStudio.App/Services/ContextManager.cs`
- **Behavior:** Has CurrentPlayableAudioId, CurrentPlayableSourcePanel, CurrentPlayableTitle, SetCurrentPlayable(). Panels publish transport on selection.
- **Status:** MainWindow.TogglePlayback uses transport context. Global transport strip reflects state.

---

---

## Task 7: Panel Play Affordances Audit

| Panel | Play Control | Selection Rule | Iconography | Disabled When |
|-------|--------------|----------------|-------------|---------------|
| Library | PlayAssetCommand (double-click, Play button) | SelectedAsset | Play icon | CanPlayAsset false |
| Timeline | PlayAudioCommand | Timeline has loaded audio | Play/Pause | No audio loaded |
| Synthesis | PlayAudioCommand | Synthesis output exists | Play icon | No output |
| Recording | Play via RecordedAudioId | After recording | Play icon | No recording |
| Analyzer | IAudioPlayerService if present | Analyzed/loaded audio | — | Varies |

**Standardization notes:** All panels use IAudioPlayerService for backend audio. Library and Synthesis use PlayBackendAudioIdAsync. Timeline has its own playback model. Consistency: panels that can play should call SetCurrentPlayable when user selects/creates playable output (Tasks 8–10).

---

## Task 14: Fallback Paths (Simplified)

**Current state:** Main transport uses direct `IContextManager` + `IAudioPlayerService`. No event-aggregator indirection for TogglePlayback/StopPlayback. Control flow: read CurrentPlayableAudioId/SourcePanel → route to Timeline or IAudioPlayerService. Single primary path; no "try this, fallback to that" in main transport.

---

## Task 16: Track B (Release Trust) Awareness

**Actions:** Do not remove or bypass `verify.ps1` / `run_verification.py` before release. Run full verify before tagging. If `taskkill` is needed, document it; do not pretend teardown is fully clean if it is not.

**Status:** Release-trust gates remain in place. See `docs/reports/verification/HARDENING_WAVE_CLOSURE_2026.md` and `.cursor/STATE.md` for current release-trust state.

---

## Changelog

- 2026-03-16: Initial document. Maps all playback entry points for Global Transport UX Plan Task 1.
- 2026-03-16: Task 7 audit: panel play affordances summary.
- 2026-03-16: Tasks 14–16: fallback paths documented, status bar current media, Track B awareness.
