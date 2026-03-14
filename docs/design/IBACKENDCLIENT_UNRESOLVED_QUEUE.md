# IBackendClient Unresolved Queue (Live)

> **Purpose:** Live ranked list of ViewModels/Panels that still take `IBackendClient` directly. Use this for the next migration wave — not the completed historical table in [IBACKENDCLIENT_LONGTAIL_RANKING.md](IBACKENDCLIENT_LONGTAIL_RANKING.md).  
> **Source:** Derived from [.ci/ibackendclient_baseline.txt](../../.ci/ibackendclient_baseline.txt) — entries without `MIGRATED` comment.  
> **Last Generated:** 2026-03-14

---

## Architecture Track (Not Routine Queue)

**EffectsMixerViewModel** is **excluded from the routine migration queue**. It requires a domain split per [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md) Option C: `IEffectsMeterClient`, `IEffectChainClient`, `IMixerStateClient`. Lifecycle is hardened; seam migration is a separate architecture track.

**Do not treat EffectsMixer as a simple one-interface migration.** Only reopen when ready to implement the full domain split. Do not list it as "recommended next" for routine migration.

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
| 1 | `Views/Panels/EffectsMixerViewModel.cs` | High | Lifecycle hardened | Yes | Domain split (Option C) | Seam tests |
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

---

## File-Level Inspection (Ranks 1–3)

### Rank 1: EffectsMixerViewModel — Seam Migration Deferred

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

**Lifecycle hardened (2026-03-13).** Still uses IBackendClient; seam migration deferred per domain split (Option C: IEffectsMeterClient, IEffectChainClient, IMixerStateClient). See [EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md](EFFECTSMIXER_DOMAIN_SPLIT_ANALYSIS.md).

| IBackendClient Call | Destructive |
|---------------------|-------------|
| GetAudioMetersAsync | No |
| GetEffectChainsAsync | No |
| GetEffectPresetsAsync | No |
| CreateEffectChainAsync | Yes |
| DeleteEffectChainAsync | Yes |
| ProcessAudioWithChainAsync | Yes |
| UpdateEffectChainAsync | Yes |
| GetMixerStateAsync | No |
| UpdateMixerStateAsync | Yes |
| ResetMixerStateAsync | Yes |
| GetMixerPresetsAsync | No |
| CreateMixerPresetAsync | Yes |
| ApplyMixerPresetAsync | Yes |
| CreateMixerSendAsync, CreateMixerReturnAsync, CreateMixerSubGroupAsync | Yes |
| DeleteMixerSubGroupAsync, DeleteMixerSendAsync, DeleteMixerReturnAsync | Yes |
| UpdateMixerSubGroupAsync, UpdateMixerSendAsync, UpdateMixerReturnAsync | Yes |

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

## Remaining Unresolved (from generator output)

AIMixingMasteringViewModel, AIProductionAssistantViewModel, AdvancedRealTimeVisualizationViewModel, AdvancedSearchViewModel, AdvancedSpectrogramVisualizationViewModel, AdvancedWaveformVisualizationViewModel, AnalyticsDashboardViewModel, AudioAnalysisViewModel, AudioMonitoringDashboardViewModel, DeepfakeCreatorViewModel, EffectsMixerViewModel, GPUStatusViewModel, ImageVideoEnhancementPipelineViewModel, MCPDashboardViewModel, MultilingualSupportViewModel, PipelineConversationViewModel, RealTimeAudioVisualizerViewModel, SLODashboardViewModel, SpatialStageViewModel, TagOrganizationViewModel, TextHighlightingViewModel, TrainingQualityVisualizationViewModel, VideoEditViewModel, VideoGenViewModel, VoiceQuickCloneViewModel, WorkflowAutomationViewModel.

---

## Next Migration Target

**VoiceQuickCloneViewModel** — Core voice cloning workflow; property-handler FAF (OnSelectedAudioFileChanged); CloneVoiceAsync. Seam: IVoiceQuickCloneClient. See [IBACKENDCLIENT_INSPECTION_TOP3.md](IBACKENDCLIENT_INSPECTION_TOP3.md) Rank 2.

Before starting any migration: run `python scripts/ci/check_ibackendclient_creep.py` and confirm baseline alignment.

---

## Changelog

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
