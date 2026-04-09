# VOICESTUDIO — GAP-015 Percentile SLO baselines (slice 3) — Lane closure

**Lane ID:** `GOV-VOICESTUDIO-GAP015-PERCENTILE-SLO-BASELINES-03`  
**Tracker:** **GAP-015** — **Closed** (slices 1–3 complete)  
**Execution row:** [GOV_VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_EXECUTION_ROW.md) — **Closed**  
**Closure date:** 2026-04-08  
**Git (proof seal):** Resolve with `git log -1 --format=%H -- docs/reports/verification/VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_LANE_CLOSURE_2026-04-08.md` after merge.

---

## 1. Goal

Record **machine-readable percentile samples** (p50 / p95; p99 when n ≥ 100) for **three** canonical ASGI workflows, emit **`slo_baselines.json` schema v1** beside `runtime_proof.json`, and surface **advisory** freshness in `run_verification.py` — **without** dashboards, telemetry warehouses, threshold enforcement, or route/service behavior changes.

---

## 2. What shipped

| Deliverable | Location |
|-------------|----------|
| Execution row + allowlist | `docs/design/GOV_VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_EXECUTION_ROW.md` |
| Timing sample append (env `VOICESTUDIO_SLO_TIMING_JSON`) | `tests/ci/slo_timing_io.py` |
| `perf_counter` hooks (health + synthesize + export rejection) | `tests/ci/test_golden_loop_smoke_real.py`, `tests/ci/test_runtime_proof_training_export.py` |
| Aggregator + artifact writer | `scripts/ci/write_slo_baseline_proof.py` |
| `-RuntimeProof` wiring | `scripts/verify.ps1` |
| Advisory freshness (72h, never hard-fail) | `scripts/run_verification.py` → `slo_baseline_freshness` |
| Governance + tracker | `TEST_CLASSIFICATION.md`, `EXECUTION_ROW_DISCIPLINE.md`, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `openmemory.md` |
| Unit tests | `tests/unit/test_runtime_proof_staleness_enforcement.py` (`_slo_baseline_freshness_result`) |

---

## 3. Workflows (hard limit: three)

| ID | Endpoint | Test surface |
|----|----------|--------------|
| `backend_readiness` | `GET /api/health` | `test_golden_loop_smoke_real.py` |
| `canonical_synthesis` | `POST /api/voice/synthesize` | `test_golden_loop_smoke_real.py` (client timing includes failed/503 responses) |
| `training_export_rejection` | `POST /api/training/export` | `test_runtime_proof_training_export.py` (2 samples per run) |

---

## 4. Proof seal — actual results (2026-04-08)

Workspace HEAD at runtime proof run: **`f4893d2a4b641ac5aa8033717dbd4128d61e85f0`** (pre-commit; re-resolve after ship).

| Surface | Command | Outcome | Artifact / evidence |
|---------|---------|---------|---------------------|
| **Quick** | `.\scripts\verify.ps1 -Quick` | **PASS** (exit **0**) | `artifacts/verify/20260408_205814/verification_report.md`; slice 3 harness did not regress Quick gates. |
| **Runtime proof** | `.\scripts\verify.ps1 -RuntimeProof` | **FAIL** (exit **1**) — honest Grade R | `artifacts/verify/20260408_205616/runtime_proof.json` — synthesis pytest **FAIL** (HTTP **503**, engines unavailable in this environment); training export **PASS** (2/2). |
| **SLO baselines** | (same run, writer after pytest) | **Emitted** | `artifacts/verify/20260408_205616/slo_baselines.json` — **schema_version** 1, **environment** `asgi_transport`, **baseline_policy** `advisory`; all three workflows **RECORDED** (health n=1; synthesis n=1 captures 503-path latency ~7.28s client-side; training_export_rejection n=2). **evidence_fingerprint** `6ae59465ccaf714edb665817bfb74d108c69b6d99b4f014b81022a85fbb39e33`. |
| **Rolling verifier** | `python scripts/run_verification.py --skip-guard` | **PASS** (exit **0**) | `.buildlogs/verification/last_run.json` — **`[ADVISORY] slo_baseline_freshness`** **STATUS=FRESH** for `artifacts\verify\20260408_205616\slo_baselines.json` (age_hours≈0). |
| **Unit — staleness + SLO freshness** | `python -m pytest tests/unit/test_runtime_proof_staleness_enforcement.py -q` | **PASS** | **4** tests (slice 2 staleness + slice 3 `slo_baseline_freshness`). |

**Interpretation:** `runtime_proof.json` **FAIL** and `slo_baselines.json` **RECORDED** for synthesis are **consistent** — the sample records client latency for the synthesize POST even when the API returns **503** (no threshold enforcement; advisory baselines only).

---

## 5. Hard OUT (verified)

- No dashboards, telemetry warehouse, Prometheus/Grafana, or SLO **gating** in CI.  
- No new pytest modules beyond the two edited CI tests + non-test helper `slo_timing_io.py`.  
- No `detect_performance_regression.py` integration in this slice.

---

## 6. Rollback

`git revert` of the slice 3 commit set (harness, tests, docs).
