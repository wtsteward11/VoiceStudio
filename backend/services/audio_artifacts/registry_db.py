"""
SQLite-backed audio artifact registry.

Milestone 2: Spec-compliant registry for the artifact spine.
Persistent store at get_path("data")/audio_registry.db.
Connection per call; no global mutable state.
"""

from __future__ import annotations

import json
import sqlite3
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable

from backend.config.path_config import get_path
from backend.services.audio_artifacts.errors import ArtifactNotFoundError
from backend.services.audio_artifacts.models import AudioArtifact
from backend.services.audio_artifacts.store import AudioArtifactStore, get_audio_artifact_store

REGISTRY_FILENAME = "audio_registry.db"
_NOT_IMPLEMENTED_MSG = "Commit 2"

SCHEMA = """
CREATE TABLE IF NOT EXISTS audio_artifacts (
    audio_id TEXT PRIMARY KEY,
    path TEXT NOT NULL,
    ext TEXT,
    duration_sec REAL,
    created_at TEXT,
    created_by TEXT,
    user_id TEXT,
    project_id TEXT,
    kind TEXT,
    source_audio_ids TEXT,
    metadata_json TEXT
);
"""


def _get_db_path(db_path: Path | None = None) -> Path:
    """Resolve registry database path."""
    if db_path is not None:
        return db_path
    return get_path("data") / REGISTRY_FILENAME


def _connect(db_path: Path) -> sqlite3.Connection:
    """Open a connection to the registry database."""
    db_path.parent.mkdir(parents=True, exist_ok=True)
    conn = sqlite3.connect(str(db_path), check_same_thread=False)
    conn.row_factory = sqlite3.Row
    return conn


def _ensure_schema(conn: sqlite3.Connection) -> None:
    """Create schema if not exists."""
    conn.executescript(SCHEMA)


def _row_to_artifact(row: sqlite3.Row) -> AudioArtifact:
    """Convert DB row to AudioArtifact."""
    source_ids: list[str] | None = None
    if row["source_audio_ids"]:
        try:
            source_ids = json.loads(row["source_audio_ids"])
        # ALLOWED: bare except - best effort, failure acceptable
        except (json.JSONDecodeError, TypeError):
            pass

    meta: dict[str, Any] = {}
    if row["metadata_json"]:
        try:
            meta = json.loads(row["metadata_json"])
        # ALLOWED: bare except - best effort, failure acceptable
        except (json.JSONDecodeError, TypeError):
            pass

    return AudioArtifact(
        audio_id=row["audio_id"],
        path=row["path"],
        ext=row["ext"] or "wav",
        duration_sec=row["duration_sec"],
        created_at=row["created_at"] or "",
        created_by=row["created_by"] or "unknown",
        user_id=row["user_id"],
        project_id=row["project_id"],
        kind=row["kind"] or "audio",
        source_audio_ids=source_ids,
        metadata=meta if meta else None,
    )


class AudioRegistryDB:
    """
    SQLite-backed registry for audio artifacts.

    Thread-safe via connection-per-call. No global mutable state.
    """

    def __init__(
        self,
        db_path: Path | None = None,
        store_factory: Callable[[], object] | None = None,
    ) -> None:
        """
        Initialize registry.

        Args:
            db_path: Override for database path (for tests).
            store_factory: Optional factory for AudioArtifactStore (for create_* methods).
        """
        self._db_path = _get_db_path(db_path)
        self._store_factory = store_factory or (
            lambda: get_audio_artifact_store()
        )

    def register(
        self,
        audio_id: str,
        path: str | Path,
        *,
        ext: str = "wav",
        duration_sec: float | None = None,
        created_by: str = "registry",
        user_id: str | None = None,
        project_id: str | None = None,
        kind: str = "audio",
        source_audio_ids: list[str] | None = None,
        metadata: dict | None = None,
    ) -> AudioArtifact:
        """Register an existing file. Path must already exist on disk."""
        path_str = str(Path(path).resolve())
        created_at = datetime.now(timezone.utc).isoformat()
        source_ids_json = json.dumps(source_audio_ids) if source_audio_ids else None
        meta_json = json.dumps(metadata) if metadata else None

        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            conn.execute(
                """
                INSERT INTO audio_artifacts (
                    audio_id, path, ext, duration_sec, created_at, created_by,
                    user_id, project_id, kind, source_audio_ids, metadata_json
                ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    audio_id,
                    path_str,
                    ext,
                    duration_sec,
                    created_at,
                    created_by,
                    user_id,
                    project_id,
                    kind,
                    source_ids_json,
                    meta_json,
                ),
            )
            conn.commit()
        finally:
            conn.close()

        return AudioArtifact(
            audio_id=audio_id,
            path=path_str,
            ext=ext,
            duration_sec=duration_sec,
            created_at=created_at,
            created_by=created_by,
            user_id=user_id,
            project_id=project_id,
            kind=kind,
            source_audio_ids=source_audio_ids,
            metadata=metadata,
        )

    def create_from_bytes(
        self,
        data_bytes: bytes,
        ext: str = "wav",
        *,
        audio_id: str | None = None,
        created_by: str = "registry",
        user_id: str | None = None,
        project_id: str | None = None,
        kind: str = "audio",
        source_audio_ids: list[str] | None = None,
        metadata: dict | None = None,
    ) -> AudioArtifact:
        """Create artifact from bytes; write to store and register."""
        aid = audio_id or str(uuid.uuid4())
        store = self._store_factory()
        if not isinstance(store, AudioArtifactStore):
            raise TypeError("store_factory must return AudioArtifactStore")
        out_path = store.write_from_bytes(aid, data_bytes, ext)
        duration: float | None = None
        if ext.lower().lstrip(".") == "wav":
            try:
                import wave

                with wave.open(str(out_path), "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sr = wav_file.getframerate()
                    if sr:
                        duration = frames / float(sr)
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        return self.register(
            aid,
            out_path,
            ext=ext,
            duration_sec=duration,
            created_by=created_by,
            user_id=user_id,
            project_id=project_id,
            kind=kind,
            source_audio_ids=source_audio_ids,
            metadata=metadata,
        )

    def create_from_path(
        self,
        src_path: str | Path,
        *,
        ext: str | None = None,
        audio_id: str | None = None,
        created_by: str = "registry",
        user_id: str | None = None,
        project_id: str | None = None,
        kind: str = "audio",
        source_audio_ids: list[str] | None = None,
        metadata: dict | None = None,
    ) -> AudioArtifact:
        """Create artifact from existing file; copy to store and register."""
        aid = audio_id or str(uuid.uuid4())
        store = self._store_factory()
        if not isinstance(store, AudioArtifactStore):
            raise TypeError("store_factory must return AudioArtifactStore")
        out_path = store.write_from_path(aid, src_path, ext=ext, copy=True)
        ext_clean = out_path.suffix.lstrip(".")
        duration: float | None = None
        if ext_clean == "wav":
            try:
                import wave

                with wave.open(str(out_path), "rb") as wav_file:
                    frames = wav_file.getnframes()
                    sr = wav_file.getframerate()
                    if sr:
                        duration = frames / float(sr)
            # ALLOWED: bare except - best effort, failure acceptable
            except Exception:
                pass
        return self.register(
            aid,
            out_path,
            ext=ext_clean or "wav",
            duration_sec=duration,
            created_by=created_by,
            user_id=user_id,
            project_id=project_id,
            kind=kind,
            source_audio_ids=source_audio_ids,
            metadata=metadata,
        )

    def resolve_path(self, audio_id: str) -> Path:
        """Resolve audio_id to file path. Raises ArtifactNotFoundError."""
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            cur = conn.execute(
                "SELECT path FROM audio_artifacts WHERE audio_id = ?", (audio_id,)
            )
            row = cur.fetchone()
            if row is None:
                raise ArtifactNotFoundError(f"Audio artifact not found: {audio_id}")
            return Path(row["path"])
        finally:
            conn.close()

    def get(self, audio_id: str) -> AudioArtifact:
        """Get full artifact record. Raises ArtifactNotFoundError."""
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            cur = conn.execute(
                "SELECT * FROM audio_artifacts WHERE audio_id = ?", (audio_id,)
            )
            row = cur.fetchone()
            if row is None:
                raise ArtifactNotFoundError(f"Audio artifact not found: {audio_id}")
            return _row_to_artifact(row)
        finally:
            conn.close()

    def update_metadata(self, audio_id: str, extra: dict[str, Any]) -> None:
        """Merge *extra* keys into existing metadata_json. No-op if row missing."""
        if not extra:
            return
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            cur = conn.execute(
                "SELECT metadata_json FROM audio_artifacts WHERE audio_id = ?",
                (audio_id,),
            )
            row = cur.fetchone()
            if row is None:
                return
            current: dict[str, Any] = {}
            if row["metadata_json"]:
                try:
                    loaded = json.loads(row["metadata_json"])
                    if isinstance(loaded, dict):
                        current = loaded
                except (json.JSONDecodeError, TypeError):
                    current = {}
            for key, value in extra.items():
                current[key] = value
            conn.execute(
                "UPDATE audio_artifacts SET metadata_json = ? WHERE audio_id = ?",
                (json.dumps(current), audio_id),
            )
            conn.commit()
        finally:
            conn.close()

    def exists(self, audio_id: str) -> bool:
        """Check if audio_id is registered."""
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            cur = conn.execute(
                "SELECT 1 FROM audio_artifacts WHERE audio_id = ?", (audio_id,)
            )
            return cur.fetchone() is not None
        finally:
            conn.close()

    def delete(self, audio_id: str) -> None:
        """Remove from registry. Does not delete file (store handles that)."""
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            conn.execute("DELETE FROM audio_artifacts WHERE audio_id = ?", (audio_id,))
            conn.commit()
        finally:
            conn.close()

    def list_artifacts(
        self,
        limit: int = 100,
        user_id: str | None = None,
        project_id: str | None = None,
    ) -> list[AudioArtifact]:
        """List artifacts with optional filters."""
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            if user_id is not None and project_id is not None:
                cur = conn.execute(
                    """SELECT * FROM audio_artifacts
                       WHERE user_id = ? AND project_id = ?
                       ORDER BY created_at DESC LIMIT ?""",
                    (user_id, project_id, limit),
                )
            elif user_id is not None:
                cur = conn.execute(
                    """SELECT * FROM audio_artifacts
                       WHERE user_id = ? ORDER BY created_at DESC LIMIT ?""",
                    (user_id, limit),
                )
            elif project_id is not None:
                cur = conn.execute(
                    """SELECT * FROM audio_artifacts
                       WHERE project_id = ? ORDER BY created_at DESC LIMIT ?""",
                    (project_id, limit),
                )
            else:
                cur = conn.execute(
                    """SELECT * FROM audio_artifacts
                       ORDER BY created_at DESC LIMIT ?""",
                    (limit,),
                )
            return [_row_to_artifact(row) for row in cur.fetchall()]
        finally:
            conn.close()

    def list_entries_sorted_by_age(self, limit: int = 10000) -> list[tuple[str, float]]:
        """
        Return [(audio_id, created_at_epoch), ...] sorted by oldest first for LRU cleanup.

        Args:
            limit: Maximum number of entries to return.
        """
        conn = _connect(self._db_path)
        try:
            _ensure_schema(conn)
            cur = conn.execute(
                """SELECT audio_id, created_at FROM audio_artifacts
                   ORDER BY created_at ASC LIMIT ?""",
                (limit,),
            )
            result: list[tuple[str, float]] = []
            for row in cur.fetchall():
                audio_id = row["audio_id"]
                created_at_str = row["created_at"] or ""
                try:
                    dt = datetime.fromisoformat(
                        created_at_str.replace("Z", "+00:00")
                    )
                    epoch = dt.timestamp()
                except (ValueError, TypeError):
                    epoch = 0.0
                result.append((audio_id, epoch))
            return result
        finally:
            conn.close()
