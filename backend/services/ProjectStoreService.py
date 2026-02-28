"""
Disk-backed project metadata store for VoiceStudio backend.

This service provides CRUD operations for project metadata and persists each project
as a JSON record stored in the project directory:

  <projects_root>/<project_id>/project.json

The existing API already uses the on-disk directory layout for project artifacts
(for example audio under <project_id>/audio/). This service adds durable project
metadata so projects can be listed and retrieved after backend restarts.
"""

from __future__ import annotations

import json
import logging
import os
import shutil
import threading
import uuid
from datetime import datetime
from pathlib import Path
from typing import Dict, List, Optional

from pydantic import BaseModel, Field, ValidationError

from backend.services.ContentAddressedAudioCache import get_audio_cache

logger = logging.getLogger(__name__)

ENV_PROJECTS_DIR = "VOICESTUDIO_PROJECTS_DIR"
PROJECT_META_FILENAME = "project.json"
CURRENT_PROJECT_SCHEMA_VERSION = 1


class ProjectRecord(BaseModel):
    # Allow 0 for legacy records pending migration
    schema_version: int = Field(default=CURRENT_PROJECT_SCHEMA_VERSION, ge=0)
    id: str
    name: str
    description: Optional[str] = None
    created_at: str
    updated_at: str
    voice_profile_ids: List[str] = Field(default_factory=list)


class ProjectStoreService:
    """
    Disk-backed project metadata store.

    - Persists metadata to a per-project JSON file.
    - Loads metadata from disk at initialization (and on demand for missing IDs).
    - Provides helpers for consistent project directory creation.
    """

    def __init__(self, projects_dir: Optional[str] = None):
        self.projects_dir = self._resolve_projects_dir(projects_dir)
        self._lock = threading.RLock()
        self._projects: Dict[str, ProjectRecord] = {}
        self._load_all_from_disk()

    @staticmethod
    def _resolve_projects_dir(projects_dir: str | None) -> Path:
        if projects_dir:
            return Path(projects_dir)

        env_dir = os.getenv(ENV_PROJECTS_DIR)
        if env_dir:
            return Path(env_dir)

        return Path.home() / ".voicestudio" / "projects"

    def _project_dir(self, project_id: str) -> Path:
        return self.projects_dir / project_id

    def _project_meta_path(self, project_id: str) -> Path:
        return self._project_dir(project_id) / PROJECT_META_FILENAME

    def _ensure_project_dirs(self, project_id: str) -> Path:
        project_dir = self._project_dir(project_id)
        project_dir.mkdir(parents=True, exist_ok=True)
        (project_dir / "audio").mkdir(parents=True, exist_ok=True)
        return project_dir

    def _normalize_audio_filename(
        self, source_path: Path, audio_id: str | None, filename: str | None
    ) -> str:
        if filename:
            normalized = filename
        elif audio_id:
            normalized = f"{audio_id}.wav"
        else:
            normalized = source_path.name

        if not normalized.lower().endswith(".wav"):
            normalized = f"{normalized}.wav"

        invalid_chars = ["<", ">", ":", '"', "/", "\\", "|", "?", "*"]
        if any(char in normalized for char in invalid_chars):
            raise ValueError(f"Filename '{normalized}' contains invalid characters.")
        return normalized

    def save_audio_file(
        self,
        project_id: str,
        source_path: Path | str,
        *,
        audio_id: str | None = None,
        filename: str | None = None,
    ) -> Path:
        if not self.exists(project_id):
            raise KeyError(project_id)

        source = Path(source_path)
        if not source.exists():
            raise FileNotFoundError(f"Audio file not found: {source}")

        project_dir = self._ensure_project_dirs(project_id)
        audio_dir = project_dir / "audio"
        normalized = self._normalize_audio_filename(source, audio_id, filename)
        dest_path = audio_dir / normalized

        try:
            audio_cache = get_audio_cache()
            cached_path, _ = audio_cache.get_or_store(source)
        except Exception as cache_error:
            logger.warning(
                "Content-addressed cache failed, using direct copy: %s",
                cache_error,
            )
            cached_path = source

        shutil.copy2(str(cached_path), str(dest_path))
        return dest_path

    def _atomic_write_json(self, path: Path, payload: dict) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        tmp_path = path.with_suffix(path.suffix + ".tmp")
        data = json.dumps(payload, indent=2, ensure_ascii=False)
        tmp_path.write_text(data, encoding="utf-8")
        os.replace(tmp_path, path)

    def _write_record(self, record: ProjectRecord) -> None:
        meta_path = self._project_meta_path(record.id)
        self._atomic_write_json(meta_path, record.model_dump(mode="json"))

    def _load_record_from_dir(self, project_dir: Path) -> ProjectRecord | None:
        if not project_dir.is_dir():
            return None

        project_id = project_dir.name
        meta_path = project_dir / PROJECT_META_FILENAME

        if meta_path.exists():
            try:
                data = json.loads(meta_path.read_text(encoding="utf-8"))

                # Check for schema_version in raw data to detect legacy v0
                if "schema_version" not in data:
                    data["schema_version"] = 0  # Force legacy version 0

                record = ProjectRecord.model_validate(data)
            except (OSError, json.JSONDecodeError, ValidationError) as e:
                logger.error(f"Failed to load project metadata for {project_id}: {e}")
                return None

            if record.id != project_id:
                logger.error(f"Project metadata id mismatch for {project_id}: file has {record.id}")
                return None

            if record.schema_version != CURRENT_PROJECT_SCHEMA_VERSION:
                record = self._migrate_record(record)
                try:
                    self._write_record(record)
                except Exception as e:
                    logger.error(f"Failed to persist migrated metadata for {project_id}: {e}")

            return record

        # Legacy directory without metadata: create durable metadata using directory timestamps.
        try:
            ts = datetime.utcfromtimestamp(project_dir.stat().st_mtime).isoformat()
        except OSError as e:
            logger.error(f"Failed to stat legacy project directory {project_id}: {e}")
            return None

        record = ProjectRecord(
            schema_version=CURRENT_PROJECT_SCHEMA_VERSION,
            id=project_id,
            name=project_id,
            description=None,
            created_at=ts,
            updated_at=ts,
            voice_profile_ids=[],
        )

        try:
            self._write_record(record)
        except Exception as e:
            logger.warning(f"Failed to write legacy metadata for {project_id}: {e}")
        return record

    def _load_all_from_disk(self) -> None:
        with self._lock:
            self.projects_dir.mkdir(parents=True, exist_ok=True)
            self._projects.clear()
            for child in self.projects_dir.iterdir():
                record = self._load_record_from_dir(child)
                if record is None:
                    continue
                self._projects[record.id] = record

    def _migrate_record(self, record: ProjectRecord) -> ProjectRecord:
        """
        Migrate a ProjectRecord to the current schema version.

        Migration is idempotent: it upgrades monotonically and never re-applies
        steps once the schema_version matches the target.
        """
        current = record.schema_version
        if current == CURRENT_PROJECT_SCHEMA_VERSION:
            return record

        # Handle v0 (pre-schema) -> v1
        if current == 0:
            logger.info(f"Migrating project {record.id} from v0 to v1")
            return record.model_copy(update={"schema_version": CURRENT_PROJECT_SCHEMA_VERSION})

        if current < 1:
            raise ValueError(f"Invalid project schema_version: {current}")

        # v1 is the baseline schema used by this backend.
        # Future migrations should transform fields and increment schema_version.
        return record.model_copy(update={"schema_version": CURRENT_PROJECT_SCHEMA_VERSION})

    def list_projects(self) -> list[ProjectRecord]:
        with self._lock:
            return list(self._projects.values())

    def exists(self, project_id: str) -> bool:
        with self._lock:
            if project_id in self._projects:
                return True
        return (
            self._project_meta_path(project_id).exists() or self._project_dir(project_id).exists()
        )

    def get_project(self, project_id: str) -> ProjectRecord:
        with self._lock:
            record = self._projects.get(project_id)
            if record is not None:
                return record

        project_dir = self._project_dir(project_id)
        record = self._load_record_from_dir(project_dir)
        if record is None:
            raise KeyError(project_id)

        with self._lock:
            self._projects[project_id] = record
            return record

    def create_project(self, name: str, description: str | None = None) -> ProjectRecord:
        project_id = str(uuid.uuid4())
        now = datetime.utcnow().isoformat()

        record = ProjectRecord(
            schema_version=CURRENT_PROJECT_SCHEMA_VERSION,
            id=project_id,
            name=name,
            description=description,
            created_at=now,
            updated_at=now,
            voice_profile_ids=[],
        )

        with self._lock:
            self._ensure_project_dirs(project_id)
            self._write_record(record)
            self._projects[project_id] = record
            return record

    def update_project(
        self,
        project_id: str,
        name: str | None = None,
        description: str | None = None,
        voice_profile_ids: list[str] | None = None,
        description_provided: bool = False,
    ) -> ProjectRecord:
        with self._lock:
            record = self.get_project(project_id)

            update: dict = {"updated_at": datetime.utcnow().isoformat()}
            if name is not None:
                update["name"] = name
            if description_provided:
                update["description"] = description
            if voice_profile_ids is not None:
                update["voice_profile_ids"] = voice_profile_ids

            record = record.model_copy(update=update)
            self._ensure_project_dirs(project_id)
            self._write_record(record)
            self._projects[project_id] = record
            return record

    def delete_project(self, project_id: str) -> None:
        with self._lock:
            self._projects.pop(project_id, None)

        project_dir = self._project_dir(project_id)
        if not project_dir.exists():
            return

        try:
            shutil.rmtree(project_dir)
        except Exception as e:
            logger.warning(f"Failed to delete project directory {project_dir}: {e}")


_service_instance: ProjectStoreService | None = None


def get_project_store_service(
    projects_dir: str | None = None,
) -> ProjectStoreService:
    """
    Get a global ProjectStoreService instance.

    Args:
        projects_dir: Optional override for the projects root directory.
    """
    global _service_instance
    if _service_instance is None:
        _service_instance = ProjectStoreService(projects_dir=projects_dir)
    return _service_instance


def reset_project_store_service() -> None:
    """Reset the global ProjectStoreService instance (used for test isolation)."""
    global _service_instance
    _service_instance = None
