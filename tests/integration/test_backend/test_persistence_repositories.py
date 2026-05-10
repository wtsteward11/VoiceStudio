"""
Persistence Repository Integration Tests.

Tests for job, training, and session repository persistence operations.
Backend-Frontend Integration Plan - Phase 6: Integration tests.
"""

import contextlib
import logging
import os
import tempfile
import uuid
from datetime import datetime, timedelta

import aiosqlite
import pytest

from .base import AsyncIntegrationTestBase, integration

logger = logging.getLogger(__name__)


async def setup_job_tables(db_path: str) -> None:
    """Create job tables for testing."""
    async with aiosqlite.connect(db_path) as conn:
        await conn.execute(
            """
            CREATE TABLE IF NOT EXISTS job_history (
                id TEXT PRIMARY KEY,
                job_type TEXT NOT NULL,
                name TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                progress REAL DEFAULT 0.0,
                current_step TEXT,
                current_step_index INTEGER,
                total_steps INTEGER,
                error TEXT,
                result_path TEXT,
                result_id TEXT,
                estimated_time_remaining INTEGER,
                metadata TEXT DEFAULT '{}',
                user_id TEXT,
                created_at TEXT DEFAULT (datetime('now')),
                updated_at TEXT DEFAULT (datetime('now')),
                started_at TEXT,
                completed_at TEXT,
                deleted_at TEXT
            )
        """
        )
        await conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_job_history_status ON job_history(status)"
        )
        await conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_job_history_type ON job_history(job_type)"
        )
        await conn.commit()


async def setup_training_tables(db_path: str) -> None:
    """Create training job tables for testing (aligned with v001_core_persistence_tables)."""
    async with aiosqlite.connect(db_path) as conn:
        await conn.execute(
            """
            CREATE TABLE IF NOT EXISTS training_jobs (
                id TEXT PRIMARY KEY,
                dataset_id TEXT,
                engine_id TEXT,
                model_name TEXT,
                status TEXT NOT NULL DEFAULT 'pending',
                progress REAL DEFAULT 0.0,
                current_epoch INTEGER DEFAULT 0,
                total_epochs INTEGER,
                current_step INTEGER DEFAULT 0,
                total_steps INTEGER,
                learning_rate REAL,
                loss REAL,
                validation_loss REAL,
                best_loss REAL,
                metrics TEXT,
                hyperparameters TEXT,
                checkpoints TEXT,
                error TEXT,
                output_path TEXT,
                user_id TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                started_at TEXT,
                completed_at TEXT,
                deleted_at TEXT
            )
        """
        )
        await conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_training_jobs_status ON training_jobs(status)"
        )
        await conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_training_jobs_dataset ON training_jobs(dataset_id)"
        )
        await conn.commit()


async def setup_session_tables(db_path: str) -> None:
    """Create session tables for testing (aligned with v001_core_persistence_tables)."""
    async with aiosqlite.connect(db_path) as conn:
        await conn.execute(
            """
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                token_hash TEXT NOT NULL,
                device_info TEXT,
                ip_address TEXT,
                user_agent TEXT,
                is_active INTEGER DEFAULT 1,
                last_activity TEXT,
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL DEFAULT (datetime('now')),
                deleted_at TEXT
            )
        """
        )
        await conn.execute("CREATE INDEX IF NOT EXISTS idx_sessions_user ON sessions(user_id)")
        await conn.execute(
            "CREATE INDEX IF NOT EXISTS idx_sessions_token ON sessions(token_hash)"
        )
        await conn.commit()


# =============================================================================
# Job Repository Tests
# =============================================================================


class TestJobRepository(AsyncIntegrationTestBase):
    """Tests for the JobRepository class."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        with contextlib.suppress(OSError):
            os.unlink(db_path)

    @pytest.fixture
    async def job_repo(self, temp_db_path):
        """Create a JobRepository instance with a temp database."""
        from backend.data.repositories.job_repository import JobRepository
        from backend.data.repository_base import ConnectionConfig, DatabaseType

        # Create tables first
        await setup_job_tables(temp_db_path)

        config = ConnectionConfig(database_type=DatabaseType.SQLITE, sqlite_path=temp_db_path)
        repo = JobRepository(config)
        return repo

    @integration
    @pytest.mark.asyncio
    async def test_create_job(self, job_repo):
        """Test creating a job entry."""
        from backend.data.repositories.job_repository import JobEntity

        job_id = str(uuid.uuid4())
        job_entity = JobEntity(
            id=job_id,
            job_type="batch_synthesis",
            status="pending",
            progress=0.0,
        )

        result = await job_repo.create(job_entity)
        assert result is not None
        assert result.id == job_id
        assert result.status == "pending"

    @integration
    @pytest.mark.asyncio
    async def test_get_job_by_id(self, job_repo):
        """Test retrieving a job by ID."""
        from backend.data.repositories.job_repository import JobEntity

        job_id = str(uuid.uuid4())
        job_entity = JobEntity(
            id=job_id,
            job_type="synthesis",
            status="pending",
            progress=0.0,
        )

        await job_repo.create(job_entity)
        retrieved = await job_repo.get_by_id(job_id)

        assert retrieved is not None
        assert retrieved.id == job_id
        assert retrieved.job_type == "synthesis"

    @integration
    @pytest.mark.asyncio
    async def test_update_job_status(self, job_repo):
        """Test updating job status and progress."""
        from backend.data.repositories.job_repository import JobEntity

        job_id = str(uuid.uuid4())
        job_entity = JobEntity(
            id=job_id,
            job_type="batch_synthesis",
            status="pending",
            progress=0.0,
        )

        await job_repo.create(job_entity)

        # Update status
        updated = await job_repo.update(
            job_id,
            {
                "status": "running",
                "progress": 0.5,
            },
        )

        assert updated is not None
        assert updated.status == "running"
        assert updated.progress == 0.5

    @integration
    @pytest.mark.asyncio
    async def test_find_jobs_by_status(self, job_repo):
        """Test finding jobs filtered by status."""
        from backend.data.repositories.job_repository import JobEntity

        # Create multiple jobs
        for _i in range(3):
            entity = JobEntity(
                id=str(uuid.uuid4()),
                job_type="synthesis",
                status="pending",
                progress=0.0,
            )
            await job_repo.create(entity)

        for _i in range(2):
            entity = JobEntity(
                id=str(uuid.uuid4()),
                job_type="synthesis",
                status="completed",
                progress=1.0,
            )
            await job_repo.create(entity)

        # Find by status
        pending = await job_repo.find({"status": "pending"})
        completed = await job_repo.find({"status": "completed"})

        assert len(pending) == 3
        assert len(completed) == 2

    @integration
    @pytest.mark.asyncio
    async def test_delete_job(self, job_repo):
        """Test deleting a job."""
        from backend.data.repositories.job_repository import JobEntity

        job_id = str(uuid.uuid4())
        entity = JobEntity(
            id=job_id,
            job_type="synthesis",
            status="completed",
            progress=1.0,
        )
        await job_repo.create(entity)

        # Verify job exists
        assert await job_repo.get_by_id(job_id) is not None

        # Delete (hard delete)
        success = await job_repo.delete(job_id, soft=False)
        assert success is True

        # Verify job is gone
        assert await job_repo.get_by_id(job_id) is None

    @integration
    @pytest.mark.asyncio
    async def test_job_not_found(self, job_repo):
        """Test getting a non-existent job returns None."""
        result = await job_repo.get_by_id("non-existent-id")
        assert result is None


# =============================================================================
# Training Job Repository Tests
# =============================================================================


class TestTrainingJobRepository(AsyncIntegrationTestBase):
    """Tests for the TrainingJobRepository class."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        with contextlib.suppress(OSError):
            os.unlink(db_path)

    @pytest.fixture
    async def training_repo(self, temp_db_path):
        """Create a TrainingJobRepository instance."""
        from backend.data.repositories.training_repository import TrainingJobRepository
        from backend.data.repository_base import ConnectionConfig, DatabaseType

        # Create tables first
        await setup_training_tables(temp_db_path)

        config = ConnectionConfig(database_type=DatabaseType.SQLITE, sqlite_path=temp_db_path)
        repo = TrainingJobRepository(config)
        return repo

    @integration
    @pytest.mark.asyncio
    async def test_create_training_job(self, training_repo):
        """Test creating a training job."""
        from backend.data.repositories.training_repository import TrainingJobEntity

        job_id = str(uuid.uuid4())
        entity = TrainingJobEntity(
            id=job_id,
            dataset_id="dataset-001",
            model_name="xtts_v2",
            engine_id="xtts_v2",
            status="pending",
            current_epoch=0,
            total_epochs=100,
        )

        result = await training_repo.create(entity)
        assert result is not None
        assert result.id == job_id
        assert result.model_name == "xtts_v2"

    @integration
    @pytest.mark.asyncio
    async def test_update_training_progress(self, training_repo):
        """Test updating training job progress."""
        from backend.data.repositories.training_repository import TrainingJobEntity

        job_id = str(uuid.uuid4())
        entity = TrainingJobEntity(
            id=job_id,
            dataset_id="dataset-001",
            model_name="rvc",
            engine_id="rvc",
            status="running",
            current_epoch=0,
            total_epochs=50,
        )
        await training_repo.create(entity)

        # Update progress
        updated = await training_repo.update(
            job_id,
            {
                "current_epoch": 25,
                "loss": 0.05,
            },
        )

        assert updated is not None
        assert updated.current_epoch == 25

    @integration
    @pytest.mark.asyncio
    async def test_find_training_jobs_by_profile(self, training_repo):
        """Test finding training jobs by profile ID."""
        from backend.data.repositories.training_repository import TrainingJobEntity

        dataset_id = "dataset-profile-test"

        # Create jobs for this dataset (surrogate for prior profile_id grouping)
        for _i in range(3):
            entity = TrainingJobEntity(
                id=str(uuid.uuid4()),
                dataset_id=dataset_id,
                model_name="xtts_v2",
                engine_id="xtts_v2",
                status="completed",
                current_epoch=100,
                total_epochs=100,
            )
            await training_repo.create(entity)

        # Create jobs for another dataset
        other_entity = TrainingJobEntity(
            id=str(uuid.uuid4()),
            dataset_id="other-dataset",
            model_name="rvc",
            engine_id="rvc",
            status="completed",
            current_epoch=50,
            total_epochs=50,
        )
        await training_repo.create(other_entity)

        # Find by dataset
        jobs = await training_repo.find({"dataset_id": dataset_id})
        assert len(jobs) == 3
        assert all(j.dataset_id == dataset_id for j in jobs)

    @integration
    @pytest.mark.asyncio
    async def test_find_active_training_jobs(self, training_repo):
        """Test finding active training jobs."""
        from backend.data.repositories.training_repository import TrainingJobEntity

        # Create active job
        active_entity = TrainingJobEntity(
            id=str(uuid.uuid4()),
            dataset_id="dataset-001",
            model_name="xtts_v2",
            engine_id="xtts_v2",
            status="running",
            current_epoch=10,
            total_epochs=100,
        )
        await training_repo.create(active_entity)

        # Create completed job
        completed_entity = TrainingJobEntity(
            id=str(uuid.uuid4()),
            dataset_id="dataset-002",
            model_name="rvc",
            engine_id="rvc",
            status="completed",
            current_epoch=50,
            total_epochs=50,
        )
        await training_repo.create(completed_entity)

        active = await training_repo.find({"status": "running"})
        assert len(active) == 1
        assert active[0].status == "running"


# =============================================================================
# Session Repository Tests
# =============================================================================


class TestSessionRepository(AsyncIntegrationTestBase):
    """Tests for the SessionRepository class."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        with contextlib.suppress(OSError):
            os.unlink(db_path)

    @pytest.fixture
    async def session_repo(self, temp_db_path):
        """Create a SessionRepository instance."""
        from backend.data.repositories.session_repository import SessionRepository
        from backend.data.repository_base import ConnectionConfig, DatabaseType

        # Create tables first
        await setup_session_tables(temp_db_path)

        config = ConnectionConfig(database_type=DatabaseType.SQLITE, sqlite_path=temp_db_path)
        repo = SessionRepository(config)
        return repo

    @integration
    @pytest.mark.asyncio
    async def test_create_session(self, session_repo):
        """Test creating a session."""
        from backend.data.repositories.session_repository import SessionEntity

        session_id = str(uuid.uuid4())
        entity = SessionEntity(
            id=session_id,
            user_id="user-001",
            token_hash="token-hash-create",
            expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
        )

        result = await session_repo.create(entity)
        assert result is not None
        assert result.id == session_id

    @integration
    @pytest.mark.asyncio
    async def test_get_session(self, session_repo):
        """Test retrieving a session by ID."""
        from backend.data.repositories.session_repository import SessionEntity

        session_id = str(uuid.uuid4())
        entity = SessionEntity(
            id=session_id,
            user_id="user-001",
            token_hash="token-hash-get",
            expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
        )
        await session_repo.create(entity)

        retrieved = await session_repo.get_by_id(session_id)
        assert retrieved is not None
        assert retrieved.id == session_id

    @integration
    @pytest.mark.asyncio
    async def test_update_session_activity(self, session_repo):
        """Test updating session last_activity timestamp."""
        from backend.data.repositories.session_repository import SessionEntity

        session_id = str(uuid.uuid4())
        original_time = datetime.utcnow() - timedelta(hours=1)
        original_iso = original_time.isoformat()

        entity = SessionEntity(
            id=session_id,
            user_id="user-001",
            token_hash="token-hash-activity",
            expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
            created_at=original_time,
            last_activity=original_iso,
        )
        await session_repo.create(entity)

        new_time = datetime.utcnow()
        updated = await session_repo.update(
            session_id,
            {
                "last_activity": new_time.isoformat(),
            },
        )

        assert updated is not None
        # The updated timestamp should be newer
        assert updated.last_activity != original_iso

    @integration
    @pytest.mark.asyncio
    async def test_get_all_sessions(self, session_repo):
        """Test getting all sessions."""
        from backend.data.repositories.session_repository import SessionEntity

        # Create multiple sessions
        datetime.utcnow()
        for i in range(3):
            entity = SessionEntity(
                id=str(uuid.uuid4()),
                user_id=f"user-{i}",
                token_hash=f"token-hash-list-{i}",
                expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
            )
            await session_repo.create(entity)

        # Get all sessions
        all_sessions = await session_repo.get_all()
        assert len(all_sessions) == 3

    @integration
    @pytest.mark.asyncio
    async def test_delete_session(self, session_repo):
        """Test deleting a session."""
        from backend.data.repositories.session_repository import SessionEntity

        session_id = str(uuid.uuid4())
        entity = SessionEntity(
            id=session_id,
            user_id="user-001",
            token_hash="token-hash-delete",
            expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
        )
        await session_repo.create(entity)

        # Verify session exists
        assert await session_repo.get_by_id(session_id) is not None

        # Delete (hard delete)
        success = await session_repo.delete(session_id, soft=False)
        assert success is True

        # Verify session is gone
        assert await session_repo.get_by_id(session_id) is None


# =============================================================================
# Cross-Repository Integration Tests
# =============================================================================


class TestRepositoryIntegration(AsyncIntegrationTestBase):
    """Tests for cross-repository operations and data integrity."""

    @pytest.fixture
    def temp_db_path(self):
        """Create a temporary database file."""
        with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
            db_path = f.name
        yield db_path
        with contextlib.suppress(OSError):
            os.unlink(db_path)

    @pytest.fixture
    async def repositories(self, temp_db_path):
        """Create all repository instances sharing the same database."""
        from backend.data.repositories.job_repository import JobRepository
        from backend.data.repositories.session_repository import SessionRepository
        from backend.data.repositories.training_repository import TrainingJobRepository
        from backend.data.repository_base import ConnectionConfig, DatabaseType

        # Create all tables first
        await setup_job_tables(temp_db_path)
        await setup_training_tables(temp_db_path)
        await setup_session_tables(temp_db_path)

        config = ConnectionConfig(database_type=DatabaseType.SQLITE, sqlite_path=temp_db_path)

        job_repo = JobRepository(config)
        training_repo = TrainingJobRepository(config)
        session_repo = SessionRepository(config)

        return {
            "job": job_repo,
            "training": training_repo,
            "session": session_repo,
        }

    @integration
    @pytest.mark.asyncio
    async def test_repositories_share_connection(self, repositories):
        """Test that repositories correctly share database connection."""
        from backend.data.repositories.job_repository import JobEntity
        from backend.data.repositories.session_repository import SessionEntity
        from backend.data.repositories.training_repository import TrainingJobEntity

        # Create entries in all repos
        job_id = str(uuid.uuid4())
        job_entity = JobEntity(
            id=job_id,
            job_type="synthesis",
            status="pending",
            progress=0.0,
        )
        await repositories["job"].create(job_entity)

        training_id = str(uuid.uuid4())
        training_entity = TrainingJobEntity(
            id=training_id,
            dataset_id="dataset-001",
            model_name="xtts_v2",
            engine_id="xtts_v2",
            status="pending",
            current_epoch=0,
            total_epochs=100,
        )
        await repositories["training"].create(training_entity)

        session_id = str(uuid.uuid4())
        session_entity = SessionEntity(
            id=session_id,
            user_id="user-001",
            token_hash="token-hash-cross",
            expires_at=(datetime.utcnow() + timedelta(days=1)).isoformat(),
        )
        await repositories["session"].create(session_entity)

        # Verify all entries exist
        assert await repositories["job"].get_by_id(job_id) is not None
        assert await repositories["training"].get_by_id(training_id) is not None
        assert await repositories["session"].get_by_id(session_id) is not None

    @integration
    @pytest.mark.asyncio
    async def test_repository_count_operations(self, repositories):
        """Test count operations across repositories."""
        from backend.data.repositories.job_repository import JobEntity

        # Create multiple jobs
        for i in range(5):
            entity = JobEntity(
                id=str(uuid.uuid4()),
                job_type="synthesis",
                status="pending" if i < 3 else "completed",
                progress=0.0 if i < 3 else 1.0,
            )
            await repositories["job"].create(entity)

        # Count all jobs
        total = await repositories["job"].count()
        assert total == 5

        # Count by filter
        pending_count = await repositories["job"].count({"status": "pending"})
        assert pending_count == 3

        completed_count = await repositories["job"].count({"status": "completed"})
        assert completed_count == 2
