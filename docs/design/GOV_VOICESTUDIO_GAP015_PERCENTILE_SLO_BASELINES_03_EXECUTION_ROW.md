# GOV-VOICESTUDIO-GAP015-PERCENTILE-SLO-BASELINES-03 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-GAP015-PERCENTILE-SLO-BASELINES-03`  
**Tracker:** **GAP-015** — **Closed** (umbrella closed after slice 3; see tracker + closure report)  
**Row type:** `measurement` — **No production app route/service behavior changes** (instrumentation in CI tests + proof scripts only).

**Status:** Closed (see [closure report](../reports/verification/VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_LANE_CLOSURE_2026-04-08.md)).

---

## Runtime proof requirement

- [x] **Grade R proof** — This lane **extends** the Grade R runtime-proof bundle with a sibling **`slo_baselines.json`** artifact (schema v1). It does not change synthesis/training product semantics.

---

## Problem statement

Slices 1–2 delivered proof taxonomy, `verify.ps1 -RuntimeProof`, schema v2 `runtime_proof.json`, and enforceable golden-path staleness. The umbrella’s **product SLO definitions** (recorded latency baselines for canonical workflows) were still unmet.

---

## In scope

- **Three** measured workflows only (hard limit):
  1. **Backend readiness** — `GET /api/health` (`tests/ci/test_golden_loop_smoke_real.py`).
  2. **Canonical synthesis** — `POST /api/voice/synthesize` after profile + consent setup (same test).
  3. **Training export rejection** — `POST /api/training/export` (`tests/ci/test_runtime_proof_training_export.py`).
- `time.perf_counter()` client-side timings; environment documented as **ASGI in-process** (`environment: asgi_transport` in artifact).
- `scripts/ci/write_slo_baseline_proof.py` — reads `slo_timing_samples.json`, writes **`slo_baselines.json`** (schema v1) next to `runtime_proof.json` under `artifacts/verify/<ts>/`.
- `verify.ps1 -RuntimeProof` — sets `VOICESTUDIO_SLO_TIMING_JSON`, runs both pytest invocations, then invokes the proof writer.
- `scripts/run_verification.py` — **`slo_baseline_freshness`** advisory row (same spirit as `runtime_proof_staleness`; **never** hard-fails in slice 3).
- Governance: `TEST_CLASSIFICATION.md`, `EXECUTION_ROW_DISCIPLINE.md`, `PROFESSIONAL_GAP_TRACKER.md`, `CANONICAL_REGISTRY.md`, `openmemory.md`, `.cursor/STATE.md`.

## Hard OUT

- No dashboards, visualization, telemetry warehouse, time-series DB, Prometheus, Grafana, or monitoring stack.
- No threshold enforcement — `baseline_policy: advisory`; baselines **observe**, they do **not** gate CI.
- No route/service behavior changes beyond **timing instrumentation** in the two allowed CI tests.
- No new **test modules** (only edits to `test_golden_loop_smoke_real.py` and `test_runtime_proof_training_export.py`). Supporting **non-test** helper `tests/ci/slo_timing_io.py` is allowed for JSON append I/O.
- No `detect_performance_regression.py` integration (future convergence).
- No fix for `test_search.py` DB connectivity (separate tracker).

## Allowlist

```
scripts/verify.ps1
scripts/run_verification.py
scripts/ci/write_slo_baseline_proof.py
tests/ci/slo_timing_io.py
tests/ci/test_golden_loop_smoke_real.py
tests/ci/test_runtime_proof_training_export.py
docs/design/GOV_VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_EXECUTION_ROW.md
docs/reports/verification/VOICESTUDIO_GAP015_PERCENTILE_SLO_BASELINES_03_LANE_CLOSURE_2026-04-08.md
docs/governance/TEST_CLASSIFICATION.md
docs/governance/EXECUTION_ROW_DISCIPLINE.md
docs/design/PROFESSIONAL_GAP_TRACKER.md
docs/governance/CANONICAL_REGISTRY.md
openmemory.md
.cursor/STATE.md
```

---

## Acceptance

- [x] `slo_baselines.json` schema v1 emitted beside `runtime_proof.json` when `-RuntimeProof` runs (including partial samples if a pytest leg fails).
- [x] All three workflow IDs appear in the artifact; `p50`/`p95`/`sample_count` populated when samples exist; `p99` null when `sample_count < 100`.
- [x] `run_verification.py` prints **`[ADVISORY] slo_baseline_freshness`** and never fails the run on that row alone.
- [x] Tracker **GAP-015** marked **Closed** with closure report proof seal.
