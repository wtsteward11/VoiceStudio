# GOV-VOICESTUDIO-GAP040-NONDESTRUCTIVE-EDIT-MODEL-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP040-NONDESTRUCTIVE-EDIT-MODEL-01 |
| **GAP** | GAP-040 (non-destructive edit authority — model / persistence / linkage coherence) |
| **Status** | **Closed** (2026-04-04) — authority slice: project clip lineage + transcript-link hygiene + round-trip persistence |
| **Phase** | Professional Roadmap v3 — Phase 4 |
| **Role** | System Architect + Core Platform + UI Engineer |
| **Dependency** | GAP-037 (bounded waveform MVP), GAP-012 (bounded coherence undo), GAP-033 (transcript–clip linkage), GAP-031 (export authority) |
| **Successor** | **GAP-038** (GPU waveform) — **only after** this lane closure proof — [GOV_VOICESTUDIO_SUCCESSOR_GAP040_GAP038_SEQUENCE_FREEZE.md](GOV_VOICESTUDIO_SUCCESSOR_GAP040_GAP038_SEQUENCE_FREEZE.md) |

## §1 Objective (frozen)

Establish **one canonical project authority** for timeline edit semantics beyond mirrored timeline API geometry:

- **Persistence:** optional **`derived_from_clip_id`** on project `AudioClip` rows (tracks store) records **split lineage** (right segment → pre-split clip id).
- **Transcript linkage:** on split, **replicate** `ClipTranscriptLink` rows to the new clip id; on coherence **undo/redo**, **remove** links for deleted clip ids and **re-copy** links when recreating a clip with lineage (Redo).
- **Export / mixdown:** unchanged — still **project →** `POST /api/timeline/import-from-project` → export (GAP-031). No side-path forks.
- **Undo:** remains **GAP-012** `TimelineTrackClipsCoherenceUndoAction` + extended link hygiene (this lane).

## §2 Hard IN

- `derived_from_clip_id` optional field on FastAPI `AudioClip` / `ClipCreateRequest` / `ClipUpdateRequest` + track JSON round-trip.
- `AudioClip.DerivedFromClipId` on `VoiceStudio.Core` + `BackendClient` create/update + undo `Clone` parity.
- `SplitClipAtPlayheadAsync`: new segment gets `DerivedFromClipId =` original clip id; `IClipTranscriptLinkageService.CopyTranscriptLinksToNewClip`.
- `TimelineTrackClipsCoherenceUndoAction`: optional `Project` + `IClipTranscriptLinkageService` — `RemoveLinksByClipId` for deleted clips; after `CreateClipAsync`, `CopyTranscriptLinksToNewClip` when `DerivedFromClipId` set.
- Design memo: [GOV_VOICESTUDIO_GAP040_AUTHORITY_DECISIONS.md](GOV_VOICESTUDIO_GAP040_AUTHORITY_DECISIONS.md).
- Tests: backend clip create with lineage; MSTest linkage + undo action expectations updated; existing matrices GREEN.

## §3 Hard OUT

- GAP-007 PanelHost / shell redesign; notification-center expansion; GAP-045 product scope expansion.
- Backend `POST /api/timeline/undo` as **primary** UX undo (still secondary stack per GAP-012).
- Full ripple / cross-track compound edit model; segment-level transcript time remapping (future).
- Replacing GAP-012 snapshot undo with operational transform / command stack (deferred).

## §4 Authority map

| Concern | Owner |
|---------|--------|
| **Project clip truth (lineage + timing)** | `TrackStore` + `/api/projects/.../tracks/.../clips` |
| **Mix graph for export** | `POST /api/timeline/import-from-project` + timeline `Clip` geometry |
| **Transcript ↔ clip** | `Project.ClipTranscriptLinks` + `IClipTranscriptLinkageService` |
| **User undo/redo (bounded edits)** | `TimelineTrackClipsCoherenceUndoAction` + `UndoRedoService` |
| **UI commands** | `TimelineViewModel` → `ITimelineUseCase` / `IBackendClient` |

## §5 Verification matrix

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_timeline_mixdown.py -q
python -m pytest tests/unit/backend/api/routes/test_tracks.py -q
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §6 Risk register

| Risk | Mitigation |
|------|------------|
| Dual stack (timeline vs project) | Import-from-project remains single exporter input; lineage is project-only metadata. |
| Orphan links after manual clip delete | Coherence undo removes links for deleted ids; broader delete path may still need the same hygiene (future pass). |
| Stale mocks on `UpdateClipAsync` signature | Update all `IBackendClient` test setups when optional params added. |

## §7 Rollback order

1. `TimelineViewModel` split / linkage calls  
2. `TimelineTrackClipsCoherenceUndoAction` linkage hooks  
3. `IClipTranscriptLinkageService` / `ClipTranscriptLinkageService` API addition  
4. `BackendClient` / `AudioClip` / FastAPI `tracks.py` fields  
5. Governance rows / closure report  

## §8 Related

- [GOV_VOICESTUDIO_GAP037_WAVEFORM_EDITING_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP037_WAVEFORM_EDITING_01_EXECUTION_ROW.md)  
- [GOV_VOICESTUDIO_GAP012_TIMELINE_EDIT_UNDO_BOUNDED_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP012_TIMELINE_EDIT_UNDO_BOUNDED_01_EXECUTION_ROW.md)  
- [GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_01_EXECUTION_ROW.md)  
