"""
Migration v005: transcriptions.updated_at for BaseRepository.update parity.

``TranscriptionRepository.update_transcription`` delegates to ``BaseRepository.update``,
which always sets ``updated_at``. The v001 ``transcriptions`` table omitted this column,
causing SQLite ``OperationalError: no such column: updated_at`` on segment updates
(e.g. ``create_timeline_clips_from_transcript``).
"""

from __future__ import annotations

from typing import Any

from backend.data.migrations.migration_runner import Migration


async def _existing_columns(connection: Any) -> set[str]:
    """Return lowercase column names for transcriptions."""
    async with connection.execute("PRAGMA table_info(transcriptions)") as cursor:
        rows = await cursor.fetchall()
    return {str(row[1]).lower() for row in rows}


class TranscriptionsUpdatedAtMigration(Migration):
    """Add updated_at to transcriptions when missing (idempotent)."""

    @property
    def version(self) -> int:
        return 5

    @property
    def name(self) -> str:
        return "transcriptions_updated_at"

    @property
    def description(self) -> str:
        return "Adds updated_at to transcriptions for BaseRepository.update compatibility."

    async def upgrade(self, connection: Any) -> None:
        cols = await _existing_columns(connection)
        if "updated_at" not in cols:
            await connection.execute(
                "ALTER TABLE transcriptions ADD COLUMN updated_at TEXT"
            )
            await connection.commit()

    async def downgrade(self, connection: Any) -> None:
        """SQLite column drop is non-trivial; downgrade is a no-op."""
        _ = connection


migration = TranscriptionsUpdatedAtMigration
