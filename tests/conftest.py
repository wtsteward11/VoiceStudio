"""
Phase 9: Test Configuration
Task 9.1: Pytest configuration and fixtures.
"""

# CRITICAL: Add project root to path BEFORE any imports
# This ensures tools.*, scripts.* are importable during collection
import sys
from pathlib import Path

# Guard against thinc/numpy seed overflow with pytest-randomly (C-T2).
# thinc registers a reseed callback that passes the seed to numpy.random.seed()
# without clamping, causing ValueError when seed + CRC32 offset exceeds 2**32.
try:
    import numpy as np

    _original_np_random_seed = np.random.seed

    def _safe_np_random_seed(seed=None):
        if seed is not None and isinstance(seed, int) and seed >= 2**32:
            seed = seed % (2**32 - 1)
        _original_np_random_seed(seed)

    np.random.seed = _safe_np_random_seed
# ALLOWED: bare except - optional dependency, import failure acceptable
except ImportError:
    pass

PROJECT_ROOT = Path(__file__).parent.parent
_project_root_str = str(PROJECT_ROOT)
# Force insert at position 0 to ensure it takes precedence
sys.path.insert(0, _project_root_str)

# Now import everything else
import asyncio
import os
import tempfile
from collections.abc import Generator
from typing import Any

import pytest


def pytest_configure(config):
    """Configure project root path and register custom markers."""
    project_root = str(Path(__file__).parent.parent)
    if project_root not in sys.path:
        sys.path.insert(0, project_root)
    else:
        sys.path.remove(project_root)
        sys.path.insert(0, project_root)

    config.addinivalue_line(
        "markers", "slow: marks tests as slow (deselect with '-m \"not slow\"')"
    )
    config.addinivalue_line("markers", "integration: marks tests as integration tests")
    config.addinivalue_line("markers", "e2e: marks tests as end-to-end tests")
    config.addinivalue_line("markers", "gpu: marks tests that require GPU")
    config.addinivalue_line("markers", "engine: marks tests that require a voice engine")
    config.addinivalue_line(
        "markers", "canonical_audio: Tests that use the canonical test audio (Allan Watts)"
    )
    config.addinivalue_line(
        "markers", "requires_models: Tests that require downloaded voice models"
    )
    config.addinivalue_line(
        "markers", "requires_winappdriver: Tests that require WinAppDriver for UI automation"
    )


def pytest_collection_modifyitems(config, items):
    """Auto-skip env-dependent tests when prerequisites are not met."""
    skip_models = pytest.mark.skip(reason="VOICESTUDIO_MODELS_PATH not set or empty")
    skip_backend = pytest.mark.skip(reason="VOICESTUDIO_BACKEND_URL not set or backend not running")
    skip_winappdriver = pytest.mark.skip(reason="WinAppDriver not available")
    skip_gpu = pytest.mark.skip(reason="GPU not available")

    for item in items:
        if "requires_models" in item.keywords and not os.getenv("VOICESTUDIO_MODELS_PATH"):
            item.add_marker(skip_models)
        if "requires_backend" in item.keywords and not os.getenv("VOICESTUDIO_BACKEND_URL"):
            item.add_marker(skip_backend)
        if "requires_winappdriver" in item.keywords and os.getenv("VOICESTUDIO_WINAPPDRIVER", "").lower() not in ("1", "true", "yes"):
            item.add_marker(skip_winappdriver)
        if "requires_gpu" in item.keywords and os.getenv("VOICESTUDIO_GPU", "").lower() not in ("1", "true", "yes"):
            item.add_marker(skip_gpu)


# ============================================================================
# Event Loop Fixtures
# ============================================================================


@pytest.fixture(scope="session")
def event_loop():
    """Create an event loop for the test session."""
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


# ============================================================================
# Path Fixtures
# ============================================================================


@pytest.fixture
def temp_dir() -> Generator[Path, None, None]:
    """Create a temporary directory for tests."""
    with tempfile.TemporaryDirectory() as tmp:
        yield Path(tmp)


@pytest.fixture
def project_root() -> Path:
    """Get the project root directory."""
    return PROJECT_ROOT


@pytest.fixture
def test_assets_dir(project_root: Path) -> Path:
    """Get the test assets directory."""
    assets_dir = project_root / "tests" / "assets"
    assets_dir.mkdir(parents=True, exist_ok=True)
    return assets_dir


@pytest.fixture
def sample_audio_path(test_assets_dir: Path) -> Path:
    """Get a sample audio file path."""
    return test_assets_dir / "sample.wav"


@pytest.fixture
def canonical_audio_path(project_root: Path) -> Path:
    """Path to the standard canonical test audio (WAV). Use for voice cloning, transcription, synthesis tests."""
    path = project_root / "tests" / "assets" / "canonical" / "standard" / "allan_watts.wav"
    return path


@pytest.fixture
def canonical_audio_segment_path(project_root: Path) -> Path:
    """Path to the 15-second canonical test audio segment (WAV). Use for quick tests."""
    path = project_root / "tests" / "assets" / "canonical" / "standard" / "allan_watts_15s.wav"
    return path


# ============================================================================
# Engine Mock Fixtures (CI-friendly)
# ============================================================================


@pytest.fixture
def mock_tts_engine():
    """Create a mock TTS engine for CI testing."""
    try:
        from tests.fixtures.engines import MockEngineFactory

        return MockEngineFactory.create_xtts()
    except ImportError:
        pytest.skip("Engine fixtures not available")


@pytest.fixture
def mock_stt_engine():
    """Create a mock STT engine for CI testing."""
    try:
        from tests.fixtures.engines import MockEngineFactory

        return MockEngineFactory.create_whisper()
    except ImportError:
        pytest.skip("Engine fixtures not available")


@pytest.fixture
def mock_engine_service():
    """Create a mock engine service with all common engines for CI testing."""
    try:
        from tests.fixtures.engines import MockEngineService

        return MockEngineService.create_with_engines()
    except ImportError:
        pytest.skip("Engine fixtures not available")


@pytest.fixture
def mock_all_engines():
    """Create all mock engines for comprehensive CI testing."""
    try:
        from tests.fixtures.engines import MockEngineFactory

        return {
            "tts": MockEngineFactory.create_all_tts(),
            "stt": {"whisper": MockEngineFactory.create_whisper()},
            "quality": {"analyzer": MockEngineFactory.create_quality_analyzer()},
        }
    except ImportError:
        pytest.skip("Engine fixtures not available")


# ============================================================================
# Mock Fixtures
# ============================================================================


@pytest.fixture
def mock_engine_config() -> dict[str, Any]:
    """Get mock engine configuration."""
    return {
        "engine_id": "test-engine",
        "name": "Test Engine",
        "version": "1.0.0",
        "capabilities": ["synthesis", "transcription"],
        "model_path": "/path/to/model",
    }


@pytest.fixture
def mock_synthesis_request() -> dict[str, Any]:
    """Get mock synthesis request."""
    return {
        "text": "Hello, this is a test.",
        "voice_id": "test-voice",
        "language": "en",
        "settings": {
            "speed": 1.0,
            "pitch": 1.0,
        },
    }


@pytest.fixture
def mock_project_data() -> dict[str, Any]:
    """Get mock project data."""
    return {
        "id": "test-project",
        "name": "Test Project",
        "created_at": "2025-01-01T00:00:00Z",
        "tracks": [],
        "settings": {},
    }


# ============================================================================
# Backend Fixtures
# ============================================================================


@pytest.fixture
def backend_config() -> dict[str, Any]:
    """Get backend configuration for testing."""
    return {
        "host": "127.0.0.1",
        "port": 8000,
        "debug": True,
        "log_level": "DEBUG",
    }


@pytest.fixture
async def test_client():
    """Create a test client for the FastAPI backend."""
    try:
        from httpx import ASGITransport, AsyncClient

        from backend.api.main import app

        async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as client:
            yield client
    except ImportError:
        pytest.skip("httpx or backend not available")


# ============================================================================
# Database Fixtures
# ============================================================================


@pytest.fixture
def test_db_path(temp_dir: Path) -> Path:
    """Get a test database path."""
    return temp_dir / "test.db"


@pytest.fixture
async def test_database(test_db_path: Path):
    """Create a test database."""
    # Create database tables
    # This would initialize the test database
    yield test_db_path

    # Cleanup
    if test_db_path.exists():
        test_db_path.unlink()


# ============================================================================
# Environment Fixtures
# ============================================================================


@pytest.fixture
def clean_env():
    """Fixture that restores environment after test."""
    original_env = os.environ.copy()
    yield
    os.environ.clear()
    os.environ.update(original_env)


@pytest.fixture
def test_env(clean_env):
    """Set up test environment variables."""
    os.environ["VOICESTUDIO_TEST"] = "1"
    os.environ["VOICESTUDIO_LOG_LEVEL"] = "DEBUG"
    return os.environ


# ============================================================================
# State Isolation Fixtures (C-T2)
# ============================================================================


@pytest.fixture(autouse=True)
def clear_route_job_stores():
    """Clear module-level in-memory job stores after each test."""
    yield
    try:
        import backend.api.routes.ensemble as ensemble_mod

        ensemble_mod._ensemble_jobs.clear()
        ensemble_mod._multi_engine_ensemble_jobs.clear()
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass
    try:
        import backend.api.routes.multi_voice_generator as mvg_mod

        mvg_mod._multi_voice_jobs.clear()
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass
    try:
        import backend.api.routes.voice_cloning_wizard as wizard_mod

        wizard_mod._wizard_jobs.clear()
    # ALLOWED: bare except - optional dependency, import failure acceptable
    except (ImportError, AttributeError):
        pass


# ============================================================================
# Markers
# ============================================================================



# NOTE: pytest_configure is defined at the top of this file (line 26).
# It handles both sys.path setup AND marker registration in a single function.
# Previously there were two definitions; the second silently overwrote the first.


# ============================================================================
# Hooks
# ============================================================================


def pytest_collection_modifyitems(config, items):
    """Modify test collection."""
    # Skip GPU tests if no GPU available
    skip_gpu = pytest.mark.skip(reason="GPU not available")

    try:
        import torch

        has_gpu = torch.cuda.is_available()
    except (ImportError, AttributeError):
        # AttributeError can occur with partial torch initialization (circular import)
        has_gpu = False

    for item in items:
        if "gpu" in item.keywords and not has_gpu:
            item.add_marker(skip_gpu)
