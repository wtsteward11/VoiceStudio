# GOV-VOICESTUDIO-EDIT-APPLY-CONTEXT-JUMP-01 — Context jump from job status / edit history (GAP-045)

## 0. Status / Overseer snapshot

- **State:** **Closed** (2026-04-02) — closure [VOICESTUDIO_EDIT_APPLY_CONTEXT_JUMP_LANE_CLOSURE_2026-04-02.md](../reports/verification/VOICESTUDIO_EDIT_APPLY_CONTEXT_JUMP_LANE_CLOSURE_2026-04-02.md); governance sync in `CANONICAL_REGISTRY.md`, `PROFESSIONAL_GAP_TRACKER.md`, `.cursor/STATE.md`.
- **Product:** **GAP-045** remains **Open**; bounded sub-lane only.
- **Depends on:** Edit-apply job status, edit history, retry/recovery, `ITranscriptSegmentTargetResolver`, `NavigateToEvent` timeline handling — all client-side.
- **Verification provenance (closure):** **Independently repo-verified locally** or **Connector-limited architectural review only** (same policy as GOV-VOICESTUDIO-EDIT-APPLY-RETRY-RECOVERY-01).

## 1. Objective

Make **session edit history** and **apply/regenerate job status** rows **actionable**: operator can jump to the **source transcription segment** and **linked timeline context** (seek + clip id) using existing resolution and navigation seams.

## 2. Hard IN

- **Edit-history row** click → same-context jump (existing `ItemClick` path; semantics hardened).
- **Job-status row** click → jump using frozen row fields: `TranscriptionId`, `ProjectId`, `SegmentIds` (anchor = first non-empty id), optional `ClipId` for mismatch preflight.
- Align timeline via existing `NavigateToEvent` (`seekPlayhead`, `timeSeconds`, `clipId`) from resolver **re-resolution** on the **current** project model.
- **Fail-closed** with `TranscriptOperatorMessage` when: wrong project, transcription not in list, no segment target, segment missing from transcription, resolver not registered, resolver not `Resolved`, **or** optional `ClipId` on row disagrees with **current** resolved clip after edits.
- **Tests:** success, missing transcription, stale segment, project mismatch, clip mismatch; retry unchanged.
- **No** new backend routes or persistence.

## 3. Hard OUT

- Backend API / schema changes.
- Persistent navigation stack or global “breadcrumb” state.
- Panel shell / `PanelHost` redesign.
- Timeline selection authority redesign.

## 4. Canonical jump source (frozen)

| Field | Source |
|--------|--------|
| `TranscriptionId` | Row model |
| `ProjectId` | Row model (optional) |
| Anchor `SegmentId` | First non-empty entry in `SegmentIds` |
| `ClipId` | Row model (optional integrity check vs fresh resolve) |

## 5. Operator messaging (frozen)

- Unified strings for history and job rows where applicable (no “history-only” wording for shared failure modes).
- Success: reuse existing **“Timeline: focused linked clip and applied seek.”** after successful resolve+publish.

## 6. Verification

- `dotnet build`, full `VoiceStudio.App.Tests`, `pytest tests/ci`, `verify.ps1 -Quick`, `run_verification.py` with **completion_guard** PASS.
- Closure report + CANONICAL_REGISTRY + PROFESSIONAL_GAP_TRACKER + `.cursor/STATE.md`.

## 7. Rollback

- Revert lane-only VM/UI/tests/docs/registry/STATE edits.

## Changelog

- **2026-04-02:** Initial execution row + lane closure report published; proof: `verify.ps1 -Quick` → `artifacts/verify/20260401_202730/`; `run_verification.py` → `timestamp_short` **20260401-203252**.
