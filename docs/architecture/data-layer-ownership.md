# VoiceStudio Data Layer Architecture Boundary

**Phase**: Phase 2B (integration/reintroduce-v1.0.2)

## Overview

The VoiceStudio backend has two separate persistence systems that coexist in the same
SQLite database file (`data/voicestudio.db` / `:memory:` for tests). They serve different
concerns and must not be conflated.

## System 1: Infrastructure Repositories (JSON-Blob Pattern)

**Location**: `backend/infrastructure/repositories/` + `backend/infrastructure/adapters/database.py`
**Pattern**: DDD Aggregate pattern. Entity data stored as JSON blob in a `data TEXT` column.
**Connection**: Uses `DatabaseAdapter` singleton from `backend.infrastructure.adapters.database`.
**Migration**: `backend/infrastructure/migrations/initial_schema.py` (inline, idempotent DDL).
**Table Ownership**:
- `projects` (id, created_at, updated_at, data JSON)
- `voice_profiles` (id, created_at, updated_at, data JSON)
- `audio_clips` (id, created_at, updated_at, data JSON)
- `jobs` (id, namespace, created_at, updated_at, data JSON)

**Purpose**: Implements `backend.domain.repositories.*` abstract interfaces for the four
primary DDD aggregates (Project, VoiceProfile, AudioClip, Job). The JSON blob
approach allows aggregate schema evolution without DDL migrations.

## System 2: Data Layer Repositories (Flat-Column Pattern)

**Location**: `backend/data/repositories/` + `backend/data/repository_base.py`
**Pattern**: API integration pattern. Data stored in normalised, flat columns.
**Connection**: Each `BaseRepository` manages its own `aiosqlite` connection.
**Migration**: `backend/data/migrations/migration_runner.py` (versioned, tracked, rollback-capable).
**Table Ownership**:
- `job_history` (20+ flat columns for job tracking)
- `training_jobs`, `training_logs`, `training_quality_history` (ML training state)
- `deepfake_jobs` (deepfake processing state)
- `sessions` (user authentication sessions)
- `transcriptions` (transcription cache)
- `pipeline_sessions` (realtime pipeline state)
- `abx_sessions`, `abx_results` (ABX evaluation sessions)
- `library_assets`, `library_folders` (library persistence)

**Purpose**: Replaces in-memory dicts that caused data loss on backend restart.
The flat-column approach enables SQL-level filtering, sorting, and aggregation
without JSON path expressions.

## Important Notes

1. The `jobs` table (System 1) and `job_history` table (System 2) are DIFFERENT tables
   serving different purposes. `jobs` stores DDD Job aggregates. `job_history` stores
   API job tracking records with rich lifecycle columns. Do not confuse them.

2. Both systems share the same physical SQLite file. Schema changes in either system
   must be made through the appropriate migration mechanism.

3. System 2's `BaseRepository.get_all()` has a known SQL injection risk in the
   `order_by` parameter -- do NOT pass user-controlled values as `order_by`.
   See: `backend/data/repository_base.py` line 326.

4. All test code MUST use `ConnectionConfig(database_type=DatabaseType.MEMORY)`
   or pass `connection_string='sqlite:///:memory:'` to avoid touching `data/voicestudio.db`.

## Migration Execution Order

During startup (handled by `StartupService`), migrations should run in this order:
1. `backend/infrastructure/migrations/initial_schema.run_migrations()` (System 1 tables)
2. `MigrationRunner.migrate()` with v001-v003 (System 2 tables)

Both are idempotent (`CREATE TABLE IF NOT EXISTS`) and safe to run on an existing DB.
