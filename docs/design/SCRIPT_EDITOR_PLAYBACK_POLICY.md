# Script Editor Playback Policy

## Product Decision

**Decision:** Local-only playback is the **current final** behavior for this release. No global transport coherence until a follow-up feature.

**Rationale:** Keeps scope bounded; avoids transport coupling until users request it.

**Revisit when:** User feedback requests playhead/transport coherence, or when building a unified playback UX.

---

## Current Policy

**Segment playback is local-only.** The Script Editor plays generated segment audio within its own context and does not update the global transport (playhead, global play state, transport bar).

## Rationale

- Keeps the Script Editor self-contained and decoupled from transport orchestration.
- Avoids transport coupling until shell coherence (e.g., playhead sync, global play state) is required.
- Reduces complexity and potential race conditions during MVP.

## Future

If users need transport coherence (playhead position, global play state, transport bar integration), implement a `PlaybackRequestedEvent` publish from the Script Editor, aligned with the pattern used in LibraryViewModel.

## Reference

- Implementation: `ScriptEditorViewModel.PlaySegmentAsync`
- Related: `LibraryViewModel` fallback path for playback
