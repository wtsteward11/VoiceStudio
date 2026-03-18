# Panel Wiring Catalog

> **Single Source of Truth** for inter-panel event wiring, backend endpoints, and shared services.
> Cross-reference: [PANEL_COMMUNICATION_MATRIX](../architecture/PANEL_COMMUNICATION_MATRIX.md)
>
> **Last Updated**: 2026-03-06  
> **Last Verified**: 2026-03-17 (Premium Reliability Task 5 re-audit)  
> **Status**: Active

## Overview

This catalog enumerates every panel's event publish/subscribe usage, backend API endpoints, shared services, and throttle/debounce policies. All inter-panel communication MUST go through `EventAggregator`; direct panel-to-panel calls are forbidden.

## Panel List (from CorePanelRegistrationService + ModulePanelRegistrationService)

Core panels: VoiceSynthesis, EnsembleSynthesis, BatchProcessing, TrainingDatasetEditor, ModelManager, Training, Transcribe, Recording, AudioAnalysis, QualityControl, Timeline, Profiles, Library, EffectsMixer, Analyzer, VoiceMorph, EmotionControl, Diagnostics, Settings, Help, SSMLControl, VoiceQuickClone, QualityDashboard, QualityBenchmark, ImageGen, VideoGen, DeepfakeCreator, DatasetQA, ScriptEditor, SceneBuilder, Macro, WorkflowAutomation, AdvancedSettings, APIKeyManager, GPUStatus, TodoPanel, TextHighlighting, UltimateDashboard.

Module panels: VoiceCloningWizard, MultiVoiceGenerator, RealTimeConverter, EmotionStyle, Multilingual, Spectrogram, RealTimeVisualizer, Sonography, QualityOptimizer, ABTesting, ProfileComparison, Upscaling, ImageSearch, VideoEdit, Automation, PresetLibrary, TemplateLibrary, TagManager, MarkerManager, BackupRestore, PluginManagement, HealthCheck, JobProgress, MCPDashboard.

---

## Core Workflow Panels (Event-Driven)

### Profiles (ProfilesView / ProfilesViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `ProfileSelectedEvent`, `ProfileUpdatedEvent`, `ProfileDeletedEvent` |
| **Subscribes** | `ProfileCreatedEvent` |
| **Backend** | `/api/profiles/{id}/preprocess-reference` |
| **Shared Services** | MultiSelectService, ToastNotificationService, IEventAggregator |
| **Throttle** | None (low-frequency) |
| **Unsubscribe** | ✅ IPanelLifecycle.OnDeactivatedAsync disposes token |

### Library (LibraryView / LibraryViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `AssetSelectedEvent`, `CloneReferenceSelectedEvent`, `VoiceProfileSelectedEvent`, `PlaybackRequestedEvent` |
| **Subscribes** | `ProfileSelectedEvent` (L173), `AssetAddedEvent` (L177), `ProfileCreatedEvent` (L181), `SynthesisCompletedEvent` (L185) |
| **Backend** | `/api/library/folders`, `/api/library/assets`, `/api/library/search`, etc. |
| **Shared Services** | MultiSelectService, ToastNotificationService, UndoRedoService, IEventAggregator, IContextManager, IWorkflowCoordinatorService |
| **Throttle** | Search: 300ms debounce (SearchDebounceMs) |
| **Unsubscribe** | ✅ IPanelLifecycle.OnDeactivatedAsync disposes all tokens |

### Timeline (TimelineView / TimelineViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `PlaybackStateChangedEvent`, `TimelineSelectionChangedEvent`, `PlaybackRequestedEvent` |
| **Subscribes** | `NavigateToEvent`, `AddToTimelineEvent`, `TranscriptionCompletedEvent`, `SynthesisCompletedEvent` (GAP-W2) |
| **Backend** | `/api/timeline/*`, `/api/projects/*`, `/api/audio/*` |
| **Shared Services** | IBackendClient, IAudioPlayerService, MultiSelectService, ToastNotificationService, UndoRedoService, ISettingsService, RecentProjectsService, IEventAggregator |
| **Throttle** | PlaybackStateChangedEvent — **SHOULD THROTTLE** (high-frequency) |
| **Unsubscribe** | ✅ IPanelLifecycle.OnDeactivatedAsync disposes all tokens |

### VoiceSynthesis (VoiceSynthesisView / VoiceSynthesisViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `SynthesisCompletedEvent`, `AddToTimelineEvent`, `AssetAddedEvent` |
| **Subscribes** | `ProfileSelectedEvent` (L273), `EngineChangedEvent`, `EngineSettingsChangedEvent` (via SynthesisViewModel) |
| **Backend** | `/api/voice/synthesize/stream`, `/api/audio/{id}` |
| **Shared Services** | IBackendClient, IEventAggregator, IContextManager, IAudioPlayerService, IQualityService |
| **Throttle** | None |
| **Unsubscribe** | ✅ IPanelLifecycle.OnDeactivatedAsync disposes ProfileSelectedEvent token |

### Synthesis (SynthesisViewModel - Feature)

| Category | Details |
|----------|---------|
| **Publishes** | `SynthesisCompletedEvent` (L499) |
| **Subscribes** | `VoiceProfileSelectedEvent` (L154) |
| **Backend** | Via BackendClient |
| **Shared Services** | IEventAggregator |
| **Throttle** | None |
| **Unsubscribe** | ❌ No Unsubscribe — **Known debt** (constructor subscription; no Dispose/OnDeactivatedAsync) |

### Training (TrainingView / TrainingViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `ProfileCreatedEvent`, `JobStartedEvent` |
| **Subscribes** | `ProfileSelectedEvent` (per matrix) — verify in code |
| **Backend** | `/api/training/*`, `/api/jobs/*` |
| **Shared Services** | IEventAggregator, JobProgressWebSocketClient |
| **Throttle** | None |
| **Unsubscribe** | WebSocket events only; EventAggregator not verified |

### VoiceQuickClone (VoiceQuickCloneView / VoiceQuickCloneViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `ProfileCreatedEvent`, `NavigateToEvent` |
| **Subscribes** | `CloneReferenceSelectedEvent` (L73) |
| **Backend** | Clone-related APIs |
| **Shared Services** | IEventAggregator |
| **Throttle** | None |
| **Unsubscribe** | ❌ No Unsubscribe — **Known debt** (constructor subscription; no Dispose/OnDeactivatedAsync) |

### VoiceCloningWizard (VoiceCloningWizardView / VoiceCloningWizardViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `ProfileCreatedEvent`, `NavigateToEvent` |
| **Subscribes** | `CloneReferenceSelectedEvent` (L118) |
| **Backend** | `/api/voice/clone/wizard/*`, `/api/audio/upload` |
| **Shared Services** | IEventAggregator |
| **Throttle** | None |
| **Unsubscribe** | ❌ No Unsubscribe — **Known debt** (constructor subscription; no Dispose/OnDeactivatedAsync) |

### ScriptEditor (ScriptEditorView / ScriptEditorViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | None |
| **Subscribes** | Selection/timeline via handler (OnActivatedAsync) |
| **Backend** | Script/SSML APIs |
| **Shared Services** | IScriptEditorClient |
| **Throttle** | None |
| **Unsubscribe** | ✅ OnDeactivatedAsync unsubscribes selection handler |

### Transcribe (TranscribeView / TranscribeViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `TranscriptionCompletedEvent`, `NavigateToEvent` |
| **Subscribes** | None (source panel) |
| **Backend** | `/api/transcribe/*` |
| **Shared Services** | IEventAggregator |
| **Throttle** | None |
| **Unsubscribe** | N/A |

### Recording (RecordingView / RecordingViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `AssetAddedEvent` |
| **Subscribes** | None |
| **Backend** | `/api/recording/devices` |
| **Shared Services** | IEventAggregator |
| **Throttle** | None |
| **Unsubscribe** | N/A |

### EffectsMixer (EffectsMixerView / EffectsMixerViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | None |
| **Subscribes** | MultiSelectService.SelectionChanged (channel selection); SelectedAudioId/SelectedProjectId from context/store |
| **Backend** | Effects/mixer APIs |
| **Shared Services** | IEffectsMeterClient, IEffectChainClient, IMixerStateClient, MultiSelectService |
| **Throttle** | N/A (no EventAggregator high-freq) |
| **Unsubscribe** | ✅ Dispose disposes _disposalCts, _pollingCts, _selectionLoadCts; OnDeactivatedAsync exists; MultiSelectService handler not explicitly unsubscribed — **Partial** |

### Analyzer (AnalyzerView / AnalyzerViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | None (sets transport via IContextManager.SetCurrentPlayable) |
| **Subscribes** | IAudioPlayerService.PositionChanged (playback position) |
| **Backend** | Analysis APIs |
| **Shared Services** | IContextManager, IAudioPlayerService, IAnalyzerClient |
| **Throttle** | None |
| **Unsubscribe** | ✅ Dispose unsubscribes PositionChanged |

### Settings (SettingsView / SettingsViewModel)

| Category | Details |
|----------|---------|
| **Publishes** | `EngineChangedEvent`, `EngineSettingsChangedEvent`, `ProjectSettingsChangedEvent` |
| **Subscribes** | None |
| **Backend** | `/api/settings/*`, `/api/settings/check/dependencies` |
| **Shared Services** | ISettingsService |
| **Throttle** | None |
| **Unsubscribe** | N/A |

---

## Services (Event Publishers/Subscribers)

### FileOperationsHandler
- **Publishes**: `AssetAddedEvent`
- **Subscribes**: None

### WorkspaceService
- **Publishes**: `WorkspaceChangedEvent`
- **Subscribes**: None

### ContextManager
- **Publishes**: `ProfileSelectedEvent`, `EngineChangedEvent`, `ProjectChangedEvent`, `AssetSelectedEvent`
- **Subscribes**: Store subscription (AppStateStore)

### JobService
- **Publishes**: `JobStartedEvent`, `JobProgressEvent`, `JobCompletedEvent`
- **Subscribes**: None

### AudioPlayerService
- **Subscribes**: `PlaybackRequestedEvent` (L40)
- **Unsubscribe**: ❌ Not verified

### WorkflowCoordinatorService
- **Publishes**: `PanelNavigationRequestEvent`, `CloneReferenceSelectedEvent`, `VoiceProfileSelectedEvent`, `PlaybackRequestedEvent`
- **Subscribes**: None

### SynchronizedScrollService
- **Publishes**: `ScrollSyncEvent`
- **Throttle**: **REQUIRED** (high-frequency scroll)

### SelectionBroadcastService
- **Publishes**: `SelectionBroadcastEvent`
- **Throttle**: Consider for high-frequency selection

---

## Allowed Event Types (Allowlist for CI)

Events that MAY be used in `Publish<T>` and `Subscribe<T>`:

- `ProfileSelectedEvent`
- `ProfileUpdatedEvent`
- `ProfileCreatedEvent`
- `ProfileDeletedEvent`
- `AssetSelectedEvent`
- `AssetAddedEvent`
- `AssetRemovedEvent`
- `ProjectChangedEvent`
- `ProjectSettingsChangedEvent`
- `JobStartedEvent`
- `JobProgressEvent`
- `JobCompletedEvent`
- `PlaybackStateChangedEvent`
- `TimelineSelectionChangedEvent`
- `EngineChangedEvent`
- `EngineSettingsChangedEvent`
- `CloneReferenceSelectedEvent`
- `VoiceProfileSelectedEvent`
- `PlaybackRequestedEvent`
- `SynthesisCompletedEvent`
- `AddToTimelineEvent`
- `TranscriptionCompletedEvent`
- `NavigateToEvent`
- `PanelNavigationRequestEvent`
- `WorkspaceChangedEvent`
- `ScrollSyncEvent`
- `SelectionBroadcastEvent`

---

## Identified Gaps

| Gap | Panel | Issue |
|-----|-------|-------|
| GAP-W1 | Library | ~~Missing `SynthesisCompletedEvent` subscription~~ **RESOLVED** |
| GAP-W2 | Timeline | ~~Missing SynthesisCompletedEvent~~ **RESOLVED** — auto-add on synthesis complete |
| GAP-W3 | Multiple | ~~No Unsubscribe~~ **Library, VoiceSynthesis, Timeline, Features/Timeline, Profiles**: IPanelLifecycle.OnDeactivatedAsync |
| GAP-W4 | Timeline | PlaybackStateChangedEvent, TimelineSelectionChangedEvent need throttling (EffectsMixer uses MultiSelectService, not EventAggregator) |
| GAP-W5 | Import flow | AssetAddedEvent published from Import; Library subscribes — verify ImportView/FileOperationsHandler wiring |

---

## Throttle/Debounce Policy

| Event Type | Recommended | Current |
|------------|--------------|---------|
| `PlaybackStateChangedEvent` | 100ms | None |
| `TimelineSelectionChangedEvent` | 100ms | None |
| `ScrollSyncEvent` | 50–100ms | Unknown |
| `SelectionBroadcastEvent` | 50ms | None |
| Search (Library, etc.) | 300ms | 300ms (Library) |
