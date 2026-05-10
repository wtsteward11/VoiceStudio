# Project Authority Generated Audio Inventory - 2026-04-29

<!-- VOICESTUDIO_PROOF_BOUNDARY_V1
classification: UNKNOWN
proof_type: generated_audio
engine_mode_source: not_applicable
runtime_claim: false
operator_claim: false
-->

**Classification:** UNKNOWN
**Date:** 2026-04-29

## Purpose

Inventory the current generated-audio project/session authority before implementation of Generated Audio Product Authority v1. This document records inspected code and gaps only; it does not claim implementation or closure.

Blocker: engine mode is not applicable because this inventory records code-surface authority, not a synthesis run.

## Scope

This inventory covers backend synthesis, audio retrieval, library, timeline, export/mixdown, persistence, and frontend generated-audio services. It explicitly excludes GAP-008, Slice 46, `MainWindow*ShellBridge`, RHVoice, and `docs/reports/verification/ENGINE_PARITY_MATRIX.md`.

## Backend Authority Inventory

| Subsystem | Authoritative ID Today | Project/session ownership today | Generated-audio linkage today | Persistence / reload status | Gap |
|---|---|---|---|---|---|
| Synthesis response | `audio_id` from `VoiceSynthesizeResponse` | `VoiceSynthesizeRequest` has no project/session field; service generally does not pass `project_id` for primary synthesis artifacts | No `generated_audio_id`; `audio_id` is the only durable generated-audio identity | Audio artifact is registered in `AudioRegistryDB` and can survive process restart | P0: generated-audio identity is implicit, not named |
| Audio download | `audio_id` | Uses `AudioRegistry.get_path(audio_id)` only | Resolves by `audio_id` only | Registry-backed file path reloads if registry DB and file remain present | P1: no project/session check on retrieval |
| Audio registry | `audio_id` | `AudioArtifact.project_id` exists; no first-class `session_id`; metadata can carry session/provenance | Metadata can carry generated audio provenance, but synthesis does not consistently write it | SQLite `audio_artifacts` with metadata JSON | P0: spine fields not populated by synthesis path |
| Library assets | `asset_id`; response exposes `audio_id` from metadata `upload_id` | No first-class `project_id` or `session_id`; metadata JSON available | Metadata currently records upload data (`upload_id`, source format), not generated-audio provenance | SQLite `library_assets` / `library_folders` | P0: library does not link back to generated audio by contract |
| Timeline session state | `session_id` query parameter, default `default` | Session authority is query-scoped; no first-class `project_id` in `TimelineState` | `Clip` has `metadata`, but `AddClipRequest` cannot accept metadata yet | SQLite `session_timeline` stores JSON state and revision | P0: clip metadata link cannot be set through API |
| Project tracks | `project_id` | Project track store is project-scoped | Project tracks can carry clip `audio_id` | SQLite `project_tracks`; import route can rebuild in-memory timeline | P1: API proof path uses session timeline, not necessarily project tracks |
| Timeline export/mixdown | `session_id` + output path | `ExportRequest.project_id` exists for effects; export renders current session timeline | Export response returns path/duration only; no export id or provenance | Output file written outside repo-safe path; not registered as audio artifact | P1: export evidence not linked to generated audio graph |

## Frontend Authority Inventory

| Subsystem | Authority currently used | Project/session ownership | Generated-audio linkage | Gap |
|---|---|---|---|---|
| `GeneratedAudioLibraryService` | `AudioId`, local file path, uploaded `LibraryAsset.Id` | Uses `IContextManager.ActiveProjectId`; saves project audio when active project exists | Request carries `AudioId`, `ProfileId`, `Engine`; no named `generated_audio_id` | P1: UI lane can project-save, but backend proof path lacks explicit identity contract |
| `GeneratedAudioTimelineService` | `AudioId`, active project id, resolved track id, created clip id | Requires `IContextManager.ActiveProjectId`; uses project track/clip services | `AudioClip.AudioId` and `DerivedFromClipId` can carry linkage | P1: frontend graph exists separately from backend `/api/timeline` proof graph |
| `VoiceSynthesisViewModel` generated result state | Result audio id/reference, profile, engine, recent result state | Uses generated-audio library/timeline services for project-backed actions | UI can carry enough fields for commands | P2: not used for automated product-closure proof because no UI/human steps allowed |
| Export UI/service surface | Timeline/export-related UI exists, but no dedicated generated-audio proof export client | Project/timeline context dependent | Not proof-oriented | P2: proof harness should use backend export route directly |

## Current Graph Reconstruction

Current reload can reconstruct parts of the graph:

- Audio retrieval can reload by `audio_id` through `AudioRegistryDB`.
- Timeline session state can reload by `session_id` through `session_timeline`.
- Library assets can reload by `asset_id` through `library_assets`.
- Export can render the current timeline session to a WAV path.

Current reload cannot reconstruct the full graph without convention:

- Synthesis does not expose `generated_audio_id`.
- Library metadata does not contractually store `generated_audio_id`, `project_id`, or `session_id`.
- Timeline clip metadata exists on `Clip`, but `AddClipRequest` cannot accept metadata through the API.
- Export response does not carry export provenance beyond output path/duration.

## Ranked Gaps

### P0

- Add explicit generated-audio identity in proof/backend response contracts (`generated_audio_id`, initially equal to `audio_id` if no separate model exists).
- Allow timeline clip creation to accept metadata so library/generated-audio links survive persistence.
- Add generated-audio provenance to library upload metadata without a schema migration.
- Extend proof JSON and validators so a REAL_ENGINE product-closure proof must carry project/session, generated audio, library, timeline, and export evidence.

### P1

- Pass project/session provenance into audio registry metadata where the backend request path has it.
- Export timeline/session to WAV and validate it as machine-decodable, non-silent audio.
- Add restart/reload verification script that explicitly refuses to claim durability when no restart command is supplied.

### P2

- Keep frontend generated-audio service behavior documented as supporting context, not proof authority.
- Consider future first-class library/project columns only if metadata JSON becomes insufficient.

## Files Inspected

- `backend/api/models_additional.py`
- `backend/api/routes/voice/synthesis.py`
- `backend/api/routes/voice/audio.py`
- `backend/api/routes/audio.py`
- `backend/api/routes/library.py`
- `backend/api/routes/timeline.py`
- `backend/services/synthesis_service.py`
- `backend/services/audio_artifacts/models.py`
- `backend/services/audio_artifacts/registry.py`
- `backend/services/audio_artifacts/registry_db.py`
- `backend/services/audio_artifacts/store.py`
- `backend/services/audio_artifacts/use_cases.py`
- `backend/project/timeline/session_repository.py`
- `backend/project/tracks/track_store.py`
- `backend/data/migrations/v003_library_tables.py`
- `src/VoiceStudio.App/Services/GeneratedAudioLibraryService.cs`
- `src/VoiceStudio.App/Services/IGeneratedAudioLibraryService.cs`
- `src/VoiceStudio.App/Services/GeneratedAudioTimelineService.cs`
- `tests/contract/test_voice_synthesis_proof_surface_contract.py`
- `tests/unit/backend/api/routes/test_timeline.py`
- `tests/unit/backend/api/routes/test_timeline_mixdown.py`
- `tests/unit/backend/services/test_timeline_export_loudness.py`

## Non-Claims

- No implementation is claimed by this inventory.
- No product closure is claimed.
- No runtime FULL PASS is claimed.
- No human/operator proof is claimed.
- No GAP-008, Slice 46, `MainWindow*ShellBridge`, RHVoice, or `ENGINE_PARITY_MATRIX.md` work is claimed.
