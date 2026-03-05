"""
SQLite Repository Integration Tests.

Task 2.3: Integration tests for domain repository layer.
"""

from __future__ import annotations

import os
import tempfile
import uuid
from datetime import datetime
from pathlib import Path

import pytest

from tests.integration.test_backend.base import IntegrationTestBase, integration


@pytest.fixture
def temp_db_path():
    """Create a temporary database file."""
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
        db_path = f.name
    yield db_path
    try:
        os.unlink(db_path)
    # ALLOWED: bare except - best effort, failure acceptable
    except OSError:
        pass


@pytest.fixture
async def repo_context(temp_db_path):
    """Create DB adapter, run migrations, yield repos."""
    from backend.domain.entities.audio_clip import AudioClip, ClipStatus, ClipType
    from backend.domain.entities.job import Job, JobStatus
    from backend.domain.entities.project import Project, ProjectStatus
    from backend.domain.entities.voice_profile import (
        VoiceGender,
        VoiceProfile,
        VoiceType,
    )
    from backend.infrastructure.adapters.database import (
        DatabaseAdapter,
        close_database_adapter,
    )
    from backend.infrastructure.migrations.initial_schema import run_migrations
    from backend.infrastructure.repositories import (
        SqliteAudioClipRepository,
        SqliteJobRepository,
        SqliteProjectRepository,
        SqliteVoiceProfileRepository,
    )

    conn_str = f"sqlite:///{temp_db_path}"
    adapter = DatabaseAdapter(connection_string=conn_str)
    await adapter.connect()
    await run_migrations(db_path=conn_str)

    voice_repo = SqliteVoiceProfileRepository(db=adapter)
    project_repo = SqliteProjectRepository(db=adapter)
    audio_repo = SqliteAudioClipRepository(db=adapter)
    job_repo = SqliteJobRepository(db=adapter)

    yield {
        "voice": voice_repo,
        "project": project_repo,
        "audio": audio_repo,
        "job": job_repo,
    }

    await adapter.disconnect()


@pytest.mark.asyncio
class TestSqliteVoiceProfileRepository(IntegrationTestBase):
    """Integration tests for SqliteVoiceProfileRepository."""

    @integration
    async def test_save_and_get(self, temp_db_path):
        from backend.domain.entities.voice_profile import (
            VoiceGender,
            VoiceProfile,
            VoiceType,
        )
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.voice_profile_repository import (
            SqliteVoiceProfileRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        repo = SqliteVoiceProfileRepository(db=adapter)
        profile = VoiceProfile(
            name="Test Profile",
            language="en",
            voice_type=VoiceType.CLONED,
            gender=VoiceGender.NEUTRAL,
        )
        saved = await repo.save(profile)
        assert saved.id

        loaded = await repo.get_by_id(saved.id)
        assert loaded is not None
        assert loaded.name == "Test Profile"
        assert loaded.language == "en"

        await adapter.disconnect()

    @integration
    async def test_list_and_count(self, temp_db_path):
        from backend.domain.entities.voice_profile import (
            VoiceGender,
            VoiceProfile,
            VoiceType,
        )
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.voice_profile_repository import (
            SqliteVoiceProfileRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        repo = SqliteVoiceProfileRepository(db=adapter)
        for i in range(3):
            p = VoiceProfile(name=f"Profile {i}", language="en")
            await repo.save(p)

        all_profiles = await repo.list_all(limit=10)
        assert len(all_profiles) == 3
        assert await repo.count() == 3

        await adapter.disconnect()

    @integration
    async def test_delete(self, temp_db_path):
        from backend.domain.entities.voice_profile import VoiceProfile
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.voice_profile_repository import (
            SqliteVoiceProfileRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        repo = SqliteVoiceProfileRepository(db=adapter)
        p = VoiceProfile(name="To Delete")
        saved = await repo.save(p)
        assert await repo.get_by_id(saved.id) is not None

        deleted = await repo.delete(saved.id)
        assert deleted is True
        assert await repo.get_by_id(saved.id) is None

        await adapter.disconnect()


@pytest.mark.asyncio
class TestSqliteProjectRepository(IntegrationTestBase):
    """Integration tests for SqliteProjectRepository."""

    @integration
    async def test_save_and_get(self, temp_db_path):
        from backend.domain.entities.project import Project, ProjectStatus
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.project_repository import (
            SqliteProjectRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        repo = SqliteProjectRepository(db=adapter)
        project = Project(name="Test Project", status=ProjectStatus.DRAFT)
        saved = await repo.save(project)
        assert saved.id

        loaded = await repo.get_by_id(saved.id)
        assert loaded is not None
        assert loaded.name == "Test Project"

        await adapter.disconnect()


@pytest.mark.asyncio
class TestSqliteAudioClipRepository(IntegrationTestBase):
    """Integration tests for SqliteAudioClipRepository."""

    @integration
    async def test_save_and_list_by_project(self, temp_db_path):
        from backend.domain.entities.audio_clip import AudioClip, ClipStatus, ClipType
        from backend.domain.entities.project import Project, ProjectStatus
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.audio_clip_repository import (
            SqliteAudioClipRepository,
        )
        from backend.infrastructure.repositories.project_repository import (
            SqliteProjectRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        project_repo = SqliteProjectRepository(db=adapter)
        audio_repo = SqliteAudioClipRepository(db=adapter)

        project = Project(name="Clip Project")
        await project_repo.save(project)

        clip = AudioClip(
            name="Clip 1",
            project_id=project.id,
            status=ClipStatus.READY,
            clip_type=ClipType.ORIGINAL,
        )
        await audio_repo.save(clip)

        clips = await audio_repo.list_by_project(project.id)
        assert len(clips) == 1
        assert clips[0].name == "Clip 1"

        await adapter.disconnect()


@pytest.mark.asyncio
class TestSqliteJobRepository(IntegrationTestBase):
    """Integration tests for SqliteJobRepository."""

    @integration
    async def test_save_and_get_with_namespace(self, temp_db_path):
        from backend.domain.entities.job import Job, JobStatus
        from backend.infrastructure.adapters.database import DatabaseAdapter
        from backend.infrastructure.migrations.initial_schema import run_migrations
        from backend.infrastructure.repositories.job_repository import (
            SqliteJobRepository,
        )

        adapter = DatabaseAdapter(connection_string=f"sqlite:///{temp_db_path}")
        await adapter.connect()
        await run_migrations(db_path=f"sqlite:///{temp_db_path}")

        repo = SqliteJobRepository(db=adapter)
        job = Job(
            namespace="test_ns",
            job_type="synthesis",
            name="Test Job",
            status=JobStatus.PENDING.value,
        )
        saved = await repo.save(job)
        assert saved.id

        loaded = await repo.get_by_id(saved.id, namespace="test_ns")
        assert loaded is not None
        assert loaded.name == "Test Job"
        assert loaded.namespace == "test_ns"

        await adapter.disconnect()
