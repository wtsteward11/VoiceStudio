# Skip Debt Cleanup Subplan (v1.2)

> **Source:** [DEFERRED_V1_2.md](../governance/DEFERRED_V1_2.md)  
> **Status:** Executable subplan for v1.2

---

## Scope

Remove or consolidate skip markers in pytest tests. Reduce skip count to fewer than 200 by v1.2 ship.

## First Slice Complete (2026-03-11)

- Regenerated baseline: 46 collection skips, 300 module-level, 1762 total skip calls.
- Classification and policy documented in [SKIP_DEBT_REPORT_2026-03-11.md](../reports/verification/SKIP_DEBT_REPORT_2026-03-11.md).
- Policy: allowed long-term (reason= + reference), must-fix (flaky/product gaps), infrastructure (document).
- First reduction batch: policy establishment; code reduction in next slice.

## First Execution Batch Complete (2026-03-11)

- Converted 6 engine test modules from bare `pytest.mark.skip` to `pytest.mark.skipif(not HAS_X, reason="... (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)")`.
- Files: test_aeneas_engine, test_espeak_ng_engine, test_festival_flite_engine, test_streaming_engine, test_router, test_performance_metrics.

## Second Execution Batch Complete (2026-03-11)

- Converted 4 modules from bare `pytest.mark.skip` to `pytest.mark.skipif` with subplan reference.
- Files: test_buffer_manager.py, test_temp_file_manager.py, test_response_cache.py, test_inference_benchmarks.py.
- Result: Policy-compliant skipif with documented reason and subplan reference.

## Third Execution Batch Complete (2026-03-11)

- Converted flaky/defect-masking and product-gap skips in test_todo_panel.py to policy-compliant skipif.
- Files: tests/unit/backend/api/routes/_archived/test_todo_panel.py.
- Changes: TestTodoEndpoints (Manipulates module state) → skipif(True, reason="... § Flaky"); TestTodoExportEndpoint (Endpoint not implemented) → skipif(True, reason="... § Product gaps").
- Result: Policy-compliant skipif with subplan section reference.

## Fourth Execution Batch Complete (2026-03-11)

- Added `(SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)` to infrastructure skipif reasons missing subplan reference.
- Files: test_memory_profiling.py, test_signer.py (supply_chain), test_websocket_realtime.py, test_load.py, test_verifier.py, test_signer.py (plugin_packaging).
- Result: Policy compliance improved; skip behavior unchanged.

## Fifth Execution Batch Complete (2026-03-11)

- Converted 9 Phase 6X product-gap modules from bare `pytest.mark.skip` to `pytest.mark.skipif(True, reason="... § Product gaps")`.
- Files: test_privacy_checker.py, test_experimental_loader.py, test_analytics.py, test_capability_tokens.py, test_host_apis.py, test_code_review.py, test_developer_portal.py, test_license_scanner.py, test_anomaly_detection.py.
- Converted 3 test_main.py skips to `skipif(True, reason="... § Flaky")`.
- Result: Policy-compliant skipif with subplan section reference.

## Sequence

1. **Regenerate skip report**
   - `python -m pytest tests/ --co -q 2>&1 | Select-String "skipped" > skip_report.txt` (Windows)
   - Or: `python -m pytest tests/ --co -q 2>&1 | grep "skipped" > skip_report.txt` (Unix)
   - Parse output for `N skipped` in session summary

2. **Cluster skips by category**
   - **Infrastructure:** CI/env missing (e.g. GPU, backend not running, external service)
   - **Product gaps:** Feature not implemented, known limitation
   - **Flaky/disabled:** Temporarily disabled pending fix
   - **Intentional:** Long-term skip with documented reason (e.g. nightly-only)

3. **Define policy**
   - **Allowed long-term skip:** Document in test or `pytest.ini`; must have `reason=` and reference (e.g. VS-XXXX)
   - **Must-fix:** Flaky, product gaps that block coverage; assign owner and deadline

4. **Burn-down execution**
   - Target: 312 → 200 (or current baseline → 200)
   - Prioritize: must-fix first, then product gaps, then infrastructure where feasible

## Proof Criteria

- Skip count ≤ 200 after execution
- Each remaining skip has documented reason
- `skip_report.txt` regenerated and committed with subplan completion

## Non-Goals

- Zero skips (not required for v1.2)
- Fixing root cause of every infrastructure skip (e.g. GPU tests without GPU)

## Owner

TBD (assign in task brief when work starts)
