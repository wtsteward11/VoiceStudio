# IBackendClient Long-Tail Consumer Ranking

> **Source:** Post-Ranked Architecture Convergence Plan (Phase 1)  
> **Purpose:** Rank the next 10 highest-value direct `IBackendClient` consumers for seam migration.  
> **Baseline:** [.ci/ibackendclient_baseline.txt](../../.ci/ibackendclient_baseline.txt) — 79 approved consumers.  
> **Last Verified:** 2026-03-13 (truth reset; TrainingDatasetEditorViewModel confirmed migrated)

---

## Ranking Criteria (Evidence-Based)

| Criterion | Weight | Evidence Source |
|-----------|--------|-----------------|
| Daily-use impact | High | Core workflows: Library, Recording, Batch, VoiceCloning |
| Async lifecycle complexity | High | Constructor fire-and-forget (ADR-047), selection-triggered loads |
| Mutation/destructive operations | High | Delete, Cancel, Remove, Upload, Create |
| View-owned workflow logic | Medium | ViewModels in `Views/Panels/` (BatchProcessing, Diagnostics) |
| Test absence | Medium | No ViewModel tests or only model tests |
| Blast radius | Medium | File size, call-site count, dependency surface |

---

## Ranked Table (1–10)

| Rank | File | Risk | Why Next | Expected Seam | Proof Requirement |
|------|------|------|----------|--------------|-------------------|
| 1 | ~~`MultiVoiceGeneratorViewModel.cs`~~ | — | DONE (2026-03-13). IMultiVoiceGeneratorClient added; seam-aware tests added. | — |
| 2 | ~~`EnsembleSynthesisViewModel.cs`~~ | — | DONE (2026-03-13). IEnsembleSynthesisClient added; seam-aware tests added. | — |
| 3 | ~~`Views/Panels/MiniTimelineViewModel.cs`~~ | — | DONE (2026-03-13). IBackendClient removed (was unused); uses IAudioPlayerService only. | — |
| 4 | ~~`TrainingDatasetEditorViewModel.cs`~~ | — | DONE (2026-03-13). ITrainingDatasetEditorClient added; constructor fire-and-forget removed; InitializeAsync from Loaded. | — |
| 5 | ~~`Views/Panels/BatchProcessingViewModel.cs`~~ | — | Migration DONE (2026-03-13). Lifecycle: selection/filter staleness gated; polling/WebSocket retained. | — |
| 6 | ~~`VoiceCloningWizardViewModel.cs`~~ | — | DONE (2026-03-13). IVoiceCloningWizardClient added. | — |
| 7 | ~~`LibraryViewModel.cs`~~ | — | DONE (2026-03-13). ILibraryClient added. | — |
| 8 | ~~`RealTimeVoiceConverterViewModel.cs`~~ | — | DONE (2026-03-12). IRealTimeVoiceConverterClient added. | — |
| 9 | ~~`TextBasedSpeechEditorViewModel.cs`~~ | — | DONE (2026-03-13). ITextBasedSpeechEditorClient added. | — |
| 10 | ~~`EmbeddingExplorerViewModel.cs`~~ | — | DONE (2026-03-13). IEmbeddingExplorerClient added. | — |

**Also DONE:** MultiVoiceGeneratorViewModel, EnsembleSynthesisViewModel, MiniTimelineViewModel, RecordingViewModel, DiagnosticsViewModel, DatasetQAViewModel, TextSpeechEditorViewModel, AnalyzerViewModel, SettingsViewModel, MacroViewModel, ModelManagerViewModel, JobProgressViewModel.

---

## Rank 11–15 (Next Wave Candidates)

| Rank | File | Risk | Why Next | Expected Seam |
|------|------|------|----------|---------------|
| 11 | ~~`GlobalSearchViewModel.cs`~~ | — | DONE (2026-03-13). ISearchClient added; seam-aware tests added. | — |
| 12 | ~~`BackupRestoreViewModel.cs`~~ | — | DONE (2026-03-13). IBackupRestoreClient added. | — |
| 13 | ~~`APIKeyManagerViewModel.cs`~~ | — | DONE (2026-03-13). IAPIKeyManagerClient added. | — |
| 14 | ~~`ScriptEditorViewModel.cs`~~ | — | DONE (2026-03-13). IScriptEditorClient added; ScriptActions updated. | — |
| 15 | ~~`AutomationViewModel.cs`~~ | — | DONE (2026-03-13). IAutomationClient added; AutomationActions updated. | — |
| 16 | ~~`SceneBuilderViewModel.cs`~~ | — | DONE (2026-03-13). ISceneBuilderClient added; SceneActions updated. Lifecycle ownership complete: OnActivatedAsync awaits LoadScenesAsync; staleness guard; IDispatcherTimer debounce; disposal. | — |

---

## Evidence Summary

| ViewModel | IBackendClient Call Sites |
|-----------|---------------------------|
| GlobalSearchViewModel | SearchAsync |
| BackupRestoreViewModel | GetBackupsAsync, CreateBackupAsync, etc. |
| APIKeyManagerViewModel | Settings/API key endpoints |
| ScriptEditorViewModel | Script CRUD (migrated) |
| AutomationViewModel | Automation curves (migrated) |
| SceneBuilderViewModel | Scene CRUD and apply (migrated) |

Migrated targets: see seam tests (BatchProcessingViewModelSeamTests, etc.) and [SEAM_MATURITY_AUDIT.md](SEAM_MATURITY_AUDIT.md).

---

## Entry Gate

- Hardening wave closed. SceneBuilder lifecycle ownership complete (2026-03-13, commit a1dfafe9). BatchProcessing: accepted exceptions (BATCH_PROCESSING_LIFECYCLE_PATTERNS.md).
- Before next migration: confirm GlobalSearch, BackupRestore, APIKeyManager still use seams (not IBackendClient); then pick next ranked target.

---

## Historical (Archived)

**Phase 4 Wave 3 re-ranking (2026-03-13):** RealTimeVoiceConverter, TextBasedSpeechEditor, EmbeddingExplorer — all migrated. **Phase 3 selection (2026-03-12):** TrainingDatasetEditor primary; BatchProcessing, VoiceCloningWizard alternates — all migrated. **Strong Candidates:** All six migrated. See Changelog for details.

---

## Changelog

- 2026-03-13: **SceneBuilder lifecycle ownership complete** (commit a1dfafe9). OnActivatedAsync awaits LoadScenesAsync; staleness guard; IDispatcherTimer debounce; disposal. Entry Gate updated.
- 2026-03-13: **SceneBuilderViewModel** lifecycle hardening complete: constructor fire-and-forget removed; OnActivatedAsync; _loadScenesCts, _searchDebounceCts; disposal cleanup; SCENEBUILDER_LIFECYCLE_PATTERNS.md; lifecycle seam tests added.
- 2026-03-13: **SceneBuilderViewModel** migrated to ISceneBuilderClient; SceneActions (CreateSceneAction, DeleteSceneAction) updated to ISceneBuilderClient; SceneModels added; seam-aware tests added.
- 2026-03-13: **ScriptEditorViewModel** migrated to IScriptEditorClient; ScriptActions (CreateScriptAction, DeleteScriptAction, AddScriptSegmentAction, RemoveScriptSegmentAction) updated to IScriptEditorClient; seam-aware tests added.
- 2026-03-13: **AutomationViewModel** migrated to IAutomationClient; AutomationActions (CreateAutomationCurveAction, DeleteAutomationCurveAction) updated to IAutomationClient; AutomationModels added.
- 2026-03-13: **MiniTimelineViewModel** IBackendClient removed (was unused); uses IAudioPlayerService only.
- 2026-03-13: **EnsembleSynthesisViewModel** migrated to IEnsembleSynthesisClient; seam-aware tests added.
- 2026-03-13: **MultiVoiceGeneratorViewModel** migrated to IMultiVoiceGeneratorClient; seam-aware tests added.
- 2026-03-13: **Truth sync.** BatchProcessing status: migration DONE; lifecycle selection/filter gated; polling/WebSocket retained. Refactored ranking doc: moved Phase 3/4, Strong Candidates, Out of Scope to Historical (Archived). Entry Gate updated.
- 2026-03-13: **Truth reset.** TrainingDatasetEditorViewModel marked DONE (verified migrated). New Rank 1–3: MultiVoiceGeneratorViewModel, EnsembleSynthesisViewModel, MiniTimelineViewModel. BatchProcessingViewModel lifecycle: selection/filter gated; polling/WebSocket retained.
- 2026-03-13: Next 10 Tasks plan: DiagnosticsViewModel (IDiagnosticsClient), TextSpeechEditorViewModel (ITextSpeechEditorClient), AnalyzerViewModel (IAnalyzerClient), SettingsViewModel (ISettingsClient), MacroViewModel (IMacroClient), ModelManagerViewModel (IModelManagerClient), JobProgressViewModel (IJobProgressApiClient) migrated; baseline cleaned; seam tests verified.
- 2026-03-13: Wave 3 batch: TextBasedSpeechEditorViewModel, EmbeddingExplorerViewModel, RecordingViewModel, DatasetQAViewModel migrated; ITextBasedSpeechEditorClient, IEmbeddingExplorerClient, IRecordingClient, IDatasetQAClient added; creep script MIGRATED_NO_IBACKENDCLIENT extended.
- 2026-03-13: Phase 4 Wave 3 re-ranking: RealTimeVoiceConverterViewModel primary (lifecycle risk); TextBasedSpeechEditorViewModel alternate 1; EmbeddingExplorerViewModel alternate 2. Evidence table added.
- 2026-03-13: Long-Tail Wave 2: Ranks 2–4 (BatchProcessingViewModel, VoiceCloningWizardViewModel, LibraryViewModel) migrated; IBatchProcessingClient, IVoiceCloningWizardClient, ILibraryClient added; seam-aware tests added.
- 2026-03-12: Initial ranking per Post-Ranked Architecture Convergence Plan Phase 1.
- 2026-03-12: Phase 3 selection: TrainingDatasetEditorViewModel primary; BatchProcessingViewModel, VoiceCloningWizardViewModel alternates.
