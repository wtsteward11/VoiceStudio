"""
Persistent track store: SQLite is authoritative; legacy per-file JSON is import-only.

Tracks are stored in `project_tracks` (see `backend/infrastructure/migrations/initial_schema.py`).
"""

from __future__ import annotations

import json
import logging
import os
import threading
import time
import uuid
from datetime import datetime
from pathlib import Path
from typing import Any

from backend.infrastructure.adapters.database import get_database_adapter
from backend.project.persistence.async_bridge import run_isolated_async

logger = logging.getLogger(__name__)


def _utc_iso() -> str:
    return datetime.utcnow().isoformat()


class TrackStore:
    """
    SQLite-backed track storage with one-time import from legacy `tracks/*.json` files.
    """

    def __init__(self, projects_dir: str | None = None):
        self._projects_dir = Path(
            projects_dir
            or os.getenv("VOICESTUDIO_PROJECTS_PATH", "")
            or Path.home() / ".voicestudio" / "projects"
        )
        self._projects_dir.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()

    def _tracks_dir(self, project_id: str) -> Path:
        return self._projects_dir / project_id / "tracks"

    def _invalidate_cache(self) -> None:
        try:
            from backend.api.optimization import invalidate_api_response_cache

            invalidate_api_response_cache()
        except Exception as e:
            logger.debug("Response cache invalidation skipped: %s", e)

    async def _import_legacy_disk_tracks_async(self, project_id: str) -> None:
        db = get_database_adapter()
        tracks_dir = self._tracks_dir(project_id)
        if not tracks_dir.is_dir():
            return

        for track_file in tracks_dir.glob("*.json"):
            track_id = track_file.stem
            existing = await db.fetch_one(
                "SELECT 1 AS ok FROM project_tracks WHERE project_id = ? AND track_id = ?",
                (project_id, track_id),
            )
            if existing:
                continue
            try:
                payload = json.loads(track_file.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as e:
                logger.warning("Skip legacy track file %s: %s", track_file, e)
                continue
            payload.setdefault("id", track_id)
            payload["project_id"] = project_id
            payload["updated_at"] = time.time()
            data = json.dumps(payload, ensure_ascii=False, default=str)
            await db.execute(
                """
                INSERT INTO project_tracks (project_id, track_id, updated_at, data)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(project_id, track_id) DO UPDATE SET
                  updated_at = excluded.updated_at,
                  data = excluded.data
                """,
                (project_id, track_id, _utc_iso(), data),
            )
            logger.info(
                "Imported legacy track %s for project %s into SQLite",
                track_id,
                project_id,
            )

    def _ensure_legacy_import(self, project_id: str) -> None:
        run_isolated_async(self._import_legacy_disk_tracks_async(project_id))

    def save_track(self, project_id: str, track: dict[str, Any]) -> str:
        track_id = track.get("id", "")
        if not track_id:
            track_id = f"track-{uuid.uuid4().hex[:8]}"
            track["id"] = track_id

        track["project_id"] = project_id
        track["updated_at"] = time.time()
        data = json.dumps(track, ensure_ascii=False, default=str)

        async def _save() -> None:
            db = get_database_adapter()
            await db.execute(
                """
                INSERT INTO project_tracks (project_id, track_id, updated_at, data)
                VALUES (?, ?, ?, ?)
                ON CONFLICT(project_id, track_id) DO UPDATE SET
                  updated_at = excluded.updated_at,
                  data = excluded.data
                """,
                (project_id, track_id, _utc_iso(), data),
            )

        with self._lock:
            run_isolated_async(_save())
        self._invalidate_cache()
        logger.debug("Track saved: %s in project %s", track_id, project_id)
        return str(track_id)

    def get_track(self, project_id: str, track_id: str) -> dict[str, Any] | None:
        self._ensure_legacy_import(project_id)

        async def _get() -> dict[str, Any] | None:
            db = get_database_adapter()
            row = await db.fetch_one(
                "SELECT data FROM project_tracks WHERE project_id = ? AND track_id = ?",
                (project_id, track_id),
            )
            if not row:
                return None
            raw = row["data"]
            return dict(json.loads(raw) if isinstance(raw, str) else raw)

        return run_isolated_async(_get())

    def list_tracks(self, project_id: str) -> list[dict[str, Any]]:
        self._ensure_legacy_import(project_id)

        async def _list() -> list[dict[str, Any]]:
            db = get_database_adapter()
            rows = await db.fetch_all(
                "SELECT data FROM project_tracks WHERE project_id = ? ORDER BY track_id",
                (project_id,),
            )
            tracks: list[dict[str, Any]] = []
            for row in rows:
                raw = row["data"]
                tracks.append(dict(json.loads(raw) if isinstance(raw, str) else raw))
            tracks.sort(key=lambda t: t.get("track_number", 0))
            return tracks

        return run_isolated_async(_list())

    def delete_track(self, project_id: str, track_id: str) -> bool:
        async def _del() -> bool:
            db = get_database_adapter()
            n = await db.execute(
                "DELETE FROM project_tracks WHERE project_id = ? AND track_id = ?",
                (project_id, track_id),
            )
            return n > 0

        with self._lock:
            ok = run_isolated_async(_del())
        if ok:
            self._invalidate_cache()
            logger.info("Track deleted: %s", track_id)
        return ok

    def update_track(
        self, project_id: str, track_id: str, updates: dict[str, Any]
    ) -> dict[str, Any] | None:
        track = self.get_track(project_id, track_id)
        if not track:
            return None

        track.update(updates)
        track["updated_at"] = time.time()
        self.save_track(project_id, track)
        return track


_store: TrackStore | None = None


def get_track_store() -> TrackStore:
    """Get the global track store singleton."""
    global _store
    if _store is None:
        _store = TrackStore()
    return _store


def reset_track_store() -> None:
    """Reset singleton (tests)."""
    global _store
    _store = None
