# Seam Maturity Audit

> **Source:** Mid-Stage Architecture Compression Plan (2026-03-11)  
> **Purpose:** Honest classification of extracted seams; avoid fake modularity.

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
| VoiceSynthesisService | Client (candidate for Service) | Pure pass-through; central workflow; deepen in Task 1.2 | Keep; deepen later |
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

**Recommendation:** All ranked targets complete. TrainingDatasetEditorViewModel: ListDatasetsAsync migrated to ITrainingClient (2026-03-12); dataset-editor endpoints (GetDataset, AddAudio, UpdateAudio, RemoveAudio, Validate) remain on IBackendClient. Consider other IBackendClient consumers.

---

## Next Steps

1. **Task 1.2** — DEFERRED. Deepen VoiceSynthesisService blocked by type location: VoiceSynthesisRequest/Response live in App.Core.Models; IVoiceSynthesisService/IBackendClient use Core.Models. Resolve type consolidation before adding request shaping/response normalization.
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
- 2026-03-12: TrainingViewModel: constructor improved; lifecycle fire-and-forget retained with _disposalCts (see [TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md](TRAINING_VIEWMODEL_LIFECYCLE_ASYNC_PATTERNS.md)).
