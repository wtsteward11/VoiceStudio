# IBackendClient Unresolved Queue (Live)

> **Purpose:** Live ranked list of ViewModels/Panels that still take `IBackendClient` directly. Use this for the next migration wave — not the completed historical table in [IBACKENDCLIENT_LONGTAIL_RANKING.md](IBACKENDCLIENT_LONGTAIL_RANKING.md).  
> **Source:** Derived from [.ci/ibackendclient_baseline.txt](../../.ci/ibackendclient_baseline.txt) — entries without `MIGRATED` comment.  
> **Last Generated:** 2026-03-15

---

## ROUTINE MIGRATION QUEUE: CLOSED (2026-03-14)

All routine IBackendClient migration targets are complete. SLODashboardViewModel and TagOrganizationViewModel migrated. Only EffectsMixerViewModel remains — on the **architecture track**, not the routine queue. Do not reopen routine migration work.

---

## Architecture Track (Not Routine Queue)

**EffectsMixerViewModel** — **MIGRATED** (2026-03-15). Domain split per [EFFECTSMIXER_SEAM_EXECUTION_PLAN.md](EFFECTSMIXER_SEAM_EXECUTION_PLAN.md) complete: `IEffectsMeterClient`, `IEffectChainClient`, `IMixerStateClient`. IBackendClient removed. EffectsMixerViewModelSeamTests (6 passed).

---

## Ranking Criteria

| Criterion | Weight | Evidence |
|-----------|--------|----------|
| Daily-use impact | High | Core workflows: mixing, assistant, settings |
| Async lifecycle complexity | High | Constructor fire-and-forget (ADR-047), selection-triggered loads |
| Mutation/destructive operations | High | Delete, Cancel, Upload, Create |
| View-owned workflow logic | Medium | ViewModels in `Views/Panels/` |
| Test absence | Medium | No ViewModel tests or only model tests |
| Blast radius | Medium | File size, call-site count |

---

## Live Unresolved Queue (Ranked 1–20)

| Rank | File | Call Sites | Lifecycle Risk | Mutation | Expected Seam | Proof |
|------|------|------------|----------------|----------|---------------|-------|
| ~~1~~ | ~~`Views/Panels/EffectsMixerViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-15) IEffectsMeterClient + IEffectChainClient + IMixerStateClient | EffectsMixerViewModelSeamTests (6 passed) |
| ~~2~~ | ~~`ViewModels/AssistantViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~3~~ | ~~`ViewModels/MixAssistantViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~4~~ | ~~`ViewModels/AdvancedSettingsViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~5~~ | ~~`ViewModels/UltimateDashboardViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~6~~ | ~~`ViewModels/ImageSearchViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~7~~ | ~~`ViewModels/TemplateLibraryViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~8~~ | ~~`ViewModels/VoiceMorphViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~9~~ | ~~`ViewModels/VoiceStyleTransferViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~10~~ | ~~`ViewModels/StyleTransferViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~11~~ | ~~`ViewModels/UpscalingViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~12~~ | ~~`Views/Panels/EngineParameterTuningViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~13~~ | ~~`Views/Panels/ImageGenViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~14~~ | ~~`ViewModels/SpectrogramViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~15~~ | ~~`ViewModels/SpatialAudioViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~16~~ | ~~`ViewModels/SonographyVisualizationViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~17~~ | ~~`ViewModels/LexiconViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~18~~ | ~~`ViewModels/TodoPanelViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~19~~ | ~~`ViewModels/PluginHealthDashboardViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~20~~ | ~~`ViewModels/ProfileHealthDashboardViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~21~~ | ~~`ViewModels/AIMixingMasteringViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~22~~ | ~~`ViewModels/AIProductionAssistantViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~23~~ | ~~`ViewModels/AdvancedSpectrogramVisualizationViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~24~~ | ~~`ViewModels/AdvancedWaveformVisualizationViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~25~~ | ~~`ViewModels/AnalyticsDashboardViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~26~~ | ~~`ViewModels/AudioAnalysisViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~27~~ | ~~`ViewModels/DeepfakeCreatorViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~28~~ | ~~`ViewModels/GPUStatusViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~29~~ | ~~`ViewModels/MCPDashboardViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~30~~ | ~~`ViewModels/MultilingualSupportViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~31~~ | ~~`ViewModels/PipelineConversationViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~32~~ | ~~`ViewModels/RealTimeAudioVisualizerViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~33~~ | ~~`ViewModels/SpatialStageViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |
| ~~34~~ | ~~`ViewModels/TextHighlightingViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-14) | Seam tests |

---

## File-Level Inspection (Ranks 1–3)

### Rank 1: EffectsMixerViewModel — DONE (2026-03-15)

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Status:** **MIGRATED** to `IEffectsMeterClient` + `IEffectChainClient` + `IMixerStateClient`. IBackendClient removed. Domain split per [EFFECTSMIXER_SEAM_EXECUTION_PLAN.md](EFFECTSMIXER_SEAM_EXECUTION_PLAN.md). EffectsMixerViewModelSeamTests (6 passed).

---

### Rank 2: AssistantViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/AssistantViewModel.cs`

**Status:** **MIGRATED** to `IAssistantClient` + `IProjectsClient`. Model types fixed (AssistantConversation, AssistantMessage, AssistantTaskSuggestion). RefreshAsync made public for IPanelLifecycle. Stray _backendClient refs replaced.

---

### Rank 3: MixAssistantViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/MixAssistantViewModel.cs`

**Status:** **MIGRATED** to `IMixAssistantClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load; constructor fire-and-forget removed. Seam tests in `MixAssistantViewModelSeamTests.cs`.

---

### Rank 4: AdvancedSettingsViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/AdvancedSettingsViewModel.cs`

**Status:** **MIGRATED** to `IAdvancedSettingsClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load; constructor fire-and-forget removed. Seam tests in `AdvancedSettingsViewModelSeamTests.cs`.

---

### Rank 5: UltimateDashboardViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/UltimateDashboardViewModel.cs`

**Status:** **MIGRATED** to `IUltimateDashboardClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load; constructor fire-and-forget removed. Seam tests in `UltimateDashboardViewModelSeamTests.cs`.

---

### Rank 6: ImageSearchViewModel — DONE (2026-03-13)

**File:** `src/VoiceStudio.App/ViewModels/ImageSearchViewModel.cs`

**Status:** **MIGRATED** to `IImageSearchClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load (LoadSources, LoadCategories, LoadColors); constructor fire-and-forget removed. Seam tests in `ImageSearchViewModelSeamTests.cs`.

---

### Rank 8: VoiceMorphViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/VoiceMorphViewModel.cs`

**Status:** **MIGRATED** to `IVoiceMorphClient` + `IProjectAudioClient`. IPanelLifecycle implemented; OnActivatedAsync for initial load (LoadConfigs, LoadAudioFiles, LoadVoiceProfiles); constructor fire-and-forget removed; RefreshAsync public for IPanelLifecycle. Seam tests in `VoiceMorphViewModelSeamTests.cs`.

---

### Rank 24: AdvancedWaveformVisualizationViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/AdvancedWaveformVisualizationViewModel.cs`

**Status:** **MIGRATED** to `IAdvancedWaveformClient` + `IProjectAudioClient`. AdvancedWaveformModels.cs (Core.Models), AdvancedWaveformClient. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync. Seam tests in `AdvancedWaveformVisualizationViewModelSeamTests.cs`.

---

### Rank 25: AnalyticsDashboardViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/AnalyticsDashboardViewModel.cs`

**Status:** **MIGRATED** to `IAnalyticsDashboardClient`. AnalyticsDashboardModels.cs (Services), AnalyticsDashboardClient. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync. Seam tests in `AnalyticsDashboardViewModelSeamTests.cs`.

---

### Rank 26: AudioAnalysisViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/AudioAnalysisViewModel.cs`

**Status:** **MIGRATED** to `IAudioAnalysisClient`. AudioAnalysisModels.cs (Core.Models), AudioAnalysisClient. IPanelLifecycle; selection-triggered load with cancellation + staleness guard. Seam tests in `AudioAnalysisViewModelSeamTests.cs`.

---

### Rank 27: DeepfakeCreatorViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/DeepfakeCreatorViewModel.cs`

**Status:** **MIGRATED** to `IDeepfakeCreatorClient`. DeepfakeCreatorModels.cs (Core.Models), DeepfakeCreatorClient. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync. Seam tests in `DeepfakeCreatorViewModelSeamTests.cs`.

---

### Rank 28: GPUStatusViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/GPUStatusViewModel.cs`

**Status:** **MIGRATED** to `IGPUStatusClient`. GPUStatusModels.cs (Core.Models), GPUStatusClient. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync. Seam tests in `GPUStatusViewModelSeamTests.cs`.

---

## Truly Remaining (from generator output, 1 total)

Source: `python scripts/ci/generate_ibackendclient_queue.py`. Last verified: 2026-03-14.

| # | File | Notes |
|---|------|-------|
| 1 | `Views/Panels/EffectsMixerViewModel.cs` | **Architecture track deferred** — domain split (Option C) |
| ~~2~~ | ~~`Views/Panels/SLODashboardViewModel.cs`~~ | **MIGRATED** (2026-03-14) to ISLODashboardClient |
| ~~3~~ | ~~`Views/Panels/TagOrganizationViewModel.cs`~~ | **MIGRATED** (2026-03-14) to ITagOrganizationClient |

---

## VideoEditViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/VideoEditViewModel.cs`

**Status:** **MIGRATED** to `IVideoEditClient`. Selection-triggered (SelectedVideoPath) FAF removed; cancellation + staleness guard for LoadVideoInfoForSelectionAsync. Seam tests in `VideoEditViewModelSeamTests.cs`.

---

## VideoGenViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/VideoGenViewModel.cs`

**Status:** **MIGRATED** to `IVideoGenClient`. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync (LoadEnginesAsync); selection-triggered quality metrics with cancellation + staleness guard. Seam tests in `VideoGenViewModelSeamTests.cs`.

---

## AdvancedRealTimeVisualizationViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/AdvancedRealTimeVisualizationViewModel.cs`

**Status:** **MIGRATED** to `IAdvancedRealTimeVisualizationClient`. GetVisualizationDataAsync, GetPlaybackPositionAsync. Timer-based updates retained (starts in constructor). Seam tests in `AdvancedRealTimeVisualizationViewModelSeamTests.cs`. Constructor invariant omitted (timer-based FAF).

---

## AudioMonitoringDashboardViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/AudioMonitoringDashboardViewModel.cs`

**Status:** **MIGRATED** to `IAudioMonitoringDashboardClient`. GetAudioMetersAsync, GetLoudnessDataAsync. No constructor FAF; polling via owned _pollingCts. Seam tests in `AudioMonitoringDashboardViewModelSeamTests.cs`.

---

## ImageVideoEnhancementPipelineViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/ImageVideoEnhancementPipelineViewModel.cs`

**Status:** **MIGRATED** to `IImageVideoEnhancementPipelineClient`. ApplyPipelineAsync, PreviewPipelineAsync. No constructor FAF; LoadEnhancementPresets/LoadAvailableEnhancements are sync in-memory. Seam tests in `ImageVideoEnhancementPipelineViewModelSeamTests.cs`.

---

## SLODashboardViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/SLODashboardViewModel.cs`

**Status:** **MIGRATED** to `ISLODashboardClient`. GetSloDataAsync. SloDashboardModels.cs (SloMetricDto, SloDataResponse), SLODashboardClient. No constructor FAF; LoadSloDataAsync from View Loaded. Seam tests in `SLODashboardViewModelSeamTests.cs`.

---

## TagOrganizationViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/Views/Panels/TagOrganizationViewModel.cs`

**Status:** **MIGRATED** to `ITagOrganizationClient` + `IProfilesClient`. UpdateTagAsync. TagOrganizationClient. Tag data from IProfilesClient.GetProfilesAsync; update via ITagOrganizationClient. No constructor FAF; RefreshAsync from View Loaded and property changes. Seam tests in `TagOrganizationViewModelSeamTests.cs`.

---

## TrainingQualityVisualizationViewModel — DONE (2026-03-14)

**File:** `src/VoiceStudio.App/ViewModels/TrainingQualityVisualizationViewModel.cs`

**Status:** **MIGRATED** to `ITrainingClient`. Constructor FAF removed; IPanelLifecycle with OnActivatedAsync; selection-triggered load with cancellation + staleness guard. Seam tests in `TrainingQualityVisualizationViewModelSeamTests.cs`.

---

## Next Migration Target (Routine)

**NONE.** Routine queue closed. Next work is EffectsMixer architecture-track split (see [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md)).

Before starting any migration: run `python scripts/ci/check_ibackendclient_creep.py` and confirm baseline alignment.

---

## Changelog

- 2026-03-14: TagOrganizationViewModel migrated to ITagOrganizationClient. 1 unresolved (EffectsMixer deferred). Routine targets complete.
- 2026-03-14: SLODashboardViewModel migrated to ISLODashboardClient. 2 unresolved. Next target: TagOrganizationViewModel.
- 2026-03-14: ImageVideoEnhancementPipelineViewModel migrated to IImageVideoEnhancementPipelineClient. 3 unresolved. Next target: SLODashboardViewModel.
- 2026-03-14: AudioMonitoringDashboardViewModel migrated to IAudioMonitoringDashboardClient. 4 unresolved. Next target: ImageVideoEnhancementPipelineViewModel.
- 2026-03-14: AdvancedRealTimeVisualizationViewModel migrated to IAdvancedRealTimeVisualizationClient. 5 unresolved. Next target: AudioMonitoringDashboardViewModel.
- 2026-03-14: VideoGenViewModel migrated to IVideoGenClient. 6 unresolved. Next target: AdvancedRealTimeVisualizationViewModel.
- 2026-03-14: VideoEditViewModel migrated to IVideoEditClient. 7 unresolved. Next target: VideoGenViewModel.
- 2026-03-14: TrainingQualityVisualizationViewModel migrated to ITrainingClient. 8 unresolved. Next target: VideoEditViewModel.
- 2026-03-14: Governance Truth Reset. Replaced "Remaining Unresolved" with "Truly Remaining" (9 from generator). Next routine target: TrainingQualityVisualizationViewModel. EffectsMixer stays deferred. TrainingQualityVisualizationViewModel inspection added (ITrainingClient; constructor + selection FAF).
- 2026-03-14: GPUStatusViewModel migrated to IGPUStatusClient. 15 unresolved. Next target: MCPDashboardViewModel.
- 2026-03-14: DeepfakeCreatorViewModel migrated to IDeepfakeCreatorClient. 16 unresolved. Next target: GPUStatusViewModel.
- 2026-03-14: AudioAnalysisViewModel migrated to IAudioAnalysisClient. 17 unresolved. Next target: DeepfakeCreatorViewModel.
- 2026-03-14: AnalyticsDashboardViewModel migrated to IAnalyticsDashboardClient. 18 unresolved. Next target: AudioAnalysisViewModel.
- 2026-03-14: AdvancedWaveformVisualizationViewModel migrated to IAdvancedWaveformClient + IProjectAudioClient. 20 unresolved. Next target: AnalyticsDashboardViewModel.
- 2026-03-14: AdvancedSearchViewModel migrated to ISearchClient. 23 unresolved. Next target: AIMixingMasteringViewModel (IAIMixingClient).
- 2026-03-14: WorkflowAutomationViewModel migrated to IWorkflowAutomationClient. Next target: TBD (regenerate queue).
- 2026-03-14: VoiceQuickCloneViewModel migrated to IVoiceQuickCloneClient. Next target: WorkflowAutomationViewModel (IWorkflowAutomationClient).
- 2026-03-14: Next target: VoiceQuickCloneViewModel (IVoiceQuickCloneClient). Rationale: core workflow, Top 3 Rank 2.
- 2026-03-14: Truth reset. Alphabetical list synced to generate_ibackendclient_queue.py output. Removed migrated ViewModels (VoiceBrowser, VoiceMorphingBlending, KeyboardShortcuts, MarkerManager, PronunciationLexicon, Prosody, TagManager). Next target set to TBD until Top-3 inspection.
- 2026-03-14: Truth Reset Task 2. Queue regenerated from generate_ibackendclient_queue.py. Next target: HelpViewModel (IHelpClient). Alphabetical list updated; ProfileHealthDashboardViewModel removed (MIGRATED).
- 2026-03-14: EngineParameterTuningViewModel migrated to IEngineParameterTuningClient (Bulletproof Plan 2.2). Rank 12 marked MIGRATED; next target Rank 13 (ImageGenViewModel).
- 2026-03-14: Queue refresh (Bulletproof Plan 2.1). VoiceStyleTransfer, StyleTransfer, Upscaling migrated. Next target Rank 12 (EngineParameterTuningViewModel) or Rank 13 (ImageGenViewModel).
- 2026-03-14: VoiceMorphViewModel migrated to IVoiceMorphClient + IProjectAudioClient. Rank 8 marked MIGRATED; next target Rank 9 (VoiceStyleTransferViewModel).
- 2026-03-13: File-level inspection for ranks 1–3. EffectsMixer deferred; MixAssistant recommended as next target. Rationale: constructor-only lifecycle fix, simpler API surface.
- 2026-03-13: ImageSearchViewModel migrated to IImageSearchClient. IPanelLifecycle; OnActivatedAsync for initial load; seam tests added.
- 2026-03-13: Initial live queue. Ranks 1–20 derived from baseline; remaining 35+ listed alphabetically. Replaces exhausted historical ranking table for next-wave selection.
- 2026-03-13: Doc sync. EffectsMixer lifecycle hardened; Rank 1 updated; seam migration deferred per domain split (Option C).
