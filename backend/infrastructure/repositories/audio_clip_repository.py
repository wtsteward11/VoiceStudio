"""
SQLite Audio Clip Repository.

Task 2.3: SQLite implementation of AudioClipRepository.
"""

from __future__ import annotations

import json
import logging

from backend.domain.entities.audio_clip import AudioClip
from backend.domain.repositories.audio_clip_repository import AudioClipRepository
from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

logger = logging.getLogger(__name__)


class SqliteAudioClipRepository(AudioClipRepository):
    """SQLite-backed audio clip repository."""

    def __init__(self, db: DatabaseAdapter | None = None):
        self._db = db or get_database_adapter()

    async def get_by_id(self, clip_id: str) -> AudioClip | None:
        row = await self._db.fetch_one(
            "SELECT data FROM audio_clips WHERE id = ?",
            (clip_id,),
        )
        if not row:
            return None
        data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
        return AudioClip.from_dict(data)

    async def save(self, clip: AudioClip) -> AudioClip:
        clip.touch()
        data = clip.to_dict()
        data_json = json.dumps(data, default=str)
        await self._db.execute(
            """
            INSERT INTO audio_clips (id, created_at, updated_at, data)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET updated_at = ?, data = ?
            """,
            (
                clip.id,
                data["created_at"],
                data["updated_at"],
                data_json,
                data["updated_at"],
                data_json,
            ),
        )
        return clip

    async def delete(self, clip_id: str) -> bool:
        result = await self._db.execute(
            "DELETE FROM audio_clips WHERE id = ?",
            (clip_id,),
        )
        return result > 0

    async def list_by_project(
        self,
        project_id: str,
        limit: int = 100,
        offset: int = 0,
    ) -> list[AudioClip]:
        rows = await self._db.fetch_all(
            """
            SELECT data FROM audio_clips
            WHERE json_extract(data, '$.project_id') = ?
            ORDER BY json_extract(data, '$.track_index'), json_extract(data, '$.start_time')
            LIMIT ? OFFSET ?
            """,
            (project_id, limit, offset),
        )
        result: list[AudioClip] = []
        for row in rows:
            data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
            result.append(AudioClip.from_dict(data))
        return result

    async def count(self) -> int:
        row = await self._db.fetch_one("SELECT COUNT(*) as c FROM audio_clips")
        return int(row["c"]) if row else 0


_audio_clip_repo: SqliteAudioClipRepository | None = None


def get_audio_clip_repository() -> SqliteAudioClipRepository:
    """Get or create the audio clip repository singleton."""
    global _audio_clip_repo
    if _audio_clip_repo is None:
        _audio_clip_repo = SqliteAudioClipRepository()
    return _audio_clip_repo
