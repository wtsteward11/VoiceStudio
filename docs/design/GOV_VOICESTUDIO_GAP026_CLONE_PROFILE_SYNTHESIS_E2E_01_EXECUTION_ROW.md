# GOV-VOICESTUDIO-GAP026-CLONE-PROFILE-SYNTHESIS-E2E-01 — Execution row (GAP-026)

## 0. Status

- **State:** **Closed** (2026-04-01) — closure [VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_LANE_CLOSURE_2026-04-01.md](../reports/verification/VOICESTUDIO_GAP026_CLONE_PROFILE_SYNTHESIS_E2E_LANE_CLOSURE_2026-04-01.md).
- **Gap:** [PROFESSIONAL_GAP_TRACKER.md](PROFESSIONAL_GAP_TRACKER.md) **GAP-026** — Clone → Profile → Synthesis E2E.
- **Product posture:** **GAP-045** remains **Open** (this lane is hero-path wiring only).

## 1. Objective (frozen)

After voice clone **finalize** succeeds, the new profile becomes the **active** synthesis target when the operator opens **Voice Synthesis**: `IContextManager.ActiveProfileId` / `VoiceSynthesisViewModel.SelectedProfile` align without manual re-selection.

## 2. Hard IN (frozen)

- **`VoiceSynthesisViewModel.OnActivatedAsync`:** after subscribing to `ProfileSelectedEvent`, sync from `IContextManager.ActiveProfileId` when it differs from `SelectedProfile` (use `OnProfileSelected` with source `context-manager-sync`).
- **`VoiceCloningWizardViewModel.FinalizeWizardAsync`:** after successful `ProfileCreatedEvent`, publish **`ProfileSelectedEvent`** with `InteractionIntent.ImmediateUse` so consumers already subscribed see the selection immediately.
- **Known limitation (documented):** `NavigateToEvent("voice-synthesis", …)` is published but **shell-level panel switching is not wired** (no subscriber switches UI). GAP-026 does **not** implement shell navigation; profile propagation is via context + events only.
- **MSTest** coverage: activation sync; wizard event order (`ProfileCreated` then `ProfileSelected`); no spurious events on finalize failure.
- Full **verification matrix** on closure.

## 3. Hard OUT (frozen)

- No **PanelHost / GAP-007** shell rewrite.
- No new **NavigateToEvent** subscribers that switch workspace (deferred to shell roadmap).
- No new backend routes or schema changes.
- No training pipeline changes.
- No multi-user or cloud profile sync.

## 4. Acceptance criteria

- Clone finalize success with non-empty `ProfileId` publishes `ProfileCreatedEvent` then `ProfileSelectedEvent`.
- Activate **VoiceSynthesis** after context holds the new profile id → `SelectedProfile` matches (or loads after list refresh).
- **VoiceSynthesis** already active when finalize runs → `ProfileSelectedEvent` selects the profile if list contains it (existing handler).
- Finalize failure / empty `ProfileId` does not publish profile selection events beyond existing error handling.
- Closure: build + App.Tests + `pytest tests/ci` + `verify.ps1 -Quick` + `run_verification.py` (**completion_guard** PASS).

## 5. Proof expectations

- Closure report under `docs/reports/verification/` with artifact paths and provenance.
- Tracker row, `CANONICAL_REGISTRY` Session State, `.cursor/STATE.md` updated in the same narrative turn.

## 6. Implementation map (reference)

| Step | Source | Sink |
|------|--------|------|
| Finalize success | `VoiceCloningWizardViewModel` | `ProfileCreatedEvent`, `ProfileSelectedEvent`, `NavigateToEvent` (nav deferred) |
| Profile list | `ProfilesViewModel.OnProfileCreatedRefresh` | `IContextManager.SetActiveProfile` via `OnSelectedProfileChanged` |
| Context read | `IContextManager.ActiveProfileId` | `VoiceSynthesisViewModel.OnActivatedAsync` sync |
| Bus (live) | `ProfileSelectedEvent` | `VoiceSynthesisViewModel.OnProfileSelected` |
