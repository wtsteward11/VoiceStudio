# Skip Debt Report (2026-03-11)

> **Source:** [SKIP_DEBT_CLEANUP_SUBPLAN.md](../../design/SKIP_DEBT_CLEANUP_SUBPLAN.md)  
> **Task:** 6.3 — Start Skip Debt Burn-Down with Categorization

## Baseline

| Metric | Count | Budget (test_skip_budget) |
|--------|-------|---------------------------|
| Collection skips | 46 | 65 |
| Module-level skips | 300 | 315 |
| Total skip calls | 1762 | 1765 |

## Classification

### Infrastructure (env/CI missing)
- GPU, backend not running, external service
- Examples: `skipif(not RVC_AVAILABLE)`, `skipif(not HAS_WEBSOCKETS)`, `skipif(not HAS_PSUTIL)`, `skipif(True, reason="Requires backend")`
- **Policy:** Allowed long-term with `reason=` and reference; add to requirements when feasible

### Product gaps (feature not implemented)
- Phase 6A/B/C/D plugins, capability_tokens, license_scanner, anomaly_detection, analytics
- Examples: `reason="Phase 6C license_scanner not implemented"`
- **Policy:** Must-fix when feature ships; track in backlog

### Flaky / defect-masking
- Temporarily disabled pending fix
- Examples: `reason="Manipulates module state - needs fixture refactoring"`
- **Policy:** Must-fix; assign owner and deadline

### Intentional (long-term)
- Nightly-only, manual run, engine not available (e.g. eSpeak-NG, Aeneas, Festival)
- **Policy:** Allowed with documented reason

## Policy Summary

1. **Allowed long-term skip:** Must have `reason=` and reference (e.g. VS-XXXX or subplan section)
2. **Must-fix:** Flaky, product gaps that block coverage; assign owner
3. **Infrastructure:** Document in test; add optional deps when feasible

## First Reduction Batch (2026-03-11)

- **Target:** Reduce module-level skips by consolidating duplicate `pytestmark = pytest.mark.skip` in engine tests where multiple modules share same condition
- **Action:** Document classification; no code changes in first slice (policy establishment)
- **Next slice:** Attack flaky/defect-masking category; convert skips to xfail where appropriate

## First Execution Batch (2026-03-11)

- **Action:** Policy compliance — convert bare `pytest.mark.skip("...")` to `pytest.mark.skipif(not HAS_X, reason="... (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")` in engine tests.
- **Files updated:** test_aeneas_engine.py, test_espeak_ng_engine.py, test_festival_flite_engine.py, test_streaming_engine.py, test_router.py, test_performance_metrics.py.
- **Result:** 6 modules now use conditional skipif with documented reason and subplan reference.

## Third Execution Batch (2026-03-11)

- **Action:** Flaky/defect-masking category — convert bare `pytest.mark.skip` to `pytest.mark.skipif(True, reason="... § Flaky")` in test_todo_panel.py.
- **Files updated:** tests/unit/backend/api/routes/_archived/test_todo_panel.py.
- **Result:** TestTodoEndpoints (Manipulates module state) and TestTodoExportEndpoint (Endpoint not implemented) now use policy-compliant skipif with subplan section reference.
