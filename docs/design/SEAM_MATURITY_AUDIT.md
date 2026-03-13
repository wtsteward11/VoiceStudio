# Seam Maturity Audit

> **Source:** Mid-Stage Architecture Compression Plan (2026-03-11)  
> **Purpose:** Honest classification of extracted seams; avoid fake modularity.

---

## Migration Truth (Source-Controlled)

| Doc | Purpose |
|-----|---------|
| [IBACKENDCLIENT_UNRESOLVED_QUEUE.md](IBACKENDCLIENT_UNRESOLVED_QUEUE.md) | Live ranked list of unresolved IBackendClient consumers; **pick next migration target from here**. |
| [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md) | Unified rule for ViewModel fire-and-forget; aligns SceneBuilder, BatchProcessing, Training. |
| [IBACKENDCLIENT_LONGTAIL_RANKING.md](IBACKENDCLIENT_LONGTAIL_RANKING.md) | Historical completed ranks 1–16; **exhausted** — do not pick from here. |

---

## Scope

Seams that sit between ViewModels/Panels and `IBackendClient`. Each is classified by actual behavior, not aspiration.

---

## Category Definitions

| Category | Definition |
|----------|------------|
| **Client** | Thin transport; no policy. Pass-through to backend. |
| **Gateway** | Routes to multiple backends/adapters; aggregates or multiplexes. |
| **Adapter** | Wraps external system with minimal translation. |
| **Policy-owning Service** | Owns defaults, normalization, retry/caching, orchestration, or business rules. |

---

## Seam Inventory

| Seam | Category | Rationale | Naming Recommendation |
|------|----------|-----------|------------------------|
| ABTestService | Client | Pure pass-through; 2 methods delegate to IBackendClient | Rename to ABTestClient or keep |
| VoiceSynthesisService | Policy-owning Service | Response normalization: AudioUrl derived from AudioId when null/empty. Request shaping: Engine default "xtts", Text validation; returns new instance (no caller mutation). Error mapping: BackendNotFoundException → profile/engine not found; HttpRequestException → backend unavailable. **Retry policy:** No retry on 5xx; single attempt (2026-03-13). Task 1.2 DONE; Phase 5 refinements: no in-place mutation, less brittle 404 detection. | Keep |
| TimelineTrackService | Policy-owning Service | OrderBy TrackNumber/Name; default track naming via GenerateDefaultTrackNameAsync | Keep |
| ProjectAudioClient | Policy-owning Client | Filename validation; dedup guard on save; list/save consistency | Keep (honest) |
| TimelineTranscriptionService | Policy-owning Service | Null/empty Segments normalization; never return null | Keep |
| TimelineClipService | Client | Pure pass-through; CreateClipAsync, DeleteClipAsync | Rename to TimelineClipClient or keep |
| EnginesClient | Client | Delegates to GetEnginesAsync; single-flight/TTL in BackendClient | Keep |
| ProfilesClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for profiles | Keep |
| ProjectsClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for projects | Keep |
| IEmotionStyleClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for emotions/styles; preset coalescing | Keep |
| IEmotionControlClient | Policy-owning Client | IRequestCoordinator; single-flight + TTL for list/presets; cache invalidation on create/delete. Apply/preview are thin pass-through; list/presets own caching policy. | Keep |
| IPresetLibraryClient | Policy-owning Client | IRequestCoordinator for types (TTL 600s); cache invalidation on create/update/delete. Search/create/update/delete/apply are thin pass-through. | Keep |
| ISSMLClient | Client | Thin pass-through; document CRUD, validate, preview. No caching policy. | Keep |
| IQualityControlClient | Client | Thin pass-through; presets, analysis, optimization, consistency, heatmap, correlations, anomalies, prediction, insights. | Keep |
| ITranscriptionClient | Client | Thin pass-through; languages, engines, transcribe, list, get, delete. | Keep |
| ITrainingClient | Client | Thin pass-through; datasets, jobs, logs, quality history. | Keep |
| ITrainingDatasetEditorClient | Client | Thin pass-through; dataset-editor GetDataset, AddAudio, UpdateAudio, RemoveAudio, Validate. | Keep |
| IBatchProcessingClient | Client | Thin pass-through; batch jobs CRUD, queue status, quality report/statistics, retry. **Policy (2026-03-13):** No retry on 5xx; single attempt. Callers responsible for retry. | Keep |
| IVoiceCloningWizardClient | Client | Thin pass-through; engines, validation, upload, wizard start/status/finalize/cancel. | Keep |
| ILibraryClient | Client | Thin pass-through; library folders, asset search, create/delete folder, asset types. | Keep |
| IRealTimeVoiceConverterClient | Client | Thin pass-through; latency, quality metrics, start/stop/pause/resume session, session list/get/delete. Exposes WebSocketService for fallback. | Keep |
| IDiagnosticsClient | Client | Thin pass-through; CheckHealthAsync, GetTelemetryAsync, GetTracesAsync, GetConnectionStatus. | Keep |
| ITextSpeechEditorClient | Client | Thin pass-through; editor session, synthesis, SSML preview. | Keep |
| IAnalyzerClient | Client | Thin pass-through; UploadAudioFileAsync, GetRadarDataAsync, GetLoudnessDataAsync, GetPhaseDataAsync. | Keep |
| ISettingsClient | Client | Thin pass-through; CheckDependenciesAsync. | Keep |
| IMacroClient | Client | Thin pass-through; macros CRUD, automation curves CRUD, execute. | Keep |
| IModelManagerClient | Client | Thin pass-through; model list, export, delete. | Keep |
| IJobProgressApiClient | Client | Thin pass-through; GetJobsAsync, GetJobSummaryAsync, Cancel/Pause/Resume/Delete/ClearCompleted. | Keep |
| ISceneBuilderClient | Client | Thin pass-through; scene CRUD, apply. No caching policy. | Keep |
| IMixAssistantClient | Client | Thin pass-through; GetSuggestionsAsync, AnalyzeMixAsync, ApplySuggestionAsync, DeleteSuggestionAsync, GeneratePresetsAsync. MixAssistantViewModel migrated 2026-03-13. | Keep |
| IAdvancedSettingsClient | Client | Thin pass-through; GetSettingsAsync, GetGpuDevicesAsync, SaveSettingsAsync, ResetSettingsAsync. AdvancedSettingsViewModel migrated 2026-03-13. | Keep |
| IUltimateDashboardClient | Client | Thin pass-through; GetDashboardAsync. UltimateDashboardViewModel migrated 2026-03-13. | Keep |

---

## Naming Mismatches

| Current Name | Issue | Recommendation |
|--------------|-------|----------------|
| ABTestService | "Service" implies policy; none exists | ABTestClient |
| TimelineClipService | Pure delegator | TimelineClipClient (optional) |

---

## Next Architecture Targets (Ranked, 2026-03-12)

| Rank | File | Risk / Blast Radius | Why Next | Expected Seam |
|------|------|---------------------|----------|---------------|
| 1 | ~~PresetLibraryViewModel~~ | — | DONE (2026-03-12). IPresetLibraryClient added; PresetLibraryViewModel migrated. | — |
| 2 | ~~SSMLControlViewModel~~ | — | DONE (2026-03-12). ISSMLClient added; SSMLControlViewModel migrated. | — |
| 3 | ~~QualityControlViewModel~~ | — | DONE (2026-03-12). IQualityControlClient added; QualityControlViewModel migrated. | — |
| 4 | ~~QualityDashboardViewModel~~ | — | DONE (2026-03-12). IQualityControlClient extended; QualityDashboardViewModel migrated. | — |
| 5 | ~~EngineRecommendationViewModel~~ | — | DONE (2026-03-12). IQualityControlClient; EngineRecommendationViewModel migrated. | — |
| 6 | ~~QualityOptimizationWizardViewModel~~ | — | DONE (2026-03-12). IVoiceSynthesisService + IQualityControlClient; QualityOptimizationWizardViewModel migrated. | — |
| 7 | ~~QualityBenchmarkViewModel~~ | — | DONE (2026-03-12). IQualityControlClient extended with RunBenchmarkAsync; QualityBenchmarkViewModel migrated. | — |
| 8 | ~~TranscribeViewModel~~ | — | DONE (2026-03-12). ITranscriptionClient added; TranscribeViewModel migrated; fire-and-forget removed. | — |
| 9 | ~~ProfileComparisonViewModel~~ | — | DONE (2026-03-12). IVoiceSynthesisService + IProfilesClient; ProfileComparisonViewModel migrated; fire-and-forget removed. | — |
| 10 | ~~TrainingViewModel~~ | — | DONE (2026-03-12). ITrainingClient added; TrainingViewModel migrated; constructor fire-and-forget removed; lifecycle fire-and-forget retained (documented in [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)). Seam migration complete; lifecycle cleanup not complete. | — |
| 11 | ~~BatchProcessingViewModel~~ | — | DONE (2026-03-13). IBatchProcessingClient added; migration complete. Lifecycle: selection/filter gated; polling/WebSocket retained. | — |
| 12 | ~~VoiceCloningWizardViewModel~~ | — | DONE (2026-03-13). IVoiceCloningWizardClient added; VoiceCloningWizardViewModel migrated; seam-aware tests added. | — |
| 13 | ~~LibraryViewModel~~ | — | DONE (2026-03-13). ILibraryClient added; LibraryViewModel migrated; seam-aware tests added. | — |
| 14 | ~~RealTimeVoiceConverterViewModel~~ | — | DONE (2026-03-12). IRealTimeVoiceConverterClient added; RealTimeVoiceConverterViewModel migrated; baseline updated. | — |
| 15 | ~~DiagnosticsViewModel~~ | — | DONE (2026-03-13). IDiagnosticsClient added; DiagnosticsViewModel migrated. | — |
| 16 | ~~TextSpeechEditorViewModel~~ | — | DONE (2026-03-13). ITextSpeechEditorClient added; TextSpeechEditorViewModel migrated. | — |
| 17 | ~~AnalyzerViewModel~~ | — | DONE (2026-03-13). IAnalyzerClient added; AnalyzerViewModel migrated. | — |
| 18 | ~~MixAssistantViewModel~~ | — | DONE (2026-03-13). IMixAssistantClient added; MixAssistantViewModel migrated; IPanelLifecycle; OnActivatedAsync for initial load. | — |
| 18 | ~~SettingsViewModel~~ | — | DONE (2026-03-13). ISettingsClient added; SettingsViewModel migrated. | — |
| 19 | ~~MacroViewModel~~ | — | DONE (2026-03-13). IMacroClient added; MacroViewModel migrated. | — |
| 20 | ~~ModelManagerViewModel~~ | — | DONE (2026-03-13). IModelManagerClient added; ModelManagerViewModel migrated. | — |
| 21 | ~~JobProgressViewModel~~ | — | DONE (2026-03-13). IJobProgressApiClient added; JobProgressViewModel migrated. | — |
| 22 | ~~SceneBuilderViewModel~~ | — | DONE (2026-03-13). Migration and lifecycle ownership complete: OnActivatedAsync awaits; staleness guard; IDispatcherTimer debounce; disposal. | — |

**Recommendation:** Rank 1–3 migration complete. BatchProcessing: lifecycle closed with accepted exceptions. SceneBuilder: lifecycle ownership complete (2026-03-13). Migration queue may proceed.

---

## Next Steps

1. **Task 1.2** — DONE (2026-03-13). VoiceSynthesisService: response normalization; request shaping (Engine default "xtts", Text validation); error mapping (HttpRequestException, 404).
2. **Task 2.1** — DONE. IEmotionStyleClient added; EmotionStyleControlViewModel migrated.
3. **Task 2.2** — DONE (2026-03-11). IEmotionControlClient added; EmotionControlViewModel migrated.
4. **Task 2.3** — DONE (2026-03-12). EmotionControlViewModel fire-and-forget removed; InitializeAsync from Loaded.
5. Rename ABTestService → ABTestClient if desired (low priority).
6. **Task 2.4** — DONE (2026-03-12). IPresetLibraryClient added; PresetLibraryViewModel migrated.
7. **Task 2.5** — DONE (2026-03-12). ISSMLClient added; SSMLControlViewModel migrated.
8. **Task 2.6** — DONE (2026-03-12). IQualityControlClient added; QualityControlViewModel migrated.
9. **Task 2.7** — DONE (2026-03-12). IQualityControlClient extended with GetQualityDashboardAsync; QualityDashboardViewModel migrated.
10. **Task 2.8** — DONE (2026-03-12). EngineRecommendationViewModel migrated to IQualityControlClient.
11. **Task 2.9** — DONE (2026-03-12). QualityOptimizationWizardViewModel migrated to IVoiceSynthesisService + IQualityControlClient.
12. **Task 2.10** — DONE (2026-03-12). IQualityControlClient extended with RunBenchmarkAsync; QualityBenchmarkViewModel migrated; fire-and-forget removed.
13. **Task 2.11** — DONE (2026-03-12). ITranscriptionClient added; TranscribeViewModel migrated; DeleteTranscriptionAction updated; fire-and-forget removed.
14. **Task 2.12** — DONE (2026-03-12). ProfileComparisonViewModel migrated to IVoiceSynthesisService + IProfilesClient; fire-and-forget removed.
15. **Task 2.13** — DONE (2026-03-12). ITrainingClient added; TrainingViewModel migrated; CreateTrainingDatasetAction updated; constructor fire-and-forget removed; lifecycle fire-and-forget retained (see TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md); empty catches fixed. Seam migration complete; lifecycle cleanup deferred.

---

## Changelog

- 2026-03-13: SceneBuilderViewModel lifecycle ownership complete: OnActivatedAsync awaits; staleness guard; IDispatcherTimer debounce; disposal (SCENEBUILDER_LIFECYCLE_PATTERNS.md).
- 2026-03-13: IBatchProcessingClient: documented retry policy (no retry on 5xx; single attempt). Second seam beyond VoiceSynthesisService with explicit policy.
- 2026-03-13: Next 10 Tasks: IDiagnosticsClient, ITextSpeechEditorClient, IAnalyzerClient, ISettingsClient, IMacroClient, IModelManagerClient, IJobProgressApiClient added; DiagnosticsViewModel, TextSpeechEditorViewModel, AnalyzerViewModel, SettingsViewModel, MacroViewModel, ModelManagerViewModel, JobProgressViewModel migrated.
- 2026-03-13: VoiceSynthesisService Phase 5 refinements: ShapeRequest returns new instance (no caller mutation); MapToUserActionableException catches BackendNotFoundException instead of ex.Message.Contains("404").
- 2026-03-11: Initial audit per Mid-Stage Architecture Compression Plan.
- 2026-03-11: Added IEmotionControlClient; EmotionControlViewModel migrated (Mypy Reassess and Architecture Pivot Plan Phase 2).
- 2026-03-12: IEmotionControlClient audit: apply/preview thin; list/presets have policy. EmotionControlViewModel fire-and-forget removed (EmotionControl Hardening Follow-Through).
- 2026-03-12: IPresetLibraryClient added; PresetLibraryViewModel migrated (PresetLibrary hardening).
- 2026-03-12: ISSMLClient added; SSMLControlViewModel migrated (SSML hardening).
- 2026-03-12: IQualityControlClient added; QualityControlViewModel migrated (QualityControl hardening).
- 2026-03-12: IQualityControlClient extended with GetQualityDashboardAsync; QualityDashboardViewModel migrated (QualityDashboard hardening).
- 2026-03-12: EngineRecommendationViewModel migrated to IQualityControlClient (EngineRecommendation hardening).
- 2026-03-12: QualityOptimizationWizardViewModel migrated to IVoiceSynthesisService + IQualityControlClient (QualityOptimizationWizard hardening).
- 2026-03-12: IQualityControlClient extended with RunBenchmarkAsync; QualityBenchmarkViewModel migrated; fire-and-forget removed (QualityBenchmark hardening).
- 2026-03-12: ITranscriptionClient added; TranscribeViewModel migrated; DeleteTranscriptionAction updated; fire-and-forget removed (Transcribe hardening).
- 2026-03-12: ProfileComparisonViewModel migrated to IVoiceSynthesisService + IProfilesClient; fire-and-forget removed (ProfileComparison hardening).
- 2026-03-12: Architecture target ranking refresh: added next batch (QualityBenchmarkViewModel, TranscribeViewModel, ProfileComparisonViewModel, TrainingViewModel).
- 2026-03-12: ITrainingClient added; TrainingViewModel migrated; CreateTrainingDatasetAction updated; fire-and-forget removed; empty catches fixed (Training hardening).
- 2026-03-12: TrainingDatasetEditorViewModel: ListDatasetsAsync migrated to ITrainingClient; dataset-editor endpoints remain on IBackendClient; creep baseline updated.
- 2026-03-12: ITrainingDatasetEditorClient added; TrainingDatasetEditorViewModel migrated; constructor fire-and-forget removed; seam-aware tests added (Post-Ranked Architecture Convergence Phase 4).
- 2026-03-12: VoiceSynthesisService: response normalization policy added; reclassified to Policy-owning Service (Post-Ranked Architecture Convergence Phase 5).
- 2026-03-13: Long-Tail Wave 2: IBatchProcessingClient, IVoiceCloningWizardClient, ILibraryClient added; BatchProcessingViewModel, VoiceCloningWizardViewModel, LibraryViewModel migrated; seam-aware tests added.
- 2026-03-13: VoiceSynthesisService: request shaping (Engine default, Text validation) and error mapping added; Task 1.2 DONE.
- 2026-03-12: TrainingViewModel: constructor improved; lifecycle fire-and-forget retained with _disposalCts (see [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)).
