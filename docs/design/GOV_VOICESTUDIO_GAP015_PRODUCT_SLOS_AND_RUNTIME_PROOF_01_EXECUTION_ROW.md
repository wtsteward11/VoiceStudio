# GOV-VOICESTUDIO-GAP015-PRODUCT-SLOS-AND-RUNTIME-PROOF-01

| Field | Value |
|-------|--------|
| **Status** | Closed |
| **GAP** | GAP-015 — Product SLO definitions + CI measurement hooks (slice 1: taxonomy + gates) |
| **Lane type** | runtime-affecting (governance + CI tooling + tests; **no production feature code**) |
| **Role** | Build Tooling / Overseer |
| **Created** | 2026-04-08 |
| **Closed** | 2026-04-08 |

## Problem statement

Seam proof (MSTest, `pytest tests/ci`, rolling verifier) is strong, but **operability truth** is not a first-class closure signal: `verify.ps1 -Quick` skips UI and integration stages, and there is no proof-grade taxonomy (seam vs integration vs live-runtime). This slice defines **Grade S / I / R**, execution-row **runtime proof requirements**, a **`-RuntimeProof`** harness stage, and a **staleness check** for real golden-path proof artifacts—without building a monitoring platform.

## Runtime proof requirement (freeze)

- [x] **Fresh Grade R proof required** — N/A (governance slice; no change to synthesis/training/startup product paths beyond CI tests)
- [x] **Inherited Grade R proof required** — N/A
- [x] **No Grade R proof** — this lane is governance + CI tooling + new CI tests only (**proof-hardening-style** for process; lane type remains runtime-affecting because scripts/tests change)

## Bounded slice

1. Document proof-grade taxonomy in `TEST_CLASSIFICATION.md` (Grade S / I / R + matrix).
2. Extend `EXECUTION_ROW_DISCIPLINE.md` with mandatory **Runtime Proof Requirement** and closure **Runtime Proof** section rules.
3. Add `tests/ci/test_runtime_proof_training_export.py` — ASGI `POST /api/training/export` rejects simulation / non-completed jobs (404).
4. Add `scripts/verify.ps1 -RuntimeProof` standalone stage: real-mode golden loop (override pytest `addopts` to allow `@pytest.mark.nightly`) + training export honesty test; write `artifacts/verify/<ts>/runtime_proof.json`.
5. Extend `scripts/run_verification.py` with `runtime_proof_staleness` (FRESH / STALE / MISSING for `PROOF_GOLDEN_PATH_REAL_*.json`) — **warning-only** (does not fail exit code).

## Evidence inventory (runtime truth surfaces)

| Surface | Location | Grade | Default CI / Quick | Closure-blocking |
|--------|----------|-------|---------------------|------------------|
| Stub golden loop | `tests/ci/test_golden_loop_smoke.py` | I | Yes (Quick Critical Gates) | Quick + full |
| Real-mode golden loop | `tests/ci/test_golden_loop_smoke_real.py` | R (engines + consent) | No (nightly marker) | **`-RuntimeProof` only** |
| Training export honesty (API) | `tests/ci/test_runtime_proof_training_export.py` | I (ASGI full app) | Yes (`pytest tests/ci`) | Quick + full |
| Training simulation unit | `tests/unit/.../test_training_simulation_honesty.py` | S | Unit suite | Lane-specific |
| Startup decision artifact | `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` | R | Full verify UI stages only | Full verify |
| Icon-launch / failure smokes | `scripts/icon-launch-failure-smoke.ps1`, etc. | R | Full verify (not Quick) | Full verify |
| Real golden path proof JSON | `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json` | R (optional) | Manual / `write_golden_path_real_proof.py` | Staleness check (warning) |

## Three closure-grade assertions (slice 1)

### A — Startup / backend readiness (inherited from full verify)

| Item | Detail |
|------|--------|
| Command | `.\scripts\verify.ps1` (full, not Quick) — stages UI Self-Test, Icon-Launch Smoke, Failure-Path Smoke, Runtime-Missing Failure Smoke |
| Artifact | `startup_decision.json`, `icon_launch_smoke_summary.json`, smoke summaries under `%LOCALAPPDATA%\VoiceStudio\crashes\` |
| Success | `startup_decision.json` decision in `reuse` \| `spawn` for happy path; failure smokes report PASS |
| Failure | `health_timeout`, `spawn_failure`, or smoke summary not PASS |

### B — Canonical synthesis with real artifact (engine-backed)

| Item | Detail |
|------|--------|
| Command | `python -m pytest tests/ci/test_golden_loop_smoke_real.py::test_golden_loop_real_health_synthesize_stream -v --override-ini "addopts=-v --strict-markers --tb=short --color=yes -p no:capture --randomly-seed=12345"` |
| Prereq | Piper (or configured engine), consent API, `VOICESTUDIO_TEST_MODE=real` (set in test module) |
| Artifact | Pytest JUnit if configured; harness writes `artifacts/verify/<ts>/runtime_proof.json` |
| Success | `audio_id` set; `GET /api/audio/file/{id}` returns WAV bytes |
| Failure | pytest.fail paths in test |

### C — Training export honesty (simulation / incomplete blocked)

| Item | Detail |
|------|--------|
| Command | `python -m pytest tests/ci/test_runtime_proof_training_export.py -v` |
| Artifact | Same `runtime_proof.json` bundle |
| Success | `POST /api/training/export` with `simulation_complete` job → **404** |
| Failure | Any 2xx with body export for simulation job |

## Allowlist

| Area | Files |
|------|-------|
| Governance | `docs/governance/TEST_CLASSIFICATION.md`, `docs/governance/EXECUTION_ROW_DISCIPLINE.md` |
| CI | `scripts/verify.ps1`, `scripts/run_verification.py` |
| Tests | `tests/ci/test_runtime_proof_training_export.py` |
| Design | This file |
| Reports | `docs/reports/verification/VOICESTUDIO_GAP015_PRODUCT_SLOS_AND_RUNTIME_PROOF_LANE_CLOSURE_2026-04-08.md` |
| Tracker / registry / state | `docs/design/PROFESSIONAL_GAP_TRACKER.md`, `docs/governance/CANONICAL_REGISTRY.md`, `.cursor/STATE.md` |

## Hard OUT

- No product feature expansion, no new training algorithms, no telemetry warehouse
- No mandatory `-RuntimeProof` inside `-Quick`
- Staleness check does not fail `run_verification.py` exit code in this slice (warning only)

## Acceptance contract (Close)

- [x] Grade S/I/R taxonomy and execution-row matrix in `TEST_CLASSIFICATION.md`
- [x] Runtime proof requirement + closure section in `EXECUTION_ROW_DISCIPLINE.md`
- [x] `tests/ci/test_runtime_proof_training_export.py` passes under `pytest tests/ci`
- [x] `verify.ps1 -RuntimeProof` runs synthesis real test + training export test; writes `runtime_proof.json`
- [x] `run_verification.py` reports `runtime_proof_staleness` (warning-only)
- [x] `verify.ps1 -Quick` + `pytest tests/ci` + `run_verification.py` GREEN for lane closure
- [x] Closure report + tracker + registry + STATE updated

## Rollback

Revert the closure commit; no production app code paths changed.

## Changelog

| Date | Change |
|------|--------|
| 2026-04-08 | Initial freeze + closure |
