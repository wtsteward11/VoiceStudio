# GOV-VOICESTUDIO-GAP025-SYNTHESIS-TIMELINE-HANDOFF-01 — Execution row (GAP-025)

## 0. Status

- **State:** **Closed** (2026-04-02).
- **Gap:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-025** — Synthesis → Timeline handoff.
- **Product posture:** **GAP-045** remains **Open** (this lane is one hero-path slice only).
- **Closure:** [VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_LANE_CLOSURE_2026-04-02.md](../reports/verification/VOICESTUDIO_GAP025_SYNTHESIS_TIMELINE_HANDOFF_LANE_CLOSURE_2026-04-02.md)

## 1. Objective (frozen)

Deliver an **explicit operator-driven** handoff: synthesized audio is placed on the timeline **only** when the operator uses **Add to Timeline** (or equivalent command that publishes `AddToTimelineEvent`). **No** automatic clip insertion from `SynthesisCompletedEvent` on the timeline. Insertion uses a **single authority** in [`TimelineViewModel`](../../src/VoiceStudio.App/Views/Panels/TimelineViewModel.cs): resolve target track and start time deterministically.

## 2. Hard IN (frozen)

- **Explicit handoff only:** `TimelineViewModel` does **not** subscribe to `SynthesisCompletedEvent` for clip insertion. `SynthesisCompletedEvent` remains available for **Library** refresh and other subscribers.
- **Track resolution precedence:** `AddToTimelineEvent.TargetTrackIndex` if `0 <= index < Tracks.Count`; else `SelectedTrack`; else first track; if still none, existing flow creates a track (`AddTrackAndClipAsync`).
- **Start-time resolution precedence:** `InsertPosition` (total seconds, clamped to `>= 0`) if present; else `CurrentPlaybackPosition` if `>= 0` and finite; else **append** (max clip `EndTime` on target track, or `0`).
- **`TargetTrackIndex`:** 0-based index into the timeline VM’s current `Tracks` collection order.
- **Profile:** unchanged — event `ProfileId` with `IContextManager` fallback; fail-closed with existing toast if missing.
- **No new backend routes** or schema changes.
- **Deterministic MSTest** coverage for explicit handoff, insertion semantics, and **no** auto-add from `SynthesisCompletedEvent`.
- Full verification matrix on closure.

## 3. Hard OUT (frozen)

- No PanelHost **GAP-007** shell rewrite.
- No new backend routes unless ADR forces it.
- No persistent navigation stack for handoff.
- No auto-insert from synthesis completion on the timeline (regression of pre–GAP-025 `OnSynthesisCompleted` behavior is **intentional**).

## 4. Acceptance criteria

- Publishing `SynthesisCompletedEvent` with an active `TimelineViewModel` subscription **does not** add clips (explicit-only).
- `AddToTimelineEvent` adds a clip with correct `ProfileId` and selection behavior preserved.
- `InsertPosition` and `TargetTrackIndex` are honored when valid.
- Playhead (`CurrentPlaybackPosition`) used when `InsertPosition` is null and playhead is valid.
- Append fallback when playhead is invalid (negative or non-finite).
- Fail-closed: no project / no profile (after fallback) — no silent corruption.

## 5. Proof expectations

- Closure report with provenance label and artifact paths (`verify.ps1 -Quick`, `run_verification.py` with **completion_guard** PASS).
- Tracker, `CANONICAL_REGISTRY` Session State row, and `.cursor/STATE.md` updated in the same narrative turn.
