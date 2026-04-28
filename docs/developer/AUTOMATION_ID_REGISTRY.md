# AutomationId Registry

> **Version**: 1.0.0  
> **Last Updated**: 2026-04-10  
> **Status**: ACTIVE  
> **Owner**: UI Engineer (Role 3)

---

## Purpose

This document is the **authoritative registry** of all stable AutomationId values in VoiceStudio. AutomationIds are treated as **public APIs** — they must not be changed casually as they are used by:

- UI automation tests (FlaUI, WinAppDriver)
- Accessibility tools (screen readers, automation frameworks)
- Quality assurance scripts
- End-to-end testing infrastructure

---

## The Golden Rule

> **AutomationIds are public contracts. Changing them is a breaking change.**

Before modifying any AutomationId:
1. Check if it's used in tests (`src/VoiceStudio.App.Tests/`)
2. Check if it's used in Python UI tests (`tests/ui/`)
3. Update all usages in a single coordinated change
4. Run `scripts/verify.ps1` to ensure tests still pass

---

## Naming Convention

### Standard Format

```
{ViewName}_{ControlType}_{Purpose}
```

Examples:
- `VoiceSynthesisView_SynthesizeButton`
- `ProfilesView_SearchBox`
- `EffectsMixerView_VolumeSlider`

### Panel Root Format

```
{ViewName}_Root
```

Every panel MUST have a root AutomationId on its outermost container:
- `VoiceSynthesisView_Root`
- `ProfilesView_Root`
- `SettingsView_Root`

### Control Type Abbreviations

| Control Type | Abbreviation |
|--------------|--------------|
| Button | Button |
| TextBox | TextBox |
| ComboBox | ComboBox |
| CheckBox | CheckBox |
| Slider | Slider |
| ListView | ListView |
| Grid | Grid |
| Toggle | Toggle |
| InfoBar | InfoBar |
| TabView | TabView |

---

## Shell / MainWindow

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `MainWindow_TitleBarIcon` | FontIcon | Custom title bar app icon (GAP-010) | v1.2.0 |
| `MainWindow_TitleBarText` | TextBlock | Custom title bar caption text | v1.2.0 |
| `MainWindow_AppTitleBarDragRegion` | Border | Custom title bar drag region (`SetTitleBar`) | v1.2.0 |
| `MainWindow_NotificationCenterButton` | Button | Notification Center bell (GAP-067 slice 1) | v1.2.0 |
| `MainWindow_NotificationCenterUnreadBadge` | Border | Unread count badge on bell | v1.2.0 |
| `MainWindow_NotificationCenterFlyout` | Flyout | Notification list flyout | v1.2.0 |
| `MainWindow_NotificationCenterList` | ListView | Notification entries | v1.2.0 |
| `MainWindow_StartupOverlay` | Border | Startup overlay (visible until backend ready) | v1.2.0 |
| `MainWindow_DegradedModeBanner` | InfoBar | Degraded mode (429/backend stress) banner | v1.0.0 |
| `MainWindow_WorkspaceGrid` | Grid | Primary workspace layout (panel hosts) | v1.2.0 |
| `MainWindow_CenterPanelHost` | PanelHost | Center region panel host | v1.2.0 |
| `MainWindow_LeftPanelHost` | PanelHost | Left region panel host | v1.2.0 |
| `StartupOverlay_RetryButton` | Button | Retry backend startup on failure | v1.2.0 |
| `StatusBar_ProcessingIndicator` | Border | Processing status indicator | v1.0.0 |
| `StatusBar_StatusText` | TextBlock | Status text | v1.0.0 |
| `StatusBar_JobStatusText` | TextBlock | Job status | v1.0.0 |
| `StatusBar_JobProgressBar` | ProgressBar | Job progress | v1.0.0 |
| `StatusBar_CurrentMedia` | TextBlock | Current media info | v1.0.0 |
| `MainWindow_StatusBar_SystemMetricsButton` | Button | System metrics flyout (CPU/GPU/RAM, format, latency, collaborators) — GAP-067 slice 5 | v1.2.0 |
| `MainWindow_StatusBar_SystemMetricsFlyout` | Flyout | System metrics flyout surface | v1.2.0 |
| `MainWindow_StatusBar_CollaboratorsButton` | Button | Collaborators toggle inside system metrics flyout — GAP-067 slice 6 | v1.2.0 |
| `CustomizableToolbar_PerformanceOverflowButton` | Button | Toolbar performance metrics flyout — GAP-067 slice 5 | v1.2.0 |
| `CustomizableToolbar_PerformanceFlyout` | Flyout | Dynamic toolbar performance items | v1.2.0 |
| `KeyboardCustomization_SearchBox` | AutoSuggestBox | Shortcut customization search (GAP-065) | v1.2.0 |
| `KeyboardCustomization_ShortcutList` | ListView | Shortcut list for rebinding (GAP-065) | v1.2.0 |
| `KeyboardCustomization_ResetAllButton` | Button | Reset all shortcuts to defaults (GAP-065) | v1.2.0 |

**Taskbar jump list (GAP-067 slice 2)** — not in-app AutomationIds; activation uses process command-line tokens (Win32 `ICustomDestinationList`, unpackaged app):

| Token / pattern | Purpose |
|-----------------|--------|
| `--jumplist-new` | Static task: create new project (same intent as File → New Project) |
| `--jumplist-open-dialog` | Static task: open project picker (same intent as File → Open Project) |
| `--jumplist-open` | Recent item: followed by quoted or unquoted project file path |

---

## FirstRunWizard (GAP-063)

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `FirstRunWizard_Step1Welcome` | StackPanel | Step 1 — Welcome | v1.2.0 |
| `FirstRunWizard_Step2SystemCheck` | StackPanel | Step 2 — System check | v1.2.0 |
| `FirstRunWizard_Step3ModelReadiness` | StackPanel | Step 3 — Model readiness | v1.2.0 |
| `FirstRunWizard_Step4BackendHealth` | StackPanel | Step 4 — Backend connection | v1.2.0 |
| `FirstRunWizard_Step5ApiComplete` | StackPanel | Step 5 — API keys + finish | v1.2.0 |
| `FirstRunWizard_GpuFallbackPanel` | Border | CPU-mode advisory (no NVIDIA GPU) | v1.2.0 |
| `FirstRunWizard_ModelDownloadCta` | Button | Model Manager location CTA | v1.2.0 |
| `FirstRunWizard_NextButton` | Button | Primary navigation | v1.2.0 |
| `FirstRunWizard_SkipButton` | Button | Skip setup | v1.2.0 |
| `FirstRunWizard_DontShowAgainCheckBox` | CheckBox | Repeat-show on startup toggle | v1.2.0 |

---

## GAP-066 Help affordances

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `FirstRunWizard_HelpButton` | Button | Contextual help (steps 3–4: model readiness, backend) | v1.2.0 |
| `KeyboardCustomization_HelpButton` | Button | Shortcut customization help | v1.2.0 |

---

## Registry by Panel

### Core Panels

#### VoiceSynthesisView
Primary voice synthesis interface.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `VoiceSynthesisView_Root` | Grid | Panel root container | v1.0.0 |
| `VoiceSynthesisView_ProfileComboBox` | ComboBox | Voice profile selector | v1.0.0 |
| `VoiceSynthesisView_EngineComboBox` | ComboBox | Engine selector | v1.0.0 |
| `VoiceSynthesisView_LanguageComboBox` | ComboBox | Language selector | v1.0.0 |
| `VoiceSynthesisView_EmotionComboBox` | ComboBox | Emotion selector | v1.0.0 |
| `VoiceSynthesisView_ProfileEngineCompatibilitySummary` | TextBlock | Profile/engine compatibility summary (neutral line) | v1.2.0 |
| `VoiceSynthesisView_ProfilePickerSummary` | TextBlock | Picker filter summary (counts / compatible-only mode) | v1.2.0 |
| `VoiceSynthesisView_CompatibleProfilesOnlyToggle` | ToggleSwitch | Restrict profile ComboBox to known-compatible allow-lists | v1.2.0 |
| `VoiceSynthesisView_SelectFirstCompatibleProfileButton` | Button | Select first profile compatible with current engine selection | v1.2.0 |
| `VoiceSynthesisView_ProfileEngineCompatibilityInfoBar` | InfoBar | Known incompatible profile vs. engine callout | v1.2.0 |
| `VoiceSynthesisView_TextInput` | TextBox | Text input for synthesis | v1.0.0 |
| `VoiceSynthesisView_SynthesizeButton` | Button | Trigger synthesis | v1.0.0 |
| `VoiceSynthesisView_PlayButton` | Button | Play synthesized audio | v1.0.0 |
| `VoiceSynthesisView_StopButton` | Button | Stop playback | v1.0.0 |
| `VoiceSynthesisView_AnalyzeButton` | Button | Analyze output | v1.0.0 |
| `VoiceSynthesisView_RefreshButton` | Button | Refresh profiles | v1.0.0 |
| `VoiceSynthesisView_HelpButton` | Button | Show help | v1.0.0 |
| `VoiceSynthesisView_EnhanceQualityCheckBox` | CheckBox | Quality enhancement toggle | v1.0.0 |
| `VoiceSynthesisView_MultiEngineCheckBox` | CheckBox | Multi-engine mode toggle | v1.0.0 |
| `VoiceSynthesisView_AutoApplyCheckBox` | CheckBox | Auto-apply toggle | v1.0.0 |
| `VoiceSynthesisView_ConsentInfoBar` | InfoBar | Consent-required recovery (navigate to Profiles, retry) | v1.2.0 |
| `VoiceSynthesisView_GoToProfileButton` | Button | Open Profiles for voice consent | v1.2.0 |
| `VoiceSynthesisView_RetryConsentButton` | Button | Retry synthesis after consent is granted | v1.2.0 |
| `VoiceSynthesisView_GeneratedAudioPanel` | Border | Generated audio result summary and actions | v1.2.0 |
| `VoiceSynthesisView_GeneratedAudioSummaryText` | TextBlock | Generated audio ID/reference summary | v1.2.0 |
| `VoiceSynthesisView_CopyAudioIdButton` | Button | Copy generated audio ID | v1.2.0 |
| `VoiceSynthesisView_CopyAudioReferenceButton` | Button | Copy generated audio URL/path/reference | v1.2.0 |
| `VoiceSynthesisView_OpenOutputLocationButton` | Button | Open local output file/folder when available | v1.2.0 |
| `VoiceSynthesisView_PlaybackErrorInfoBar` | InfoBar | Playback error diagnostics and recovery | v1.2.0 |
| `VoiceSynthesisView_RetryPlaybackButton` | Button | Retry failed playback | v1.2.0 |
| `VoiceSynthesisView_CopyPlaybackErrorButton` | Button | Copy playback error details to clipboard | v1.2.0 |
| `VoiceSynthesisView_RecentResultsPanel` | Border | Recent synthesis results mini-list (in-memory, max 5) | v1.2.0 |
| `VoiceSynthesisView_RecentResultsList` | ListView | Recent synthesis entries | v1.2.0 |
| `VoiceSynthesisView_RestoreRecentResultButton` | Button | Restore a recent result as the active generated audio | v1.2.0 |
| `VoiceSynthesisView_ClearRecentResultsButton` | Button | Clear all recent synthesis entries from the in-memory list | v1.2.0 |
| `VoiceSynthesisView_RemoveRecentResultButton` | Button | Remove one recent synthesis entry from the in-memory list | v1.2.0 |
| `VoiceSynthesisView_ErrorInfoBar` | InfoBar | Error display (hidden while consent callout is primary) | v1.0.0 |
| `VoiceSynthesisView_LongFormToggle` | CheckBox | Long-form (chunked) synthesis mode | v1.2.0 |
| `VoiceSynthesisView_LongFormProgressText` | TextBlock | Long-form processing status | v1.2.0 |
| `VoiceSynthesisView_AdvancedControlsExpander` | Expander | Advanced synthesis sliders + mode toggles — GAP-067 slice 5 | v1.2.0 |

#### SpeechToSpeechView
Batch speech-to-speech conversion (RVC) — GAP-051.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `SpeechToSpeechView_Root` | StackPanel | Panel root container | v1.2.0 |
| `SpeechToSpeechView_SourceAudioSelector` | TextBox | Source artifact audio id | v1.2.0 |
| `SpeechToSpeechView_TargetVoiceSelector` | ComboBox | Target voice profile | v1.2.0 |
| `SpeechToSpeechView_ConvertButton` | Button | Run conversion | v1.2.0 |
| `SpeechToSpeechView_ConsentCheckBox` | CheckBox | User consent acknowledgement gate | GAP-055 |
| `SpeechToSpeechView_StatusText` | TextBlock | Status / progress text | v1.2.0 |
| `SpeechToSpeechView_OutputAudioLink` | TextBlock | Output audio URL line | v1.2.0 |
| `SpeechToSpeechView_DisclosureText` | TextBlock | Transformed-output disclosure label | GAP-056 |
| `SpeechToSpeechView_MarkingBadge` | TextBlock | Durable-marking verified indicator | GAP-056 slice 2 |

#### ProfilesView
Voice profile management.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `ProfilesView_Root` | Grid | Panel root container | v1.0.0 |
| `ProfilesView_CreateButton` | Button | Create new profile | v1.0.0 |
| `ProfilesView_RefreshButton` | Button | Refresh profile list | v1.0.0 |
| `ProfilesView_HelpButton` | Button | Show help | v1.0.0 |
| `ProfilesView_SearchBox` | TextBox | Search profiles | v1.0.0 |
| `ProfilesView_ProfilesGrid` | DataGrid | Profile list display | v1.0.0 |
| `ProfilesView_CompatibleEnginesSummary` | TextBlock | Compatible engines summary line | 2026-04-28 |
| `ProfilesView_CompatibleEnginesList` | ListView | Allowed engine ids for selected profile | 2026-04-28 |
| `ProfilesView_RemoveEngineButton` | Button | Remove one engine id from compatibility list | 2026-04-28 |
| `ProfilesView_AddEngineTextBox` | TextBox | Type engine id to add to compatibility list | 2026-04-28 |
| `ProfilesView_AddEngineButton` | Button | Add typed engine id | 2026-04-28 |
| `ProfilesView_SaveCompatibleEnginesButton` | Button | Persist compatibility list (`vs:engines:` tags) | 2026-04-28 |
| `ProfilesView_ClearCompatibleEnginesButton` | Button | Clear all compatible engine restrictions | 2026-04-28 |
| `ProfilesView_BatchExportButton` | Button | Batch export profiles | v1.0.0 |
| `ProfilesView_BatchDeleteButton` | Button | Batch delete profiles | v1.0.0 |

#### ModelManagerView
Model storage, import, and URL download (GAP-043).

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `ModelManagerView_Root` | Grid | Panel root container | v1.0.0 |
| `ModelManager.DownloadUrl` | TextBox | Remote model URL | v1.1.0 |
| `ModelManager.DownloadModelName` | TextBox | Target model name | v1.1.0 |
| `ModelManager.DownloadVersion` | TextBox | Model version | v1.1.0 |
| `ModelManager.DownloadExpectedSha256` | TextBox | Optional SHA-256 gate | v1.1.0 |
| `ModelManager.DownloadEngine` | ComboBox | Engine for download | v1.1.0 |
| `ModelManager.StartDownload` | Button | Start canonical download job | v1.1.0 |
| `ModelManager.CancelDownload` | Button | Cancel job via `/api/jobs` | v1.1.0 |
| `ModelManager.RetryDownload` | Button | Retry failed download job | v1.1.0 |
| `ModelManager.PauseDownload` | Button | Pause running download job | v1.1.0 |
| `ModelManager.ResumeDownload` | Button | Resume paused download job | v1.1.0 |

#### EffectsMixerView
Audio effects and mixing.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `EffectsMixerView_Root` | Grid | Panel root container | v1.0.0 |
| `EffectsMixerView_MixerPresetsComboBox` | ComboBox | Mixer preset selector | v1.0.0 |
| `EffectsMixerView_RealTimeToggle` | ToggleSwitch | Real-time processing toggle | v1.0.0 |
| `EffectsMixerView_SaveMixerButton` | Button | Save mixer state | v1.0.0 |
| `EffectsMixerView_ResetMixerButton` | Button | Reset mixer | v1.0.0 |
| `EffectsMixerView_HelpButton` | Button | Show help | v1.0.0 |
| `EffectsMixerView_ChannelsItemsControl` | ItemsControl | Channel list | v1.0.0 |
| `EffectsMixerView_VolumeSlider` | Slider | Channel volume | v1.0.0 |
| `EffectsMixerView_PanSlider` | Slider | Channel pan | v1.0.0 |
| `EffectsMixerView_MuteButton` | Button | Mute channel | v1.0.0 |
| `EffectsMixerView_SoloButton` | Button | Solo channel | v1.0.0 |
| `EffectsMixerView_ClearSelectionButton` | Button | Clear selection | v1.0.0 |
| `EffectsMixerView_AddChainButton` | Button | Add effect chain | v1.0.0 |
| `EffectsMixerView_NewChainNameTextBox` | TextBox | New chain name input | v1.0.0 |
| `EffectsMixerView_CreateChainButton` | Button | Create chain | v1.0.0 |
| `EffectsMixerView_EffectChainsListView` | ListView | Effect chains list | v1.0.0 |
| `EffectsMixerView_StudioSoundButton` | Button | One-click Studio Sound (denoise → compressor → normalize) | v1.0.0 |
| `EffectsMixerView_AddEffectComboBox` | ComboBox | Add effect selector | v1.0.0 |
| `EffectsMixerView_EffectsListView` | ListView | Effects list | v1.0.0 |
| `EffectsMixerView_MasterVolumeSlider` | Slider | Master volume | v1.0.0 |
| `EffectsMixerView_MasterPanSlider` | Slider | Master pan | v1.0.0 |
| `EffectsMixerView_MasterMuteButton` | Button | Master mute | v1.0.0 |

#### ScriptEditorView
Script editor for multi-segment voice synthesis.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `ScriptEditorView_Root` | Grid | Panel root container | v1.1.0 |
| `ScriptEditorView_RefreshButton` | Button | Refresh scripts list | v1.1.0 |
| `ScriptEditorView_HelpButton` | Button | Show help overlay | v1.1.0 |
| `ScriptEditorView_ProjectComboBox` | ComboBox | Project selector | v1.1.0 |
| `ScriptEditorView_SearchBox` | TextBox | Search scripts | v1.1.0 |
| `ScriptEditorView_ScriptsList` | ListView | Scripts list | v1.1.0 |
| `ScriptEditorView_CreateButton` | Button | Create new script | v1.1.0 |
| `ScriptEditorView_ScriptName` | TextBox | Script name input | v1.1.0 |
| `ScriptEditorView_ScriptDescription` | TextBox | Script description input | v1.1.0 |
| `ScriptEditorView_AddSegmentButton` | Button | Add segment | v1.1.0 |
| `ScriptEditorView_SegmentsList` | ListView | Segments list | v1.1.0 |
| `ScriptEditorView_SaveButton` | Button | Save script | v1.1.0 |
| `ScriptEditorView_DeleteButton` | Button | Delete script | v1.1.0 |
| `ScriptEditorView_StatusText` | TextBlock | Status message | v1.1.0 |
| `NavScript` | Button | Navigation to Script Editor panel | v1.1.0 |

#### AnalyzerView
Audio analysis tools.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `AnalyzerView_Root` | Grid | Panel root container | v1.0.0 |
| `Analyzer_TabView` | TabView | Analysis mode tabs | v1.0.0 |
| `Analyzer_HelpButton` | Button | Show help | v1.0.0 |
| `Analyzer_BrowseButton` | Button | Browse for audio file | v1.0.0 |
| `Analyzer_AudioIdTextBox` | TextBox | Audio ID input | v1.0.0 |
| `Analyzer_LoadButton` | Button | Load audio | v1.0.0 |

#### TimelineView
Main timeline / arrangement surface.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `TimelineView_Root` | Grid | Panel root container | v1.0.0 |
| `TimelineView_TransportMoreButton` | Button | More transport flyout (record, loop, zoom) — GAP-067 slice 5 | v1.2.0 |
| `TimelineView_TransportMoreFlyout` | Flyout | Transport overflow content | v1.2.0 |
| `TimelineView_AddTrackButton` | Button | Add track (primary transport) — GAP-067 slice 5 | v1.2.0 |
| `TimelineView_PlayButton` | Button | Play transport button — GAP-067 slice 6 | v1.2.0 |
| `TimelineView_StopButton` | Button | Stop transport button — GAP-067 slice 6 | v1.2.0 |
| `TimelineView_OpenRecordingButton` | Button | Open Recording inside transport flyout — GAP-067 slice 6 | v1.2.0 |
| `TimelineView_LoopToggle` | ToggleSwitch | Loop playback inside transport flyout — GAP-067 slice 6 | v1.2.0 |
| `TimelineView_ZoomInButton` | Button | Zoom in inside transport flyout — GAP-067 slice 6 | v1.2.0 |
| `TimelineView_ZoomOutButton` | Button | Zoom out inside transport flyout — GAP-067 slice 6 | v1.2.0 |

#### MiniTimelineView
Compact playback timeline.

| AutomationId | Control Type | Purpose | Stable Since |
|--------------|--------------|---------|--------------|
| `MiniTimelineView_Root` | Grid | Panel root container | v1.0.0 |
| `MiniTimeline_PlayPauseButton` | Button | Play/pause toggle | v1.0.0 |
| `MiniTimeline_StopButton` | Button | Stop playback | v1.0.0 |
| `MiniTimeline_ZoomOutButton` | Button | Zoom out timeline | v1.0.0 |
| `MiniTimeline_ZoomInButton` | Button | Zoom in timeline | v1.0.0 |

### Panel Root IDs (All Panels)

Every panel has a `_Root` AutomationId for test navigation:

| Panel | AutomationId |
|-------|--------------|
| VoiceSynthesisView | `VoiceSynthesisView_Root` |
| ProfilesView | `ProfilesView_Root` |
| EffectsMixerView | `EffectsMixerView_Root` |
| AnalyzerView | `AnalyzerView_Root` |
| TimelineView | `TimelineView_Root` |
| TrainingView | `TrainingView_Root` |
| QualityBenchmarkView | `QualityBenchmarkView_Root` |
| LibraryView | `LibraryView_Root` |
| LibraryView (context — Add to Timeline, GAP-027) | `LibraryView_Menu_AddToTimeline` |
| SettingsView | `SettingsView_Root` |
| DiagnosticsView | `DiagnosticsView_Root` |
| JobProgressView | `JobProgressView_Root` |
| GPUStatusView | `GPUStatusView_Root` |
| MCPDashboardView | `MCPDashboardView_Root` |
| SLODashboardView | `SLODashboardView_Root` |
| PluginManagementView | `PluginManagementView_Root` |
| TextBasedSpeechEditorView | `TextBasedSpeechEditorView_Root` |
| MiniTimelineView | `MiniTimelineView_Root` |
| MacroView | `MacroView_Root` |
| TodoPanelView | `TodoPanelView_Root` |
| VoiceBrowserView | `VoiceBrowserView_Root` |
| VoiceQuickCloneView | `VoiceQuickCloneView_Root` |
| VoiceMorphView | `VoiceMorphView_Root` |
| VoiceMorphingBlendingView | `VoiceMorphingBlendingView_Root` |
| BatchProcessingView | `BatchProcessingView_Root` |
| BackupRestoreView | `BackupRestoreView_Root` |
| BackupRestoreView (merge expectation hint) | `BackupRestore_MergeExpectationHint` |
| BackupRestoreView (restore busy) | `BackupRestore_RestoreBusyRow` |
| BackupRestoreView (cancel restore) | `BackupRestore_CancelRestoreButton` |
| TranscribeView (persistence scope footnote, Pass 01) | `TranscribeView_PersistenceScopeFootnote` |
| TranscribeView (GAP-045 inline segment edit hint) | `TranscribeView_SegmentEditOperatorHint` |
| TranscribeView (segment row busy ring; one per segment in ItemsRepeater template) | `TranscribeView_SegmentBusyRing` |
| TranscribeView (GAP-045 session edit history list) | `TranscribeView_EditHistoryList` |
| TranscribeView (GAP-045 clear session edit history) | `TranscribeView_ClearEditHistoryButton` |
| TranscribeView (GOV-EDIT-APPLY-JOB-STATUS: apply/regenerate job status list) | `TranscribeView_ApplyJobStatusList` |
| TranscribeView (GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01: retry failed job row) | `TranscribeView_ApplyJobRetryButton` |
| TranscribeView (GOV-EDIT-APPLY-JOB-STATUS: clear job status rows) | `TranscribeView_ClearApplyJobStatusButton` |
| TranscribeView (GAP-067 slice 5 advanced options) | `TranscribeView_AdvancedOptionsExpander` |
| LibraryView (import vs drag-drop scope footnote, Pass 01 slice 2) | `LibraryView_ImportDragDropScopeFootnote` |
| TrainingView (surface maturity footnote, Pass 01 slice 3) | `TrainingView_SurfaceMaturityFootnote` |
| QualityBenchmarkView (surface maturity footnote, Pass 01 slice 4) | `QualityBenchmarkView_SurfaceMaturityFootnote` |
| QualityBenchmarkView (W8-C1 operational shell, Pass 08) | `QualityBenchmarkView_HelpButton` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_ErrorInfoBar` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_ProfileComboBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_TestTextBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_EngineXTTSCheckBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_EngineChatterboxCheckBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_EngineTortoiseCheckBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_EnhanceQualityCheckBox` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_RunButton` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_LoadingRing` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_ResultsSummary` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_NextStepHint` |
| QualityBenchmarkView (W8-C1) | `QualityBenchmarkView_ResultsListView` |
| QualityBenchmarkView (GAP-052 side-by-side comparison) | `QualityBenchmarkView_RunComparisonButton` |
| QualityBenchmarkView (GAP-052 comparison result cards) | `QualityBenchmarkView_ComparisonSlots` |
| ProfileComparisonView (Pass 08 W8-C3) | `ProfileComparisonView_Root` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_HelpButton` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ErrorInfoBar` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ProfileAComboBox` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ProfileBComboBox` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_PreviewTextBox` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_EngineComboBox` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_CompareButton` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_CompareProgressRing` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_LoadingProgressRing` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ScoreA` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ScoreB` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_ScoreDifference` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_PlayAButton` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_PlayBButton` |
| ProfileComparisonView (W8-C3) | `ProfileComparisonView_StopButton` |
| ABTestingView (Pass 08 W8-C2) | `ABTestingView_Root` |
| ABTestingView (W8-C2) | `ABTestingView_HelpButton` |
| ABTestingView (W8-C2) | `ABTestingView_ErrorInfoBar` |
| ABTestingView (W8-C2) | `ABTestingView_ProfileComboBox` |
| ABTestingView (W8-C2) | `ABTestingView_TestTextBox` |
| ABTestingView (W8-C2) | `ABTestingView_EngineATextBox` |
| ABTestingView (W8-C2) | `ABTestingView_EngineBTextBox` |
| ABTestingView (W8-C2) | `ABTestingView_EmotionATextBox` |
| ABTestingView (W8-C2) | `ABTestingView_EmotionBTextBox` |
| ABTestingView (W8-C2) | `ABTestingView_EnhanceQualityACheckBox` |
| ABTestingView (W8-C2) | `ABTestingView_EnhanceQualityBCheckBox` |
| ABTestingView (W8-C2) | `ABTestingView_RunButton` |
| ABTestingView (W8-C2) | `ABTestingView_LoadingRing` |
| ABTestingView (W8-C2) | `ABTestingView_SampleAMetrics` |
| ABTestingView (W8-C2) | `ABTestingView_SampleBMetrics` |
| ABTestingView (W8-C2) | `ABTestingView_ComparisonSummary` |
| ABTestingView (W8-C2) | `ABTestingView_PlaySampleAButton` |
| ABTestingView (W8-C2) | `ABTestingView_PlaySampleBButton` |
| DatasetQAView | `DatasetQAView_Root` |
| DeepfakeCreatorView | `DeepfakeCreatorView_Root` |
| ImageGenView | `ImageGenView_Root` |
| VideoGenView | `VideoGenView_Root` |
| UpscalingView | `UpscalingView_Root` |
| ImageVideoEnhancementPipelineView | `ImageVideoEnhancementPipelineView_Root` |

---

## Rules for Adding New AutomationIds

### When to Add

Add an AutomationId when the control:
1. Is a primary action button (Save, Delete, Submit, etc.)
2. Is a key input field (search, text input, etc.)
3. Is needed for test navigation (panel roots)
4. Must be accessible to automation tools
5. Is part of a critical user workflow

### How to Add

1. **Choose a name** following the naming convention:
   ```
   {ViewName}_{ControlType}_{Purpose}
   ```

2. **Add to XAML**:
   ```xml
   <Button AutomationProperties.AutomationId="MyView_SaveButton"
           Content="Save" />
   ```

3. **Update this registry** with:
   - AutomationId
   - Control type
   - Purpose
   - Version it was added

4. **Add a test** that uses the new ID:
   ```csharp
   [TestMethod]
   public async Task SaveButton_Exists()
   {
       await NavigateToPanelAsync("MyView");
       var button = FindElement("MyView_SaveButton");
       Assert.IsNotNull(button);
   }
   ```

5. **Run verification**:
   ```powershell
   .\scripts\verify.ps1 -SkipIntegration
   ```

### What NOT to Do

- Do NOT add AutomationIds to every control (only key ones)
- Do NOT use dynamic or generated IDs
- Do NOT change existing IDs without updating all usages
- Do NOT use spaces or special characters in IDs

---

## Deprecation Process

When an AutomationId must be changed or removed:

1. **Mark as deprecated** in this registry with removal version
2. **Add a migration note** to CHANGELOG.md
3. **Update all test usages** in the same PR
4. **Keep old ID for 1 minor version** if possible
5. **Remove in next major version**

### Deprecated IDs

| Old ID | New ID | Deprecated Since | Remove In |
|--------|--------|------------------|-----------|
| (none) | — | — | — |

---

## Test Helper Integration

### C# Test Helpers

The `SmokeTestBase` class uses these IDs:

```csharp
// Find element by AutomationId
var button = FindElement("VoiceSynthesisView_SynthesizeButton");

// Click button by AutomationId
await ClickButtonAsync("VoiceSynthesisView_SynthesizeButton");

// Navigate to panel (uses _Root IDs)
await NavigateToPanelAsync("VoiceSynthesis");
```

### Python Test Helpers

The `ElementHelper` class uses these IDs:

```python
# Find element by AutomationId
button = element_helper.find_by_id("VoiceSynthesisView_SynthesizeButton")

# Click button
element_helper.click_button("VoiceSynthesisView_SynthesizeButton")

# Navigate to panel
navigation_helper.navigate_to_panel("VoiceSynthesis")
```

---

## Accessibility Integration

AutomationIds work with other accessibility properties:

```xml
<Button AutomationProperties.AutomationId="VoiceSynthesisView_SynthesizeButton"
        AutomationProperties.Name="Synthesize voice"
        AutomationProperties.HelpText="Generate audio from the text input"
        Content="Synthesize" />
```

| Property | Purpose |
|----------|---------|
| `AutomationId` | Stable identifier for automation |
| `Name` | Human-readable label (read by screen readers) |
| `HelpText` | Extended description (shown in tooltips) |

---

## Verification

Run this command to check AutomationId coverage:

```powershell
# Count AutomationIds in XAML files
Get-ChildItem -Path src -Filter *.xaml -Recurse | 
    Select-String 'AutomationProperties.AutomationId' | 
    Measure-Object

# Check for missing _Root IDs
Get-ChildItem -Path src/VoiceStudio.App/Views/Panels -Filter *View.xaml | 
    ForEach-Object { 
        $content = Get-Content $_.FullName -Raw
        if ($content -notmatch 'AutomationProperties.AutomationId="\w+_Root"') {
            Write-Host "MISSING ROOT: $($_.Name)"
        }
    }
```

---

## Related Documents

- [UI_TESTING_GUIDE.md](UI_TESTING_GUIDE.md) - UI testing documentation
- [UI_TEST_HOOKS.md](../design/UI_TEST_HOOKS.md) - Test hook specification
- [SmokeTestBase.cs](../../src/VoiceStudio.App.Tests/UI/SmokeTestBase.cs) - C# test base class
- [helpers.py](../../tests/ui/helpers.py) - Python test helpers
