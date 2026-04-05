# GOV-VOICESTUDIO-GAP031-TIMELINE-MULTITRACK-MIXDOWN-01

## §0 Lane Status

| Field | Value |
|-------|-------|
| **Lane ID** | GOV-VOICESTUDIO-GAP031-TIMELINE-MULTITRACK-MIXDOWN-01 |
| **GAP** | GAP-031 |
| **Status** | **Closed** (2026-04-02) |
| **Phase** | 3 (Wiring) |
| **Role** | Core Platform |
| **Effort** | 32h |
| **Dependency** | GAP-017 (Closed), GAP-029 export authority (Closed) |
| **Created** | 2026-04-02 |

## §1 Objective (frozen)

Deterministic multi-track timeline mixdown to a single master render through the canonical **`POST /api/timeline/export`** path (GAP-029). Before export, the in-memory timeline router state is **hydrated from persisted project tracks** (`TrackStore`) so the mix reflects the same clips as the WinUI Timeline panel. Solo logic, empty-timeline fail-closed (unless `fallback_project_audio_id` resolves), and deterministic track ordering are mandatory.

## §2 Hard IN

- `_render_timeline_audio` honors **solo**: when any **audio** track is soloed, only soloed audio tracks contribute.
- Tracks are mixed in deterministic order: **`order`**, then **`id`** tie-break.
- **Only** `type == "audio"` tracks participate in the mix (video/subtitle skipped).
- **`POST /api/timeline/import-from-project`**: rebuilds `_timeline_state` from `track_store.list_tracks(project_id)` with `AudioRegistry.get_path(audio_id)` for each clip’s `source_path`.
- **`ExportRequest`** gains **no** new per-track fields; mix state comes from timeline state after import and from persisted `is_muted` / `is_solo` on project tracks.
- **Project API** `PUT /api/projects/{project_id}/tracks/{track_id}` accepts **`is_muted`** / **`is_solo`** and persists in track JSON.
- **`TimelineUseCase.ExportAsync`**: when `ProjectId` is set, calls **import-from-project** first, then export.
- Empty mix after import (no resolvable clip audio) and no usable **fallback_project_audio_id** → **HTTP 400** with operator-facing `detail` (no silent full-zero render).
- **`PUT /api/timeline/tracks/{track_id}`** updates in-memory timeline track mix fields (for API/tests); UI may persist via project track API only.
- Backend + C# tests; full verification matrix on closure.

## §3 Hard OUT

- No waveform editing, realtime DSP preview redesign, clip surgery, stereo pan math (pan field ignored).
- No effect engine overhaul, PanelHost GAP-007, notifications GAP-034.
- No new global persistence architecture beyond optional fields on existing track JSON.

## §4 Acceptance criteria

1. Multiple audible **audio** tracks in project export as one mixed file via canonical export route after import.
2. **Mute** (`is_muted` on project track → import → timeline `muted`) excludes the track from the mix.
3. **Solo** restricts the mix to soloed **audio** tracks when any audio track is soloed.
4. Track order is deterministic across repeated imports/renders (`order`, then `id`).
5. No audible clips and no valid fallback → **HTTP 400**.
6. GAP-029 route remains the only timeline export path from the app menu.
7. Single-track / fallback path still works when `fallback_project_audio_id` resolves.
8. Build / tests / `run_verification.py` GREEN; no skip increase.

## §5 Authority map (state / command / export)

| Concern | Owner | Canonical surface |
|--------|--------|-------------------|
| Persisted timeline clips / layout | `TrackStore` + `/api/projects/{id}/tracks/*` | WinUI `TimelineViewModel` + `ITimelineTrackService` / `ITimelineClipService` |
| Mute / solo persistence | Track JSON keys **`is_muted`**, **`is_solo`** | `PUT /api/projects/{project_id}/tracks/{track_id}` |
| Mixdown render graph | `_timeline_state` in `timeline.py` | Built by **`POST /api/timeline/import-from-project`** and in-memory clip ops |
| File menu export command | `FileOperationsHandler.ExportAudioAsync` | **`ITimelineUseCase.ExportAsync`** → import (if `ProjectId`) → **`POST /api/timeline/export`** only |
| Effect bake + LUFS | `timeline_effect_bake`, `timeline_export_loudness` | Unchanged from GAP-029 / GAP-041 |

**Gap closed this lane:** Export previously read an empty or stale global `_timeline_state` while the UI edited **project** tracks. Import-from-project bridges persisted project authority into the export render graph.

## §6 Verification commands

```powershell
dotnet build VoiceStudio.sln -c Debug -p:Platform=x64
dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64
python -m pytest tests/unit/backend/api/routes/test_timeline.py -q
python -m pytest tests/unit/backend/api/routes/test_timeline_mixdown.py -q
python -m pytest tests/ci/ -q --randomly-seed=12345
.\scripts\verify.ps1 -Quick
python scripts/run_verification.py
```

## §7 Rollback

Revert order: C# export/import + track mute persistence → `tracks.py` fields → `timeline.py` import/mix/export → execution row.
