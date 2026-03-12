# Request Coordination Audit — 2026-03-11

**Purpose:** Task 10.3 — Inventory stable shared endpoints, canonical access paths, and bypasses.

---

## Endpoints

| Endpoint | Canonical Client | Coordinated? |
|----------|------------------|--------------|
| `/api/profiles` (list) | IProfilesClient | Yes (single-flight + TTL via BackendClient) |
| `/api/projects` (list) | IProjectsClient | Yes (single-flight + TTL via BackendClient) |
| `/api/engines` (list) | IBackendClient (BackendClient.Engines) | Yes (single-flight + TTL) |
| Tracks (create/list/delete) | ITimelineTrackService | No coordinator (delegates to BackendClient) |
| Clips (create/delete) | ITimelineClipService | No coordinator (delegates to BackendClient) |
| Transcription (get by ID) | ITimelineTranscriptionService | No coordinator (delegates to BackendClient) |
| Project audio (list/get/save) | IProjectAudioClient | No coordinator (delegates to BackendClient) |
| Waveform/spectrogram | IAudioVisualizationService | No coordinator (delegates to BackendClient) |

---

## Open Remediation Queue

| Endpoint | Canonical Path | Coordination Gap | Severity | Owner / Task Ref |
|----------|----------------|------------------|----------|------------------|
| GET /api/engines | IBackendClient.GetEnginesAsync | No IEnginesClient | P3 | TBD |
| GET /api/projects/{id}/tracks | ITimelineTrackService | No RequestCoordinator | P2 | Task 3A |
| GET /api/projects/{id}/audio | IProjectAudioClient | No coordinator | P2 | Task 3B |
| POST /api/synthesize | ITimelineSynthesisService | Resolved by Task 1B | — | Task 1B (done) |

---

## Timeline-Supporting Endpoints

### Tracks (Timeline hardening Phase 1 complete)
- **Canonical path:** ITimelineTrackService → TimelineTrackService → BackendClient
- **Consumers:** TimelineViewModel, AudioStore
- **Bypasses:** None
- **Coordination:** Direct; no RequestCoordinator for track ops

### Clips (Task 10.1 hardening complete)
- **Canonical path:** ITimelineClipService → TimelineClipService → BackendClient
- **Consumers:** TimelineViewModel (paste, duplicate, delete, add clip, delete selected)
- **Bypasses:** None (TimelineViewModel uses _clipService for all clip CRUD)
- **Coordination:** Direct; clip seam extracted 2026-03-11

### Transcription (Timeline hardening Phase 2 complete)
- **Canonical path:** ITimelineTranscriptionService → TimelineTranscriptionService → BackendClient
- **Consumers:** TimelineViewModel
- **Bypasses:** None
- **Coordination:** Direct

### Project audio (Timeline hardening Phase 3 complete)
- **Canonical path:** IProjectAudioClient → ProjectAudioClient → BackendClient
- **Consumers:** TimelineViewModel (others still use IBackendClient; batch migration optional)
- **Bypasses:** None in TimelineViewModel
- **Coordination:** Direct

### Waveform / spectrogram (Timeline hardening Phase 4 complete)
- **Canonical path:** IAudioVisualizationService → AudioVisualizationService → BackendClient
- **Consumers:** TimelineViewModel, AnalyzerViewModel
- **Bypasses:** None identified
- **Coordination:** Direct

---

## Profiles List — GetProfilesAsync

### Canonical path
- **IProfilesClient** → ProfilesClient → BackendClient.GetProfilesAsync (RequestCoordinator)
- **ProfilesUseCase.ListAsync** → _profilesClient.GetProfilesAsync

### Consumers using canonical path
- ProfilesViewModel (via ProfilesUseCase / IProfilesClient)
- ProfilesClient (implementation)

### Bypasses (direct IBackendClient.GetProfilesAsync)
| Call site | File | Line | Priority | Status |
|-----------|------|------|----------|--------|
| ProfileHealthDashboardViewModel | ViewModels/ProfileHealthDashboardViewModel.cs | 87 | P2 | ✅ Uses IProfilesClient |
| EmbeddingExplorerViewModel | ViewModels/EmbeddingExplorerViewModel.cs | 527 | P2 | ✅ Migrated |
| RealTimeVoiceConverterViewModel | ViewModels/RealTimeVoiceConverterViewModel.cs | 498 | P2 | ✅ Migrated |
| TextSpeechEditorViewModel | ViewModels/TextSpeechEditorViewModel.cs | 668 | P2 | ✅ Migrated |
| ABTestingViewModel | Views/Panels/ABTestingViewModel.cs | 168 | P2 | ✅ Migrated |
| PronunciationLexiconViewModel | ViewModels/PronunciationLexiconViewModel.cs | 379 | P2 | ✅ Migrated |
| TextBasedSpeechEditorViewModel | ViewModels/TextBasedSpeechEditorViewModel.cs | 581 | P2 | ✅ Migrated |
| VoiceMorphViewModel | ViewModels/VoiceMorphViewModel.cs | 481 | P2 | ✅ Migrated |
| QualityBenchmarkViewModel | Views/Panels/QualityBenchmarkViewModel.cs | 103 | P2 | ✅ Migrated |
| TagOrganizationViewModel | Views/Panels/TagOrganizationViewModel.cs | 91 | P2 | ✅ Migrated |
| TimelineViewModel | Views/Panels/TimelineViewModel.cs | 931 | P2 | ✅ Migrated |
| VoiceSynthesisViewModel | Views/Panels/VoiceSynthesisViewModel.cs | 480 | P2 | ✅ Migrated |
| StyleTransferViewModel | ViewModels/StyleTransferViewModel.cs | 176 | P2 | ✅ Migrated |
| VoiceMorphingBlendingViewModel | ViewModels/VoiceMorphingBlendingViewModel.cs | 181 | P2 | ✅ Migrated |
| QualityOptimizationWizardViewModel | ViewModels/QualityOptimizationWizardViewModel.cs | 156 | P2 | ✅ Migrated |
| VoiceStyleTransferViewModel | ViewModels/VoiceStyleTransferViewModel.cs | 264 | P2 | ✅ Migrated |
| ProfileComparisonViewModel | ViewModels/ProfileComparisonViewModel.cs | 157 | P2 | ✅ Migrated |

**Remediation:** Inject IProfilesClient; replace _backendClient.GetProfilesAsync with _profilesClient.GetProfilesAsync.

---

## Projects List — GetProjectsAsync

### Canonical path
- **IProjectsClient** → ProjectsClient → BackendClient.GetProjectsAsync (RequestCoordinator)

### Consumers using canonical path
- TimelineViewModel, ProjectStore, AssistantViewModel, AdvancedWaveformVisualizationViewModel, TextHighlightingViewModel, MixAssistantViewModel, EmbeddingExplorerViewModel, SpatialStageViewModel, SonographyVisualizationViewModel, TextSpeechEditorViewModel, AdvancedSpectrogramVisualizationViewModel, VoiceMorphViewModel, StyleTransferViewModel

### Bypasses
None identified. All project list consumers use IProjectsClient.

---

## Engines List — GetEnginesAsync

### Canonical path
- **IBackendClient.GetEnginesAsync** → BackendClient.Engines partial (RequestCoordinator)

### Consumers
All use _backendClient.GetEnginesAsync. No IEnginesClient exists; BackendClient is the canonical path. RequestCoordinator provides coalescing.

### Bypasses
None. Engines are not yet behind a domain client; BackendClient.Engines is the single path.

---

## Remediation Priority

| Priority | Action |
|----------|--------|
| P1 | (None — projects already migrated) |
| P2 | Migrate 16 profiles bypasses to IProfilesClient |
| P3 | Consider IEnginesClient for consistency (optional) |

---

## Next Steps

1. ~~Create migration worklist for profiles bypasses~~ — Complete.
2. ~~Add IProfilesClient to each bypass ViewModel constructor~~ — Complete.
3. ~~Replace _backendClient.GetProfilesAsync with _profilesClient.GetProfilesAsync~~ — Complete.
4. ~~Update DI registration and test mocks~~ — Complete.
5. Timeline clip seam extraction (Task 10.1): ITimelineClipService introduced; TimelineViewModel uses _clipService for all clip CRUD. No direct _backendClient.CreateClipAsync/DeleteClipAsync in TimelineViewModel.
6. Timeline hardening Phases 1–4 (2026-03-11): ITimelineTrackService, ITimelineTranscriptionService, IProjectAudioClient, IAudioVisualizationService extracted. TimelineViewModel uses focused seams for tracks, transcription, project audio, and visualization. Remaining _backendClient usage: SynthesizeVoiceAsync only.

## Migration Progress (2026-03-11)

- **Migrated:** All 16 profiles bypasses (Timeline, TagOrganization, ABTesting, VoiceSynthesis, QualityBenchmark, StyleTransfer, VoiceMorph, EmbeddingExplorer, RealTimeVoiceConverter, TextSpeechEditor, PronunciationLexicon, TextBasedSpeechEditor, VoiceMorphingBlending, QualityOptimizationWizard, VoiceStyleTransfer, ProfileComparison).
- **Remaining:** 0 bypasses.
