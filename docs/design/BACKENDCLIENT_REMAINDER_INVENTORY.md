# BackendClient Post-PR-12 Remainder Inventory

**Purpose:** Evidence-based ranking of remaining `IBackendClient` method clusters for PR-13 slice selection.  
**Date:** 2026-03-22  
**Source:** [IBackendClient.cs](../../src/VoiceStudio.App/Core/Services/IBackendClient.cs), [BackendClient.cs](../../src/VoiceStudio.App/Services/BackendClient.cs)  
**Related:** [BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md](BACKENDCLIENT_TRANSPORT_EXTRACTION_INVENTORY.md), PR-13 Slice Selection plan

---

## Extraction Status

| Status | Meaning |
|--------|---------|
| **Fully extracted** | Methods removed from IBackendClient/BackendClient; client uses BackendClientHttpPipeline |
| **Thin client** | Interface + client exist but delegate to IBackendClient; methods still on monolith |
| **No client** | No dedicated interface; callers use IBackendClient directly |

---

## Post-PR-12 Remainder Clusters (Ranked)

### Rank 1 — Thin clients (easiest: migrate to pipeline, remove from monolith)

| Cluster | Size | Thin client | Callers | Endpoint | DTO risk | Verdict |
|---------|------|--------------|---------|----------|----------|---------|
| **Models** | 9 | IModelManagerClient (7) | ModelManagerViewModel, ModelManagerView, ModelActions, TrainingViewModelTests (GetModel), Training datasets | `/api/models/*` | Low | **Extract** — Add GetModelAsync, RegisterModelAsync to IModelManagerClient; migrate ModelManagerClient to pipeline; remove all 9 from monolith |
| **Mixer** | 22 | IMixerStateClient (16) | EffectsMixerViewModel, MixerStateClient | `/api/mixer/*` | Low | **Extract** — Add GetMixerPresetAsync, UpdateMixerPresetAsync, DeleteMixerPresetAsync, UpdateMixerMasterAsync, UpdateChannelRoutingAsync to IMixerStateClient; migrate to pipeline |
| **Backup/Restore** | 7 | IBackupRestoreClient | BackupRestoreViewModel, DataBackupService | `/api/backups/*` | Low | **Extract** — Migrate BackupRestoreClient to pipeline; remove from monolith |
| **Pipeline** | 2 | IPipelineConversationClient | PipelineConversationViewModel | `/api/pipeline/*` | Low | **Extract** — Migrate to pipeline; remove from monolith |
| **Video** | 4 | IVideoGenClient (2), IVideoEditClient (2) | VideoGenViewModel, VideoEditViewModel | `/api/video/*` | Low | **Extract** — Migrate both to pipeline; consolidate or keep split |

### Rank 2 — Bounded clusters, no thin client

| Cluster | Size | Callers | Endpoint | DTO risk | Verdict |
|---------|------|---------|----------|----------|---------|
| **Emotion presets** | 6 | EmotionStyleClient (delegates), EmotionActions, EmotionControlViewModel, EmotionStyleControlViewModel | `/api/emotion/*` | Medium (IEmotionStyleClient uses different DTOs) | **Mixed** — Two APIs: emotion-style vs emotion presets CRUD. Assess before extracting |
| **Ensemble** | 2 | Unknown | `/api/ensemble/*` | Low | **Extract** — Small; create IEnsembleClient or fold into existing |

### Rank 3 — Larger clusters, higher blast radius

| Cluster | Size | Callers | Endpoint | DTO risk | Verdict |
|---------|------|---------|----------|----------|---------|
| **Profiles** | 5 | ProfilesViewModel, multiple panels | `/api/profiles/*` | Low | Medium — High caller count |
| **Projects** | 6 | ProjectViewModel, Timeline, multiple | `/api/projects/*` | Low | Medium |
| **Project audio** | 3 | Project/timeline related | `/api/projects/*/audio` | Low | Medium |
| **Timeline (tracks/clips)** | 9 | TimelineViewModel, clip/track actions | `/api/projects/*/tracks`, `clips` | Low | Medium |
| **Timeline (markers)** | 5 | TimelineViewModel, marker actions | `/api/projects/*/markers` | Low | Medium |
| **Transcription** | 6 | TranscriptionViewModel, panels | `/api/transcribe/*` | Low | Medium |
| **Batch (core)** | 4 | BatchViewModel, job actions | `/api/batch/*` | Low | Medium |
| **Batch quality** | 4 | Batch quality panel | `/api/batch/*/quality` | Low | Medium |

### Rank 4 — Cross-cutting or high complexity

| Cluster | Size | Callers | Endpoint | DTO risk | Verdict |
|---------|------|---------|----------|----------|---------|
| **Voice** | 3 | SynthesisViewModel, CloneViewModel | `/api/voice/*` | Low | High — Core synthesis path |
| **Audio retrieval/export** | 5+ | Many (timeline, export, playback) | `/api/audio/*` | Medium | High — Cross-cutting |
| **Audio visualization** | 6 | Waveform, spectrogram panels | waveform, spectrogram, meters, etc. | Low | Medium |
| **Training** | 11+ | TrainingViewModel, training panel | `/api/training/*` | Medium | High — Large surface |
| **Settings** | 5 | SettingsService, SettingsViewModel | `/api/settings/*` | Low | Medium |
| **Quality** | 25+ | Quality panels, benchmarking | `/api/quality/*` | High | **Not worth yet** — Huge, mixed, IDEA-* endpoints |
| **MCP** | 1 | MCP bridge | `/api/mcp/*` | N/A | **Keep** — Cross-cutting |
| **Generic helpers** | 4 | Many (GetAsync, PostAsync, PutAsync, SendRequestAsync) | N/A | N/A | **Keep** — Must stay |

---

## Recommended PR-13 Candidates (Evidence-Based)

**Decision rule:** Smallest caller graph, cleanest endpoint family, least DTO churn, easiest closure.

### Option A: Pipeline (2 methods) — smallest surface

- **Methods:** GetPipelineProvidersAsync, ProcessPipelineAsync
- **Destination:** IPipelineConversationClient (already exists; migrate to pipeline)
- **Callers:** PipelineConversationViewModel only
- **Blast radius:** 1 ViewModel
- **Verdict:** Cleanest next slice

### Option B: Models (complete) — thin client exists

- **Methods:** All 9 (GetModels, GetModel, RegisterModel, VerifyModel, UpdateModelChecksum, DeleteModel, GetStorageStats, ExportModel, ImportModel)
- **Destination:** IModelManagerClient (add GetModelAsync, RegisterModelAsync; migrate to pipeline)
- **Callers:** ModelManagerViewModel, ModelActions, TrainingViewModelTests
- **Blast radius:** 2–3 ViewModels/panels
- **Verdict:** Good — thin client exists; complete the migration

### Option C: Backup/Restore (7 methods) — thin client exists

- **Methods:** GetBackupsAsync, GetBackupAsync, CreateBackupAsync, DownloadBackupAsync, RestoreBackupAsync, UploadBackupAsync, DeleteBackupAsync
- **Destination:** IBackupRestoreClient (migrate to pipeline)
- **Callers:** BackupRestoreViewModel, DataBackupService
- **Blast radius:** 2 callers
- **Verdict:** Good — bounded, thin client exists

### Option D: Video (4 methods) — two thin clients

- **Methods:** ListVideoEnginesAsync, GenerateVideoAsync, UpscaleVideoAsync, GetVideoInfoAsync, EditVideoAsync (split across IVideoGenClient, IVideoEditClient)
- **Destination:** Consolidate or migrate both to pipeline
- **Callers:** VideoGenViewModel, VideoEditViewModel
- **Blast radius:** 2 ViewModels
- **Verdict:** Good — thin clients exist

---

## PR-13 Recommendation

**Primary recommendation: Option A — Pipeline (2 methods)**

- Smallest surface
- Single caller
- IPipelineConversationClient already exists
- Lowest risk, fastest closure

**Fallback: Option B (Models) or Option C (Backup)** if Pipeline has unexpected coupling.

---

## PR-13 Track Decision (2026-03-22)

**Chosen: Track A — PR-13 extraction**

**Rationale:** Remainder inventory revealed a clean slice (Pipeline, 2 methods, 1 caller). Stop-extraction criteria satisfied (thin client exists, not DTO glue, not cross-cutting).

**Scope doc:** [PR-13_PIPELINE_SCOPE.md](PR-13_PIPELINE_SCOPE.md)

**Next action:** Execute PR-13 per scope doc (migrate PipelineConversationClient to pipeline, remove from monolith, seam tests, anti-regression, proof).

---

## PR-14 Selection (2026-03-22)

**Chosen: BackupRestore**

**Ranking rationale:**

| Rank | Slice | Size | Rationale |
|------|-------|------|------------|
| 1 | **BackupRestore** | 7 methods | Thin client exists; bounded endpoint family; 1 primary caller (BackupRestoreViewModel); low blast radius; no training/model weirdness |
| 2 | Models | 9 methods | Thin client exists; may drag more incidental behavior |
| 3 | Video | 4 methods | Two thin clients (IVideoGenClient, IVideoEditClient); split surfaces |
| 4 | Mixer | 22 methods | Large; higher risk of mistakes |

**Rationale:** BackupRestore is the cleanest next slice — 7 methods, IBackupRestoreClient exists, BackupRestoreViewModel is the sole API caller (DataBackupService does local file backup, not IBackupRestoreClient). Lower blast radius than Mixer; cleaner than Models.

---

## Classification Summary

| Category | Cluster count | Action |
|----------|---------------|--------|
| Thin client, migrate to pipeline | 5 (Models, Mixer, Backup, Pipeline, Video) | PR-13+ candidates |
| Bounded, no client | 2 (Emotion, Ensemble) | Assess after thin-client migrations |
| Larger, medium complexity | 8 (Profiles, Projects, Timeline, Transcription, Batch, Audio viz, Settings) | Defer |
| High complexity / keep | 4 (Voice, Audio retrieval, Training, Quality, MCP, Generic) | Defer or never |

---

## Caller Counts (Excluding Tests, Interface, Implementation)

| Method family | Source files (src/, excl. IBackendClient, BackendClient, *Tests) |
|---------------|------------------------------------------------------------------|
| Pipeline | PipelineConversationViewModel, PipelineConversationClient |
| Models | ModelManagerViewModel, ModelManagerView, ModelActions, ModelManagerClient |
| Backup | BackupRestoreViewModel, DataBackupService, BackupRestoreClient |
| Video | VideoGenViewModel, VideoEditViewModel, VideoGenClient, VideoEditClient |
| Mixer | EffectsMixerViewModel, MixerStateClient |
