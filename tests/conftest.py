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
    skip_gpu_env = pytest.mark.skip(reason="GPU not available")
    try:
        import torch

        torch_cuda_available = torch.cuda.is_available()
    except (ImportError, AttributeError):
        torch_cuda_available = False
    skip_gpu_torch = pytest.mark.skip(reason="GPU not available")

    for item in items:
        if "requires_models" in item.keywords and not os.getenv("VOICESTUDIO_MODELS_PATH"):
            item.add_marker(skip_models)
        if "requires_backend" in item.keywords and not os.getenv("VOICESTUDIO_BACKEND_URL"):
            item.add_marker(skip_backend)
        if "requires_winappdriver" in item.keywords and os.getenv("VOICESTUDIO_WINAPPDRIVER", "").lower() not in ("1", "true", "yes"):
            item.add_marker(skip_winappdriver)
        if "requires_gpu" in item.keywords and os.getenv("VOICESTUDIO_GPU", "").lower() not in ("1", "true", "yes"):
            item.add_marker(skip_gpu_env)
        if "gpu" in item.keywords and not torch_cuda_available:
            item.add_marker(skip_gpu_torch)


# ============================================================================
# Event Loop Fixtures
# ============================================================================


def _close_session_event_loop(loop: asyncio.AbstractEventLoop) -> None:
    """Cancel pending tasks and shut down executors before ``loop.close()`` (Windows Proactor hang fix)."""
    if loop.is_closed():
        return
    try:
        asyncio.set_event_loop(loop)
    except RuntimeError:
        pass
    try:
        pending = [t for t in asyncio.all_tasks(loop) if not t.done()]
        for task in pending:
            task.cancel()
        if pending:
            loop.run_until_complete(asyncio.gather(*pending, return_exceptions=True))
    except RuntimeError:
        pass
    try:
        loop.run_until_complete(loop.shutdown_asyncgens())
    except RuntimeError:
        pass
    try:
        if hasattr(loop, "shutdown_default_executor"):
            loop.run_until_complete(loop.shutdown_default_executor())
    except RuntimeError:
        pass
    loop.close()


@pytest.fixture(scope="session")
def event_loop():
    """Create an event loop for the test session."""
    policy = asyncio.get_event_loop_policy()
    loop = policy.new_event_loop()
    asyncio.set_event_loop(loop)
    yield loop
    _close_session_event_loop(loop)


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
# JsonFileStore root redirect (runtime store JSON must not dirty repo source tree)
# ============================================================================


@pytest.fixture(scope="session", autouse=True)
def redirect_voicestudio_json_stores_to_session_tmp(
    tmp_path_factory: pytest.TempPathFactory,
) -> None:
    """Point JsonFileStore at pytest temp (effect chains / presets), not backend/data/stores."""
    import backend.audio.effects.effect_chain_store as ecs
    import backend.infrastructure.adapters.json_file_store as jfs

    root = tmp_path_factory.mktemp("vs_jfs_data")
    previous = jfs._DATA_ROOT
    jfs._DATA_ROOT = str(root)
    ecs._chain_store = None
    ecs._preset_store = None
    yield
    jfs._DATA_ROOT = previous
    ecs._chain_store = None
    ecs._preset_store = None


# ============================================================================
# Hooks
# ============================================================================


def pytest_sessionfinish(session, exitstatus: int) -> None:
    """Session cleanup hook — cooperative shutdown of known leak sources.

    Do **not** scan ``gc.get_objects()`` here: after large ML imports the heap can
    contain millions of tracked objects; a full scan can stall for minutes and
    block process exit (GAP-069 Slice 10). ``EnhancedResourceManager`` teardown
    is handled in ``tests/unit/core/runtime/test_resource_manager_enhanced.py``
    (autouse fixture) and ``shutdown()`` joins the monitoring thread.

    Safety-net (PR #49 post-pytest hang): even when individual fixtures correctly
    drive FastAPI lifespan shutdown, ad-hoc ``TestClient(app)`` constructions in
    individual test modules can still leave non-daemon worker threads alive,
    which blocks process exit on CI Linux runners. Best-effort cooperative
    shutdown of well-known module-level singletons is performed below; any
    ImportError or AttributeError is swallowed because the singleton may not
    have been loaded by this test session.
    """
    import logging
    import sys
    import threading

    log = logging.getLogger(__name__)

    # 1. Stop the background task scheduler (asyncio task; harmless if already
    # stopped or never started). The scheduler is started by FastAPI lifespan
    # ``on_startup_prepare``; if any test uses ``with TestClient(app)`` and
    # shutdown is interrupted, this idempotent stop ensures no asyncio task
    # is left waiting on a dead loop.
    try:
        from app.core.tasks.scheduler import get_scheduler

        scheduler = get_scheduler()
        if getattr(scheduler, "_running", False):
            scheduler.stop()
    except (ImportError, AttributeError, RuntimeError) as exc:  # noqa: PERF203
        log.debug("scheduler stop skipped: %s", exc)

    # 2. Drain any leaked ``concurrent.futures.thread`` worker threads. The
    # offending pattern is ``ThreadPoolExecutor`` constructed without a
    # ``with`` block in route handlers (e.g., ``backend/api/routes/orchestrator.py``);
    # CPython worker threads are non-daemon and only exit when ``_python_exit``
    # signals their queues. Calling it now (rather than via atexit) lets us
    # observe the result in pytest output instead of waiting for atexit.
    try:
        from concurrent.futures import thread as _futures_thread

        py_exit = getattr(_futures_thread, "_python_exit", None)
        if py_exit is not None:
            py_exit()
    except Exception as exc:  # noqa: BLE001 - best effort cleanup
        log.debug("ThreadPoolExecutor drain skipped: %s", exc)

    # 3. Diagnostic: log any non-daemon threads still alive so that if the
    # process does hang, CI logs show exactly what survived. We never kill
    # them (Python forbids forced thread termination); the visibility is
    # what makes future regressions debuggable.
    survivors = [
        t
        for t in threading.enumerate()
        if t is not threading.main_thread() and not t.daemon and t.is_alive()
    ]
    if survivors:
        sys.stderr.write(
            "\n[pytest_sessionfinish] non-daemon survivors that may block exit:\n"
        )
        for t in survivors:
            sys.stderr.write(f"  - name={t.name!r} ident={t.ident}\n")
        sys.stderr.flush()
