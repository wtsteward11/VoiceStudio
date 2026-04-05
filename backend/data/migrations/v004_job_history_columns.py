"""
Migration v004: job_history columns for canonical JobEntity.

Aligns SQLite `job_history` with `JobRepository` / `JobEntity`:
- name (UI + API)
- current_step_index, result_id, estimated_time_remaining (progress + completion)

GOV-VOICESTUDIO-DURABLE-JOB-QUEUE-01
"""

from __future__ import annotations

from typing import Any

from backend.data.migrations.migration_runner import Migration


async def _existing_columns(connection: Any) -> set[str]:
    """Return lowercase column names for job_history."""
    async with connection.execute("PRAGMA table_info(job_history)") as cursor:
        rows = await cursor.fetchall()
    # aiosqlite Row: name at index 1
    return {str(row[1]).lower() for row in rows}


class JobHistoryColumnsMigration(Migration):
    """Add missing columns to job_history for durable canonical jobs."""

    @property
    def version(self) -> int:
        return 4

    @property
    def name(self) -> str:
        return "job_history_columns"

    @property
    def description(self) -> str:
        return (
            "Adds name, current_step_index, result_id, estimated_time_remaining "
            "to job_history for JobRepository field parity."
        )

    async def upgrade(self, connection: Any) -> None:
        cols = await _existing_columns(connection)
        alters: list[str] = []
        if "name" not in cols:
            alters.append(
                "ALTER TABLE job_history ADD COLUMN name TEXT NOT NULL DEFAULT ''"
            )
        if "current_step_index" not in cols:
            alters.append(
                "ALTER TABLE job_history ADD COLUMN current_step_index INTEGER"
            )
        if "result_id" not in cols:
            alters.append("ALTER TABLE job_history ADD COLUMN result_id TEXT")
        if "estimated_time_remaining" not in cols:
            alters.append(
                "ALTER TABLE job_history ADD COLUMN estimated_time_remaining INTEGER"
            )
        for sql in alters:
            await connection.execute(sql)
        await connection.commit()

    async def downgrade(self, connection: Any) -> None:
        """
        SQLite cannot DROP COLUMN in older versions; downgrade is a no-op.

        Fresh installs that never had v004 would not need downgrade.
        """
        _ = connection


migration = JobHistoryColumnsMigration
