"""
Tests for ProjectStoreService: SQLite authority + legacy JSON import (GAP-016 lane).
"""

from __future__ import annotations

import asyncio
import json
from pathlib import Path

import pytest

from backend.infrastructure.adapters.database import (
    close_database_adapter,
    get_database_adapter,
    reset_database_adapter_singleton,
)
from backend.infrastructure.migrations.initial_schema import run_migrations
from backend.infrastructure.repositories.project_repository import (
    reset_project_repository_singleton,
)
from backend.project.management.project_store_service import (
    CURRENT_PROJECT_SCHEMA_VERSION,
    PROJECT_META_FILENAME,
    ProjectStoreService,
    UnsupportedProjectPayloadError,
    reset_project_store_service,
)


@pytest.fixture
def store_with_db(tmp_path: Path) -> ProjectStoreService:
    projects_root = tmp_path / "projects"
    projects_root.mkdir()
    db_file = tmp_path / "vs.db"

    async def _setup() -> None:
        await close_database_adapter()
        reset_database_adapter_singleton()
        reset_project_repository_singleton()
        reset_project_store_service()
        conn = f"sqlite:///{db_file}"
        await run_migrations(db_path=conn)
        db = get_database_adapter(conn)
        await db.connect()

    asyncio.run(_setup())
    return ProjectStoreService(projects_dir=str(projects_root))


def _restore_event_loop_after_async() -> None:
    """asyncio.run() clears the main-thread loop; restore for pytest global hooks."""
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)


@pytest.fixture(autouse=True)
def _teardown_sqlite_singletons() -> None:
    yield
    asyncio.run(close_database_adapter())
    reset_database_adapter_singleton()
    reset_project_repository_singleton()
    reset_project_store_service()
    _restore_event_loop_after_async()


def test_load_legacy_project_v0_imports_to_sqlite(store_with_db: ProjectStoreService, tmp_path: Path) -> None:
    """Legacy project.json (missing schema_version) imports into SQLite with v1 record."""
    project_id = "legacy-project-1"
    project_dir = tmp_path / "projects" / project_id
    project_dir.mkdir(parents=True)
    (project_dir / "audio").mkdir()

    legacy_meta = {
        "id": project_id,
        "name": "Legacy Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
        "voice_profile_ids": [],
    }
    (project_dir / PROJECT_META_FILENAME).write_text(json.dumps(legacy_meta), encoding="utf-8")

    project = store_with_db.get_project(project_id)
    assert project.id == project_id
    assert project.schema_version == CURRENT_PROJECT_SCHEMA_VERSION

    async def _assert_sqlite() -> None:
        from backend.infrastructure.repositories.project_repository import get_project_repository

        row = await get_project_repository().get_by_id(project_id)
        assert row is not None
        assert row.name == "Legacy Project"

    asyncio.run(_assert_sqlite())


def test_migration_idempotent_in_sqlite(store_with_db: ProjectStoreService, tmp_path: Path) -> None:
    """Repeated get_project returns stable SQLite-backed metadata."""
    project_id = "migrated-project-1"
    project_dir = tmp_path / "projects" / project_id
    project_dir.mkdir(parents=True)
    (project_dir / "audio").mkdir()

    current_meta = {
        "schema_version": CURRENT_PROJECT_SCHEMA_VERSION,
        "id": project_id,
        "name": "Migrated Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
        "voice_profile_ids": [],
    }
    (project_dir / PROJECT_META_FILENAME).write_text(json.dumps(current_meta), encoding="utf-8")

    project1 = store_with_db.get_project(project_id)
    assert project1.schema_version == CURRENT_PROJECT_SCHEMA_VERSION
    project2 = store_with_db.get_project(project_id)
    assert project2.name == project1.name


def test_invalid_schema_version_on_disk_not_loaded(store_with_db: ProjectStoreService, tmp_path: Path) -> None:
    """Invalid project.json fails validation and does not produce a record."""
    project_id = "invalid-project-1"
    project_dir = tmp_path / "projects" / project_id
    project_dir.mkdir()

    invalid_meta = {
        "schema_version": -1,
        "id": project_id,
        "name": "Invalid Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
    }
    (project_dir / PROJECT_META_FILENAME).write_text(json.dumps(invalid_meta), encoding="utf-8")

    with pytest.raises(KeyError):
        store_with_db.get_project(project_id)


def test_unsupported_sqlite_payload_version_raises(store_with_db: ProjectStoreService, tmp_path: Path) -> None:
    import uuid

    from backend.domain.entities.project import Project, ProjectStatus
    from backend.infrastructure.repositories.project_repository import get_project_repository

    project_id = str(uuid.uuid4())
    store_with_db._ensure_project_dirs(project_id)

    async def _seed() -> None:
        p = Project(
            id=project_id,
            name="future",
            description="",
            status=ProjectStatus.ACTIVE,
            metadata={"vs_payload_version": 99},
        )
        await get_project_repository().save(p)

    asyncio.run(_seed())

    with pytest.raises(UnsupportedProjectPayloadError):
        store_with_db.get_project(project_id)
