"""
Persistent Track Store for VoiceStudio (Phase 13.2.1-13.2.2)

Persists audio tracks to project directories instead of in-memory storage.
Tracks are stored as part of the project structure.
"""

from __future__ import annotations

import json
import logging
import os
import threading
import time
import uuid
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class TrackStore:
    """
    Disk-backed audio track storage.

    Stores tracks within their parent project directory:
        projects/{project_id}/tracks/
        ├── {track_id}.json
    """

    def __init__(self, projects_dir: str | None = None):
        self._projects_dir = Path(
            projects_dir
            or os.getenv("VOICESTUDIO_PROJECTS_PATH", "")
            or Path.home() / ".voicestudio" / "projects"
        )
        self._projects_dir.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()

    def save_track(self, project_id: str, track: dict[str, Any]) -> str:
        """Save a track to a project."""
        track_id = track.get("id", "")
        if not track_id:
            track_id = f"track-{uuid.uuid4().hex[:8]}"
            track["id"] = track_id

        track["project_id"] = project_id
        track["updated_at"] = time.time()

        with self._lock:
            tracks_dir = self._projects_dir / project_id / "tracks"
            tracks_dir.mkdir(parents=True, exist_ok=True)

            track_file = tracks_dir / f"{track_id}.json"
            tmp_file = track_file.with_suffix(".tmp")

            try:
                with open(tmp_file, "w", encoding="utf-8") as f:
                    json.dump(track, f, indent=2, ensure_ascii=False)
                os.replace(str(tmp_file), str(track_file))
            except OSError as exc:
                logger.error(f"Failed to save track {track_id}: {exc}")
                raise

        logger.debug(f"Track saved: {track_id} in project {project_id}")
        return track_id

    def get_track(self, project_id: str, track_id: str) -> dict[str, Any] | None:
        """Get a track by ID from a project."""
        track_file = self._projects_dir / project_id / "tracks" / f"{track_id}.json"

        if not track_file.exists():
            return None

        try:
            with open(track_file, encoding="utf-8") as f:
                return json.load(f)
        except (json.JSONDecodeError, OSError) as exc:
            logger.warning(f"Failed to load track {track_id}: {exc}")
            return None

    def list_tracks(self, project_id: str) -> list[dict[str, Any]]:
        """List all tracks for a project."""
        tracks_dir = self._projects_dir / project_id / "tracks"
        if not tracks_dir.exists():
            return []

        tracks = []
        for track_file in tracks_dir.glob("*.json"):
            try:
                with open(track_file, encoding="utf-8") as f:
                    tracks.append(json.load(f))
            except (json.JSONDecodeError, OSError):
                continue

        tracks.sort(key=lambda t: t.get("track_number", 0))
        return tracks

    def delete_track(self, project_id: str, track_id: str) -> bool:
        """Delete a track from a project."""
        track_file = self._projects_dir / project_id / "tracks" / f"{track_id}.json"

        if not track_file.exists():
            return False

        try:
            track_file.unlink()
            logger.info(f"Track deleted: {track_id}")
            return True
        except OSError as exc:
            logger.error(f"Failed to delete track {track_id}: {exc}")
            return False

    def update_track(
        self, project_id: str, track_id: str, updates: dict[str, Any]
    ) -> dict[str, Any] | None:
        """Update specific fields of a track."""
        track = self.get_track(project_id, track_id)
        if not track:
            return None

        track.update(updates)
        track["updated_at"] = time.time()
        self.save_track(project_id, track)
        return track


# Singleton
_store: TrackStore | None = None


def get_track_store() -> TrackStore:
    """Get the global track store singleton."""
    global _store
    if _store is None:
        _store = TrackStore()
    return _store
