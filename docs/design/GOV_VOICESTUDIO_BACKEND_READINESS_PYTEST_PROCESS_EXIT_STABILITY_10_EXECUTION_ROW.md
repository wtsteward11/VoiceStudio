# GOV-VOICESTUDIO-BACKEND-READINESS-PYTEST-PROCESS-EXIT-STABILITY-10 — Execution Row (GAP-069 Slice 10)

**Status:** Closed  
**Lane:** GAP-069 — Python Unit Tests **process exit** / teardown stabilization (pytest subprocess must exit after `tests/unit` completes)  
**Date:** 2026-04-13

## Problem statement

Full **`verify.ps1`** **Python Unit Tests** stage runs **5432+** tests successfully in ~**261s**, but the **pytest process does not exit**. The harness outer timeout (**1200s**) surfaces **`TIMED_OUT`**, blocking **`-ResumeFrom "Python Unit Tests"`** from advancing to Contract / Security / Gate stages. This is a **lifecycle / teardown** defect, not failing assertions.

## Root cause (architectural classification)

**Hybrid:**

1. **Primary:** [`EnhancedResourceManager`](../../app/core/runtime/resource_manager_enhanced.py) starts a **monitoring thread** in `__init__`; [`shutdown()`](../../app/core/runtime/resource_manager_enhanced.py) joins the thread, but [`tests/unit/core/runtime/test_resource_manager_enhanced.py`](../../tests/unit/core/runtime/test_resource_manager_enhanced.py) constructed multiple instances without calling **`shutdown()`**, leaving polling + **resource alert** activity after the test session’s logical completion.

2. **Secondary (interaction):** Post-summary **TensorFlow / HuggingFace** / trainer paths can still run during interpreter or fixture teardown; noisy monitoring loops compound wall-clock and log volume until the harness kills the tree.

## Intended contract

- Every test that constructs **`EnhancedResourceManager`** must **`shutdown()`** in teardown (explicit fixture or equivalent).
- **`pytest`** process **exits** after the last test with **exit code** matching test outcome (0 on all pass).
- **`-ResumeFrom "Python Unit Tests"`** completes the stage with **PASSED** (not **`TIMED_OUT`**) and continues downstream stages.

## Acceptance criteria

1. Python Unit Tests still pass (**5432+** in full `tests/unit` selection).
2. Pytest **process exits** cleanly (no 1200s hang after “passed” summary).
3. **`-ResumeFrom "Python Unit Tests"`** — stage **PASSED**, harness advances.
4. `python scripts/check_empty_catches.py` — PASS  
5. `python scripts/ci/check_ibackendclient_creep.py` — PASS  
6. `python scripts/run_verification.py` — PASS  
7. `.\scripts\verify.ps1 -Quick` — PASS  

## Hard IN scope

- Test / harness teardown fixes that restore **clean pytest process exit**  
- Minimal **`tests/conftest.py`** hooks if needed (session-finish sweep, env defaults for noisy ML stacks)  

## Hard OUT scope

- Changing passing test semantics  
- Broad engine / training refactors  
- Product feature work  

## Related closure artifact

[VOICESTUDIO_GAP069_PYTEST_PROCESS_EXIT_STABILITY_LANE_CLOSURE_2026-04-13.md](../reports/verification/VOICESTUDIO_GAP069_PYTEST_PROCESS_EXIT_STABILITY_LANE_CLOSURE_2026-04-13.md)
