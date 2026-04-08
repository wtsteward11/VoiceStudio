# GOV-VOICESTUDIO-GAP024-TRAINING-SIMULATION-HONESTY-01

**Status:** Closed  
**GAP:** GAP-024 — Training simulation: modal + block “complete” on simulated runs  
**Phase:** 2 (Broken)  
**Role:** UI Engineer / Core Platform  
**Created / Closed:** 2026-04-08  

---

## Problem Statement

Training simulation runs could be treated like real completions: WebSocket completion handler forced `status = "completed"`, success toasts, profile `training_completed` events, and OS “success” notifications even when the backend finished with `simulation_complete` and no exported model.

## Bounded Slice

- **UI:** Distinguish real completion vs simulation terminal state; reload canonical job from API before completion UX; gate profile events and success toasts on real completion only.
- **Model:** Friendly `StatusDisplay` for job list when simulation terminal.
- **Backend:** Already used `SIMULATION_STATUS` (`simulation_complete`) and blocked export for non-`completed` jobs; extended pytest coverage for export rejection.

## Allowlist

| Area | Files |
|------|--------|
| ViewModel | `src/VoiceStudio.App/Views/Panels/TrainingViewModel.cs` |
| UI | `src/VoiceStudio.App/Views/Panels/TrainingView.xaml` |
| Contracts | `src/VoiceStudio.App/Core/Models/Training.cs` (`StatusDisplay`) |
| Tests | `src/VoiceStudio.App.Tests/ViewModels/TrainingViewModelSeamTests.cs`, `tests/unit/backend/services/test_training_simulation_honesty.py` |
| Governance | Tracker, `CANONICAL_REGISTRY.md`, `.cursor/STATE.md`, this row, closure report |

## Hard OUT

- No engine-layer or training-algorithm changes.
- No new routes or pip/NuGet dependencies for this lane.

## Acceptance Contract

- [x] Simulated runs do not trigger profile “trained” completion events or success toast as if a model shipped.
- [x] WebSocket completion path reloads server truth before UX.
- [x] Polling completion events use the same “real completion” predicate.
- [x] Job list shows a clear simulation-complete label via `StatusDisplay`.
- [x] Backend tests: simulation status ≠ `completed`; export returns `None` for simulation.
- [x] Full proof set executed for this lane (see closure report).

## Rollback

Revert the GAP-024 implementation commit(s) listed in the closure report.

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| WebSocket completion race | Reload jobs/logs before any completion UX |
| Missed simulation flag | Predicate uses `SimulationMode` and `simulation_complete` status |
