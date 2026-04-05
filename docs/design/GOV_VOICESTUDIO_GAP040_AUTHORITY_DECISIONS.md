# GAP-040 — Authority decisions (design before code)

**Execution row:** [GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_GAP040_NONDESTRUCTIVE_EDIT_MODEL_01_EXECUTION_ROW.md)  
**Status:** Accepted for implemented slice (2026-04-04)

## 1) Clip identity and lineage (trim / split)

| Operation | Continuing identity | New identity | Lineage field |
|-----------|--------------------|--------------|---------------|
| **Trim start/end** | Same `clip.id` | — | No new clip; no `derived_from_clip_id` change. |
| **Split** | Left (first) segment keeps **original** `clip.id` | Right segment gets **new** uuid (timeline API) | Right segment persists `derived_from_clip_id` = **original** clip id (the id shared with the left segment pre-split). |

**Rationale:** One stable id for the “head” of the split preserves existing links and undo snapshots keyed by id; the tail explicitly references the head forRedo/link replication.

**Restoration:** Undo that removes the right segment deletes its project row and any `ClipTranscriptLink` rows with `clip_id` = right id.

## 2) Transcript-linkage invariants (trim / split / fade)

- **Trim / fade:** Same `clip_id` → existing `ClipTranscriptLink` rows remain attached; segment text may be temporally wider than clip (known limitation until segment-level remap).
- **Split:** For each link on the **source** clip id (pre-split, same as left id after split), insert an **additional** link for the **new** right clip id with the **same** `TranscriptionId`, `AudioId`, and **copied** `SegmentIds` list (shallow copy).  
  - **Invariant:** Selecting either segment can resolve transcript segments until a future lane refines per-segment time bounds.
- **Undo split:** Remove links for the deleted right clip id only; left clip links unchanged.

## 3) Undo boundaries

- **Single bounded edit** (trim, split, fade): one `TimelineTrackClipsCoherenceUndoAction` registration per user gesture (GAP-012).  
- **Compound chains:** Each gesture remains its own undo unit; GAP-040 does **not** merge multiple gestures into one macro-undo.  
- **Redo after split:** Recreating the right clip from the after-snapshot runs `CopyTranscriptLinksToNewClip(derived_from, new_id)` so linkage matches the pre-undo state.

## 4) Export / mixdown

- **No change:** Export continues to use **project** tracks → `import-from-project` → timeline mix graph. `derived_from_clip_id` is **not** required for ffmpeg/mix geometry; it is **authority metadata** for UI, linkage, and future analytics.

## 5) Non-goals (this slice)

- Segment-accurate transcript trimming after edit.  
- Cross-track transactional undo.  
- Conflict resolution when multiple links per clip id exist (model assumes **at most one** primary link per clip id via `AddOrUpdateLink`).
