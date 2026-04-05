# GOV-VOICESTUDIO-TRANSCRIPT-CLIP-LINKAGE-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-TRANSCRIPT-CLIP-LINKAGE-01`  
**Status:** **Closed** 2026-03-30 — GAP-033 transcript ⇄ timeline clip linkage. Closure: [VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_LANE_CLOSURE_2026-03-30.md](../reports/verification/VOICESTUDIO_TRANSCRIPT_CLIP_LINKAGE_LANE_CLOSURE_2026-03-30.md).  
**Tracker:** [GAP-033](PROFESSIONAL_GAP_TRACKER.md)

## Frozen objective

Create an **authoritative, persisted** association between transcript segments and timeline clips and project audio context, so transcription is **project-meaningful** (selection coherence, reload survival)—not only an overlay.

## Authority map (frozen)

| Concern | Owner | Notes |
|--------|--------|-------|
| Segment stable identity | Backend `TranscriptionSegment.id` (UUID) + C# `TranscriptionSegment.Id` | Serialized in SQLite segments JSON; backfilled on read if missing then persisted |
| Clip–transcript linkage | `ClipTranscriptLink` on `Project.ClipTranscriptLinks` | Persisted via `JsonProjectRepository` (schema v2) |
| Linkage mutations | `IClipTranscriptLinkageService` | CRUD against current `Project`; save handled by existing shell save |
| Primary selection | Timeline | `ClipTranscriptSelectionEvent` follows clip selection; transcript UI follows timeline |
| Transcription complete / send to timeline | `TranscribeViewModel`, `TimelineViewModel` | Create links on explicit paths only |

## Policy (frozen)

- **Cardinality:** One clip ↔ one transcription row per link record; many segments may be listed in `SegmentIds` (overlap with clip timeline bounds vs segment times).
- **Linkage creation:** Only when transcription completes for project context or user loads transcript on timeline (“Send to Timeline” path)—no silent background linking.
- **Clip delete:** Remove `ClipTranscriptLink` for that `clipId`; do not delete transcription rows.
- **Re-transcribe:** Replacing transcription for same audio: remove links for old `transcription_id` then add new links with fresh segment IDs.
- **Timeline vs transcript:** Timeline selection drives transcript highlight; transcript segment click may seek timeline (secondary).

## Hard IN

- UUID per segment; `Project.ClipTranscriptLinks`; schema bump to 2 with v1 JSON backward compat.
- `ClipTranscriptSelectionEvent`; wiring in `TimelineViewModel` + `TranscribeViewModel`.
- Clip delete removes linkage; tests + verification matrix for closure.

## Hard OUT

- Descript-style text editing (GAP-045); waveform rewrite; subtitle/export overhaul; diarization redesign; NLP quality; generic transcript UX redesign.

## Binary acceptance

- [x] Segment IDs present on API responses and stable after backfill persist.
- [x] `ClipTranscriptLinks` round-trip through project JSON save/load.
- [x] Clip selection publishes selection event with segment IDs when link exists.
- [x] Transcription complete + load transcript paths create links for matching `AudioId` + time overlap.
- [x] Clip delete clears linkage deterministically.
- [x] Verification matrix green (scoped + full `tests/ci`); closure report + tracker/registry/STATE.

## Honest limits

- Segment timestamps are **source-audio seconds** (0…file duration). Clip `StartTime`/`EndTime` on the **timeline** are not used for overlap with raw segment times. **MVP linkage filter:** include segment `s` iff `[s.start, s.end]` intersects `[0, clip.Duration]` in source time (full-clip assumption; trim/slip not modeled on `AudioClip`).

## Rollback

- Revert schema v2 + linkage types; remove segment `id` from API only with coordinated client rollback. Document partial state in this row if aborted mid-lane.
