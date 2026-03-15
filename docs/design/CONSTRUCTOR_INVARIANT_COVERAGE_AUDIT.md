# Constructor Invariant Coverage Audit (ST-01)

> **Purpose:** Grep-verifiable matrix of migrated ViewModels vs. constructor-no-client-call invariant coverage.  
> **Source:** MIGRATED_NO_IBACKENDCLIENT from `scripts/ci/check_ibackendclient_creep.py`; seam tests from `src/VoiceStudio.App.Tests/ViewModels/*SeamTests.cs`.  
> **Last Audit:** 2026-03-14

---

## Invariant Definition

**Constructor_DoesNotCallClient_BeforeActivation:** A test that instantiates the ViewModel with mocked clients and verifies no client methods are called before `OnActivatedAsync` (or equivalent). Prevents constructor fire-and-forget regression (ADR-047, RETAINED_ASYNC_RULE).

**Grep verification:** `rg "Constructor_DoesNotCallClient_BeforeActivation" src/VoiceStudio.App.Tests/ViewModels/`

---

## Coverage Matrix

| ViewModel | Has Seam Test | Has Constructor Invariant | Notes |
|-----------|----------------|--------------------------|-------|
| AdvancedRealTimeVisualizationViewModel | Y | N | AdvancedRealTimeVisualizationViewModelSeamTests (migrated 2026-03-14); **documented exemption** — timer-based FAF from constructor; Dispose() stops timer; per RETAINED_ASYNC_RULE §Allowed Cases (polling loop). See Exemption Justification below. |
| AdvancedSearchViewModel | Y | Y | AdvancedSearchViewModelSeamTests (migrated 2026-03-14) |
| AudioMonitoringDashboardViewModel | Y | Y | AudioMonitoringDashboardViewModelSeamTests (migrated 2026-03-14) |
| ImageVideoEnhancementPipelineViewModel | Y | Y | ImageVideoEnhancementPipelineViewModelSeamTests (migrated 2026-03-14) |
| SLODashboardViewModel | Y | Y | SLODashboardViewModelSeamTests (migrated 2026-03-14) |
| AnalyticsDashboardViewModel | Y | Y | AnalyticsDashboardViewModelSeamTests (migrated 2026-03-14) |
| AudioAnalysisViewModel | Y | Y | AudioAnalysisViewModelSeamTests (migrated 2026-03-14) |
| DeepfakeCreatorViewModel | Y | Y | DeepfakeCreatorViewModelSeamTests (migrated 2026-03-14) |
| GPUStatusViewModel | Y | Y | GPUStatusViewModelSeamTests (migrated 2026-03-14) |
| AdvancedSpectrogramVisualizationViewModel | Y | Y | AdvancedSpectrogramVisualizationViewModelSeamTests (migrated 2026-03-14) |
| AdvancedWaveformVisualizationViewModel | Y | Y | AdvancedWaveformVisualizationViewModelSeamTests (migrated 2026-03-14) |
| AIMixingMasteringViewModel | Y | Y | AIMixingMasteringViewModelSeamTests (migrated 2026-03-14) |
| AIProductionAssistantViewModel | Y | Y | AIProductionAssistantViewModelSeamTests (migrated 2026-03-14) |
| AdvancedSettingsViewModel | Y | Y | AdvancedSettingsViewModelSeamTests |
| APIKeyManagerViewModel | Y | Y | APIKeyManagerViewModelSeamTests |
| AssistantViewModel | Y | Y | AssistantViewModelSeamTests |
| AutomationViewModel | Y | Y | AutomationViewModelSeamTests |
| BackupRestoreViewModel | Y | Y | BackupRestoreViewModelSeamTests |
| BatchProcessingViewModel | Y | Y | BatchProcessingViewModelSeamTests |
| DatasetQAViewModel | Y | Y | DatasetQAViewModelSeamTests |
| DiagnosticsViewModel | Y | Y | DiagnosticsViewModelSeamTests |
| EffectsMixerViewModel | Y | Y | EffectsMixerViewModelSeamTests (migrated 2026-03-15) |
| EmbeddingExplorerViewModel | Y | Y | EmbeddingExplorerViewModelSeamTests |
| EmotionStylePresetEditorViewModel | Y | Y | EmotionStylePresetEditorViewModelSeamTests (migrated 2026-03-14) |
| EngineParameterTuningViewModel | Y | Y | EngineParameterTuningViewModelSeamTests (migrated 2026-03-14) |
| EmotionControlViewModel | Y | Y | EmotionControlViewModelSeamTests |
| EmotionStyleControlViewModel | Y | Y | EmotionStyleControlViewModelSeamTests |
| EngineRecommendationViewModel | Y | Y | EngineRecommendationViewModelSeamTests |
| EnsembleSynthesisViewModel | Y | Y | EnsembleSynthesisViewModelSeamTests |
| GlobalSearchViewModel | Y | Y | GlobalSearchViewModelSeamTests |
| HelpViewModel | Y | Y | HelpViewModelSeamTests (migrated 2026-03-14) |
| ImageGenViewModel | Y | Y | ImageGenViewModelSeamTests (migrated 2026-03-14) |
| ImageSearchViewModel | Y | Y | ImageSearchViewModelSeamTests |
| JobProgressViewModel | Y | Y | JobProgressViewModelSeamTests |
| LibraryViewModel | Y | Y | LibraryViewModelSeamTests |
| MacroViewModel | Y | Y | MacroViewModelSeamTests |
| MiniTimelineViewModel | N | N | **N/A** — No backend seam; uses IAudioPlayerService only. In MIGRATED_NO_IBACKENDCLIENT (no IBackendClient). Exemption: no seam test required. |
| MCPDashboardViewModel | Y | Y | MCPDashboardViewModelSeamTests (migrated 2026-03-14) |
| MixAssistantViewModel | Y | Y | MixAssistantViewModelSeamTests |
| MultilingualSupportViewModel | Y | Y | MultilingualSupportViewModelSeamTests (migrated 2026-03-14) |
| PipelineConversationViewModel | Y | Y | PipelineConversationViewModelSeamTests (migrated 2026-03-14) |
| RealTimeAudioVisualizerViewModel | Y | Y | RealTimeAudioVisualizerViewModelSeamTests (migrated 2026-03-14) |
| SpatialStageViewModel | Y | Y | SpatialStageViewModelSeamTests (migrated 2026-03-14) |
| TextHighlightingViewModel | Y | Y | TextHighlightingViewModelSeamTests (migrated 2026-03-14) |
| TrainingQualityVisualizationViewModel | Y | Y | TrainingQualityVisualizationViewModelSeamTests (migrated 2026-03-14) |
| VideoEditViewModel | Y | Y | VideoEditViewModelSeamTests (migrated 2026-03-14) |
| VideoGenViewModel | Y | Y | VideoGenViewModelSeamTests (migrated 2026-03-14) |
| ModelManagerViewModel | Y | Y | ModelManagerViewModelSeamTests |
| MultiVoiceGeneratorViewModel | Y | Y | MultiVoiceGeneratorViewModelSeamTests |
| PresetLibraryViewModel | Y | Y | PresetLibraryViewModelSeamTests |
| ProfileComparisonViewModel | Y | Y | ProfileComparisonViewModelSeamTests |
| QualityBenchmarkViewModel | Y | Y | QualityBenchmarkViewModelSeamTests |
| QualityControlViewModel | Y | Y | QualityControlViewModelSeamTests |
| QualityDashboardViewModel | Y | Y | QualityDashboardViewModelSeamTests |
| QualityOptimizationWizardViewModel | Y | Y | QualityOptimizationWizardViewModelSeamTests |
| RealTimeVoiceConverterViewModel | Y | Y | RealTimeVoiceConverterViewModelSeamTests |
| RecordingViewModel | Y | Y | RecordingViewModelSeamTests |
| SceneBuilderViewModel | Y | Y | SceneBuilderViewModelSeamTests |
| ScriptEditorViewModel | Y | Y | ScriptEditorViewModelSeamTests |
| SettingsViewModel | Y | Y | SettingsViewModelSeamTests |
| SSMLControlViewModel | Y | Y | SSMLControlViewModelSeamTests |
| TemplateLibraryViewModel | Y | Y | TemplateLibraryViewModelSeamTests |
| TextBasedSpeechEditorViewModel | Y | Y | TextBasedSpeechEditorViewModelSeamTests |
| TextSpeechEditorViewModel | Y | Y | TextSpeechEditorViewModelSeamTests |
| TrainingDatasetEditorViewModel | Y | Y | TrainingDatasetEditorViewModelSeamTests |
| TrainingViewModel | Y | Y | TrainingViewModelSeamTests |
| UltimateDashboardViewModel | Y | Y | UltimateDashboardViewModelSeamTests |
| VoiceBrowserViewModel | Y | Y | VoiceBrowserViewModelSeamTests (added 2026-03-14) |
| VoiceCloningWizardViewModel | Y | Y | VoiceCloningWizardViewModelSeamTests |
| VoiceMorphViewModel | Y | Y | VoiceMorphViewModelSeamTests |
| VoiceQuickCloneViewModel | Y | Y | VoiceQuickCloneViewModelSeamTests (migrated 2026-03-14) |
| VoiceStyleTransferViewModel | Y | Y | VoiceStyleTransferViewModelSeamTests |
| SpectrogramViewModel | Y | Y | SpectrogramViewModelSeamTests (migrated 2026-03-14) |
| SpatialAudioViewModel | Y | Y | SpatialAudioViewModelSeamTests (migrated 2026-03-14) |
| PluginHealthDashboardViewModel | Y | Y | PluginHealthDashboardViewModelSeamTests (migrated 2026-03-14) |
| ProfileHealthDashboardViewModel | Y | Y | ProfileHealthDashboardViewModelSeamTests (migrated 2026-03-14) |
| SonographyVisualizationViewModel | Y | Y | SonographyVisualizationViewModelSeamTests (migrated 2026-03-14) |
| LexiconViewModel | Y | Y | LexiconViewModelSeamTests (migrated 2026-03-14) |
| TodoPanelViewModel | Y | Y | TodoPanelViewModelSeamTests (migrated 2026-03-14) |
| StyleTransferViewModel | Y | Y | StyleTransferViewModelSeamTests |
| TagOrganizationViewModel | Y | Y | TagOrganizationViewModelSeamTests (migrated 2026-03-14) |
| UpscalingViewModel | Y | Y | UpscalingViewModelSeamTests |
| WorkflowAutomationViewModel | Y | Y | WorkflowAutomationViewModelSeamTests (migrated 2026-03-14) |
| AnalyzerViewModel | Y | Y | AnalyzerViewModelSeamTests |

---

## Exemption Justification

| ViewModel | Exemption | Rationale |
|-----------|-----------|------------|
| MiniTimelineViewModel | No seam test required | No backend client; uses IAudioPlayerService only. Never had IBackendClient to migrate. Not a migrated ViewModel in the IBackendClient sense. |
| AdvancedRealTimeVisualizationViewModel | Constructor invariant omitted | Timer-based FAF starts in constructor for real-time viz updates. Per [RETAINED_ASYNC_RULE.md](RETAINED_ASYNC_RULE.md) §Allowed Cases: polling loop. `Dispose()` calls `StopUpdateTimer()` which disposes `System.Threading.Timer`; no Tick after dispose. Disposal safe. Not selection-triggered (staleness N/A). |

---

## Summary

| Metric | Count |
|--------|-------|
| Migrated ViewModels (baseline) | 80 |
| With seam test + constructor invariant | 77 |
| **Exemptions (documented)** | **2** (MiniTimeline N/A; AdvancedRealTimeVisualization timer-based FAF) |

**Canonical source:** `scripts/ci/check_ibackendclient_creep.py` MIGRATED_NO_IBACKENDCLIENT (80 entries). Audit matrix must include all entries. Exemption math: 80 - 2 exempt = 78 required; 77 with invariant; 1 in baseline (AdvancedRealTimeVisualization). No contradiction with STATE.md.

---

## High-Priority Backfill Targets (Plan Task 10)

Per plan: Start with Assistant, EmbeddingExplorer, Recording, Diagnostics.

| ViewModel | Rationale | Status |
|-----------|-----------|--------|
| AssistantViewModel | Core workflow; daily-use impact | **DONE** |
| EmbeddingExplorerViewModel | IProjectAudioClient + IEmbeddingExplorerClient | **DONE** |
| RecordingViewModel | Mutation surface; recording workflow | **DONE** |
| DiagnosticsViewModel | Health/telemetry; diagnostic workflow | **DONE** |

---

## Changelog

- 2026-03-15: EffectsMixerViewModel added to matrix (Y/Y). Summary updated to 80 migrated, 77 with invariant.
- 2026-03-14: Truth sync. Summary updated to 79 migrated, 76 with invariant (derived from check_ibackendclient_creep.py). MiniTimelineViewModel note corrected: in MIGRATED_NO_IBACKENDCLIENT.
- 2026-03-14: Exemption Justification subsection added. MiniTimelineViewModel: N/A (no backend seam). AdvancedRealTimeVisualizationViewModel: documented exemption per RETAINED_ASYNC_RULE (timer-based FAF, disposal safe).
- 2026-03-14: VideoEditViewModel migrated to IVideoEditClient. Seam tests added. 72 migrated, 71 with invariant.
- 2026-03-14: TrainingQualityVisualizationViewModel migrated to ITrainingClient. Seam tests added. 71 migrated, 70 with invariant.
- 2026-03-14: Governance Truth Reset. Summary correction: "With invariant" 70 → 69. 69 + 1 exempt = 70 migrated. No contradiction.
- 2026-03-14: Initial audit. 21 with invariant; 22 gaps. Document created per ST-01.
- 2026-03-14: Backfill: Assistant, EmbeddingExplorer, Recording, Diagnostics. 25 with invariant; 18 gaps. RecordingViewModel: removed constructor FAF, added IPanelLifecycle.
- 2026-03-14: Backfill: Settings, TextSpeechEditor, DatasetQA, Macro. 29 with invariant; 14 gaps. DatasetQAViewModel, SettingsViewModel: removed constructor FAF, added IPanelLifecycle.
- 2026-03-14: Backfill: QualityBenchmark, QualityControl, QualityDashboard, QualityOptimizationWizard, RealTimeVoiceConverter. 33 with invariant; 10 gaps.
- 2026-03-14: Backfill: AnalyzerViewModel, JobProgressViewModel. 35 with invariant; 8 gaps.
- 2026-03-14: Constructor Invariant Gap Closure: TextBasedSpeechEditorViewModel constructor FAF removed, IPanelLifecycle added; 7 seam tests (EmotionControl, EmotionStyleControl, EngineRecommendation, ModelManager, PresetLibrary, SSMLControl, TextBasedSpeechEditor). 42/43 with invariant; MiniTimeline exempt.
- 2026-03-14: TextSpeechEditorViewModel constructor FAF fixed (Bulletproof Plan 0.1): removed 4 constructor fire-and-forget calls, implemented IPanelLifecycle with OnActivatedAsync. Audit Y/Y now accurate; seam tests pass.
- 2026-03-14: EngineParameterTuningViewModel migrated to IEngineParameterTuningClient (Bulletproof Plan 2.2). Seam tests added; 44 migrated, 43 with invariant.
- 2026-03-14: ImageGenViewModel migrated to IImageGenClient. Seam tests added; 45 migrated, 44 with invariant.
- 2026-03-14: SpectrogramViewModel migrated to ISpectrogramClient. Constructor FAF removed (LoadColorSchemesAsync moved to OnActivatedAsync). Seam tests added; 46 migrated, 45 with invariant.
- 2026-03-14: SpatialAudioViewModel migrated to ISpatialAudioClient. Seam tests added; 47 migrated, 46 with invariant.
- 2026-03-14: LexiconViewModel migrated to ILexiconClient. TodoPanelViewModel migrated to ITodoPanelClient. Seam tests added; 52 migrated, 51 with invariant.
- 2026-03-14: HelpViewModel migrated to IHelpClient. Constructor FAF removed; ILifecyclePanelView with OnActivatedAsync; IDispatcherTimer for search debounce (RETAINED_ASYNC_RULE). Seam tests added; 53 migrated, 52 with invariant.
- 2026-03-14: EmotionStylePresetEditorViewModel migrated to IEmotionControlClient. Constructor FAF removed; LoadPresetsAsync called from view Loaded. UpdatePresetAsync added to IEmotionControlClient. Seam tests added; 54 migrated, 53 with invariant.
- 2026-03-14: Truth-sync with STATE. Canonical: 54 migrated, 53 with invariant, 1 exempt (MiniTimeline).
- 2026-03-14: Creep alignment. VoiceBrowserViewModelSeamTests added; 7 ViewModels added to MIGRATED_NO_IBACKENDCLIENT. 54 migrated, 54 with invariant.
- 2026-03-14: WorkflowAutomationViewModel migrated to IWorkflowAutomationClient. Seam tests added; 55 migrated, 55 with invariant.
- 2026-03-14: AdvancedSearchViewModel migrated to ISearchClient. Seam tests added; 56 migrated, 56 with invariant.
- 2026-03-14: AIMixingMasteringViewModel migrated to IAIMixingClient. Seam tests added; 57 migrated, 57 with invariant.
- 2026-03-14: AIProductionAssistantViewModel migrated to IAIProductionAssistantClient. Seam tests added; 58 migrated, 58 with invariant.
- 2026-03-14: AdvancedSpectrogramVisualizationViewModel migrated to IAdvancedSpectrogramClient + IProjectAudioClient. Constructor FAF removed; IPanelLifecycle. Seam tests added; 59 migrated, 59 with invariant.
- 2026-03-14: SpatialStageViewModel migrated to ISpatialStageClient + IProjectsClient + IProjectAudioClient. Constructor FAF removed; IPanelLifecycle. Seam tests added; 69 migrated, 69 with invariant.
- 2026-03-14: TextHighlightingViewModel migrated to ITextHighlightingClient + IProjectsClient + IProjectAudioClient. Constructor FAF removed; IPanelLifecycle. Seam tests added; 70 migrated, 70 with invariant.
