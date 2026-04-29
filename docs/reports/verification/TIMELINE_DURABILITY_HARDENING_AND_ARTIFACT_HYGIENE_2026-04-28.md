# Timeline durability hardening + artifact hygiene — verification report

**Date:** 2026-04-28  
**Scope:** Post–D-001: explicit **session scoping** (`session_id` query, default `default`), **optimistic concurrency** (`revision` + compare-and-swap in SQLite), **test isolation** for JsonFileStore effect-chain artifacts, **`.gitignore`** safety net for `backend/data/stores/`.

---

## 1. Repo reality

- Builds on [TIMELINE_STATE_DURABILITY_D001_2026-04-28.md](TIMELINE_STATE_DURABILITY_D001_2026-04-28.md) (SQLite `session_timeline`).
- Mission: **local commit only** (no push), per plan stop condition.

## 2. Scope

| Area | Change |
|------|--------|
| Schema | `revision INTEGER NOT NULL DEFAULT 0` on `session_timeline`; `ALTER TABLE ... ADD COLUMN` for existing DBs (idempotent). |
| Repository | `TimelineConflictError`; `save_session_timeline_raw(..., expected_revision)` CAS `UPDATE ... WHERE session_id=? AND revision=?`; load returns `revision`; timeline JSON strips embedded `revision` before persist to avoid column/blob drift. |
| Routes | `TimelineState.revision`; `_hydrate(session_id)` → base revision; `_persist(..., session_id, expected_revision)`; **`session_id`** query on handlers; **409** `TIMELINE_CONFLICT` on stale write. |
| Hygiene | Session-scoped autouse fixture: `json_file_store._DATA_ROOT` → pytest temp; reset effect-chain / preset singletons; `.gitignore` `backend/data/stores/`. |

## 3. Root causes addressed

1. **Cross-session bleed:** All timeline traffic defaulted to one logical key; multi-project servers need **explicit `session_id`** (default preserves WinUI).
2. **Lost updates:** Read-modify-write without versioning allowed last writer to win; **revision + CAS** surfaces conflicts as **409**.
3. **Repo pollution:** Tests imported stores resolving under `backend/data/stores/`; fixture + gitignore stop accidental commits.

## 4. Tests

**Command:**

```text
python -m pytest tests/unit/backend/project/test_timeline_session_repository.py \
  tests/unit/backend/api/routes/test_timeline.py \
  tests/unit/backend/api/routes/test_timeline_mixdown.py \
  tests/unit/backend/infrastructure/test_json_file_store_isolation.py -q
```

**Result:** **53 passed** (2026-04-28).

## 5. Verification artifacts

| Check | Result | Path / notes |
|-------|--------|----------------|
| `python scripts/run_verification.py` | PASS | `.buildlogs/verification/last_run.json` |
| `.\scripts\verify.ps1 -Quick` | PASS | `artifacts/verify/20260428_190651/verification_report.md` |

## 6. Non-claims

- **Not** GAP-008 / **Slice 46** / new **`MainWindow*ShellBridge`** work.
- **Not** RHVoice or **`ENGINE_PARITY_MATRIX`** changes.
- **Not** a WinUI **runtime FULL PASS** or operator-attested playback claim.
- **409** only under genuine concurrent stale writes on the same `session_id`; default single-threaded clients unchanged.

## 7. Residual risks

- Clients must send current **`revision`** implicitly via reload-then-mutate pattern; explicit client header for revision is a future improvement.
- `ALTER TABLE` duplicate-column handling uses broad exception catch for idempotency (schema migration pattern only).

## 8. Rollback

- Revert the implementation commit; existing DBs retain `revision` column (harmless if code reverted with compatible reads).

## 9. Verdict

**PASS** — session-scoped durable timeline + CAS concurrency + test/store hygiene; **`verify.ps1 -Quick`** green at `artifacts/verify/20260428_190651/`.
