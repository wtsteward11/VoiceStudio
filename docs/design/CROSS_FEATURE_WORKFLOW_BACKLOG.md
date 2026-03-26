# Cross-Feature Workflow Backlog

**Purpose:** Product-value backlog of end-user workflows spanning multiple panels/services.  
**Date:** 2026-03-24  
**Related:** [POST_EXTRACTION_TRANSITION_PLAN.md](POST_EXTRACTION_TRANSITION_PLAN.md), [FEATURE_CATALOG_MASTER.md](../governance/FEATURE_CATALOG_MASTER.md)

**Workflow / STATE lane:** **[Workflow Coherence Pass 08](WORKFLOW_COHERENCE_PASS_08_QUALITY_BENCHMARK_PROFILE_COMPARISON.md)** — **W8-C1 closed** (historical product closure, 2026-03-25): Quality Benchmark operational UI; seam **`QualityBenchmarkViewModelSeamTests`** **8** passed; Quick **`artifacts/verify/20260325_191036`** (**Quick ≠ seam**; **`commit_hash`** **`bcd6d4e5`**). **Hermetic compile-closure hardening** (2026-03-26): **`8ba6363f`**; Quick **`artifacts/verify/20260326_020644`**. **W8-C3 closed** (2026-03-26): Profile Comparison operational UI + engine policy; seam **`ProfileComparisonViewModelSeamTests`** **8** passed; Quick **`artifacts/verify/20260326_025824`**; **`latest_pointer.json`** **`commit_hash`** **`eb98604039b390f676c98fdb805957a46cd9429c`** (Pass 08 **§8.7**). **W8-C2 closed** (2026-03-26): A/B Testing operational UI + seam **`ABTestingViewModelSeamTests`** **8**; proof **Pass 08 §8.10** + authoritative **`latest_pointer.json`** after W8-C2 implementation commit (**HEAD** may trail if docs-only commits follow — see **STATE** truth sync). **Quality cluster row set** complete for **Pass 08 §5** bounded matrix unless a **new §5** row is added. **[Workflow Coherence Pass 07](WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md)** — **W7-C1 closed** (§8.2); **Workflow 7 paused after W7-C1** ([§8.4](WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md#84-workflow-7--continuation--pause-governance)) — Training-cluster **`src/`** reopen only with **signed** [§5 / §8](WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md#8-sign-off-and-execution-record). **[Product trust Pass 01](PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md)** remains **paused after slice 4** (§8.9 Option 1). **P05-Persist-A4** §12-gated. **Pass 06** `src/` only with new §5 row; **do not** open Pass 06 slice 5 by inertia.

---

## Template (per workflow)

| Field | Content |
|-------|---------|
| **Entry point** | User action or panel that starts the flow |
| **Involved panels/services** | ViewModels, clients, services |
| **Current wiring status** | EventAggregator, IContextManager, direct calls, missing links |
| **Known gaps/friction** | Where flow breaks or is inconsistent |
| **Recommended next improvement** | Bounded change to improve coherence |
| **Proof/test opportunity** | Manual, E2E, or unit test surface |

---

## Workflow 1 — Profile → synthesis → timeline/project

| Field | Content |
|-------|---------|
| **Entry point** | Profile selection in Profiles panel (ProfilesViewModel) or VoiceProfile panel; navigation to Synthesis with profile |
| **Involved panels/services** | ProfilesViewModel, VoiceProfileViewModel; VoiceSynthesisViewModel, VoiceSynthesisView; TimelineViewModel; ITimelineSynthesisService, IProfilesClient; IContextManager, IEventAggregator; ProfileSelectedEvent, VoiceProfileSelectedEvent |
| **Current wiring status** | ContextManager publishes ProfileSelectedEvent on selection change. VoiceSynthesisView handles profile selection (code-behind sets ViewModel.SelectedProfile). AddToTimelineEvent and SynthesisCompletedEvent carry ProfileId. TimelineViewModel sets clip.ProfileId (event or IContextManager fallback), blocks add when no profile, selects inserted clip. IWorkflowCoordinatorService.StartSynthesizeWithVoiceAsync exists. |
| **Known gaps/friction** | Profile propagation depends on panel activation (subscription in OnActivatedAsync). Features/Timeline TimelineViewModel OnProfileSelected no-op. |
| **Pass 01 status** | **Complete** (2026-03-23). ProfileId propagation, timeline insertion coherence, focus/selection after insert, workflow-step-specific failure handling. **Authoritative proof:** artifacts/verify/20260323_141258. Reconciliation: [WORKFLOW_PASS_01_ARTIFACT_RECONCILIATION.md](WORKFLOW_PASS_01_ARTIFACT_RECONCILIATION.md). |
| **Pass 01 — what improved** | AddToTimelineEvent/SynthesisCompletedEvent carry ProfileId; VoiceSynthesisViewModel/SynthesisViewModel pass it; TimelineViewModel sets clip.ProfileId, blocks add when no profile, selects inserted clip; "Voice profile required", "Insertion failed", "Clip saved locally but failed to save to project" messages. |
| **Pass 01 — known leftovers** | Profile propagation depends on OnActivatedAsync; Features/Timeline TimelineViewModel OnProfileSelected no-op; VoiceSynthesisViewModel vs SynthesisViewModel not consolidated. |
| **Recommended next improvement** | Pass 05 **C3 Option B** (semantics) is **closed**; persistence behaviors remain deferred. **Next:** Workflow 6 backup/restore scope freeze (recommended) or Pass 05 **Option A/C** follow-up with a **new** bounded matrix—do not extend closed Pass 05 slices. |
| **Proof/test opportunity** | Manual: select profile → switch to synthesis → verify profile pre-populated. E2E: golden path if it covers this flow. Unit: ProfileSelectedEvent subscription in VoiceSynthesisViewModel. |

---

## Workflow 2 — Project open/create → timeline → effects/mixer

| Field | Content |
|-------|---------|
| **Entry point** | Project open in Projects panel; project create; center panel switches to Timeline |
| **Involved panels/services** | ProjectsViewModel, IProjectsClient; TimelineViewModel, ITimelineTrackService, ITimelineClipService; EffectsMixerViewModel, IMixerStateClient; IContextManager, IEventAggregator, ProjectChangedEvent |
| **Current wiring status** | TimelineViewModel.OnSelectedProjectChanged syncs to IContextManager.SetActiveProject; ProjectChangedEvent publishes; EffectsMixerViewModel subscribes, sets SelectedProjectId, loads effect chains/mixer state; OnActivatedAsync syncs SelectedProjectId from ActiveProjectId; stale state cleared when SelectedProjectId null. |
| **Pass 02 status** | **Complete** (2026-03-24). **Authoritative proof:** artifacts/verify/20260324_012252 (verify.ps1 -Quick PASSED; `latest_pointer.json` aligned). Run 20260323_144107 incomplete — superseded. Reconciliation: [WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md](WORKFLOW_PASS_02_ARTIFACT_RECONCILIATION.md). |
| **Pass 02 — what improved** | Timeline→`IContextManager.SetActiveProject`; EffectsMixer `ProjectChangedEvent` + `OnActivatedAsync` sync; clear effect/mixer collections on null project; "Effects/Mixer sync failed" toast. |
| **Pass 02 — known leftovers** | Timeline.Projects vs ProjectStore duplication; `Channels` not cleared on project null; `ProjectChangedEvent` path not covered by dedicated unit test (test host lacks full `IContextManager`). |
| **Recommended next improvement** | Pass 05 slices through **C3 Option B** complete. **Next:** Workflow 6 scope freeze (recommended), Pass 05 persistence follow-up (**Option C before A** if staying on Workflow 5), or Pass 02 leftovers (timeline/mixer). |
| **Proof/test opportunity** | Manual: open project → verify timeline and mixer; switch project → verify mixer clears/repopulates. Unit: SelectedProjectId_SetToNull_ClearsStaleState. |

---

## Workflow 3 — Search → panel focus → item navigation

| Field | Content |
|-------|---------|
| **Entry point** | Global search; search result click |
| **Involved panels/services** | Search service/results; ISelectionBroadcastService, IContextManager; target panels (Library, Profiles, etc.) |
| **Current wiring status** | `SearchOverlayCoordinator` → `ShellNavigationCoordinator` → `PanelHost.Content` → `INavigatablePanel.NavigateToItemAsync` (+ optional `searchMetadata`). Toasts distinguish panel open vs selection success vs partial failure. |
| **Known gaps/friction** | `ISelectionBroadcastService` not driven from global search; no E2E for search navigation; `PanelNavigationTestHook` exists for unit tests only (production must leave null). |
| **Pass 03 status** | **Complete** (2026-03-24). **Authoritative proof:** `artifacts/verify/20260324_030133` (verify.ps1 -Quick PASSED; `latest_pointer.json` aligned). Scope/closure: [WORKFLOW_COHERENCE_PASS_03_SEARCH_PANEL_FOCUS_NAVIGATION.md](WORKFLOW_COHERENCE_PASS_03_SEARCH_PANEL_FOCUS_NAVIGATION.md). |
| **Pass 03 — what improved** | Honest toasts (success only when item selected); `ToPanelResultTypeString` for unknown types; marker→project via `metadata.project_id`; metadata forwarded to panels; best-effort focus with logged catch. |
| **Recommended next improvement** | Pass 05 **C3 Option B** complete. **Next:** Workflow 6 freeze (recommended) or Pass 05 Option A/C matrix + implementation. |
| **Proof/test opportunity** | Unit: `SearchOverlayCoordinatorTests` (16) + `SearchResultTypeMapperTests` (2) = 18. Manual: search → result → panel + selection. verify.ps1 -Quick. Latest repo pointer may be newer (see STATE.md). |

---

## Workflow 4 — Script editor → synthesis / preview

| Field | Content |
|-------|---------|
| **Entry point** | ScriptEditor panel; segment Generate; segment Play (generated audio) |
| **Involved panels/services** | ScriptEditorViewModel, IScriptEditorClient, **IVoiceSynthesisService** (synthesis hot path). `VoiceSynthesisViewModel` is **not** used by Script Editor today — see Pass 04 §1. |
| **Current wiring status** | **Generation:** `IVoiceSynthesisService` → backend. **Playback:** `IAudioPlayerService` + `BackendPlaybackBaseUrl.Resolve(BackendClientConfig)`. **Persist:** `IScriptEditorClient.UpdateScriptAsync`. |
| **Pass 04 status** | **Complete** (2026-03-24). **Proof:** `artifacts/verify/20260324_070722` (verify.ps1 -Quick PASSED; `latest_pointer.json`). Scope/closure: [WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md](WORKFLOW_COHERENCE_PASS_04_SCRIPT_EDITOR_SYNTHESIS_PREVIEW.md). |
| **Pass 04 — what improved** | `ScriptEditorSynthesisRequestBuilder` (engine/language policy); honest no-`AudioId` path; playback URL via `BackendPlaybackBaseUrl`; backlog seam truth. |
| **Known gaps/friction** | New segment profile onboarding (D2); help copy vs features (D5); context menu when Generate disabled (C5 deferred). |
| **Recommended next improvement** | Pass 05 through **C3 Option B** complete for Workflow 5. **Next:** Workflow 6 freeze, or Script Editor C5 / §Pass 04 gaps, or Pass 05 **persistence** slice (Option C/A)—separate doc/matrix. |
| **Proof/test opportunity** | Pass 04 §7 filter — **39 passed** (2026-03-24); verify.ps1 -Quick; `latest_pointer.json`. |

---

## Workflow 5 — Record/import → analysis/transcription → project persistence

| Field | Content |
|-------|---------|
| **Entry point** | Recording panel (`RecordingViewModel`); library import (`LibraryUseCase.ImportFilesAsync` → backend); Transcribe panel (`TranscribeViewModel`); timeline transcript overlay / project audio (`TimelineViewModel`) |
| **Involved panels/services** | `RecordingViewModel` (`IRecordingClient` upload); **`TranscribeViewModel`** (not `TranscriptionViewModel` — type does not exist); `ITranscriptionClient`; `ITimelineTranscriptionService` (load transcript by id); `TimelineViewModel`; `IProjectAudioClient`; `ITimelineClipService` |
| **Current wiring status** | **Transcribe:** `ITranscriptionClient.TranscribeAudioAsync` with optional `SelectedProjectId`; project id syncs from `IContextManager` + `ProjectChangedEvent` (Pass 05 C1); **slice 2:** `SelectedAudioId` prefilled from `AssetAddedEvent` (`recording-panel`, `import-workflow`) when empty; **Option A1 (2026-03-25):** after successful transcribe, **`SaveAudioToProjectAsync`** once for source `AudioId` when `SelectedProjectId` set (non-blocking on failure) — [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_A_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_A_FOLLOWUP.md). **Single-file import (shell):** **`ImportWorkflowService`** after successful **`UploadLibraryAssetAsync`**, **`SaveAudioToProjectAsync`** when **`IContextManager.ActiveProjectId`** set (**P05-Persist-A2**, same doc §10). `TranscriptionCompletedEvent` + `NavigateToEvent` (`loadTranscript`) → timeline. **Timeline:** `ITimelineTranscriptionService.GetTranscriptionAsync` for subtitle segments; `IProjectAudioClient` heavily used on **synthesis add-clip** path. **Recording:** upload → library asset id; **`ProjectId` synced** (Pass 05 C1); **Option C (2026-03-25):** after successful upload, **`SaveAudioToProjectAsync`** when `ProjectId` set — see [WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_C_FOLLOWUP.md). |
| **Pass 05 status** | **Slices 1–3 complete** (2026-03-24): C1+C4, C2, **C3 Option B** — proof `artifacts/verify/20260324_190103`. **Option C slice 1** (2026-03-25): record-only bridge — seam **27** + Quick **`20260325_031737`**. **Option A** (2026-03-25): **A1** + **A2** + **A3** — Quick **`20260325_044801`**; seam **50** ([Option A follow-up](WORKFLOW_COHERENCE_PASS_05_PERSISTENCE_OPTION_A_FOLLOWUP.md) §7–§11). Parent: [WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md](WORKFLOW_COHERENCE_PASS_05_RECORD_IMPORT_TRANSCRIPTION_PROJECT.md). |
| **Known gaps/friction** | **A3** delivers batch **`ImportFilesAsync`** → project copy when **`ActiveProjectId`** set — still **no** per-file **`AssetAddedEvent`** from that API (§11). **Library** multi-file **drag-drop** (`LibraryView.xaml.cs`, per-file **`UploadAudioFileAsync`**) — **no** automatic project copy; **P05-Persist-A4** **deferred** (post-A3 lane pick — sign §12 before any code). **Product trust:** Transcribe **`Transcribe.Pass01.PersistenceScopeFootnote`** + Library **`Library.Pass01.ImportDragDropScopeFootnote`** disclose import vs drag-drop→project until A4 (Pass 01 §8). |
| **Recommended next improvement** | **Default:** pause Workflow 5 persistence — **Pass 06 slice 4 closed**; pick a **new** Pass 06 §5 row, another backlog workflow, or Pass 05 persistence. **Optional later:** **P05-Persist-A4** (drag-drop → project) only after **planning freeze + sign-off** in Option A doc §12. |
| **Proof/test opportunity** | **Seam filter:** Option A follow-up doc §7 — **50 passed** (incl. `LibraryUseCaseImportFilesPersistenceTests`, `ImportToProjectPersistenceTests`, `ImportWorkflowServiceTests`); cite **separately** from `verify.ps1 -Quick`. |

---

## Workflow 6 — Backup/restore → project/settings/profile recovery

| Field | Content |
|-------|---------|
| **Entry point** | BackupRestoreViewModel; restore action |
| **Involved panels/services** | `BackupRestoreViewModel`, `IBackupRestoreClient`; FastAPI [backup.py](../../backend/api/routes/backup.py); downstream owners per Pass 06 §1.2: `ProjectStore`, `TimelineViewModel`, `ProfilesViewModel`, `SettingsViewModel`, `IContextManager`, `TranscribeViewModel` (and optionally `RecordingViewModel`) |
| **Current wiring status** | PR-14: client owns HTTP. **Restore** updates on-disk stores; **Pass 06:** `BackupRestoredEvent` after API success; timeline/profiles reload (slice 1); **settings** reload when `RestoreSettings` (slice 2); active project via timeline → `IContextManager` (Pass 02). |
| **Known gaps/friction** | **Slice 4 closed (2026-03-25):** §5.4 D4 merge-expectation **copy** in restore UI; **no** `backup.py` merge change. Residual: D6 upload validation **OUT** unless new §5 row. |
| **Pass 06 status** | **Slices 1–4 complete** (2026-03-25): §8 — slice 1 `20260324_204541` / **10**; slice 2 **`20260324_221954`** / **27**; slice 3 **`20260324_225957`** / **30**; slice 4 **`20260325_055851`** / **32** (D4 merge-hint + seam tests). |
| **Recommended next improvement** | **Pass 06 slice 4 closed.** [Product trust Pass 01](PRODUCT_TRUST_AND_RELEASE_HONESTY_PASS_01.md) — **paused after slice 4** (§8.9 Option 1); no further Pass 01 honesty **`src/`** without new §8 sign-off. **Next lanes:** new bounded Pass 06 §5 row (D6), Option A **§12** (A4), or other backlog—**not** Pass 01 slice 5 by default. |
| **Proof/test opportunity** | **Slices 1–4:** same extended filter — **32 passed**; `verify.ps1 -Quick` **separate** — Pass 06 §7–§8. |

---

## Workflow 7 — Training/dataset flows

| Field | Content |
|-------|---------|
| **Entry point** | TrainingViewModel; dataset selection; training start |
| **Involved panels/services** | **Primary:** `TrainingViewModel`, `ITrainingClient`, WebSocket job progress + polling. **Adjacent:** `TrainingDatasetEditorViewModel` + `ITrainingDatasetEditorClient`; **`DatasetQAViewModel`** + `IDatasetQAClient` (not a “DatasetQAClient” type); `TrainingQualityVisualizationViewModel`; **`ModelManagerViewModel`** + `IModelManagerClient` (**separate** panel — not injected into `TrainingViewModel`). Events: `JobStartedEvent`, `ProfileCreatedEvent`. |
| **Current wiring status** | See [WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md](WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md) §2–§3. Training completion path publishes **`ProfileCreatedEvent`** when `job.ProfileId` set; toast + job/log refresh. |
| **Pass 07 status** | **W7-C1 closed** (2026-03-25); **lane paused** ([§8.4](WORKFLOW_COHERENCE_PASS_07_TRAINING_DATASET_MODEL_PROFILE.md#84-workflow-7--continuation--pause-governance)) — Profiles-first `ProfileCreatedEvent` → **`ProfilesViewModel`**; seam **2**; Quick **`20260325_162114`**. **W7-C2** (or any further Training `src/`) → **new signed §8** only. |
| **Known gaps/friction** | **W7-D3–D6** and §4 residuals; **Model Manager** orthogonal to W7-C1. **Product trust slice 3:** Training partial / not workflow-pass-closed. |
| **Recommended next improvement** | **Default:** other backlog (**Pass 06 §5**, **Option A §12** / A4, etc.). **Workflow 7:** only if product signs **§8** for a **single** bounded row — not open-ended training UX. |
| **Proof/test opportunity** | Pass 07 §8.2; **Quick ≠ seam** (same discipline as other passes). |

---

## Workflow 8 — Quality/benchmark/profile comparison

| Field | Content |
|-------|---------|
| **Entry point** | **Quality Benchmark** ([`QualityBenchmarkViewModel`](../../src/VoiceStudio.App/Views/Panels/QualityBenchmarkViewModel.cs)); **A/B Testing** ([`ABTestingViewModel`](../../src/VoiceStudio.App/Views/Panels/ABTestingViewModel.cs)); **Profile Comparison** ([`ProfileComparisonViewModel`](../../src/VoiceStudio.App/ViewModels/ProfileComparisonViewModel.cs)) |
| **Involved panels/services** | **Clients:** [`IQualityControlClient`](../../src/VoiceStudio.App/Core/Services/IQualityControlClient.cs) (`RunBenchmarkAsync`); [`IABTestService`](../../src/VoiceStudio.App/Services/IABTestService.cs); [`IVoiceSynthesisService`](../../src/VoiceStudio.App/Services/IVoiceSynthesisService.cs) + [`IProfilesClient`](../../src/VoiceStudio.App/Core/Services/IProfilesClient.cs); [`IAudioPlayerService`](../../src/VoiceStudio.App/Services/IAudioPlayerService.cs). **Registration:** Quality Benchmark in [`CorePanelRegistrationService`](../../src/VoiceStudio.App/Services/CorePanelRegistrationService.cs); A/B + Profile Comparison in [`ModulePanelRegistrationService`](../../src/VoiceStudio.App/Services/ModulePanelRegistrationService.cs). |
| **Current wiring status** | **Quality Benchmark:** operational **`QualityBenchmarkView`** — [Pass 08](WORKFLOW_COHERENCE_PASS_08_QUALITY_BENCHMARK_PROFILE_COMPARISON.md) §8.3. **Profile Comparison:** operational **`ProfileComparisonView`** (W8-C3 closed, §8.7). **A/B Testing:** operational **`ABTestingView`** (W8-C2 closed, §8.10). Cluster still **partial** on **global** profile harmonization (**W8-D009** class). |
| **Pass 08 status** | **W8-C1** + **W8-C2** + **W8-C3** closed. Proofs: **§8.3** (C1), **§8.10** (C2), **§8.7** (C3); **`latest_pointer.json`** advances with each closure verify. |
| **Known gaps/friction** | No shared profile selection with **`IContextManager`** cluster-wide (**W8-D009**). Engine semantics still differ across panels where not explicitly aligned (**W8-D008** class for Profile Comparison history). |
| **Recommended next improvement** | **New §5 row + §8** only — e.g. cluster **`IContextManager`** harmonization or engine policy follow-ups; **do not** expand **Pass 08** without an explicit matrix row. |
| **Proof/test opportunity** | Pass 08: QB / A/B / Profile Comparison seam classes **8** each; `verify.ps1 -Quick` cited **separately** from seams. |

---

## Prioritization

| Rank | Workflow | Rationale |
|------|----------|-----------|
| 1 | Profile → synthesis → timeline | **Pass 01 complete** (2026-03-23). |
| 2 | Project → timeline → effects/mixer | **Pass 02 complete** (2026-03-24). Proof: artifacts/verify/20260324_012252. |
| 3 | Search → panel focus | **Pass 03 complete** (2026-03-24). Proof: artifacts/verify/20260324_030133. |
| 4 | Script editor → synthesis | **Pass 04 complete** (2026-03-24). Proof: artifacts/verify/20260324_070722. |
| 5 | Record → transcription → project | **Pass 05:** slices 1–3 complete (C3 Option B). Proof: `artifacts/verify/20260324_190103`. |
| 6 | Backup/restore | **Pass 06 slices 1–4 complete** (2026-03-25). Doc §8; slice 4 proof **`artifacts/verify/20260325_055851`**; seam **32 passed** (extended filter). |
| 7 | Training | **Workflow 7 paused** after W7-C1 — Pass 07 §8.4; reopen = signed §8 only. |
| 8 | Quality / benchmark / comparison | **Pass 08** — **W8-C1** + **W8-C2** + **W8-C3** closed (2026-03-25–26); further `src/` → **new §5/§8** row only. |
