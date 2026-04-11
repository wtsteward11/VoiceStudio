# Lane closure: Backend Readiness Regression Guard — GOV-VOICESTUDIO-BACKEND-READINESS-REGRESSION-GUARD-01

**Date:** 2026-04-11  
**Row type:** proof-hardening (no production app/backend code paths changed)  
**Gap:** GAP-069 second bounded slice (adds CI/operator regression guard; umbrella GAP-069 remains **Open** for continuous items)

---

## Goal

Canonical `startup_decision.json` (schema v2) validation, pytest coverage, operator backend smoke script, and `startup_artifact_check` in `scripts/run_verification.py`.

---

## Runtime proof (Grade S / I / R)

| Grade | What was proven |
|-------|-----------------|
| **S** | `tests/unit/test_startup_artifact_checker.py` **10** PASS; `check_startup_artifact.py` JSON contract; `run_verification.py` wiring with argv list (Windows-safe). |
| **I** | Rolling verifier: `startup_artifact_check` **PASS** with `VOICESTUDIO_STARTUP_ARTIFACT_PATH` → repo sample `docs/reports/verification/samples/startup_decision_success_v2.json` (or live `%LocalAppData%\VoiceStudio\crashes\startup_decision.json` when present). |
| **R (inherited)** | Cites existing **Grade R** artifact **`docs/reports/verification/PROOF_GOLDEN_PATH_REAL_2026-04-10.json`** — **FRESH** at closure (`runtime_proof_staleness` advisory ~8h &lt; 72h policy window). No new `-RuntimeProof` run required for this proof-hardening lane. |

---

## SLO posture

- **`slo_baseline_freshness`**: **Advisory only** (GAP-015 slice 3); no threshold enforcement in this lane.
- **Timing budgets in checker** (`healthy_elapsed_ms` / `spawn_elapsed_ms`): **Advisory warnings only**; do not fail exit code.

---

## Commands and artifacts

| Command | Result |
|---------|--------|
| `python -m pytest tests/unit/test_startup_artifact_checker.py -v` | **10** PASS |
| `python scripts/ci/check_startup_artifact.py` (default or `--path` sample) | exit **0** on valid v2 success payload |
| `.\scripts\verify.ps1 -Quick` | **PASS** → `artifacts/verify/20260411_070615/` |
| `python scripts/run_verification.py` (with `VOICESTUDIO_STARTUP_ARTIFACT_PATH` set to sample for reproducibility) | **Overall PASS**; `[PASS] startup_artifact_check`; `completion_guard` PASS |
| `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` | **3337** PASS / **274** skipped / **1** failed (`ApplyEdit_Failure_PreservesEditingState` timeout) on first run; **PASS** on targeted re-run — treated as flaky; no app code changed in this lane |
| `python scripts/ci/run_backend_smoke.py` | **Optional** operator path (not required for lane closure); emits `PROOF_BACKEND_SMOKE_*.json` under `docs/reports/verification/` when run |

---

## Honest limits

- **`startup_artifact_check`** without `VOICESTUDIO_STARTUP_ARTIFACT_PATH` uses the default crashes path; if the file is missing, the checker **fails** (regression guard). Mitigations: run the shell once, set `VOICESTUDIO_STARTUP_ARTIFACT_PATH` to the committed sample, or set `VOICESTUDIO_SKIP_STARTUP_ARTIFACT_CHECK=1` to skip (documented in execution row).
- **Windows `shlex.quote` + string command** broke `--path`; fixed by using **argv list** for the checker when a path override is set.

---

## References

- Execution row (closed): [GOV_VOICESTUDIO_BACKEND_READINESS_REGRESSION_GUARD_01_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_REGRESSION_GUARD_01_EXECUTION_ROW.md)
- Prior slice: [VOICESTUDIO_BACKEND_READINESS_TRUTH_LANE_CLOSURE_2026-04-11.md](VOICESTUDIO_BACKEND_READINESS_TRUTH_LANE_CLOSURE_2026-04-11.md)
