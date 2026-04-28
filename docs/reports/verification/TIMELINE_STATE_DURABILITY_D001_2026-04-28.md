# Timeline session state durability (D-001) — verification report

**Date:** 2026-04-28  
**Scope:** Backend `/api/timeline/*` session state (WinUI `TimelineUseCase`) persisted to SQLite; optional D-003 consent messaging.

---

## 1. Repo Reality

- Smoke documentation push (`d62a75f8` narrative in plan) was completed earlier in the lane; **implementation commit is intended local-only** (no push), per mission.
- Workspace rules forbid editing the attached plan file (honored).

## 2. Scope

- **D-001:** Replace authoritative module-level timeline globals with SQLite-backed session storage shared across Uvicorn workers (same DB file as the rest of the backend).
- **D-003 (optional):** Clearer `HTTP 403` detail from `require_synthesis_clearance` when voice consent is missing (string detail only; status unchanged).

## 3. Root Cause (D-001)

- `/api/timeline/*` previously relied on in-process globals; **each Uvicorn worker had isolated memory**, so routing between workers broke coherence for GET/POST.

## 4. Design / Persistence

- Table **`session_timeline`** (`session_id` PK, JSON columns for timeline + undo/redo stacks, `updated_at`), created via **`initial_schema`** migration pattern.
- Module **`backend/project/timeline/session_repository.py`**: load/save/delete keyed by session id (`default` for current API).
- **`timeline.py`**: hydrate → mutate → persist on each relevant handler; read-through/write-through SQLite for correctness (v1).
- **`_ensure_db_connected`**: if the singleton adapter is not connected (e.g. **`TestClient`** without app lifespan), **`connect()`** is invoked before timeline DB access so security tests do not raise **`RuntimeError: Database not connected`**.

## 5. Files Touched (conceptual)

- `backend/infrastructure/migrations/initial_schema.py` — `session_timeline` DDL.
- `backend/project/timeline/session_repository.py` — persistence + lazy connect.
- `backend/api/routes/timeline.py` — repository-backed state.
- `backend/api/dependencies.py` — D-003 consent detail text.
- Tests: `tests/unit/backend/api/routes/test_timeline.py`, `test_timeline_mixdown.py`, `tests/unit/backend/project/test_timeline_session_repository.py`, `tests/unit/test_synthesis_policy.py`.

## 6. Tests

- **Scoped unit:**  
  `tests/unit/backend/api/routes/test_timeline.py`  
  `tests/unit/backend/api/routes/test_timeline_mixdown.py`  
  `tests/unit/backend/project/test_timeline_session_repository.py`  
  `tests/unit/test_synthesis_policy.py`  
  **Result:** **49 passed** (run 2026-04-28).
- **Note:** Full `pytest tests -k timeline` may collect broken unrelated modules in this workspace (`tests/integration/test_model_drift.py`, `tests/ui/test_smoke_workflows.py` import errors); prefer explicit paths or CI configuration for green runs.

## 7. Verification Artifacts

- **`python scripts/run_verification.py`:** PASS — `.buildlogs/verification/last_run.json`
- **`.\scripts\verify.ps1 -Quick`:** PASS — **`artifacts/verify/20260428_174825/`** (`verification_report.md` under that folder); gate/ledger PASS embedded in run log.

## 8. D-002

- **Script-only / OpenAPI field naming** — **no product code change** in this commit.

## 9. D-003

- **Fixed:** Expanded plain-string **`detail`** on synthesis clearance **`HTTP 403`** with actionable consent/grant/`consent_id` guidance; tests assert substring **`grant`** + **`consent`**.

## 10. Non-Claims

- **Not** a GAP-008 / **Slice 46** / **`MainWindow*ShellBridge`** deliverable.
- **Not** RHVoice or **`ENGINE_PARITY_MATRIX`** updates.
- **Not** claiming WinUI **runtime FULL PASS** or operator-attested playback from this backend-only durability lane.

## 11. Residual Risks

- Large JSON blobs per request (v1 correctness first); optimize with caching later if needed.
- **`ON CONFLICT`** upsert is SQLite-oriented; if production PostgreSQL uses different SQL, migrate with dialect branching.

## 12. Rollback

- Revert the implementation commit; optionally `DROP TABLE session_timeline` on dev DBs (non-production).

## 13. Final Verdict

**Fixed** — D-001 persistence + lazy DB connect for tests; D-003 messaging improved; **`verify.ps1 -Quick`** PASS with artifacts above. Implementation remains **commit locally without push** until product publishes.
