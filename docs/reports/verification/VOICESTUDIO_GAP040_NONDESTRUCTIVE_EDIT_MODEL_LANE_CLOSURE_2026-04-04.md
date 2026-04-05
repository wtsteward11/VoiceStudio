# VoiceStudio GAP-040 non-destructive edit authority (slice) — 2026-04-04

**Lane:** **GOV-VOICESTUDIO-GAP040-NONDESTRUCTIVE-EDIT-MODEL-01** — project clip **`derived_from_clip_id`** lineage, transcript **link copy + undo hygiene**, coherence undo integration; **no** change to export graph semantics (still `import-from-project`).

**Execution row:** [GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md)

**Authority memo:** [GOV_VOICESTUDIO_GAP040_AUTHORITY_DECISIONS.md](../../design/GOV_VOICESTUDIO_GAP040_AUTHORITY_DECISIONS.md)

**Tracker:** **GAP-040** **Closed** (this slice) — see [PROFESSIONAL_GAP_TRACKER.md](../../design/PROFESSIONAL_GAP_TRACKER.md).

**GAP-034 runtime caveat:** [VOICESTUDIO_GAP034_OS_NOTIFICATIONS_RUNTIME_ADDENDUM_2026-04-03.md](./VOICESTUDIO_GAP034_OS_NOTIFICATIONS_RUNTIME_ADDENDUM_2026-04-03.md) (unchanged).

## 0) Verification provenance

**Label:** Repo-verified on developer machine after implementation.

## 1) Scope summary

- **`VoiceStudio.Core`:** `AudioClip.DerivedFromClipId`
- **`backend/api/routes/tracks.py`:** `derived_from_clip_id` on models + create/update + list round-trip
- **`BackendClient` / `IBackendClient`:** create + update + deserialize; optional `derivedFromClipId` on `UpdateClipAsync`
- **`IClipTranscriptLinkageService`:** `CopyTranscriptLinksToNewClip`
- **`TimelineViewModel.SplitClipAtPlayheadAsync`:** lineage + link copy + dirty `clip_transcript_links`
- **`TimelineTrackClipsCoherenceUndoAction`:** remove links for deleted clip ids; after create, copy links when `DerivedFromClipId` set; mark `clip_transcript_links` dirty when linkage service used
- **Tests:** `test_tracks_clip_update.py::test_put_derived_from_clip_id_persists`; `ClipTranscriptLinkageServiceTests.CopyTranscriptLinksToNewClip_*`; `TimelineTrackClipsCoherenceUndoActionTests` signature update; transcript regen / inline edit mocks updated for `UpdateClipAsync`

## 2) Verification matrix (closure run)

| Command | Result |
|--------|--------|
| `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64` | PASS (0 errors) |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | PASS — **3039** passed / **274** skipped |
| `python -m pytest tests/unit/backend/api/routes/test_timeline.py tests/unit/backend/api/routes/test_timeline_mixdown.py tests/unit/backend/api/routes/test_tracks_clip_update.py -q` | PASS — **51** passed |
| `python -m pytest tests/ci/ -q --randomly-seed=12345` | PASS — **217** passed |
| `.\scripts\verify.ps1 -Quick` | PASS — `artifacts/verify/20260403_215237/` |
| `python scripts/run_verification.py` | PASS — `.buildlogs/verification/last_run.json` **timestamp_short** **20260403-215821** (**completion_guard** PASS) |

## 3) Successor

**GAP-038** slice 0 (waveform cache) shipped same session — [GOV_VOICESTUDIO_GAP038_GPU_WAVEFORM_RENDERING_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP038_GPU_WAVEFORM_RENDERING_01_EXECUTION_ROW.md); full GPU path remains **Open** / deferred per row §1.

## 4) Closure

**GOV-VOICESTUDIO-GAP040-NONDESTRUCTIVE-EDIT-MODEL-01:** **Closed** 2026-04-04 for the bounded slice defined in the execution row.
