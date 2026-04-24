# Execution Row: Backend Readiness Smoke Freshness — GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-FRESHNESS-04

**Lane ID:** GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-FRESHNESS-04  
**Gap:** GAP-069 bounded slice 4 (Ops — deterministic smoke proof emission + freshness discovery)  
**Row type:** **proof-hardening** — *No production app/backend route behavior changes; scripts, tests, verification harness, governance only.*  
**Status:** CLOSED  
**Date frozen:** 2026-04-12  
**Date closed:** 2026-04-12  
**Owner Role:** Build Tooling (Role 2) / Overseer (Role 0)  
**Validator:** Skeptical Validator  
**Predecessor:** [GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_ENFORCEMENT_03_EXECUTION_ROW.md](GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_ENFORCEMENT_03_EXECUTION_ROW.md) (CLOSED)

---

## Context

Slice 3 established `PROOF_BACKEND_SMOKE_*.json` schema v1, `backend_smoke_freshness` in `run_verification.py`, and `-BackendSmoke` / `-EnforceBackendSmoke`. Normal full `verify.ps1` runs did **not** invoke the smoke writer, so `backend_smoke_freshness` often reported **MISSING** (advisory). Age for PASS proofs used file `st_mtime` instead of JSON `timestamp_utc`, which is unstable across checkout/copy.

This slice makes the **authoritative full-verify path** emit a smoke proof before Gate/Ledger and makes freshness age **prefer `timestamp_utc`**.

---

## Canonical proof contract (frozen)

| Item | Value |
|------|--------|
| **Directory** | `docs/reports/verification/` |
| **Filename pattern** | `PROOF_BACKEND_SMOKE_<YYYYMMDD_HHMMSS>.json` (UTC, from writer) |
| **Schema** | v1 (unchanged from slice 3) |
| **Freshness window** | 72 hours |
| **Age source (PASS)** | Primary: `timestamp_utc` in JSON; fallback: file `st_mtime` if missing/unparseable |
| **Authoritative producer** | `python scripts/ci/run_backend_smoke.py` |
| **Full verify integration** | `.\scripts\verify.ps1` (non-Quick) runs Backend Smoke Auto-Probe unless `-SkipSmoke` |

---

## Hard IN (Scope)

1. `scripts/run_verification.py` — `_backend_smoke_freshness_result`: prefer `timestamp_utc`; improved MISSING message; `age_source` in PASS/BLOCKED/FAIL output where applicable.
2. `scripts/verify.ps1` — `-SkipSmoke`; Backend Smoke Auto-Probe stage before Gate/Ledger (full verify, not Quick).
3. `tests/unit/test_backend_smoke_freshness_v4.py` — bounded tests for freshness logic (no live uvicorn required).
4. Governance: closure report, tracker, CANONICAL_REGISTRY, `.cursor/STATE.md`, openmemory.

## Hard OUT

- No new backend routes, engine, shell/UI, or installer changes.
- No change to `run_backend_smoke.py` proof schema or exit-code contract (writer frozen).

---

## Acceptance Contract

- [x] After `.\scripts\verify.ps1` (full, non-Quick, without `-SkipSmoke`), `backend_smoke_freshness` is **FRESH** or **BLOCKED** — not **MISSING** (assuming smoke stage completes and writes proof).
- [x] PASS proof staleness uses `timestamp_utc` when present.
- [x] `python -m pytest tests/unit/test_backend_smoke_freshness_v4.py` — **6+** PASS.
- [x] `python scripts/run_verification.py` — Overall PASS, `completion_guard` PASS.
- [x] `.\scripts\verify.ps1 -Quick` unchanged (no auto-smoke).

**Closure report:** [VOICESTUDIO_BACKEND_READINESS_SMOKE_FRESHNESS_LANE_CLOSURE_2026-04-12.md](../reports/verification/VOICESTUDIO_BACKEND_READINESS_SMOKE_FRESHNESS_LANE_CLOSURE_2026-04-12.md)

---

## Rollback

Revert harness/script/test edits; remove execution row reference from registry; restore prior `verify.ps1` stage order.
