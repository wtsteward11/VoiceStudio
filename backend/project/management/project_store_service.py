"""
Project metadata store: SQLite is authoritative; disk holds artifacts only.

- **Authority:** `projects` table via `SqliteProjectRepository` (JSON blob = domain `Project`).
- **Disk:** `<projects_root>/<project_id>/audio/` (and optional legacy `project.json` for import-only).
- **Legacy:** Strategy A — on first list (or lazy get), rows missing in SQLite are imported from
  per-project `project.json` or bare directories, then reads use SQLite only.
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
from typing import List, Optional

from pydantic import BaseModel, Field, ValidationError

from backend.domain.entities.project import Project, ProjectStatus
from backend.infrastructure.repositories.project_repository import get_project_repository
from backend.project.persistence.async_bridge import run_isolated_async

logger = logging.getLogger(__name__)

ENV_PROJECTS_DIR = "VOICESTUDIO_PROJECTS_DIR"
PROJECT_META_FILENAME = "project.json"
CURRENT_PROJECT_SCHEMA_VERSION = 1
SUPPORTED_PAYLOAD_VERSION = 1
METADATA_API_SCHEMA_KEY = "vs_api_project_schema_version"
METADATA_PAYLOAD_VERSION_KEY = "vs_payload_version"


class ProjectRecord(BaseModel):
    # Allow 0 for legacy records pending migration
    schema_version: int = Field(default=CURRENT_PROJECT_SCHEMA_VERSION, ge=0)
    id: str
    name: str
    description: Optional[str] = None
    created_at: str
    updated_at: str
    voice_profile_ids: List[str] = Field(default_factory=list)


class UnsupportedProjectPayloadError(ValueError):
    """Raised when SQLite row payload version is newer than this backend supports."""


def _parse_iso(dt: str) -> datetime:
    if dt.endswith("Z"):
        dt = dt[:-1] + "+00:00"
    return datetime.fromisoformat(dt)


def record_to_domain(record: ProjectRecord, projects_root: Path) -> Project:
    meta = {
        METADATA_API_SCHEMA_KEY: record.schema_version,
        METADATA_PAYLOAD_VERSION_KEY: SUPPORTED_PAYLOAD_VERSION,
    }
    return Project(
        id=record.id,
        created_at=_parse_iso(record.created_at),
        updated_at=_parse_iso(record.updated_at),
        name=record.name,
        description=record.description or "",
        status=ProjectStatus.ACTIVE,
        project_path=str(projects_root / record.id),
        voice_profile_ids=list(record.voice_profile_ids),
        metadata=meta,
    )


def domain_to_record(proj: Project) -> ProjectRecord:
    meta = proj.metadata or {}
    schema_v = int(meta.get(METADATA_API_SCHEMA_KEY, CURRENT_PROJECT_SCHEMA_VERSION))
    return ProjectRecord(
        schema_version=schema_v,
        id=proj.id,
        name=proj.name,
        description=proj.description or None,
        created_at=proj.created_at.isoformat(),
        updated_at=proj.updated_at.isoformat(),
        voice_profile_ids=list(proj.voice_profile_ids),
    )


class ProjectStoreService:
    """
    SQLite-backed project metadata with on-disk artifact directories.

    Legacy `project.json` files are import sources only after SQLite authority is in use.
    """

    def __init__(self, projects_dir: Optional[str] = None):
        self.projects_dir = self._resolve_projects_dir(projects_dir)
        self._lock = threading.RLock()
        self._legacy_disk_scan_done = False

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

        from backend.services.audio_registry_service import ensure_cached

        project_dir = self._ensure_project_dirs(project_id)
        audio_dir = project_dir / "audio"
        normalized = self._normalize_audio_filename(source, audio_id, filename)
        dest_path = audio_dir / normalized

        try:
            cached_path = ensure_cached(source)
        except Exception as cache_error:
            logger.warning(
                "Content-addressed cache failed, using direct copy: %s",
                cache_error,
            )
            cached_path = source

        shutil.copy2(str(cached_path), str(dest_path))
        return dest_path

    def _migrate_record(self, record: ProjectRecord) -> ProjectRecord:
        current = record.schema_version
        if current == CURRENT_PROJECT_SCHEMA_VERSION:
            return record

        if current == 0:
            logger.info("Migrating project %s from v0 to v1 (legacy JSON import)", record.id)
            return record.model_copy(update={"schema_version": CURRENT_PROJECT_SCHEMA_VERSION})

        if current < 1:
            raise ValueError(f"Invalid project schema_version: {current}")

        return record.model_copy(update={"schema_version": CURRENT_PROJECT_SCHEMA_VERSION})

    def _peek_legacy_record_from_dir(self, project_dir: Path) -> ProjectRecord | None:
        """Read legacy disk metadata for SQLite import (does not update SQLite)."""
        if not project_dir.is_dir():
            return None

        project_id = project_dir.name
        meta_path = project_dir / PROJECT_META_FILENAME

        if meta_path.exists():
            try:
                data = json.loads(meta_path.read_text(encoding="utf-8"))
                if "schema_version" not in data:
                    data["schema_version"] = 0
                record = ProjectRecord.model_validate(data)
            except (OSError, json.JSONDecodeError, ValidationError) as e:
                logger.error("Failed to load legacy project metadata for %s: %s", project_id, e)
                return None

            if record.id != project_id:
                logger.error(
                    "Project metadata id mismatch for %s: file has %s", project_id, record.id
                )
                return None

            if record.schema_version != CURRENT_PROJECT_SCHEMA_VERSION:
                record = self._migrate_record(record)
            return record

        try:
            ts = datetime.utcfromtimestamp(project_dir.stat().st_mtime).isoformat()
        except OSError as e:
            logger.error("Failed to stat legacy project directory %s: %s", project_id, e)
            return None

        return ProjectRecord(
            schema_version=CURRENT_PROJECT_SCHEMA_VERSION,
            id=project_id,
            name=project_id,
            description=None,
            created_at=ts,
            updated_at=ts,
            voice_profile_ids=[],
        )

    def _ensure_payload_supported(self, proj: Project) -> None:
        meta = proj.metadata or {}
        pv = int(meta.get(METADATA_PAYLOAD_VERSION_KEY, 1))
        if pv > SUPPORTED_PAYLOAD_VERSION:
            raise UnsupportedProjectPayloadError(
                f"Project {proj.id} payload version {pv} is not supported "
                f"(max {SUPPORTED_PAYLOAD_VERSION})"
            )

    async def _import_legacy_disk_projects_async(self) -> None:
        self.projects_dir.mkdir(parents=True, exist_ok=True)
        repo = get_project_repository()
        existing = {p.id for p in await repo.list_all(limit=100_000, offset=0)}
        for child in self.projects_dir.iterdir():
            if not child.is_dir():
                continue
            pid = child.name
            if pid in existing:
                continue
            record = self._peek_legacy_record_from_dir(child)
            if record is None:
                continue
            await repo.save(record_to_domain(record, self.projects_dir))
            existing.add(pid)
            logger.info("Imported legacy disk project %s into SQLite", pid)

    def _ensure_legacy_scan(self) -> None:
        with self._lock:
            if self._legacy_disk_scan_done:
                return
            run_isolated_async(self._import_legacy_disk_projects_async())
            self._legacy_disk_scan_done = True

    async def _get_record_async(self, project_id: str) -> ProjectRecord:
        repo = get_project_repository()
        proj = await repo.get_by_id(project_id)
        if proj is not None:
            self._ensure_payload_supported(proj)
            return domain_to_record(proj)

        record = self._peek_legacy_record_from_dir(self._project_dir(project_id))
        if record is None:
            raise KeyError(project_id)
        await repo.save(record_to_domain(record, self.projects_dir))
        proj2 = await repo.get_by_id(project_id)
        if proj2 is None:
            raise KeyError(project_id)
        self._ensure_payload_supported(proj2)
        return domain_to_record(proj2)

    def list_projects(self) -> list[ProjectRecord]:
        self._ensure_legacy_scan()

        async def _list() -> list[ProjectRecord]:
            repo = get_project_repository()
            projects = await repo.list_all(limit=100_000, offset=0)
            out: list[ProjectRecord] = []
            for p in projects:
                self._ensure_payload_supported(p)
                out.append(domain_to_record(p))
            return out

        return run_isolated_async(_list())

    def exists(self, project_id: str) -> bool:
        async def _ex() -> bool:
            repo = get_project_repository()
            row = await repo.get_by_id(project_id)
            if row is not None:
                return True
            return self._project_dir(project_id).is_dir()

        return run_isolated_async(_ex())

    def get_project(self, project_id: str) -> ProjectRecord:
        self._ensure_legacy_scan()

        async def _get() -> ProjectRecord:
            return await self._get_record_async(project_id)

        return run_isolated_async(_get())

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

        self._ensure_project_dirs(project_id)

        async def _save() -> None:
            await get_project_repository().save(record_to_domain(record, self.projects_dir))

        run_isolated_async(_save())
        self._invalidate_project_cache()
        return record

    def update_project(
        self,
        project_id: str,
        name: str | None = None,
        description: str | None = None,
        voice_profile_ids: list[str] | None = None,
        description_provided: bool = False,
    ) -> ProjectRecord:
        self._ensure_legacy_scan()

        async def _upd() -> ProjectRecord:
            dom = await get_project_repository().get_by_id(project_id)
            if dom is None:
                raise KeyError(project_id)
            self._ensure_payload_supported(dom)
            rec = domain_to_record(dom)
            update: dict = {"updated_at": datetime.utcnow().isoformat()}
            if name is not None:
                update["name"] = name
            if description_provided:
                update["description"] = description
            if voice_profile_ids is not None:
                update["voice_profile_ids"] = voice_profile_ids
            rec = rec.model_copy(update=update)
            self._ensure_project_dirs(project_id)
            await get_project_repository().save(record_to_domain(rec, self.projects_dir))
            return rec

        result = run_isolated_async(_upd())
        self._invalidate_project_cache()
        return result

    def delete_project(self, project_id: str) -> None:
        async def _del() -> None:
            await get_project_repository().delete(project_id)

        run_isolated_async(_del())

        project_dir = self._project_dir(project_id)
        if project_dir.exists():
            try:
                shutil.rmtree(project_dir)
            except Exception as e:
                logger.warning("Failed to delete project directory %s: %s", project_dir, e)

        self._invalidate_project_cache()

    def _invalidate_project_cache(self) -> None:
        try:
            from backend.api.optimization import invalidate_api_response_cache

            invalidate_api_response_cache()
        except Exception as e:
            logger.debug("Response cache invalidation skipped: %s", e)


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
