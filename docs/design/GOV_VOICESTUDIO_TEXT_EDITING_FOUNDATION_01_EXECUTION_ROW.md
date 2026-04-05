# GOV-VOICESTUDIO-TEXT-EDITING-FOUNDATION-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-TEXT-EDITING-FOUNDATION-01`  
**Status:** **Closed** 2026-03-31 — GAP-045 **foundation** slice (target resolution + seek/focus + non-executing edit intents). Closure: [VOICESTUDIO_TEXT_EDITING_FOUNDATION_LANE_CLOSURE_2026-03-31.md](../reports/verification/VOICESTUDIO_TEXT_EDITING_FOUNDATION_LANE_CLOSURE_2026-03-31.md).  
**Tracker:** [GAP-045](PROFESSIONAL_GAP_TRACKER.md) (parent gap partially satisfied: foundation only; full Descript-class editing remains future work).

## Frozen objective

Turn **GAP-033 transcript–clip linkage** into a **deterministic editing foundation**: transcript segments resolve to at most one timeline clip, navigation applies **clip focus + correct timeline seek time**, and **typed edit intents** exist with explicit non-execution reasons until downstream lanes wire synthesis/regeneration.

## Authority map (frozen)

| Concern | Owner | Notes |
|--------|--------|------|
| Timeline `Project` snapshot for other panels | `ITimelineSelectedProjectGate` | Updated from `TimelineViewModel.OnSelectedProjectChanged` only |
| Segment → clip + timeline seek | `ITranscriptSegmentTargetResolver` | Uses `IClipTranscriptLinkageService` + in-memory `Project` |
| Edit intent session state | `ITranscriptEditIntentService` | Validates via resolver; **does not** mutate audio |
| Transcript segment tap / status | `TranscribeViewModel` + `TranscribeView` | Publishes `NavigateToEvent` `seekPlayhead` with `clipId` + `timeSeconds` |
| Timeline focus + seek | `TimelineViewModel.OnNavigateToTimeline` | `seekPlayhead` applies optional `clipId` focus before seek |

## Time basis (frozen)

- Segment times are treated as **source-audio seconds** in the same space as GAP-033 linkage overlap (`[0, clip.Duration)`).
- **Timeline seek seconds** = `clip.StartTime` (timeline seconds, `double`) + `segmentStartSeconds`.

## Policy (frozen)

- **Fail closed** on ambiguous multi-clip mapping for the same `(transcriptionId, segmentId)`.
- **No silent** clip choice when multiple links match.
- **Timeline** remains transport/playback authority; transcript panel requests navigation via events.
- **Remove / replace / regenerate** intents are **recorded only** in this lane; execution is explicitly blocked with an operator-visible reason.

## Hard IN

- `ITimelineSelectedProjectGate`, `TranscriptSegmentTargetResolver`, `TranscriptEditIntentService` + DI.
- `NavigateToEvent` `seekPlayhead` support for **`clipId`**
- Transcribe segment UI: tap → resolve → message + navigate.
- Segment row highlight when `LinkedTranscriptSegmentIds` contains segment id.
- MSTest coverage for resolver + intent service.

## Hard OUT

- Waveform editor, destructive timeline edits, regeneration/synthesis execution, subtitle overhaul, diarization rewrite, PanelHost work, broad transcript UX redesign.

## Binary acceptance

- [x] Resolver: `Resolved`, `Unlinked`, `AmbiguousMultipleClips`, `NoTimelineProject`, `InvalidInput` are test-covered.
- [x] Timeline `seekPlayhead` optional `clipId` focuses clip before seek.
- [x] Transcript segment tap uses resolver + publishes navigate with **timeline** seek seconds (not raw source time only).
- [x] `ITranscriptEditIntentService` records intent with `DownstreamExecutable == false` and explicit `ExecutionBlockedReason`.
- [x] Closure report + tracker + registry + STATE agree.

## Honest limits

- Full **GAP-045** product vision (transcript → edit → regen) spans multiple future lanes; this row is **foundation only**.
- Clip trim/slip vs source time not modeled beyond GAP-033 overlap assumptions.

## Rollback

Revert DI registrations, gate wiring, `NavigateToEvent` `clipId` branch, Transcribe segment UI changes, and new Core types; keep GAP-033 linkage intact.
