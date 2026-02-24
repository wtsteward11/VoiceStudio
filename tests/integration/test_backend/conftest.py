"""
Backend Integration Test Configuration.

Provides pytest fixtures and configuration for backend integration tests.
"""

import sys
from pathlib import Path

import pytest

# Add project root to path
PROJECT_ROOT = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

# Import fixtures from local module
from .fixtures import (
    SCHEMA_AUDIO,
    SCHEMA_JOBS,
    SCHEMA_PROFILES,
    SCHEMA_PROJECTS,
    DatabaseTestContext,
    ServiceTestContext,
    create_test_client,
    create_test_database,
    db_context,
    db_with_standard_schema,
    integration_client,
    sample_audio_metadata,
    sample_profile_data,
    sample_project_data,
    sample_synthesis_request,
    service_context,
    svc_context,
)

# Re-export all fixtures for pytest discovery
__all__ = [
    "SCHEMA_AUDIO",
    "SCHEMA_JOBS",
    "SCHEMA_PROFILES",
    "SCHEMA_PROJECTS",
    "DatabaseTestContext",
    "ServiceTestContext",
    "create_test_client",
    "create_test_database",
    "db_context",
    "db_with_standard_schema",
    "integration_client",
    "sample_audio_metadata",
    "sample_profile_data",
    "sample_project_data",
    "sample_synthesis_request",
    "service_context",
    "svc_context",
]


# =============================================================================
# Backend-Specific Fixtures
# =============================================================================


@pytest.fixture(scope="session")
def backend_app():
    """
    Provide FastAPI application for testing.

    Uses session scope to reuse app across all tests.
    """
    try:
        from backend.api.main import app

        return app
    except ImportError:
        # Return None if backend not available
        pytest.skip("Backend module not available")
        return None


@pytest.fixture
def test_client_with_app(backend_app):
    """
    Provide test client connected to actual backend app.

    Use this when you need to test against real routes.
    """
    if backend_app is None:
        pytest.skip("Backend app not available")
    return create_test_client(app=backend_app)


@pytest.fixture
def mock_backend_services():
    """
    Mock all backend services for isolated testing.

    Returns dict of mock objects.
    """
    from unittest.mock import MagicMock, patch

    mocks = {}
    patches = []

    # Mock engine service
    engine_mock = MagicMock()
    engine_mock.get_engines.return_value = [
        {"id": "xtts_v2", "name": "XTTS v2", "status": "ready"},
        {"id": "chatterbox", "name": "Chatterbox", "status": "ready"},
    ]
    mocks["engine_service"] = engine_mock

    # Mock storage service
    storage_mock = MagicMock()
    storage_mock.list_projects.return_value = []
    mocks["storage_service"] = storage_mock

    # Apply patches
    try:
        patches.append(
            patch("backend.services.engine_service.EngineService", return_value=engine_mock)
        )
        for p in patches:
            p.start()
    except Exception:
        pass  # Module may not be available

    yield mocks

    # Cleanup
    for p in patches:
        try:
            p.stop()
        # ALLOWED: bare except - Best effort cleanup, failure is acceptable
        except Exception:
            pass


@pytest.fixture
def clean_test_state():
    """
    Ensure clean test state before and after each test.

    Clears any cached state that might leak between tests.
    """
    # Pre-test cleanup
    import gc

    gc.collect()

    yield

    # Post-test cleanup
    gc.collect()


# =============================================================================
# Test Markers
# =============================================================================


def pytest_configure(config):
    """Register custom markers."""
    config.addinivalue_line(
        "markers",
        "backend_integration: marks test as backend integration test",
    )
    config.addinivalue_line(
        "markers",
        "requires_backend: marks test as requiring running backend",
    )
    config.addinivalue_line(
        "markers",
        "database: marks test as database integration test",
    )
    config.addinivalue_line(
        "markers",
        "slow: marks test as slow running",
    )
