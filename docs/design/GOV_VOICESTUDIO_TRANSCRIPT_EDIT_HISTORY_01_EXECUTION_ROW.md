# GOV-VOICESTUDIO-TRANSCRIPT-EDIT-HISTORY-01 — Session transcript edit history (GAP-045 bounded slice)

## 0. Status

- **State:** **Closed** 2026-04-01 — closure [VOICESTUDIO_TRANSCRIPT_EDIT_HISTORY_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_TRANSCRIPT_EDIT_HISTORY_LANE_CLOSURE_2026-04-01.md).
- **Product scope:** **GAP-045** — **Open**; **GAP-047** — **Open**; this lane is **session-local operator traceability** only (no product “editing complete”).
- **Depends on:** Inline apply, multi-segment apply, regenerate-segment, transcribe-first filler cleanup + review controls — all prior bounded lanes **Closed**.

## Changelog

- **2026-04-01:** Lane opened — frozen contract: session-only ring buffer, append-only history (undo/redo does **not** remove or rewrite entries), no backend.
- **2026-04-01:** Lane closed — `TranscriptEditHistoryService` + Transcribe instrumentation + UI + tests; **ClipId** for successful regen recorded **before** coordinator (linkage cleared on success).

## 1. Objective

Provide **in-session, operator-visible history** of transcript-driven edit operations so users see what ran, whether regeneration succeeded, and can jump back to affected clip/segment context—**without** new API routes or persistent audit storage.

## 2. Event source map (frozen)

| Source | Operation kind | Regenerated? | When entry is written |
|--------|----------------|--------------|------------------------|
| `ApplyEditedSegmentAsync` success (single) | `SingleSegmentApply` | Yes | After coordinator returns success |
| `ApplyEditedSegmentAsync` success (contiguous range) | `MultiSegmentRangeApply` | Yes | After coordinator returns success |
| `ApplyEditedSegmentAsync` / `RegenerateSegmentAudioAsync` failure | Same kind as attempted | No | After coordinator returns error |
| `RegenerateSegmentAudioAsync` (UI regen, no replacement text) | `RegenerateSegment` | Yes if success | After coordinator returns |
| `TryRemoveFillersFromEditingDraft` success with removals | `FillerCleanupDraft` | **No** (draft-only) | After draft text updated, `RemovedOccurrenceCount > 0` |

**Not recorded:** validation failures before coordinator (empty draft, missing segment, intent service errors)—no regeneration attempted.

## 3. Entry schema (frozen)

| Field | Description |
|-------|-------------|
| `EntryId` | Stable GUID string |
| `CreatedUtc` | UTC timestamp |
| `OperationKind` | Enum: `RegenerateSegment`, `SingleSegmentApply`, `MultiSegmentRangeApply`, `FillerCleanupDraft` |
| `ProjectId` | Active project id (may be null) |
| `ClipId` | Resolved linked clip when success + resolver resolves; else null. **Regen:** snapshot **before** `TranscriptSegmentRegenerationCoordinator` — success path removes linkage, so post-coordinator resolve can be null. |
| `TranscriptionId` | Transcription id |
| `SegmentIds` | Affected segments (ordered: range = contiguous ids) |
| `WasRegenerated` | True iff backend regen path completed successfully for that operation |
| `Succeeded` | True if operation completed as intended (including draft-only filler pass) |
| `MessageSummary` | Short operator-facing line (success / error snippet) |
| `SummaryLine` | Display line for list UI (derived) |

## 4. Session authority (frozen)

- **Storage:** Client-only `TranscriptEditHistoryService`; **ring buffer** max **20** entries, **newest first**.
- **Lifecycle:** Persists for app session until **Clear session** or process exit. **Not** cleared on project/transcription change (operator may compare across context switches).
- **Undo/redo:** **Append-only** — undo does not delete or alter history rows (document-class undo is out of scope).

## 5. Navigation (frozen)

- History row activation resolves `TranscriptionId` → select transcription if present in list.
- Seeks/focuses timeline via existing `TranscriptSegmentTargetResolver` + `NavigateToEvent` (`seekPlayhead` + `clipId` when resolved), reusing `OnTargetTranscriptionSegmentTapped` semantics for the **first** segment id in `SegmentIds`.
- Missing segment after reload: non-fatal operator message; no crash.

## 6. Hard IN

- `TranscriptEditHistoryService` + `TranscribeViewModel` instrumentation at regen boundary.
- Transcribe panel compact list + **Clear session** + click-to-navigate.
- MSTest coverage: success/fail apply, range ids, filler cleanup kind, clear, navigation event.
- Full verification matrix + governance sync on closure.

## 7. Hard OUT

- New backend `/api/*` routes or job types.
- SQLite / persistent audit DB.
- Collaborative comments, branches, merge models.
- NLP or timeline waveform editing.

## 8. Verification (closure)

- `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
- `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64`
- `python -m pytest tests/ci/ -q --randomly-seed=12345`
- `.\scripts\verify.ps1 -Quick`
- `python scripts/run_verification.py` — **completion_guard** PASS

## 9. Binary acceptance

- [x] Entries appear for successful single apply, range apply, regen, and filler draft removal (>0 terms).
- [x] Failed regen produces failure entry with reason; no duplicate entries for one coordinator call.
- [x] No backend route/job additions.
- [x] Clear removes all session entries; navigation publishes expected `NavigateToEvent` when resolver resolves.
