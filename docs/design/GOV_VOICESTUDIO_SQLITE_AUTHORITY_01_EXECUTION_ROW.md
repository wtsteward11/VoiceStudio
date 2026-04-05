# GOV-VOICESTUDIO-SQLITE-AUTHORITY-01 — Execution row

**Lane ID:** `GOV-VOICESTUDIO-SQLITE-AUTHORITY-01`  
**Status:** Closed (2026-03-28)  
**Tracker:** **GAP-016 — Closed** (this lane)  
**Closure:** [VOICESTUDIO_SQLITE_AUTHORITY_LANE_CLOSURE_2026-03-28.md](../reports/verification/VOICESTUDIO_SQLITE_AUTHORITY_LANE_CLOSURE_2026-03-28.md)  
**Migrations ADR:** [ADR-050](../architecture/decisions/ADR-050-sqlite-project-authority-migrations.md)

## Frozen objective

SQLite is the **authoritative** store for **in-scope** core project metadata and timeline tracks, with explicit payload versioning, idempotent schema migration at startup, and **one** legacy import rule (Strategy A) for disk JSON. No silent dual-write to parallel authorities for the same fields.

## Hard IN

- Backend `/api/projects` metadata via `projects` table + `SqliteProjectRepository` / `ProjectStoreService`.
- Backend tracks via `project_tracks` table + `TrackStore` (`backend/project/tracks/track_store.py`).
- Legacy import: first list/get imports missing rows from `project.json` and `tracks/*.json`; thereafter SQLite only.
- WinUI shell save: backend update only (no parallel `JsonProjectRepository` write on unified save).
- Migrations: `MigrationRunner` + `initial_schema` (ADR-050); no Alembic unless a future ADR adds it.

## Hard OUT

- PanelHost GAP-007, export/transcript/metering/waveform/collab/marketplace/telemetry expansion.
- Reopening **Persistence Foundation** row for SQLite scope.
- Alembic as mandatory without ADR (explicitly rejected for this lane — see ADR-050).

---

## Slice 1 — Authority map (binary acceptance)

| Domain | Previous owner | SQLite owner after lane | Transitional / notes | Out of scope |
|--------|----------------|-------------------------|----------------------|--------------|
| Project metadata (API) | `ProjectStoreService` + `project.json` | `ProjectStoreService` → `SqliteProjectRepository` (`projects` table) | Legacy `project.json` = **import-only** | — |
| Tracks / clips (API) | `TrackStore` disk JSON | `TrackStore` → `project_tracks` table | Legacy `tracks/*.json` = **import-only** | — |
| Mixer / effects | HTTP clients from WinUI | Unchanged (backend services) | — | Phased |
| WinUI local project file | `JsonProjectRepository` | **Non-authoritative** for shell save | **Export/import / explicit file ops** via `FileOperationsHandler` | — |
| Workspace / layout | `PanelStateService` | N/A | — | **Out** |
| Crash / session recovery | Existing services | Unchanged | Documented as parallel, not replaced | — |

**Binary acceptance:** Each in-scope domain has exactly one authoritative SQLite-backed write path; legacy disk paths classified **import-only**; no ongoing dual metadata write from routes.

---

## Slice 2 — SQLite write authority

- Create/update/delete project metadata persist to SQLite; directories created for artifacts only.
- Track save/update/delete persist to `project_tracks`.
- `invalidate_api_response_cache()` on mutating project/track operations.
- **Binary acceptance:** Integration/unit tests prove writes hit SQLite; no second metadata writer on normal CRUD.

---

## Slice 3 — SQLite read / load authority

- List/get projects read SQLite (after optional legacy import).
- List/get tracks read SQLite (after optional legacy import per project).
- WinUI list/load continues to use backend API as truth (`TimelineProjectOpenHandler`, `IProjectsClient`).

---

## Slice 4 — Migration / compatibility (Strategy A)

**Strategy A — Import on first open/list:** Detect legacy artifacts; import into SQLite; subsequent reads use SQLite only.

- Idempotent import (re-run safe).
- Forward incompatibility: `vs_payload_version` in domain metadata > supported → `UnsupportedProjectPayloadError` → HTTP 422 on API.
- Desktop `PersistedProjectSchemaVersion` (`VoiceStudio.Core.Models.Project`) remains the **local file** schema; backend API record uses `schema_version` on `ProjectRecord` and metadata keys `vs_api_project_schema_version` / `vs_payload_version` for DB payload evolution.

**Tests:** `tests/unit/backend/services/test_project_store_migration.py`, `tests/unit/backend/test_track_store.py` (includes legacy track import).

---

## Slice 5 — Closure

See closure report for proof matrix and verification commands.

## Audit commands (honesty appendix)

```bash
rg "ProjectStoreService|TrackStore|JsonProjectRepository|SqliteProjectRepository" backend/api/routes backend/project -g "*.py"
rg "UnifiedProjectSaveHandler|IProjectRepository" src/VoiceStudio.App -g "*.cs"
```
