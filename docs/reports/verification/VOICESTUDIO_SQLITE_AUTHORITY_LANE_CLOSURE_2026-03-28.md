# VOICESTUDIO SQLite Authority Lane — Closure Report

**Date:** 2026-03-28  
**Lane:** GOV-VOICESTUDIO-SQLITE-AUTHORITY-01  
**GAP:** GAP-016 — **Closed** (SQLite authoritative project metadata + tracks; Alembic not required per ADR-050)

## Summary

Project metadata and timeline tracks are persisted in SQLite (`projects`, `project_tracks`) with legacy disk JSON reduced to **import-only** on first list/get. WinUI unified shell save updates the backend only (no parallel `JsonProjectRepository` write). Schema changes use existing `initial_schema` + lifespan migrations (ADR-050).

## Slice → evidence matrix

| Slice | Evidence |
|-------|----------|
| 1 Authority map | `docs/design/GOV_VOICESTUDIO_SQLITE_AUTHORITY_01_EXECUTION_ROW.md` § Slice 1 |
| 2 Writes | `backend/project/management/project_store_service.py`, `backend/project/tracks/track_store.py`, `backend/api/optimization.py` `invalidate_api_response_cache` |
| 3 Reads | Same modules; routes unchanged contract in `backend/api/routes/projects.py` |
| 4 Migration | Strategy A in execution row; tests below |
| 5 Governance | This file; ADR-050; GAP-016 Closed in tracker |

## Automated tests (honest scope)

| Test path | Purpose |
|-----------|---------|
| `tests/unit/backend/services/test_project_store_migration.py` | Legacy `project.json` → SQLite; idempotent read; invalid disk schema; unsupported payload version |
| `tests/unit/backend/test_track_store.py` | CRUD + legacy `tracks/*.json` import |
| `tests/unit/backend/api/routes/test_projects.py` | Route contract (mocked store; unchanged) |

## Verification commands (lane closure)

Run from repo root (Windows):

1. `dotnet build VoiceStudio.sln -c Debug -p:Platform=x64`
2. `dotnet test src/VoiceStudio.App.Tests/VoiceStudio.App.Tests.csproj -c Debug -p:Platform=x64` (full suite recommended; lane closure used build + targeted Python tests below when iterating)
3. `python -m pytest tests/unit/backend/services/test_project_store_migration.py tests/unit/backend/test_track_store.py -q`
4. `python -m pytest tests/ci/ -q --randomly-seed=12345`
5. `.\scripts\verify.ps1 -Quick` → e.g. `artifacts/verify/20260328_151131/verification_report.md`
6. `python scripts/run_verification.py` → **completion_guard** PASS

## Limits / follow-ups

- **Alembic:** Explicitly out of scope (ADR-050).  
- **Jobs / marketplace / other SQLite stores:** Not consolidated in this lane (GAP-019 etc. remain).  
- **WinUI `JsonProjectRepository`:** Still registered for explicit file save/open paths; not used by `UnifiedProjectSaveHandler`.  
- **MSB3027 / test locks:** If full App.Tests fail intermittently, document and treat Quick verify as authoritative per project discipline.

## Related artifacts

- `docs/architecture/decisions/ADR-050-sqlite-project-authority-migrations.md`
- `docs/design/GOV_VOICESTUDIO_SQLITE_AUTHORITY_01_EXECUTION_ROW.md`
