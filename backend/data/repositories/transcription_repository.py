"""
Transcription Repository.

Core Workflow Audit Remediation - Phase A (C-3).
Replaces the volatile in-memory _transcriptions dict in backend/api/routes/transcribe.py
with database-backed persistence using the existing transcriptions table from
v001_core_persistence_tables migration.
"""

from __future__ import annotations

import json
import logging
import uuid
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from backend.data.repository_base import (
    BaseEntity,
    BaseRepository,
    ConnectionConfig,
)

logger = logging.getLogger(__name__)


@dataclass
class TranscriptionEntity(BaseEntity):
    """
    Transcription entity for persistent storage.

    Maps to the transcriptions table created by v001_core_persistence_tables migration.
    """

    audio_path: str = ""
    language: str | None = None
    text: str | None = None
    segments: str = "[]"  # JSON-serialized list of segment dicts
    word_timestamps: str = "[]"  # JSON-serialized list of word timestamp dicts
    duration: float | None = None
    confidence: float | None = None
    engine_id: str | None = None
    user_id: str | None = None
    expires_at: str | None = None
    audio_id: str | None = None
    project_id: str | None = None

    def get_segments(self) -> list:
        """Parse segments from JSON string."""
        try:
            return json.loads(self.segments) if self.segments else []
        except json.JSONDecodeError:
            return []

    def set_segments(self, segments: list) -> None:
        """Serialize segments to JSON string."""
        self.segments = json.dumps(segments, default=str)

    def get_word_timestamps(self) -> list:
        """Parse word timestamps from JSON string."""
        try:
            return json.loads(self.word_timestamps) if self.word_timestamps else []
        except json.JSONDecodeError:
            return []

    def set_word_timestamps(self, timestamps: list) -> None:
        """Serialize word timestamps to JSON string."""
        self.word_timestamps = json.dumps(timestamps, default=str)


class TranscriptionRepository(BaseRepository[TranscriptionEntity]):
    """
    Repository for transcription persistence.

    Replaces the in-memory _transcriptions dict with database-backed storage.
    Uses the existing 'transcriptions' table from the v001 migration.
    """

    def __init__(self, config: ConnectionConfig | None = None):
        super().__init__(
            entity_type=TranscriptionEntity,
            table_name="transcriptions",
            config=config,
        )

    def _entity_to_dict(self, entity: TranscriptionEntity) -> dict[str, Any]:
        """
        Convert TranscriptionEntity to database row dict.

        Maps to the transcriptions table columns from v001 migration:
        id, audio_path, language, text, segments, word_timestamps,
        duration, confidence, engine_id, user_id, created_at, expires_at, deleted_at.

        Note: project_id is stored in the user_id column as a composite
        "user_id:project_id" string to avoid schema migration. The audio_id
        value from the API is stored in audio_path.
        """
        # Encode project_id into user_id field if present
        user_id_value = entity.user_id or ""
        if entity.project_id:
            user_id_value = f"{user_id_value}|project:{entity.project_id}"

        return {
            "id": entity.id,
            "audio_path": entity.audio_path,
            "language": entity.language,
            "text": entity.text,
            "segments": entity.segments,
            "word_timestamps": entity.word_timestamps,
            "duration": entity.duration,
            "confidence": entity.confidence,
            "engine_id": entity.engine_id,
            "user_id": user_id_value,
            "created_at": (
                entity.created_at.isoformat()
                if isinstance(entity.created_at, datetime)
                else entity.created_at
            ),
            "expires_at": entity.expires_at,
            "deleted_at": (entity.deleted_at.isoformat() if entity.deleted_at else None),
        }

    def _row_to_entity(self, row: dict[str, Any]) -> TranscriptionEntity:
        """Convert database row to TranscriptionEntity."""
        # Decode project_id from the composite user_id field
        raw_user_id = row.get("user_id", "") or ""
        user_id = raw_user_id
        project_id = None
        if "|project:" in raw_user_id:
            parts = raw_user_id.split("|project:", 1)
            user_id = parts[0]
            project_id = parts[1] if len(parts) > 1 else None

        audio_path = row.get("audio_path", "")

        return TranscriptionEntity(
            id=row["id"],
            audio_path=audio_path,
            language=row.get("language"),
            text=row.get("text"),
            segments=row.get("segments", "[]"),
            word_timestamps=row.get("word_timestamps", "[]"),
            duration=row.get("duration"),
            confidence=row.get("confidence"),
            engine_id=row.get("engine_id"),
            user_id=user_id if user_id else None,
            audio_id=audio_path,  # audio_id maps to audio_path column
            project_id=project_id,
            created_at=(
                datetime.fromisoformat(row["created_at"])
                if row.get("created_at")
                else datetime.now()
            ),
            updated_at=datetime.now(),  # Table has no updated_at column
            deleted_at=(
                datetime.fromisoformat(row["deleted_at"]) if row.get("deleted_at") else None
            ),
            expires_at=row.get("expires_at"),
        )

    async def store_transcription(self, transcription_data: dict[str, Any]) -> TranscriptionEntity:
        """
        Store a transcription result from the transcribe endpoint.

        Accepts the dict format produced by TranscriptionResponse.model_dump()
        and persists it to the database.
        """
        # Convert the API response format to entity format
        entity = TranscriptionEntity(
            id=transcription_data["id"],
            audio_path=transcription_data.get("audio_id", ""),
            audio_id=transcription_data.get("audio_id"),
            project_id=transcription_data.get("project_id"),
            language=transcription_data.get("language"),
            text=transcription_data.get("text"),
            segments=json.dumps(
                [
                    s if isinstance(s, dict) else s.dict() if hasattr(s, "dict") else str(s)
                    for s in transcription_data.get("segments", [])
                ],
                default=str,
            ),
            word_timestamps=json.dumps(
                [
                    w if isinstance(w, dict) else w.dict() if hasattr(w, "dict") else str(w)
                    for w in transcription_data.get("word_timestamps", [])
                ],
                default=str,
            ),
            duration=transcription_data.get("duration"),
            engine_id=transcription_data.get("engine"),
            created_at=transcription_data.get("created", datetime.now()),
        )

        return await self.create(entity)

    async def _persist_segments_json(self, transcription_id: str, segments_json: str) -> None:
        """Update segments column only (transcriptions table has no updated_at in v001)."""
        await self.connect()
        await self._connection.execute(
            f"UPDATE {self.table_name} SET segments = ? WHERE id = ?",
            (segments_json, transcription_id),
        )
        await self._connection.commit()

    def _ensure_segment_ids(self, entity: TranscriptionEntity) -> bool:
        """Assign UUIDs to segments missing id. Returns True if entity.segments JSON was mutated."""
        segments = entity.get_segments()
        changed = False
        out: list[Any] = []
        for seg in segments:
            if not isinstance(seg, dict):
                out.append(seg)
                continue
            sid = seg.get("id")
            if not sid or (isinstance(sid, str) and sid.strip() == ""):
                seg = {**seg, "id": str(uuid.uuid4())}
                changed = True
            out.append(seg)
        if changed:
            entity.set_segments(out)
        return changed

    async def _backfill_segment_ids_if_needed(self, entity: TranscriptionEntity) -> None:
        if not self._ensure_segment_ids(entity):
            return
        await self._persist_segments_json(entity.id, entity.segments)

    async def get_transcription(self, transcription_id: str) -> dict[str, Any] | None:
        """
        Get a transcription by ID, returned in the API response format.

        Returns None if not found.
        """
        entity = await self.get_by_id(transcription_id)
        if entity is None:
            return None
        await self._backfill_segment_ids_if_needed(entity)
        return self._entity_to_api_dict(entity)

    async def list_transcriptions(
        self,
        audio_id: str | None = None,
        project_id: str | None = None,
    ) -> list[dict[str, Any]]:
        """
        List transcriptions with optional filters, returned in API response format.

        Results are sorted by creation time (most recent first).
        Note: project_id is encoded in the user_id column as "|project:{id}",
        so we use LIKE for the filter.
        """
        await self.connect()

        conditions = ["deleted_at IS NULL"]
        params: list = []

        if audio_id:
            conditions.append("audio_path = ?")
            params.append(audio_id)

        if project_id:
            # project_id is stored in user_id column as "|project:{id}"
            conditions.append("user_id LIKE ?")
            params.append(f"%|project:{project_id}%")

        where_clause = " AND ".join(conditions)

        query = f"""
            SELECT * FROM {self.table_name}
            WHERE {where_clause}
            ORDER BY created_at DESC
        """

        async with self._connection.execute(query, params) as cursor:
            rows = await cursor.fetchall()
            entities = [self._row_to_entity(dict(row)) for row in rows]
            out = []
            for e in entities:
                await self._backfill_segment_ids_if_needed(e)
                out.append(self._entity_to_api_dict(e))
            return out

    async def delete_transcription(self, transcription_id: str) -> bool:
        """
        Soft-delete a transcription by ID.

        Returns True if the transcription was found and deleted.
        """
        entity = await self.get_by_id(transcription_id)
        if entity is None:
            return False

        await self.delete(transcription_id)
        return True

    async def update_transcription(
        self,
        transcription_id: str,
        text: str | None = None,
        segments: list | None = None,
        word_timestamps: list | None = None,
    ) -> dict[str, Any] | None:
        """
        Update a transcription's text and/or segments.

        Returns the updated transcription in API format, or None if not found.
        """
        updates: dict[str, Any] = {}
        if text is not None:
            updates["text"] = text
        if segments is not None:
            normalized: list[Any] = []
            for seg in segments:
                if isinstance(seg, dict):
                    sid = seg.get("id")
                    if not sid or (isinstance(sid, str) and sid.strip() == ""):
                        seg = {**seg, "id": str(uuid.uuid4())}
                    normalized.append(seg)
                else:
                    normalized.append(seg)
            updates["segments"] = json.dumps(normalized, default=str)
        if word_timestamps is not None:
            updates["word_timestamps"] = json.dumps(word_timestamps, default=str)

        if not updates:
            # Nothing to update; just return current state
            return await self.get_transcription(transcription_id)

        result = await self.update(transcription_id, updates)
        if result is None:
            return None
        return self._entity_to_api_dict(result)

    async def cleanup_expired(self, max_age_seconds: int = 86400) -> int:
        """
        Clean up transcriptions older than max_age_seconds (soft delete).

        Returns the number of transcriptions cleaned up.
        """
        await self.connect()

        query = f"""
            UPDATE {self.table_name}
            SET deleted_at = ?
            WHERE deleted_at IS NULL
            AND datetime(created_at, '+' || ? || ' seconds') < datetime('now')
        """

        await self._connection.execute(query, (datetime.now().isoformat(), max_age_seconds))
        await self._connection.commit()

        return int(self._connection.total_changes)

    def _entity_to_api_dict(self, entity: TranscriptionEntity) -> dict[str, Any]:
        """
        Convert a TranscriptionEntity to the API response dict format
        expected by TranscriptionResponse.
        """
        return {
            "id": entity.id,
            "audio_id": entity.audio_id or entity.audio_path,
            "text": entity.text or "",
            "language": entity.language or "unknown",
            "duration": entity.duration or 0.0,
            "segments": entity.get_segments(),
            "word_timestamps": entity.get_word_timestamps(),
            "created": (
                entity.created_at.isoformat()
                if isinstance(entity.created_at, datetime)
                else entity.created_at
            ),
            "engine": entity.engine_id or "unknown",
            "project_id": entity.project_id,
        }


# Singleton instance
_transcription_repository: TranscriptionRepository | None = None


def get_transcription_repository() -> TranscriptionRepository:
    """Get or create TranscriptionRepository singleton."""
    global _transcription_repository
    if _transcription_repository is None:
        _transcription_repository = TranscriptionRepository()
    return _transcription_repository
