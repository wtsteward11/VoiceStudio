"""
Initial migration: Create repository tables.

Task 2.3: Idempotent creation of voice_profiles, projects, audio_clips, jobs.
Tables use JSON data column with id, created_at, updated_at indexed.
"""

from __future__ import annotations

import logging

logger = logging.getLogger(__name__)


async def run_migrations(db_path: str | None = None) -> None:
    """
    Run migrations idempotently.

    Args:
        db_path: SQLite database path. Defaults to ConnectionConfig.sqlite_path.
    """
    from backend.infrastructure.adapters.database import DatabaseAdapter

    if db_path is None:
        try:
            from backend.data.repository_base import ConnectionConfig

            db_path = ConnectionConfig().sqlite_path
        except Exception:
            db_path = "data/voicestudio.db"
    if not db_path.startswith("sqlite"):
        db_path = f"sqlite:///{db_path}"

    adapter = DatabaseAdapter(connection_string=db_path)
    connected = await adapter.connect()
    if not connected:
        logger.warning("Migration: database connection failed, skipping")
        return

    # Check placeholder mode
    if isinstance(getattr(adapter, "_pool", None), dict) and adapter._pool.get(
        "placeholder", False
    ):
        logger.warning("Migration: database in placeholder mode, skipping")
        return

    statements = [
        """
        CREATE TABLE IF NOT EXISTS voice_profiles (
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            data TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_voice_profiles_created_at ON voice_profiles(created_at)",
        "CREATE INDEX IF NOT EXISTS idx_voice_profiles_updated_at ON voice_profiles(updated_at)",
        """
        CREATE TABLE IF NOT EXISTS projects (
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            data TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_projects_created_at ON projects(created_at)",
        "CREATE INDEX IF NOT EXISTS idx_projects_updated_at ON projects(updated_at)",
        """
        CREATE TABLE IF NOT EXISTS audio_clips (
            id TEXT PRIMARY KEY,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            data TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_audio_clips_created_at ON audio_clips(created_at)",
        "CREATE INDEX IF NOT EXISTS idx_audio_clips_updated_at ON audio_clips(updated_at)",
        """
        CREATE TABLE IF NOT EXISTS jobs (
            id TEXT NOT NULL,
            namespace TEXT NOT NULL DEFAULT 'default',
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            data TEXT NOT NULL,
            PRIMARY KEY (id, namespace)
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_jobs_namespace ON jobs(namespace)",
        "CREATE INDEX IF NOT EXISTS idx_jobs_created_at ON jobs(created_at)",
        "CREATE INDEX IF NOT EXISTS idx_jobs_updated_at ON jobs(updated_at)",
        """
        CREATE TABLE IF NOT EXISTS project_tracks (
            project_id TEXT NOT NULL,
            track_id TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            data TEXT NOT NULL,
            PRIMARY KEY (project_id, track_id)
        )
        """,
        "CREATE INDEX IF NOT EXISTS idx_project_tracks_project ON project_tracks(project_id)",
    ]

    for stmt in statements:
        try:
            await adapter.execute(stmt.strip())
        except Exception as e:
            logger.warning("Migration statement failed: %s: %s", stmt[:50], e)

    await adapter.disconnect()
    logger.info("Migrations completed")
