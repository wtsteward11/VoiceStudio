# VOICESTUDIO — GAP-015 Runtime proof hard gate (slice 2) — Lane closure

**Lane ID:** `GOV-VOICESTUDIO-GAP015-RUNTIME-PROOF-HARD-GATE-02`  
**Tracker:** **GAP-015** — **Partial** (percentile SLO measurement = slice 3)  
**Execution row:** [GOV_VOICESTUDIO_GAP015_RUNTIME_PROOF_HARD_GATE_02_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP015_RUNTIME_PROOF_HARD_GATE_02_EXECUTION_ROW.md) — **Closed**  
**Closure date:** 2026-04-08  
**Git (harness):** `main` @ `cb804077c32fba7b30d1b57e94e708967d2ed678` (GAP-015 slice 2 implementation)  
**Git (proof seal):** `main` — verification evidence §4 + probe fix ship in the same commit as this doc revision; resolve hash with `git log -1 --format=%H -- docs/reports/verification/VOICESTUDIO_GAP015_RUNTIME_PROOF_HARD_GATE_02_LANE_CLOSURE_2026-04-08.md`.

---

## 1. Goal

Make Grade-R proof **enforceable** on an opt-in path: hard fail for missing/stale `PROOF_GOLDEN_PATH_REAL_*.json`, upgrade `runtime_proof.json` to **schema v2**, and add explicit **PASS / FAIL / BLOCKED** semantics for `verify.ps1 -RuntimeProof` without mandating Quick mode.

---

## 2. What shipped

| Deliverable | Location |
|-------------|----------|
| Prerequisite probe (manifest + router + consent) | `scripts/ci/check_runtime_prerequisites.py` |
| `runtime_proof.json` schema v2 + exit 2 BLOCKED | `scripts/verify.ps1` (`-RuntimeProof`) |
| `--enforce-runtime-proof` on rolling verifier | `scripts/run_verification.py` |
| `-EnforceRuntimeProof` on full verify Stage 9 | `scripts/verify.ps1` |
| Enforcement matrix (Quick / full / enforce) | `docs/governance/EXECUTION_ROW_DISCIPLINE.md` |
| Grade R + schema v2 note | `docs/governance/TEST_CLASSIFICATION.md` |
| Unit test (staleness enforce) | `tests/unit/test_runtime_proof_staleness_enforcement.py` |
| Probe stdout isolation (proof seal) | `scripts/ci/check_runtime_prerequisites.py` — redirect stderr during engine-router probe so `verify.ps1` can parse JSON from stdout |

---

## 3. Verification (expected behavior)

| Step | Command | Expected |
|------|---------|----------|
| Rolling advisory | `python scripts/run_verification.py` | PASS (staleness row advisory unless proof fresh) |
| Rolling enforce | `python scripts/run_verification.py --enforce-runtime-proof` | FAIL if proof missing/stale |
| Quick unchanged | `.\scripts\verify.ps1 -Quick` | GREEN (no `-EnforceRuntimeProof`) |

---

## 4. Proof seal — actual results (2026-04-08)

Recorded against **`6580766898f6cc2826ca7d65851590d75c6f512d`** (workspace HEAD at verification runs). Proof seal shipped with stderr-isolation fix in **`scripts/ci/check_runtime_prerequisites.py`** and this closure update (same commit as doc revision — hash via `git log` command above).

| Surface | Command | Outcome | Artifact / evidence |
|---------|---------|---------|---------------------|
| **Quick** | `.\scripts\verify.ps1 -Quick` | **PASS** (exit **0**) | `artifacts/verify/20260408_200238/verification_report.md`; `latest_pointer.json` commit_hash matched slice-2 HEAD at Quick run; Stage 27 Gate/Ledger **ADVISORY** staleness (Quick uses `--skip-guard` for `completion_guard`) |
| **Runtime proof** | `.\scripts\verify.ps1 -RuntimeProof` | **FAIL** (exit **1**) — honest | `artifacts/verify/20260408_201308/runtime_proof.json`: **schema_version** 2, **status** `FAIL`, **proof_grade** `R`, **commit_hash** `6580766898f6cc2826ca7d65851590d75c6f512d`. Synthesis assertion failed: HTTP **503** (engines unavailable in ASGI path); training export tests **passed** (2/2). |
| **Enforce — full** | `.\scripts\verify.ps1 -EnforceRuntimeProof` | **Stopped** at **Python Unit Tests** (exit **1**) | Failed before Gate/Ledger: `tests/unit/backend/api/routes/test_search.py` — `RuntimeError: Database not connected` (environment; not enforcement wiring). |
| **Enforce — Stage 9 only** | `.\scripts\verify.ps1 -OnlyStage "Gate/Ledger Validation" -EnforceRuntimeProof` | **FAIL** at Stage 23 (exit **1**) — **expected** | `artifacts/verify/20260408_202359/logs/gate_ledger_validation.log`; console: `runtime_proof_staleness enforce mode ON (GAP-015 slice 2)`; `[FAIL] runtime_proof_staleness` — **STATUS=STALE** (>72h policy). Proves PowerShell passes `--enforce-runtime-proof` into `run_verification.py`. |
| **Rolling advisory** | `python scripts/run_verification.py` (no `--skip-guard`) | **PASS** (exit **0**) | `.buildlogs/verification/last_run.json` — **timestamp_short** `20260408-202647`, **all_passed** true, **completion_guard** PASS, **runtime_proof_staleness** ADVISORY (STALE, warning-only) |
| **Rolling enforce** | `python scripts/run_verification.py --enforce-runtime-proof` | **FAIL** (exit **1**) — **expected** | **runtime_proof_staleness** FAIL (STALE); demonstrates enforce mode |

**Note:** `artifacts/verify/latest_pointer.json` may point at a later failed-only run (e.g. OnlyStage enforce folder). Authoritative **Quick PASS** folder for this seal is **`20260408_200238`**.

---

## 5. Deferred (slice 3)

- Percentile latency SLOs, baselines, automated trend detection  
- Product telemetry integration for SLO dashboards  

---

## 6. Rollback

`git revert` of the slice 2 commit set (harness + docs only).
