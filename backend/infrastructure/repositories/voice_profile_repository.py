"""
SQLite Voice Profile Repository.

Task 2.3: SQLite implementation of VoiceProfileRepository.
"""

from __future__ import annotations

import json
import logging

from backend.domain.entities.voice_profile import VoiceProfile
from backend.domain.repositories.voice_profile_repository import (
    VoiceProfileRepository,
)
from backend.infrastructure.adapters.database import DatabaseAdapter, get_database_adapter

logger = logging.getLogger(__name__)


class SqliteVoiceProfileRepository(VoiceProfileRepository):
    """SQLite-backed voice profile repository."""

    def __init__(self, db: DatabaseAdapter | None = None):
        self._db = db or get_database_adapter()

    async def get_by_id(self, profile_id: str) -> VoiceProfile | None:
        row = await self._db.fetch_one(
            "SELECT data FROM voice_profiles WHERE id = ?",
            (profile_id,),
        )
        if not row:
            return None
        data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
        return VoiceProfile.from_dict(data)

    async def save(self, profile: VoiceProfile) -> VoiceProfile:
        profile.touch()
        data = profile.to_dict()
        data_json = json.dumps(data, default=str)
        await self._db.execute(
            """
            INSERT INTO voice_profiles (id, created_at, updated_at, data)
            VALUES (?, ?, ?, ?)
            ON CONFLICT(id) DO UPDATE SET updated_at = ?, data = ?
            """,
            (
                profile.id,
                data["created_at"],
                data["updated_at"],
                data_json,
                data["updated_at"],
                data_json,
            ),
        )
        return profile

    async def delete(self, profile_id: str) -> bool:
        result = await self._db.execute(
            "DELETE FROM voice_profiles WHERE id = ?",
            (profile_id,),
        )
        return result > 0

    async def list_all(
        self,
        limit: int = 100,
        offset: int = 0,
        language: str | None = None,
        search: str | None = None,
    ) -> list[VoiceProfile]:
        query = "SELECT data FROM voice_profiles WHERE 1=1"
        params: list[object] = []
        if language:
            query += " AND json_extract(data, '$.language') = ?"
            params.append(language)
        if search:
            query += (
                " AND (json_extract(data, '$.name') LIKE ? OR json_extract(data, '$.tags') LIKE ?)"
            )
            params.extend([f"%{search}%", f"%{search}%"])
        query += " ORDER BY json_extract(data, '$.name') LIMIT ? OFFSET ?"
        params.extend([limit, offset])

        rows = await self._db.fetch_all(query, tuple(params))
        result: list[VoiceProfile] = []
        for row in rows:
            data = json.loads(row["data"]) if isinstance(row["data"], str) else row["data"]
            result.append(VoiceProfile.from_dict(data))
        return result

    async def count(self) -> int:
        row = await self._db.fetch_one("SELECT COUNT(*) as c FROM voice_profiles")
        return int(row["c"]) if row else 0


_voice_profile_repo: SqliteVoiceProfileRepository | None = None


def get_voice_profile_repository() -> SqliteVoiceProfileRepository:
    """Get or create the voice profile repository singleton."""
    global _voice_profile_repo
    if _voice_profile_repo is None:
        _voice_profile_repo = SqliteVoiceProfileRepository()
    return _voice_profile_repo
