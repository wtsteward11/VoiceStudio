"""
Pytest configuration and fixtures for quality features integration tests.
"""

# Import the FastAPI app
import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

# Add project root to path
project_root = Path(__file__).parent.parent.parent.parent
sys.path.insert(0, str(project_root))

from backend.api.main import app


@pytest.fixture
def client():
    """Create a test client for the FastAPI app.

    ``with TestClient(app)`` so FastAPI lifespan ``shutdown`` actually runs
    (engine stop, scheduler stop, db close); without it the heavy-startup task
    is never created and shutdown is skipped, which has been observed to leave
    non-daemon worker threads alive on CI and block process exit (PR #49).
    """
    with TestClient(app) as c:
        yield c


@pytest.fixture
def sample_profile_id():
    """Sample profile ID for testing."""
    return "test-profile-123"


@pytest.fixture
def sample_reference_audio_id():
    """Sample reference audio ID for testing."""
    return "test-audio-123"


@pytest.fixture
def sample_test_text():
    """Sample test text for synthesis."""
    return "This is a test sentence for quality testing."
