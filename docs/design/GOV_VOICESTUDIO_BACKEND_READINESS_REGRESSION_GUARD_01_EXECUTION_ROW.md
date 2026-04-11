# Execution Row: Backend Readiness Regression Guard — GOV-VOICESTUDIO-BACKEND-READINESS-REGRESSION-GUARD-01

**Lane ID:** GOV-VOICESTUDIO-BACKEND-READINESS-REGRESSION-GUARD-01  
**Gap:** GAP-069 bounded slice 2 (Ops — regression guard for backend readiness / startup artifact)  
**Row type:** **proof-hardening** — *No production code paths changed in this lane.* (scripts, tests, governance only.)  
**Status:** CLOSED  
**Date frozen:** 2026-04-11  
**Owner Role:** Build Tooling (Role 2) / Core Platform (Role 4)  
**Validator:** Overseer (Role 0) + Skeptical Validator  
**Predecessor:** [GOV_VOICESTUDIO_BACKEND_READINESS_TRUTH_01_EXECUTION_ROW.md](GOV_VOICESTUDIO_BACKEND_READINESS_TRUTH_01_EXECUTION_ROW.md) (CLOSED)

---

## Context

Prior slice unified `startup_decision.json` schema v2, `engines_ready` on `/health`, and Grade R runtime proof. This lane adds a **canonical checker**, **pytest coverage**, **operator backend smoke**, and threads the checker into `run_verification.py` so readiness regressions are detectable without new product features.

---

## Runtime proof requirement

- [x] **Inherited Grade R proof required** — no new product startup paths; closure cites the most recent `docs/reports/verification/PROOF_GOLDEN_PATH_REAL_*.json` within the **72h** policy window (e.g. `PROOF_GOLDEN_PATH_REAL_2026-04-10.json` at freeze/close).

---

## Hard IN (Scope)

1. `scripts/ci/check_startup_artifact.py` — canonical validator (hard fail vs advisory timing).
2. `tests/unit/test_startup_artifact_checker.py` — ≥ 8 regression scenarios.
3. `scripts/ci/run_backend_smoke.py` — bounded uvicorn smoke; emits `PROOF_BACKEND_SMOKE_*.json`.
4. `scripts/run_verification.py` — `startup_artifact_check` in the quality block (after `empty_catch_check`).
5. Governance: execution row, closure report, GAP-069 addendum, CANONICAL_REGISTRY, `.cursor/STATE.md`, `openmemory.md`.

## Hard OUT

- No production backend/frontend/runtime code changes.
- No `startup_decision.json` schema changes (v2 fixed).
- No shell redesign, new engines, or telemetry warehouse.

---

## Regression definition (checker)

| Failure mode | Category | Posture |
|--------------|----------|---------|
| File missing | Structural | Hard fail |
| `schema_version != 2` | Structural | Hard fail |
| `status == "failure"` with `decision` in `{health_timeout, spawn_failure, app_root_invalid, runtime_missing}` | Operational | Hard fail |
| `status == "success"` but `health_probe_result == false` | Logic contradiction | Hard fail |
| Required field missing (key absent after parse) | Structural | Hard fail |
| `healthy_elapsed_ms` or `spawn_elapsed_ms` over advisory budgets | Timing | Advisory (warn; exit 0) |

**Advisory budgets (ms):** `healthy_elapsed_ms` ≤ 45_000; `spawn_elapsed_ms` ≤ 10_000 (do not fail CI on breach).

---

## Failure-path parity

- **Checker hard fail:** operator sees non-zero exit, JSON on stdout with `errors[]`; fix artifact or startup path; rollback = remove checker wiring.
- **Checker advisory:** stderr warnings; exit 0.
- **Smoke BLOCKED (exit 2):** `check_runtime_prerequisites.py` blocked — operator fixes env/manifest/import; no partial proof JSON as PASS.
- **Smoke FAIL:** uvicorn or health timeout — proof JSON records `status: FAIL`; subprocess terminated in `finally`.

---

## Acceptance Contract

- [x] Execution row frozen before first code change
- [x] `scripts/ci/check_startup_artifact.py` exists; exits 0 on valid schema-v2 success artifact
- [x] Checker exits 1 on: missing file, wrong schema version, `status=failure` hard decision, `health_probe_result=false` on success, any missing required field
- [x] Checker exits 0 with advisory warning (not failure) for timing budget breaches
- [x] Budget posture explicit (constants + comment)
- [x] `tests/unit/test_startup_artifact_checker.py` has ≥ 8 tests, all PASS (**10** tests)
- [x] `scripts/ci/run_backend_smoke.py` exists; standalone; emits `PROOF_BACKEND_SMOKE_*.json`
- [x] `run_verification.py` includes `startup_artifact_check`; reports `[PASS] startup_artifact_check` when artifact valid
- [x] App.Tests count non-regressing (full run: 3337 PASS + 1 flaky fail; filter re-run PASS — no product code change)
- [x] `verify.ps1 -Quick` PASS (`artifacts/verify/20260411_070615/`)
- [x] `runtime_proof_staleness` FRESH — `PROOF_GOLDEN_PATH_REAL_2026-04-10.json` ~8h at close
- [x] `completion_guard` PASS post-change (`python scripts/run_verification.py`)

---

## Proof Matrix (fill on close)

| Check | Result |
|-------|--------|
| `pytest tests/unit/test_startup_artifact_checker.py` | PASS — **10** tests |
| `python scripts/ci/check_startup_artifact.py` | PASS (sample or live artifact) |
| `python scripts/run_verification.py` | PASS; `[PASS] startup_artifact_check`; `completion_guard` PASS |
| `verify.ps1 -Quick` | PASS — `artifacts/verify/20260411_070615/` |
| Inherited `PROOF_GOLDEN_PATH_REAL_*.json` | **FRESH** — `PROOF_GOLDEN_PATH_REAL_2026-04-10.json` |

---

## Rollback

Delete new scripts; remove `startup_artifact_check` from `scripts/run_verification.py`; remove tests; revert governance edits.

---

## Allowlist (intended commit paths)

- `docs/design/GOV_VOICESTUDIO_BACKEND_READINESS_REGRESSION_GUARD_01_EXECUTION_ROW.md`
- `scripts/ci/check_startup_artifact.py`
- `scripts/ci/run_backend_smoke.py`
- `tests/unit/test_startup_artifact_checker.py`
- `scripts/run_verification.py`
- `docs/reports/verification/VOICESTUDIO_BACKEND_READINESS_REGRESSION_GUARD_LANE_CLOSURE_2026-04-11.md`
- `docs/reports/verification/samples/startup_decision_success_v2.json`
- `docs/design/PROFESSIONAL_GAP_TRACKER.md`
- `docs/governance/CANONICAL_REGISTRY.md`
- `.cursor/STATE.md`
- `openmemory.md`

---

## Operator notes

- **Rolling verifier:** If `%LOCALAPPDATA%\VoiceStudio\crashes\startup_decision.json` is absent (never launched shell), `startup_artifact_check` fails by design. Set `VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK=1` to skip the check, or run the app once to emit the artifact. Optional: `VOICESTUDIO_STARTUP_ARTIFACT_PATH` passed through `run_verification.py` to `--path` for a known file.
