# Lane closure: GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-ENFORCEMENT-03

**Lane ID:** GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-ENFORCEMENT-03  
**Gap:** GAP-069 bounded slice 3  
**Row type:** proof-hardening (scripts, tests, verification harness, governance only — no production route/UI changes)  
**Status:** CLOSED  
**Date:** 2026-04-11  

**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_ENFORCEMENT_03_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_ENFORCEMENT_03_EXECUTION_ROW.md)

---

## 1. Objective

Promote `scripts/ci/run_backend_smoke.py` from optional operator-only to a **preserved canonical proof surface** (schema v1), add **unit tests**, integrate **backend smoke freshness** into `run_verification.py` (parallel to `runtime_proof_staleness`), and add **`-BackendSmoke`** / **`-EnforceBackendSmoke`** to `verify.ps1`.

---

## 2. Delivered

| Item | Evidence |
|------|----------|
| Smoke proof schema v1 | `schema_version`, `blocking_reason`, `failure_reason`, `environment_hints`; BLOCKED and prerequisite-FAIL paths write `PROOF_BACKEND_SMOKE_*.json` |
| Samples | `docs/reports/verification/samples/backend_smoke_pass_v1.json`, `backend_smoke_blocked_v1.json` |
| Tests | `tests/unit/test_backend_smoke_script.py` — **10** PASS |
| Rolling verifier | `_backend_smoke_freshness_result()` in `scripts/run_verification.py`; `--enforce-backend-smoke`; `--skip-backend-smoke-staleness` |
| Harness | `verify.ps1 -BackendSmoke` (standalone; exit 2 → advisory exit 0); `-EnforceBackendSmoke` → Gate/Ledger |
| BLOCKED posture | Latest proof `status=BLOCKED` never fails the verifier, even under `--enforce-backend-smoke` |

---

## 3. Verification (proof)

| Command | Result |
|---------|--------|
| `python -m pytest tests/unit/test_backend_smoke_script.py -v` | **10** PASS |
| `python scripts/run_verification.py` | Overall **PASS**; `completion_guard` **PASS**; `backend_smoke_freshness` **ADVISORY** STATUS=MISSING (no committed `PROOF_BACKEND_SMOKE_*.json` yet — expected on clean tree) |
| `python scripts/run_verification.py --enforce-backend-smoke` | **FAIL** when no smoke proof (enforce contract) |
| `.\scripts\verify.ps1 -Quick` | **PASS** — `artifacts/verify/20260411_080545/` |
| Inherited Grade R | `PROOF_GOLDEN_PATH_REAL_2026-04-10.json` — `runtime_proof_staleness` **FRESH** (advisory) |

---

## 4. Honest limits

- **Missing `PROOF_BACKEND_SMOKE_*.json`:** Default rolling verifier reports STATUS=MISSING **advisory** (does not fail). Operators/nightly jobs with prerequisites should run `python scripts/ci/run_backend_smoke.py` or `verify.ps1 -BackendSmoke` to emit a timestamped proof under `docs/reports/verification/`.
- **Enforce mode:** CI or release lanes may use `-EnforceBackendSmoke` / `--enforce-backend-smoke` only when a fresh PASS proof is expected in the environment.
- **Umbrella GAP-069:** Remains **Open** for remaining continuous items (GHA full verify, mypy burn-down, deps split, etc.).

---

## 5. Rollback

Revert listed files in execution row; remove harness flags; remove tests and samples.
