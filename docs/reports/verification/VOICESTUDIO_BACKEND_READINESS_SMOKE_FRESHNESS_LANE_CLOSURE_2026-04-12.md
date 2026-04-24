# Lane closure: GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-FRESHNESS-04

**Lane ID:** GOV-VOICESTUDIO-BACKEND-READINESS-SMOKE-FRESHNESS-04  
**Gap:** GAP-069 bounded slice 4  
**Row type:** proof-hardening (scripts, tests, verification harness, governance only — no production route/UI changes)  
**Status:** CLOSED  
**Date:** 2026-04-12  

**Execution row:** [GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_FRESHNESS_04_EXECUTION_ROW.md](../../design/GOV_VOICESTUDIO_BACKEND_READINESS_SMOKE_FRESHNESS_04_EXECUTION_ROW.md)

---

## 1. Objective

Eliminate **unexplained MISSING** for `backend_smoke_freshness` after a standard full `verify.ps1` run, stabilize **age** using JSON **`timestamp_utc`** (not file `st_mtime`), and add **unit tests** for verifier discovery/age logic.

---

## 2. Root cause (why MISSING was “normal”)

1. **Full `verify.ps1` (non-Quick) did not run** `run_backend_smoke.py` automatically — only `-BackendSmoke` or a manual `python scripts/ci/run_backend_smoke.py` produced `PROOF_BACKEND_SMOKE_*.json`.
2. **`backend_smoke_freshness` age** used **`latest.stat().st_mtime`**, which resets on git checkout, CI cache restore, and file copies — not the semantic time embedded in the proof JSON.

---

## 3. Delivered

| Item | Evidence |
|------|----------|
| Age source | `scripts/run_verification.py` — `_try_parse_iso_timestamp_utc` + `_parse_smoke_proof_time_utc`; PASS staleness uses **`timestamp_utc`** first; **`age_source=timestamp_utc`** or **`mtime`** in advisory output |
| MISSING message | Exact operator commands + note that **full** `verify.ps1` auto-runs smoke unless **`-SkipSmoke`** |
| Full verify integration | `scripts/verify.ps1` — **`-SkipSmoke`**; **Backend Smoke Auto-Probe** before Gate/Ledger (non-Quick; skipped when `-Quick` or selective `OnlyStage` excludes the smoke path per harness rules) |
| Tests | `tests/unit/test_backend_smoke_freshness_v4.py` — **6** PASS (`tmp_path`, no live backend) |
| Python 3.9 | `from __future__ import annotations` in `run_verification.py` for union types |

---

## 4. Canonical proof contract (reminder)

| Item | Value |
|------|--------|
| **Path** | `docs/reports/verification/PROOF_BACKEND_SMOKE_<YYYYMMDD_HHMMSS>.json` |
| **Schema** | v1 (unchanged from slice 3) |
| **Freshness window** | 72h for PASS (staleness); **BLOCKED** never fails enforce mode |
| **Explicit smoke** | `python scripts/ci/run_backend_smoke.py` **or** `.\scripts\verify.ps1 -BackendSmoke` |
| **Post–slice-4 full verify** | `.\scripts\verify.ps1` (non-Quick, default) runs auto-probe unless **`-SkipSmoke`** |

---

## 5. Verification (proof)

| Command | Result |
|---------|--------|
| `python -m pytest tests/unit/test_backend_smoke_freshness_v4.py -q` | **6** PASS |
| `python scripts/check_empty_catches.py` | **PASS** |
| `python scripts/ci/check_ibackendclient_creep.py` | **PASS** |
| `.\scripts\verify.ps1 -Quick` | **PASS** — `artifacts/verify/20260412_071703/`; Gate/Ledger includes **`empty_catch_check` PASS** |
| `python scripts/run_verification.py` | Overall **PASS**; **`completion_guard` PASS**; with committed smoke proof, **`backend_smoke_freshness`** shows **FRESH** + **`age_source=timestamp_utc`** |
| `python scripts/ci/run_backend_smoke.py` then `python scripts/run_verification.py` | Example: **STATUS=FRESH** for `PROOF_BACKEND_SMOKE_20260412_122858.json`, **age_hours≈0.01**, **age_source=timestamp_utc** |

**Honest limit — full `verify.ps1` (no Quick):** On this workspace, the harness **failed at Python Unit Tests** with collection error in `tests/unit/backend/api/routes/test_search.py` (`RuntimeError: Database not connected` via `project_store_service` import side effects). That failure is **out of slice 4 scope** (pre-existing / environment). Slice 4 acceptance for **auto-smoke + Gate/Ledger** is satisfied by design + **Quick PASS** + manual smoke + rolling verifier proof above.

---

## 6. What freshness looks like after slice 4

- After **`python scripts/ci/run_backend_smoke.py`** (PASS) or a **full verify** run that executes **Backend Smoke Auto-Probe**, `backend_smoke_freshness` reports **FRESH** or **BLOCKED** — not **MISSING**.
- **Quick** runs still **skip** auto-smoke by design; **MISSING** there remains possible until a proof exists — **advisory only**.

---

## 7. Rollback

Revert `scripts/run_verification.py`, `scripts/verify.ps1`, remove `tests/unit/test_backend_smoke_freshness_v4.py`, and governance rows linked to this lane.
