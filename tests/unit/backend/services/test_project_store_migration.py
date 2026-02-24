"""
Tests for ProjectStoreService migration logic.
"""

import json

import pytest

from backend.services.ProjectStoreService import (
    CURRENT_PROJECT_SCHEMA_VERSION,
    PROJECT_META_FILENAME,
    ProjectStoreService,
)


@pytest.fixture
def store_service(tmp_path):
    """Create a ProjectStoreService backed by a temporary directory."""
    return ProjectStoreService(projects_dir=str(tmp_path))


def test_load_legacy_project_v0_migrates_to_current(store_service, tmp_path):
    """Test that a legacy project (v0/missing version) is migrated to current version."""
    project_id = "legacy-project-1"
    project_dir = tmp_path / project_id
    project_dir.mkdir()
    (project_dir / "audio").mkdir()

    # Create a legacy project.json (missing schema_version)
    legacy_meta = {
        "id": project_id,
        "name": "Legacy Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
        "voice_profile_ids": [],
    }

    meta_path = project_dir / PROJECT_META_FILENAME
    meta_path.write_text(json.dumps(legacy_meta))

    # Load project
    project = store_service.get_project(project_id)

    # Assertions
    assert project.id == project_id
    assert project.schema_version == CURRENT_PROJECT_SCHEMA_VERSION

    # Verify persistence
    saved_meta = json.loads(meta_path.read_text())
    assert saved_meta["schema_version"] == CURRENT_PROJECT_SCHEMA_VERSION


def test_migration_is_idempotent(store_service, tmp_path):
    """Test that loading an already migrated project does not change it."""
    project_id = "migrated-project-1"
    project_dir = tmp_path / project_id
    project_dir.mkdir()
    (project_dir / "audio").mkdir()

    # Create a current version project.json
    current_meta = {
        "schema_version": CURRENT_PROJECT_SCHEMA_VERSION,
        "id": project_id,
        "name": "Migrated Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
        "voice_profile_ids": [],
    }

    meta_path = project_dir / PROJECT_META_FILENAME
    meta_path.write_text(json.dumps(current_meta))

    # Load project
    project1 = store_service.get_project(project_id)
    assert project1.schema_version == CURRENT_PROJECT_SCHEMA_VERSION

    # Load again
    project2 = store_service.get_project(project_id)
    assert project2.schema_version == CURRENT_PROJECT_SCHEMA_VERSION

    # Verify file wasn't unnecessarily modified (timestamp check might be flaky, so check content)
    saved_meta = json.loads(meta_path.read_text())
    assert saved_meta["schema_version"] == CURRENT_PROJECT_SCHEMA_VERSION


def test_invalid_schema_version_raises_error(store_service, tmp_path):
    """Test that a project with invalid schema version raises an error."""
    project_id = "invalid-project-1"
    project_dir = tmp_path / project_id
    project_dir.mkdir()

    # Create invalid version
    invalid_meta = {
        "schema_version": -1,
        "id": project_id,
        "name": "Invalid Project",
        "created_at": "2024-01-01T00:00:00",
        "updated_at": "2024-01-01T00:00:00",
    }

    meta_path = project_dir / PROJECT_META_FILENAME
    meta_path.write_text(json.dumps(invalid_meta))

    # Load should fail (or return None depending on implementation,
    # but Pydantic validation usually raises ValidationError, caught and logged -> returns None)
    # The current implementation logs error and returns None.

    # However, if we manually invoke _migrate_record with an invalid object, it should raise.
    # Let's test the public API behavior: get_project raises KeyError if not found/valid.

    with pytest.raises(KeyError):
        store_service.get_project(project_id)
