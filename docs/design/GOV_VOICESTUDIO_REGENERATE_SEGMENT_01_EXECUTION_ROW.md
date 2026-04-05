# GOV-VOICESTUDIO-REGENERATE-SEGMENT-01 — Execution row

**Lane ID:** `GOV_VOICESTUDIO-REGENERATE-SEGMENT-01`  
**Status:** **Closed** — 2026-03-31; GAP-046; transcript-driven single-segment regeneration execution + proof seam.  
**Tracker:** [GAP-046](PROFESSIONAL_GAP_TRACKER.md) (**Closed**)  
**Depends on:** [GOV_VOICESTUDIO_TEXT_EDITING_FOUNDATION_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_TEXT_EDITING_FOUNDATION_01_EXECUTION_ROW.md) (closed)

## Frozen objective

Execute **one** transcript segment regeneration end-to-end: canonical backend job + synthesis, canonical clip apply (REST + ref-count), explicit linkage invalidation after success, undo-safe replacement, session dirty/autosave compatibility.

## Architecture decisions (binary)

| Decision | Choice |
|----------|--------|
| Mutation model | Replacement **artifact** + explicit `PUT` clip update; no silent overwrite |
| Granularity | One segment → one resolved clip; ambiguity **fail closed** on client before request |
| Text source | Segment repository text **or** optional `replacement_text` in API request |
| Backend authority | **One** route: `POST /api/transcribe/regenerate-segment` (starts canonical `job_history` job) |
| Execution mode | `create_job` + `asyncio.create_task` + `SynthesisService.synthesize` + `complete_job` / `fail_job` |
| Post-success transcript truth | **Remove** clip transcript links for that clip; operator copy: **re-transcription required** |
| Timeline authority | Playback/transport unchanged; transcript triggers regeneration **request** only |

## Hard IN

- Backend validation: transcription + segment id, track + clip on store, profile_id from clip or request
- `ClipUpdateRequest` supports optional `audio_id`, `audio_url`, `duration_seconds` with `ArtifactRefCounter` on swap
- App: `ITranscriptRegenerationClient`, job poll via jobs API, `UpdateClipAsync` extended, timeline event for in-memory sync
- Undo: `ReplaceClipAudioAction` registered on success
- Tests: pytest route/worker smoke (stub mode); MSTest client/orchestration/undo

## Hard OUT

- Bulk regeneration, Descript-class editor, waveform/subtitle/diarization work, new job subsystem

## Implementation progress (historical)

- **Backend:** `POST /api/transcribe/regenerate-segment` + `_validate_regenerate_segment_request` in `backend/api/routes/transcribe.py`; worker `backend/services/transcript_segment_regeneration.py`; clip update + `ArtifactRefCounter` in `backend/api/routes/tracks.py` (`PUT .../clips/{clip_id}`).
- **App:** `TranscriptSegmentRegenerationCoordinator`, `TranscriptClipAudioReplaceUndoAction`, `ITranscriptRegenerationClient` / `TranscriptRegenerationClient`, `ClipAudioArtifactReplacedEvent`, `TimelineViewModel` subscription + apply, `TranscribeViewModel.RegenerateSegmentAudioAsync`, segment context menu in `TranscribeView.xaml.cs`, DI in `AppServices.cs`.
- **Proof:** [VOICESTUDIO_REGENERATE_SEGMENT_LANE_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_REGENERATE_SEGMENT_LANE_CLOSURE_2026-03-31.md); pytest `test_transcribe_regenerate.py`, `test_tracks_clip_update.py`; MSTest coordinator / undo / timeline event / transcribe VM (null coordinator).

## Binary acceptance

- [x] `POST /api/transcribe/regenerate-segment` returns `job_id`; job completes with valid artifact in stub/CI mode
- [x] Invalid segment/track/clip returns 4xx with explicit body (no fake 202)
- [x] Clip update increments/decrements refs when `audio_id` changes
- [x] After success: linkage removed for clip; `MarkProjectDirty`; undo restores prior audio fields
- [x] Closure report + tracker + registry + STATE agree

## Rollback

Revert transcribe regenerator module, `tracks.py` update_clip changes, client/orchestrator, timeline snap/sync if needed; keep foundation intents.
