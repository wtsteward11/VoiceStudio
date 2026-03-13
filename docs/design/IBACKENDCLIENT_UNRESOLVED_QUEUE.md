# IBackendClient Unresolved Queue (Live)

> **Purpose:** Live ranked list of ViewModels/Panels that still take `IBackendClient` directly. Use this for the next migration wave — not the completed historical table in [IBACKENDCLIENT_LONGTAIL_RANKING.md](IBACKENDCLIENT_LONGTAIL_RANKING.md).  
> **Source:** Derived from [.ci/ibackendclient_baseline.txt](../../.ci/ibackendclient_baseline.txt) — entries without `MIGRATED` comment.  
> **Last Generated:** 2026-03-13

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
| 1 | `Views/Panels/EffectsMixerViewModel.cs` | High | High (panel) | Yes | IEffectsMixerClient | Seam tests |
| ~~2~~ | ~~`ViewModels/AssistantViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~3~~ | ~~`ViewModels/MixAssistantViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| ~~4~~ | ~~`ViewModels/AdvancedSettingsViewModel.cs`~~ | — | — | — | **MIGRATED** (2026-03-13) | Seam tests |
| 5 | `ViewModels/UltimateDashboardViewModel.cs` | Medium | Medium | No | IUltimateDashboardClient | Seam tests |
| 6 | `ViewModels/ImageSearchViewModel.cs` | Medium | Medium | Yes | IImageSearchClient | Seam tests |
| 7 | `ViewModels/TemplateLibraryViewModel.cs` | Medium | Medium | Yes | ITemplateLibraryClient | Seam tests |
| 8 | `ViewModels/VoiceMorphViewModel.cs` | Medium | Medium | Yes | IVoiceMorphClient | Seam tests |
| 9 | `ViewModels/VoiceStyleTransferViewModel.cs` | Medium | Medium | Yes | IVoiceStyleTransferClient | Seam tests |
| 10 | `ViewModels/StyleTransferViewModel.cs` | Medium | Medium | Yes | IStyleTransferClient | Seam tests |
| 11 | `ViewModels/UpscalingViewModel.cs` | Medium | Medium | Yes | IUpscalingClient | Seam tests |
| 12 | `Views/Panels/EngineParameterTuningViewModel.cs` | Medium | Medium | Yes | IEngineParameterTuningClient | Seam tests |
| 13 | `Views/Panels/ImageGenViewModel.cs` | Medium | Medium | Yes | IImageGenClient | Seam tests |
| 14 | `ViewModels/SpectrogramViewModel.cs` | Low | Low | No | ISpectrogramClient | Seam tests |
| 15 | `ViewModels/SpatialAudioViewModel.cs` | Low | Low | No | ISpatialAudioClient | Seam tests |
| 16 | `ViewModels/SonographyVisualizationViewModel.cs` | Low | Low | No | ISonographyClient | Seam tests |
| 17 | `ViewModels/LexiconViewModel.cs` | Low | Low | Yes | ILexiconClient | Seam tests |
| 18 | `ViewModels/TodoPanelViewModel.cs` | Low | Low | Yes | ITodoPanelClient | Seam tests |
| 19 | `ViewModels/PluginHealthDashboardViewModel.cs` | Low | Low | No | IPluginHealthClient | Seam tests |
| 20 | `ViewModels/ProfileHealthDashboardViewModel.cs` | Low | Low | No | IProfileHealthClient | Seam tests |

---

## File-Level Inspection (Ranks 1–3)

### Rank 1: EffectsMixerViewModel — DEFER

**File:** `src/VoiceStudio.App/Views/Panels/EffectsMixerViewModel.cs`

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

**Lifecycle risks:** OnSelectedProjectIdChanged, OnSelectedAudioIdChanged use `ContinueWith` — no `_disposalCts`, no staleness guard. No IDisposable. UndoRedo actions hold `_backendClient` reference.

**Recommendation:** **Defer** until lifecycle hardened (CTS ownership, disposal, staleness guard).

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

## Remaining Unresolved (Alphabetical)

AIMixingMasteringViewModel, AIProductionAssistantViewModel, AdvancedSpectrogramVisualizationViewModel, AdvancedWaveformVisualizationViewModel, AnalyticsDashboardViewModel, AudioAnalysisViewModel, AudioMonitoringDashboardViewModel, DeepfakeCreatorViewModel, EmotionStylePresetEditorViewModel, GPUStatusViewModel, HelpViewModel, ImageVideoEnhancementPipelineViewModel, KeyboardShortcutsViewModel, MCPDashboardViewModel, MarkerManagerViewModel, MultilingualSupportViewModel, PipelineConversationViewModel, ProfileHealthDashboardViewModel, PronunciationLexiconViewModel, ProsodyViewModel, RealTimeAudioVisualizerViewModel, SLODashboardViewModel, SpatialStageViewModel, TagManagerViewModel, TagOrganizationViewModel, TextHighlightingViewModel, TrainingQualityVisualizationViewModel, VideoEditViewModel, VideoGenViewModel, VoiceBrowserViewModel, VoiceMorphingBlendingViewModel, VoiceQuickCloneViewModel, AdvancedRealTimeVisualizationViewModel, AdvancedSearchViewModel, WorkflowAutomationViewModel.

---

## Next Migration Target

**Recommended:** Rank 5 — `ViewModels/UltimateDashboardViewModel.cs` (IUltimateDashboardClient). EffectsMixer (Rank 1) deferred until lifecycle hardened. MixAssistant, Assistant, AdvancedSettings migrated 2026-03-13.

**Alternative:** Rank 1 — `Views/Panels/EffectsMixerViewModel.cs` (IEffectsMixerClient) after lifecycle hardening.

Before starting: run `python scripts/ci/check_ibackendclient_creep.py` and confirm baseline alignment.

---

## Changelog

- 2026-03-13: File-level inspection for ranks 1–3. EffectsMixer deferred; MixAssistant recommended as next target. Rationale: constructor-only lifecycle fix, simpler API surface.
- 2026-03-13: Initial live queue. Ranks 1–20 derived from baseline; remaining 35+ listed alphabetically. Replaces exhausted historical ranking table for next-wave selection.
