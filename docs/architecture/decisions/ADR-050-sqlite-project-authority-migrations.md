# ADR-050: SQLite Project Authority and Schema Migrations (No Alembic)

**Status:** Accepted  
**Date:** 2026-03-28  
**Decision Makers:** Engineering (GOV-VOICESTUDIO-SQLITE-AUTHORITY-01)

## Context

GAP-016 required a single authoritative store for core project-shaped state. The codebase already ran `backend.data.migrations.MigrationRunner` (aiosqlite) and `backend.infrastructure.migrations.initial_schema.run_migrations` at API lifespan startup. Alembic is not wired into that path.

## Options Considered

1. **Introduce Alembic** — Familiar for teams expecting SQL-first migrations; adds dependency, parallel migration story, and cutover work.
2. **Extend existing MigrationRunner + `initial_schema`** — Aligns with current startup; idempotent `CREATE TABLE IF NOT EXISTS`; JSON blob columns match existing `SqliteProjectRepository` pattern.
3. **Ad-hoc DDL in services** — Rejected (scattered, untestable).

## Decision

Use **option 2**: versioned schema changes go through **`initial_schema.run_migrations`** (and, when needed, **`MigrationRunner`** for the `backend.data` surface), not Alembic, until a future ADR explicitly adopts Alembic with a migration cutover plan.

Project metadata authority uses the existing **`projects`** table and **`SqliteProjectRepository`**. Timeline tracks use a new **`project_tracks`** table created by `initial_schema`.

## Consequences

**Positive:** One migration story at startup; no new Python dependency; matches existing integration tests (`tests/integration/test_sqlite_repositories.py`).

**Negative:** JSON blob evolution requires disciplined payload versioning (e.g. `vs_payload_version` in domain metadata) and tests for forward incompatibility.

## Related

- `docs/design/GOV_VOICESTUDIO_SQLITE_AUTHORITY_01_EXECUTION_ROW.md`
- `backend/api/lifecycle.py` (startup migrations)
- `backend/infrastructure/migrations/initial_schema.py`
