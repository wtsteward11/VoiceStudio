# BackendClient Post-PR-17 Remainder Inventory

**Purpose:** Code-backed inventory of remaining `IBackendClient` method clusters for PR-18 stop-decision. Derived from current `IBackendClient.cs` and `BackendClient.cs`, not from stale post-PR-12 fiction.  
**Date:** 2026-03-24  
**Source:** [IBackendClient.cs](../../src/VoiceStudio.App/Core/Services/IBackendClient.cs), [BackendClient.cs](../../src/VoiceStudio.App/Services/BackendClient.cs)  
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md), [EXTRACTION_STOP_CRITERIA.md](EXTRACTION_STOP_CRITERIA.md)

---

## Changelog

| Date       | Change |
|------------|--------|
| 2026-03-24 | **Re-baseline from code.** Replaced Post-PR-12 ranked clusters with table derived from current IBackendClient/BackendClient. PR-13 through PR-17 (Pipeline, BackupRestore, Models, Video, Mixer) confirmed extracted; remainder clusters rebuilt from scratch. Added cluster table with exact methods, endpoint families, caller sets, thin-client status, extraction difficulty, blast radius, and recommendation. Date corrected: must be after PR-17 (2026-03-23). |
| 2026-03-22 | Original Post-PR-12 inventory (superseded by 2026-03-24 re-baseline) |

---

## Extraction Status

| Status | Meaning |
|--------|---------|
| **Fully extracted** | Methods removed from IBackendClient/BackendClient; client uses BackendClientHttpPipeline |
| **Thin client** | Interface + client exist but delegate to IBackendClient; methods still on monolith |
| **No client** | No dedicated interface; callers use IBackendClient directly |

---

## Post-PR-17 Remainder Clusters (Code-Backed)

**Confirmed extracted (PR-13–PR-17):** Pipeline (2), BackupRestore (7), Models (9), Video (5), Mixer (19). These methods are **not** on IBackendClient/BackendClient.

**Excluded from extraction (per EXTRACTION_STOP_CRITERIA):** `SendRequestAsync` (2 overloads), `SendMcpOperationAsync`, `GetAsync`, `PostAsync`, `PutAsync` — generic/cross-cutting; must stay on monolith.

---

### Cluster Table (One Table Per Remaining Cluster)

| Cluster name | Exact remaining methods | Endpoint family | Caller set (grep-backed) | Thin client exists? | Extraction difficulty | Blast radius | Recommendation |
|--------------|-------------------------|-----------------|--------------------------|---------------------|----------------------|--------------|----------------|
| **Voice** | SynthesizeVoiceAsync, AnalyzeVoiceAsync, CloneVoiceAsync | `/api/voice/*` | VoiceSynthesisViewModel, VoiceQuickCloneClient, VoiceGateway, VoiceSynthesisService, ReferenceAudioQualityAnalyzer, TimelineSynthesisService, ProfilePreviewService, ScriptEditorViewModel, VoiceProfileViewModel, QualityOptimizationWizardViewModel, EmotionStylePresetEditorViewModel | No (IVoiceGateway delegates; VoiceQuickCloneClient uses _backend.CloneVoiceAsync) | L | High | **defer** — core synthesis path, cross-cutting |
| **Profiles** | GetProfilesAsync, GetProfileAsync, CreateProfileAsync, UpdateProfileAsync, DeleteProfileAsync | `/api/profiles/*` | ProfilesClient (delegates), ProfilesViewModel, TimelineViewModel, VoiceSynthesisViewModel, ProfileComparisonViewModel, ABTestingViewModel, QualityBenchmarkViewModel, QualityOptimizationWizardViewModel, ProfileHealthDashboardViewModel, TagOrganizationViewModel, EmbeddingExplorerViewModel, VoiceMorphViewModel, VoiceMorphingBlendingViewModel, PronunciationLexiconViewModel, TextBasedSpeechEditorViewModel, TextSpeechEditorViewModel, StyleTransferViewModel, ProfilesUseCase, ProfileOperationsHandler | Y — IProfilesClient / ProfilesClient | M | High | **split first** — complete thin client migration |
| **Projects** | GetProjectsAsync, GetProjectAsync, CreateProjectAsync, UpdateProjectAsync, DeleteProjectAsync | `/api/projects/*` | ProjectsClient (delegates), TimelineViewModel, EmbeddingExplorerViewModel, TextSpeechEditorViewModel, MixAssistantViewModel, AssistantViewModel, StyleTransferViewModel, SpatialStageViewModel, VoiceMorphViewModel, TextHighlightingViewModel, SonographyVisualizationViewModel, AdvancedWaveformVisualizationViewModel, AdvancedSpectrogramVisualizationViewModel, ProjectStore, TimelineProjectHandlers | Y — IProjectsClient / ProjectsClient | M | High | **split first** — complete thin client migration |
| **Project audio** | SaveAudioToProjectAsync, ListProjectAudioAsync, GetProjectAudioAsync | `/api/projects/*/audio` | ProjectAudioClient (delegates), TimelineViewModel, SpatialStageViewModel, SonographyVisualizationViewModel, StyleTransferViewModel, VoiceMorphViewModel, TextHighlightingViewModel, EmbeddingExplorerViewModel | Y — IProjectAudioClient / ProjectAudioClient | S | Med | **extract now** — complete IProjectAudioClient migration |
| **Audio retrieval** | GetAudioStreamAsync | `/api/audio/*` | Many (playback, timeline) | No | S | High | **defer** — cross-cutting playback |
| **Audio export** | ExportAudioAsync, GetSupportedAudioFormatsAsync, UploadAudioFileAsync | `/api/audio/*`, `/api/upload/*` | RecordingClient, AnalyzerClient, many panels | No | M | High | **defer** — cross-cutting |
| **Upload helpers** | UploadFileWithProgressAsync, UploadFilesWithProgressAsync | N/A (generic) | Multiple clients | No | — | — | **stop** — cross-cutting; keep on monolith |
| **Audio visualization** | GetWaveformDataAsync, GetSpectrogramDataAsync, GetAudioMetersAsync, GetRadarDataAsync, GetLoudnessDataAsync, GetPhaseDataAsync | `/api/audio/*` (waveform, spectrogram, etc.) | SpectrogramClient (SendRequest), AnalyzerClient (GetRadarDataAsync, GetLoudnessDataAsync, GetPhaseDataAsync) | No | M | Med | **defer** — fragmented callers |
| **Timeline tracks** | GetTracksAsync, GetTrackAsync, CreateTrackAsync, UpdateTrackAsync, DeleteTrackAsync | `/api/projects/*/tracks` | TimelineTrackService (direct _backend) | No | S | Med | **extract now** — bounded; TimelineTrackService sole direct caller |
| **Timeline clips** | CreateClipAsync, UpdateClipAsync, DeleteClipAsync | `/api/projects/*/clips` | TimelineClipService (direct _backend) | No | S | Med | **extract now** — bounded |
| **Timeline markers** | GetMarkersAsync, GetMarkerAsync, CreateMarkerAsync, UpdateMarkerAsync, DeleteMarkerAsync | `/api/projects/*/markers` | MarkerManagerClient (SendRequest — different API?) | No | M | Med | **split first** — MarkerManagerClient uses /api/markers; IBackendClient uses project-scoped markers; verify endpoint alignment |
| **Batch core** | CreateBatchJobAsync, GetBatchJobsAsync | `/api/batch/*` | BatchViewModel, JobProgressApiClient | No | M | Med | **defer** — assess with Batch quality |
| **Batch quality** | GetBatchJobQualityAsync, GetBatchQualityReportAsync, GetBatchQualityStatisticsAsync, RetryBatchJobWithQualityAsync | `/api/batch/*/quality` | Batch quality panel | No | M | Med | **defer** |
| **Transcription** | GetSupportedLanguagesAsync, GetTranscriptionEnginesAsync, TranscribeAudioAsync, GetTranscriptionAsync, ListTranscriptionsAsync, DeleteTranscriptionAsync | `/api/transcribe/*` | TranscriptionClient (delegates), TimelineTranscriptionService, TranscribeViewModel | Y — ITranscriptionClient / TranscriptionClient | M | Med | **split first** — complete thin client migration |
| **Training** | CreateDatasetAsync, ListDatasetsAsync, GetDatasetAsync, DeleteDatasetAsync, StartTrainingAsync, GetTrainingStatusAsync, ListTrainingJobsAsync, CancelTrainingAsync, GetTrainingLogsAsync, DeleteTrainingJobAsync, GetTrainingQualityHistoryAsync | `/api/training/*` | TrainingViewModel, DatasetQAClient, TrainingDatasetEditorClient | No (DatasetQAClient uses GetTrainingDatasetsAsync, GetTrainingDatasetAsync) | L | High | **defer** — large surface |
| **Ensemble** | CreateMultiEngineEnsembleAsync, GetMultiEngineEnsembleStatusAsync | `/api/ensemble/*` | EnsembleService/panel | No | S | Low | **extract now** — 2 methods, small blast |
| **Channel routing** | UpdateChannelRoutingAsync | `/api/mixer/*` or `/api/projects/*` | EffectsMixerViewModel? (Mixer extracted; verify) | No | S | Low | **split first** — Mixer extracted; may belong with IMixerStateClient or separate |
| **Settings** | GetSettingsAsync, GetSettingsCategoryAsync, SaveSettingsAsync, UpdateSettingsCategoryAsync, ResetSettingsAsync | `/api/settings/*` | SettingsService, AdvancedSettingsClient, SettingsViewModel, SettingsOperationsHandler, AccessibilityService | Y — IAdvancedSettingsClient (partial) | M | Med | **split first** — complete Settings client |
| **Quality** | 31 methods: presets, analysis, optimization, A/B, benchmark, dashboard, history, degradation, baseline, text analysis, pipeline presets, engines, consistency, heatmap, correlations, anomalies, prediction, insights | `/api/quality/*`, `/api/engines/*` | QualityOptimizationWizardViewModel, QualityBenchmarkViewModel, ABTestingViewModel, many quality panels | No | L | High | **stop** — huge cluster; IDEA-* endpoints; not worth yet |
| **Emotion presets** | GetEmotionPresetsAsync, GetEmotionPresetAsync, CreateEmotionPresetAsync, UpdateEmotionPresetAsync, DeleteEmotionPresetAsync, GetAvailableEmotionsAsync | `/api/emotion/*` | EmotionStyleClient (delegates?), EmotionActions | Y (partial — emotion-style vs presets CRUD) | M | Med | **defer** — assess DTO split |
| **Batch lifecycle** | DeleteBatchJobAsync, StartBatchJobAsync, CancelBatchJobAsync, GetBatchQueueStatusAsync | `/api/batch/*` | BatchViewModel, JobProgressApiClient | No | S | Med | **extract now** — combine with Batch core for full Batch client |
| **Training datasets alias** | GetTrainingDatasetsAsync, GetTrainingDatasetAsync | `/api/training/datasets` | DatasetQAClient | No | S | Low | **defer** — fold into Training if extracted |

---

## Stop-Criteria Matrix (Task 2)

See [EXTRACTION_STOP_CRITERIA.md](EXTRACTION_STOP_CRITERIA.md) for criteria definitions.

| Cluster | C1 Leverage (≥3 callers or thin client) | C2 Fragmentation (≥5% methods) | C3 Sparse callers | C4 DTO glue | C5 Cross-cutting | Overall |
|---------|----------------------------------------|------------------------------|-------------------|-------------|------------------|---------|
| Voice | Pass (many) | Fail (3 methods, ~3%) | Fail | Fail | **Stop** — core synthesis | **Stop** |
| Profiles | Pass (thin client) | Pass (5) | Pass | Pass | Pass | Continue |
| Projects | Pass (thin client) | Pass (5) | Pass | Pass | Pass | Continue |
| Project audio | Pass (thin client) | Fail (3, ~2.5%) | Pass | Pass | Pass | Exception — complete thin client |
| Audio retrieval | Pass | Fail (1) | Pass | Pass | **Stop** — playback | **Stop** |
| Audio export | Pass | Fail (3) | Pass | Pass | **Stop** — cross-cutting | **Stop** |
| Upload helpers | — | — | — | — | **Stop** — generic | **Stop** |
| Audio viz | Pass | Pass (6) | Pass | Pass | Pass | Defer — fragmented |
| Timeline tracks | Pass | Fail (5, ~4%) | Pass | Pass | Pass | Borderline |
| Timeline clips | Pass | Fail (3) | Pass | Pass | Pass | Borderline |
| Timeline markers | Pass | Pass (5) | Pass | Pass | Pass | Split first |
| Batch core | Pass | Fail (2) | Pass | Pass | Pass | Defer |
| Batch quality | Pass | Pass (4) | Pass | Pass | Pass | Defer |
| Transcription | Pass (thin client) | Pass (6) | Pass | Pass | Pass | Continue |
| Training | Pass | Pass (11) | Pass | Pass | Pass | Defer — large |
| Ensemble | Fail (1–2 callers) | Fail (2) | Pass | Pass | Pass | Borderline — small |
| Channel routing | Fail | Fail (1) | Pass | Pass | Pass | Split first |
| Settings | Pass (thin client) | Pass (5) | Pass | Pass | Pass | Continue |
| Quality | Pass | Pass (31) | Pass | Pass | **Stop** — huge, IDEA-* | **Stop** |
| Emotion presets | Pass | Pass (6) | Pass | Pass | Pass | Defer |
| Batch lifecycle | Pass | Fail (4) | Pass | Pass | Pass | Extract with Batch core |

---

## Explicit Decision (Task 3)

**Decision: PAUSE**

**Rationale:**

1. **Leverage:** After PR-13–PR-17, the remaining thin-client clusters (Profiles, Projects, Project audio, Transcription, Settings) would each require pipeline migration + DI + caller sweep. No cluster clears "≥5% of remaining methods" with low fragmentation cost except Quality — and Quality fails C5 (huge, IDEA-* endpoints) as a hard stop.

2. **Fragmentation cost:** Extracting Project audio (3 methods), Ensemble (2), or Batch lifecycle (4) adds new client types for &lt;5% reduction. The "complete thin client" exception applies to Profiles, Projects, Transcription, Settings — but each has high blast radius (10+ ViewModels).

3. **Sparse caller risk:** Ensemble, Channel routing have 1–2 callers. Extraction would add types without meaningful coupling reduction.

4. **DTO-glue risk:** ProfilesClient, ProjectsClient are thin delegators. Migrating to pipeline is correct but does not reduce DTO knowledge — callers still depend on same models.

5. **Cross-cutting:** Voice, Audio retrieval, Audio export, Upload helpers, Quality are cross-cutting or core-path. Stop.

**Conclusion:** Pause PR-18 extraction. Execute STATE_TRIM_PLAN; archive historical bulk; preserve ACTIVE WINDOW, LATEST MILESTONE, PROOF INDEX. Revisit extraction when product reasons justify the migration cost.

---

## Re-entry Rule (when to resume extraction)

Extraction should be resumed when one or more of the following applies:

- **Product requirement changes:** A new feature or refactor demands domain isolation (e.g., a bounded caller cluster emerges for a new panel).
- **Monolith residue blocker:** Remaining IBackendClient methods block UX, performance, or maintainability (e.g., profiling shows coupling bottleneck).
- **Thin-client migration request:** Product or architecture decision explicitly requests completing a thin-client migration (Profiles, Projects, Transcription, Settings, Project audio).
- **Stop criteria re-assessment:** A cluster that previously failed leverage/fragmentation thresholds gains enough callers or a cleaner endpoint family to clear the bar.

Do **not** resume extraction purely for purity or momentum. The pause is intentional.

---

## Classification Summary (Post-PR-17)

| Category | Cluster count | Action |
|----------|---------------|--------|
| Hard stop (cross-cutting, generic) | 5 (Voice, Audio retrieval, Audio export, Upload helpers, Quality) | Do not extract |
| Thin client, complete migration | 4 (Profiles, Projects, Project audio, Transcription, Settings) | Pause — high blast radius |
| Bounded, extractable | 3 (Timeline tracks, Timeline clips, Ensemble, Batch lifecycle) | Pause — leverage too low |
| Split first / defer | 4 (Timeline markers, Batch core, Batch quality, Training, Emotion, Channel routing) | Pause |

---

## Caller Counts (Production, Excluding Tests)

| Method family | Source files (src/, excl. IBackendClient, BackendClient, *Tests) |
|---------------|------------------------------------------------------------------|
| Profiles | ProfilesClient, ProfilesViewModel, TimelineViewModel, VoiceSynthesisViewModel, ProfileComparisonViewModel, ABTestingViewModel, QualityBenchmarkViewModel, QualityOptimizationWizardViewModel, ProfileHealthDashboardViewModel, TagOrganizationViewModel, EmbeddingExplorerViewModel, VoiceMorphViewModel, VoiceMorphingBlendingViewModel, PronunciationLexiconViewModel, TextBasedSpeechEditorViewModel, TextSpeechEditorViewModel, StyleTransferViewModel, ProfilesUseCase, ProfileOperationsHandler |
| Projects | ProjectsClient, TimelineViewModel, EmbeddingExplorerViewModel, TextSpeechEditorViewModel, MixAssistantViewModel, AssistantViewModel, StyleTransferViewModel, SpatialStageViewModel, VoiceMorphViewModel, TextHighlightingViewModel, SonographyVisualizationViewModel, AdvancedWaveformVisualizationViewModel, AdvancedSpectrogramVisualizationViewModel, ProjectStore, TimelineProjectHandlers |
| Project audio | ProjectAudioClient, TimelineViewModel, SpatialStageViewModel, SonographyVisualizationViewModel, StyleTransferViewModel, VoiceMorphViewModel, TextHighlightingViewModel, EmbeddingExplorerViewModel |
| Transcription | TranscriptionClient, TimelineTranscriptionService, TranscribeViewModel |
| Timeline tracks | TimelineTrackService |
| Timeline clips | TimelineClipService |
