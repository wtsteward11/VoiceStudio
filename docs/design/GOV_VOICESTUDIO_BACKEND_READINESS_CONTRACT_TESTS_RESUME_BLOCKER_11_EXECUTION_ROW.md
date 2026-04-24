# GOV-VOICESTUDIO-BACKEND-READINESS-CONTRACT-TESTS-RESUME-BLOCKER-11 — Execution Row (GAP-069 Slice 11)

**Status:** Closed (2026-04-13)  
**Lane:** GAP-069 — **Contract Tests** failure on **post–Slice-10** resumed non-Quick harness (`-ResumeFrom "Python Unit Tests"`) — **resolved**  
**Date opened:** 2026-04-13  
**Date closed:** 2026-04-13

## Problem statement (frozen)

After **GAP-069 Slice 10** (Python Unit Tests stage certification-safe exit), the **authoritative** certification path is:

1. `.\scripts\verify.ps1 -StopAfterStage "C# Unit Tests - Other"` → checkpoint  
2. `.\scripts\verify.ps1 -ResumeFrom "Python Unit Tests"` → downstream stages  

The **2026-04-13** initial resumed run **`artifacts/verify/20260413_124949/`** **passed** **Python Unit Tests** and **failed** **Contract Tests** (pytest exit **1**, harness fail-fast). **Follow-on fix:** `custom_openapi()` in **`backend/api/main.py`** held a stale **`_openapi_schema_generated`** flag so OpenAPI did not regenerate after cache clear; the contract fixture could fall back to a **static** OpenAPI spec **missing** routes such as **`GET /api/voice/voices`**. **Closure proof:** `pytest tests/contract/` → **238 passed**, **5 skipped**; **`verify.ps1 -OnlyStage "Contract Tests"`** **PASS**. A later resumed run (**`artifacts/verify/20260413_143616/`**) advanced **past** Contract (+ Security, Backend Integration) and **failed** at **UI Smoke Tests** (600s timeout) — tracked as **Slice 12**, not this row.

## Proof bundle (primary)

| Artifact | Path |
|----------|------|
| Checkpoint (pre-Python seam) | `artifacts/verify/20260413_124401/` + `artifacts/verify/latest/checkpoint.json` |
| Resumed run (truth) | `artifacts/verify/20260413_124949/` |
| Harness summary | `artifacts/verify/20260413_124949/summary.json` — **`overall_status`: `FAILED`** |
| Contract Tests log | `artifacts/verify/20260413_124949/logs/contract_tests.log` |
| Python Unit Tests log | `artifacts/verify/20260413_124949/logs/python_unit_tests.log` |

## Resumed run results (frozen)

- **Python Unit Tests:** **PASSED** (~**418.6s** wall); **5432 passed** in ~**297s** (per pytest summary); Slice 10 **HARNESS NOTE** may apply (junit-green child termination after grace).
- **Contract Tests:** **FAILED** (~**45s**); **17 failed**, **221 passed**, **5 skipped** (per `contract_tests.log` tail).

### Representative failure classes (from contract log)

1. **OpenAPI / request bodies:** `test_post_endpoints_have_request_body` — POST routes without `requestBody` in OpenAPI for listed paths.  
2. **Voice gateway contract:** `IVoiceGateway` missing `GET /api/voice/voices`.  
3. **Engine manifests:** duplicate `engines/audio/*` vs `engines/*` trees; malformed / incomplete manifests (`engine_id`, `type`, `capabilities`, dependency version types, `entry_point` shape).  
4. **WebSocket routes (contract harness):** `/ws/realtime`, `/ws/events`, `/ws/plugins` returned **404** in contract test client (registration / app wiring under test).  
5. **Shared JSON schema refs:** unresolved `production_chain.schema.json`, `quality_threshold_policy.schema.json`; `engine_manifest_v3` array `items` gap.

## Closure proof (met)

1. **`pytest tests/contract/`** — **238 passed**, **5 skipped** (representative direct proof).  
2. **`verify.ps1 -OnlyStage "Contract Tests"`** — **PASS** (harness Contract stage).  
3. Resumed path **after fix** advanced **past** Contract on **`artifacts/verify/20260413_143616/`** (see Slice 12 report for downstream **UI Smoke** timeout).  

### Root cause (summary)

- **`custom_openapi()`** stale **`_openapi_schema_generated`** prevented OpenAPI regeneration when **`app.openapi_schema`** was cleared; contract tests then saw incomplete/static spec (e.g. missing **`/api/voice/voices`**).  
- Accompanying work: engine manifest / schema / route registration alignment per contract suite (see git history for **`backend/api/main.py`**, contract tests, shared schemas, engine stubs/manifests as applicable).

## Hard IN scope (historical)

- Contract test failures, OpenAPI alignment, engine manifest hygiene (dedupe / fix paths), schema file completeness, FastAPI route registration as exercised by `tests/contract/`.  

## Hard OUT scope (historical)

- Re-litigating Slice 10 (closed).  

## Related report

[VOICESTUDIO_GAP069_CONTRACT_TESTS_POST_RESUME_BLOCKER_2026-04-13.md](../reports/verification/VOICESTUDIO_GAP069_CONTRACT_TESTS_POST_RESUME_BLOCKER_2026-04-13.md) — updated with **Resolution** section.
