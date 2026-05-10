"""
Backend Integration Test Fixtures.

Provides reusable fixtures for backend integration testing:
- Database context managers
- Service mocks and stubs
- Test data generators
- API client factories
"""

from __future__ import annotations

import logging
import os
import sqlite3
import sys
import tempfile
from collections.abc import Generator
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Any
from unittest.mock import MagicMock, patch

import pytest

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from tests.integration.conftest import (
    TEST_CONFIG,
    IntegrationTestClient,
    IntegrationTestConfig,
)

logger = logging.getLogger(__name__)


# =============================================================================
# Database Fixtures
# =============================================================================


@dataclass
class DatabaseTestContext:
    """
    Context for database testing with automatic cleanup.

    Usage:
        with create_test_database() as ctx:
            ctx.execute("INSERT INTO profiles (name) VALUES (?)", ("test",))
            rows = ctx.query("SELECT * FROM profiles")
    """

    connection: sqlite3.Connection
    path: Path
    tables_created: list[str] = field(default_factory=list)
    _should_cleanup: bool = True

    def execute(self, sql: str, params: tuple = ()) -> sqlite3.Cursor:
        """Execute SQL statement."""
        return self.connection.execute(sql, params)

    def executemany(self, sql: str, params_list: list[tuple]) -> sqlite3.Cursor:
        """Execute SQL statement with multiple parameter sets."""
        return self.connection.executemany(sql, params_list)

    def query(self, sql: str, params: tuple = ()) -> list[dict[str, Any]]:
        """Execute query and return results as list of dicts."""
        cursor = self.connection.execute(sql, params)
        columns = [desc[0] for desc in cursor.description]
        return [dict(zip(columns, row)) for row in cursor.fetchall()]

    def query_one(self, sql: str, params: tuple = ()) -> dict[str, Any] | None:
        """Execute query and return single result or None."""
        results = self.query(sql, params)
        return results[0] if results else None

    def commit(self) -> None:
        """Commit current transaction."""
        self.connection.commit()

    def rollback(self) -> None:
        """Rollback current transaction."""
        self.connection.rollback()

    def create_table(self, name: str, schema: str) -> None:
        """Create a table and track it for cleanup."""
        self.execute(f"CREATE TABLE IF NOT EXISTS {name} ({schema})")
        self.tables_created.append(name)

    def seed_data(self, table: str, rows: list[dict[str, Any]]) -> int:
        """Insert seed data into a table. Returns number of rows inserted."""
        if not rows:
            return 0

        columns = list(rows[0].keys())
        placeholders = ", ".join(["?" for _ in columns])
        column_names = ", ".join(columns)

        sql = f"INSERT INTO {table} ({column_names}) VALUES ({placeholders})"
        values = [tuple(row[col] for col in columns) for row in rows]

        self.executemany(sql, values)
        return len(rows)

    def cleanup(self) -> None:
        """Drop all created tables and close connection."""
        if self._should_cleanup:
            for table in reversed(self.tables_created):
                try:
                    self.execute(f"DROP TABLE IF EXISTS {table}")
                except Exception as e:
                    logger.warning(f"Failed to drop table {table}: {e}")

        try:
            self.connection.close()
        except Exception as e:
            logger.warning(f"Failed to close connection: {e}")


@contextmanager
def create_test_database(
    path: Path | None = None,
    in_memory: bool = False,
    cleanup: bool = True,
) -> Generator[DatabaseTestContext, None, None]:
    """
    Create a test database with automatic cleanup.

    Args:
        path: Path to database file. If None, creates temp file.
        in_memory: If True, creates in-memory database.
        cleanup: If True, drops tables and closes connection on exit.

    Yields:
        DatabaseTestContext with connection and utilities.

    Usage:
        with create_test_database() as db:
            db.create_table("users", "id INTEGER PRIMARY KEY, name TEXT")
            db.seed_data("users", [{"id": 1, "name": "Test"}])
            users = db.query("SELECT * FROM users")
    """
    if in_memory:
        db_path = Path(":memory:")
        connection = sqlite3.connect(":memory:")
    else:
        if path is None:
            fd, temp_path = tempfile.mkstemp(suffix=".sqlite", prefix="vs_test_")
            os.close(fd)
            db_path = Path(temp_path)
        else:
            db_path = path
        connection = sqlite3.connect(str(db_path))

    # Enable foreign keys
    connection.execute("PRAGMA foreign_keys = ON")

    ctx = DatabaseTestContext(
        connection=connection,
        path=db_path,
        _should_cleanup=cleanup,
    )

    try:
        yield ctx
    finally:
        ctx.cleanup()

        # Remove temp file if we created it
        if not in_memory and path is None and db_path.exists():
            try:
                db_path.unlink()
            except Exception as e:
                logger.warning(f"Failed to remove temp database: {e}")


# =============================================================================
# Service Fixtures
# =============================================================================


@dataclass
class ServiceTestContext:
    """
    Context for testing services with mocked dependencies.

    Usage:
        with ServiceTestContext() as ctx:
            ctx.mock_engine_service()
            ctx.mock_storage_service()
            # Test your service
    """

    mocks: dict[str, MagicMock] = field(default_factory=dict)
    patches: list[Any] = field(default_factory=list)

    def mock_engine_service(self, **overrides) -> MagicMock:
        """Create mock engine service."""
        mock = MagicMock()
        mock.get_engines.return_value = overrides.get(
            "engines",
            [
                {"id": "xtts_v2", "name": "XTTS v2", "status": "ready"},
                {"id": "chatterbox", "name": "Chatterbox", "status": "ready"},
            ],
        )
        mock.get_engine_status.return_value = overrides.get("status", "ready")
        mock.synthesize.return_value = overrides.get("audio_id", "test-audio-123")
        self.mocks["engine_service"] = mock
        return mock

    def mock_storage_service(self, **overrides) -> MagicMock:
        """Create mock storage service."""
        mock = MagicMock()
        mock.save_project.return_value = overrides.get("project_id", "test-project-123")
        mock.load_project.return_value = overrides.get(
            "project",
            {
                "id": "test-project-123",
                "name": "Test Project",
                "created_at": datetime.utcnow().isoformat(),
            },
        )
        mock.list_projects.return_value = overrides.get("projects", [])
        self.mocks["storage_service"] = mock
        return mock

    def mock_audio_service(self, **overrides) -> MagicMock:
        """Create mock audio service."""
        mock = MagicMock()
        mock.save_audio.return_value = overrides.get("audio_id", "test-audio-123")
        mock.load_audio.return_value = overrides.get("audio_data", b"\x00" * 1000)
        mock.get_audio_metadata.return_value = overrides.get(
            "metadata",
            {
                "id": "test-audio-123",
                "duration": 5.0,
                "sample_rate": 22050,
            },
        )
        self.mocks["audio_service"] = mock
        return mock

    def mock_profile_service(self, **overrides) -> MagicMock:
        """Create mock profile service."""
        mock = MagicMock()
        mock.create_profile.return_value = overrides.get("profile_id", "test-profile-123")
        mock.get_profile.return_value = overrides.get(
            "profile",
            {
                "id": "test-profile-123",
                "name": "Test Profile",
                "language": "en",
            },
        )
        mock.list_profiles.return_value = overrides.get("profiles", [])
        self.mocks["profile_service"] = mock
        return mock

    def patch(self, target: str, **kwargs) -> MagicMock:
        """Create a patch and track it for cleanup."""
        patcher = patch(target, **kwargs)
        mock = patcher.start()
        self.patches.append(patcher)
        self.mocks[target] = mock
        return mock

    def cleanup(self) -> None:
        """Stop all patches."""
        for patcher in self.patches:
            try:
                patcher.stop()
            except Exception as e:
                logger.warning(f"Failed to stop patch: {e}")


@contextmanager
def service_context() -> Generator[ServiceTestContext, None, None]:
    """
    Create service test context with automatic cleanup.

    Usage:
        with service_context() as ctx:
            engine_mock = ctx.mock_engine_service()
            storage_mock = ctx.mock_storage_service()
            # Test your code
    """
    ctx = ServiceTestContext()
    try:
        yield ctx
    finally:
        ctx.cleanup()


# =============================================================================
# API Client Fixtures
# =============================================================================


def create_test_client(
    app: Any = None,
    config: IntegrationTestConfig | None = None,
) -> IntegrationTestClient:
    """
    Create an enhanced test client for API testing.

    Args:
        app: FastAPI application instance. If None, imports from backend.
        config: Test configuration. If None, uses default.

    Returns:
        IntegrationTestClient with tracking and validation. The returned
        wrapper exposes ``close()`` and ``__enter__``/``__exit__`` so callers
        and fixtures can drive FastAPI lifespan shutdown (which otherwise
        never runs and has been observed to leave non-daemon worker threads
        alive on CI; see PR #49 post-pytest hang).

    Usage:
        with create_test_client() as client:
            response = client.get("/api/health")
            assert response.is_success
    """
    from fastapi.testclient import TestClient

    if app is None:
        try:
            from backend.api.main import app as fastapi_app

            app = fastapi_app
        except ImportError:
            # Create minimal mock app
            from fastapi import FastAPI

            app = FastAPI()

            @app.get("/api/health")
            def health():
                return {"status": "ok"}

    # Drive lifespan startup explicitly via __enter__ so on_shutdown can later
    # be triggered via __exit__ / close(); IntegrationTestClient delegates the
    # context-manager protocol to the underlying TestClient.
    test_client = TestClient(app)
    test_client.__enter__()
    return IntegrationTestClient(
        client=test_client,
        config=config or TEST_CONFIG,
    )


# =============================================================================
# Test Data Fixtures
# =============================================================================


@pytest.fixture
def sample_profile_data() -> dict[str, Any]:
    """Sample profile data for testing."""
    return {
        "name": "Integration Test Profile",
        "description": "Profile created for integration testing",
        "language": "en",
        "engine": "xtts_v2",
    }


@pytest.fixture
def sample_project_data() -> dict[str, Any]:
    """Sample project data for testing."""
    return {
        "name": "Integration Test Project",
        "description": "Project created for integration testing",
        "created_at": datetime.utcnow().isoformat(),
        "settings": {
            "sample_rate": 22050,
            "quality_preset": "standard",
        },
    }


@pytest.fixture
def sample_synthesis_request() -> dict[str, Any]:
    """Sample synthesis request for testing."""
    return {
        "profile_id": "test-profile-integration",
        "text": "This is a test sentence for integration testing.",
        "engine": "xtts_v2",
        "language": "en",
        "sample_rate": 22050,
    }


@pytest.fixture
def sample_audio_metadata() -> dict[str, Any]:
    """Sample audio metadata for testing."""
    return {
        "id": "test-audio-integration",
        "filename": "test_audio.wav",
        "duration": 5.0,
        "sample_rate": 22050,
        "channels": 1,
        "format": "wav",
        "size_bytes": 220500,
    }


# =============================================================================
# Pytest Fixtures
# =============================================================================


@pytest.fixture(scope="function")
def db_context() -> Generator[DatabaseTestContext, None, None]:
    """Provide database context for testing."""
    with create_test_database(in_memory=True) as ctx:
        yield ctx


@pytest.fixture(scope="function")
def svc_context() -> Generator[ServiceTestContext, None, None]:
    """Provide service context for testing."""
    with service_context() as ctx:
        yield ctx


@pytest.fixture(scope="function")
def integration_client() -> Generator[IntegrationTestClient, None, None]:
    """Provide integration test client (with lifespan shutdown on teardown)."""
    client = create_test_client()
    try:
        yield client
    finally:
        client.close()


# =============================================================================
# Schema Fixtures for Common Tables
# =============================================================================


SCHEMA_PROFILES = """
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    language TEXT DEFAULT 'en',
    engine TEXT DEFAULT 'xtts_v2',
    created_at TEXT,
    updated_at TEXT
"""

SCHEMA_PROJECTS = """
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    settings TEXT,
    created_at TEXT,
    updated_at TEXT
"""

SCHEMA_AUDIO = """
    id TEXT PRIMARY KEY,
    project_id TEXT,
    filename TEXT NOT NULL,
    duration REAL,
    sample_rate INTEGER,
    channels INTEGER,
    format TEXT,
    size_bytes INTEGER,
    created_at TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
"""

SCHEMA_JOBS = """
    id TEXT PRIMARY KEY,
    project_id TEXT,
    type TEXT NOT NULL,
    status TEXT DEFAULT 'pending',
    progress REAL DEFAULT 0.0,
    result TEXT,
    error TEXT,
    created_at TEXT,
    started_at TEXT,
    completed_at TEXT,
    FOREIGN KEY (project_id) REFERENCES projects(id)
"""


@pytest.fixture
def db_with_standard_schema(
    db_context: DatabaseTestContext,
) -> DatabaseTestContext:
    """Provide database with standard tables created."""
    db_context.create_table("profiles", SCHEMA_PROFILES)
    db_context.create_table("projects", SCHEMA_PROJECTS)
    db_context.create_table("audio", SCHEMA_AUDIO)
    db_context.create_table("jobs", SCHEMA_JOBS)
    return db_context
