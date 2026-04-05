# GOV-VOICESTUDIO-GAP028-TRAINING-PROFILE-METADATA-REFRESH-01 — Execution row (GAP-028)

## 0. Status

- **State:** **Closed** (2026-04-01) — closure [VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_GAP028_TRAINING_PROFILE_METADATA_REFRESH_LANE_CLOSURE_2026-04-01.md).
- **Gap:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-028** — Training complete → profile quality / metadata refresh.
- **Product posture:** **GAP-045** remains **Open** (this lane is cross-panel refresh only).
- **GAP-024 note:** Training simulation UX (GAP-024) is a **tracker ordering preference**, not a runtime prerequisite for completion signals via WebSocket `JobCompleted` or polling `status == "completed"`.

## 1. Objective (frozen)

When a training job completes (WebSocket or polling), **Profiles** reloads from the backend so profile cards and quality metadata are not stale. Polling path has **parity** with WebSocket for cross-panel `ProfileCreatedEvent` + **`ProfileUpdatedEvent`**, with **deduplication** so repeated polls do not spam events.

## 2. Hard IN (frozen)

- **`TrainingViewModel`:** On polling-observed completion (`status` equals `completed`, non-empty `ProfileId`), publish **`ProfileCreatedEvent`** and **`ProfileUpdatedEvent`** (payload includes `training_completed`, `training_job_id`), gated by **`_lastPublishedCompletedTrainingJobId`**.
- **WebSocket** `OnTrainingJobCompleted`: same publishes when `ProfileId` present, **skipping** if that job id was already published (e.g. polling won the race).
- **`ProfilesViewModel`:** Subscribe to **`ProfileUpdatedEvent`** (constructor, with `ProfileCreatedEvent`); `OnProfileUpdatedRefresh` reloads list and re-selects matching profile when not self-sourced.
- **MSTest:** polling publish + duplicate guard + non-publish paths + `ProfileUpdatedEvent` → second `ListAsync`; seam tests use `SeamTryPublishPollingTrainingCompletion` / `SeamPublishTrainingCompletedProfileEvents`.
- Full **verification matrix** on closure.

## 3. Hard OUT (frozen)

- No new backend routes or shared-schema changes (metadata refresh = **reload existing profiles API**).
- No duplicate profile store or training pipeline rewrite.
- No multi-user / cloud sync.

## 4. Acceptance criteria

- Completed training with `ProfileId` (polling seam) publishes exactly one **`ProfileCreatedEvent`** and one **`ProfileUpdatedEvent`** per job id.
- Second poll for the same completed job **does not** republish.
- Non-`completed` status or empty `ProfileId` **does not** publish profile events.
- **`ProfilesViewModel`** handles **`ProfileUpdatedEvent`** from `PanelIds.Training` by calling **`LoadProfilesAsync`** again (list load count increases).
- Closure: build + App.Tests + `pytest tests/ci` + `verify.ps1 -Quick` + `run_verification.py` (**completion_guard** PASS).

## 5. Proof expectations

- Closure report under `docs/reports/verification/` with artifact paths and provenance.
- Tracker row **Closed**, execution row **Closed**, `CANONICAL_REGISTRY` + `.cursor/STATE.md` synced in the same narrative turn.

## 6. Implementation map (reference)

| Step | Source | Sink |
|------|--------|------|
| Polling | `PollTrainingStatusAsync` → `TryPublishPollingTrainingCompletion` | `ProfileCreatedEvent`, `ProfileUpdatedEvent` |
| WebSocket | `OnTrainingJobCompleted` | Same publish path when not deduped |
| Profiles UI | `ProfilesViewModel.OnProfileUpdatedRefresh` | `LoadProfilesAsync` + selection |
