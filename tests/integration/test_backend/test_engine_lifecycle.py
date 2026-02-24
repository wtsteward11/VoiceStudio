"""
Engine Lifecycle Integration Tests.

Tests for engine startup, health checks, and shutdown scenarios.
Verifies that engines can be properly initialized, monitored, and cleaned up.
"""

import asyncio
import logging
from unittest.mock import MagicMock, patch

import pytest

from .base import AsyncIntegrationTestBase, IntegrationTestBase, integration

logger = logging.getLogger(__name__)


# =============================================================================
# Engine Lifecycle Test Base
# =============================================================================


class EngineLifecycleTestBase(IntegrationTestBase):
    """Base class for engine lifecycle tests."""

    @pytest.fixture
    def mock_engine_router(self):
        """Mock the engine router for testing."""
        mock_router = MagicMock()

        # Configure mock engines
        mock_engines = [
            {
                "id": "xtts_v2",
                "name": "XTTS v2",
                "status": "ready",
                "capabilities": ["tts", "voice_cloning"],
            },
            {
                "id": "chatterbox",
                "name": "Chatterbox",
                "status": "ready",
                "capabilities": ["tts"],
            },
            {
                "id": "whisper",
                "name": "Whisper",
                "status": "ready",
                "capabilities": ["transcription"],
            },
        ]

        mock_router.list_engines.return_value = mock_engines
        mock_router.get_engine.side_effect = self._create_mock_engine

        return mock_router

    def _create_mock_engine(self, engine_id: str) -> MagicMock:
        """Create a mock engine with standard interface."""
        engine = MagicMock()
        engine.id = engine_id
        engine.ready = True
        engine.is_initialized = True
        engine.health_check.return_value = {
            "status": "healthy",
            "engine_id": engine_id,
            "uptime_seconds": 100,
        }
        return engine


# =============================================================================
# Engine Startup Tests
# =============================================================================


class TestEngineStartup(EngineLifecycleTestBase):
    """Test engine startup and initialization scenarios."""

    @integration
    def test_engine_service_initialization(self):
        """Verify engine service can be initialized."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        assert service is not None
        assert service._engines_loaded is False  # Lazy loading

    @integration
    def test_engine_lazy_loading(self):
        """Verify engines are lazily loaded on first access."""
        from backend.services.engine_service import EngineService

        service = EngineService()

        # Before first access
        assert service._engines_loaded is False
        assert service._engine_router is None

        # After first access - triggers lazy loading
        engines = service.list_engines()
        assert service._engines_loaded is True
        # Engines may be empty list if engine layer not available
        assert isinstance(engines, list)

    @integration
    def test_engine_discovery(self, mock_engine_router):
        """Verify engine discovery returns available engines."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        engines = service.list_engines()

        assert len(engines) >= 1
        mock_engine_router.list_engines.assert_called_once()

    @integration
    def test_engine_service_singleton(self):
        """Verify get_engine_service returns singleton."""
        from backend.services.engine_service import get_engine_service

        service1 = get_engine_service()
        service2 = get_engine_service()

        assert service1 is service2

    @integration
    def test_engine_startup_with_missing_dependencies(self):
        """Verify graceful handling when engine dependencies are missing."""
        from backend.services.engine_service import EngineService

        with patch.dict("sys.modules", {"app.core.engines.router": None}):
            service = EngineService()
            service._engines_loaded = False

            # Should not raise, should return empty
            engines = service.list_engines()
            assert engines == []


# =============================================================================
# Engine Health Check Tests
# =============================================================================


class TestEngineHealthCheck(EngineLifecycleTestBase):
    """Test engine health check functionality."""

    @integration
    def test_engine_status_check(self, mock_engine_router):
        """Verify engine status check returns proper format."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        status = service.get_engine_status("xtts_v2")

        assert "status" in status
        # With mock, should be available
        assert status["status"] == "available"
        assert status["engine_id"] == "xtts_v2"

    @integration
    def test_engine_not_found_status(self, mock_engine_router):
        """Verify proper status for non-existent engine."""
        from backend.services.engine_service import EngineService

        # Clear side_effect and set return_value
        mock_engine_router.get_engine.side_effect = None
        mock_engine_router.get_engine.return_value = None

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        status = service.get_engine_status("nonexistent_engine")

        assert status["status"] == "not_found"
        assert "nonexistent_engine" in status.get("engine_id", "")

    @integration
    def test_engine_availability_check(self, mock_engine_router):
        """Verify is_engine_available returns correct state."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        # Available engine (uses side_effect from fixture)
        assert service.is_engine_available("xtts_v2") is True

        # Non-existent engine - clear side_effect first
        mock_engine_router.get_engine.side_effect = None
        mock_engine_router.get_engine.return_value = None
        assert service.is_engine_available("nonexistent") is False

    @integration
    def test_multiple_engine_health_checks(self, mock_engine_router):
        """Verify health checks for multiple engines."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        engine_ids = ["xtts_v2", "chatterbox", "whisper"]

        for engine_id in engine_ids:
            status = service.get_engine_status(engine_id)
            assert status["status"] == "available"
            assert status["engine_id"] == engine_id

    @integration
    def test_engine_health_error_handling(self, mock_engine_router):
        """Verify health check error is properly handled."""
        from backend.services.engine_service import EngineService

        mock_engine_router.get_engine.side_effect = Exception("Connection error")

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        status = service.get_engine_status("xtts_v2")

        assert status["status"] == "error"
        assert "error" in status


# =============================================================================
# Engine Shutdown Tests
# =============================================================================


class TestEngineShutdown(EngineLifecycleTestBase):
    """Test engine shutdown and cleanup scenarios."""

    @integration
    def test_engine_router_not_loaded_on_shutdown(self):
        """Verify clean state when engine router not loaded."""
        from backend.services.engine_service import EngineService

        service = EngineService()

        # Force engine router to stay None (simulate import failure)
        with patch.object(service, "_ensure_engines_loaded"):
            service._engine_router = None
            service._engines_loaded = True

            status = service.get_engine_status("xtts_v2")

            assert status["status"] == "unavailable"
            assert "error" in status

    @integration
    def test_engine_service_reset(self):
        """Verify engine service can be reset for clean state."""
        from backend.services.engine_service import (
            EngineService,
        )

        # Create new service
        service = EngineService()
        service._engines_loaded = True

        # Reset by creating new instance
        new_service = EngineService()

        assert new_service._engines_loaded is False
        assert new_service._engine_router is None

    @integration
    def test_graceful_degradation_on_engine_failure(self, mock_engine_router):
        """Verify service continues working when one engine fails."""
        from backend.services.engine_service import EngineService

        def get_engine_with_failure(engine_id):
            if engine_id == "failing_engine":
                raise Exception("Engine initialization failed")
            return self._create_mock_engine(engine_id)

        mock_engine_router.get_engine.side_effect = get_engine_with_failure

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        # Working engine should still work
        assert service.is_engine_available("xtts_v2") is True

        # Failed engine should report unavailable (not crash)
        assert service.is_engine_available("failing_engine") is False


# =============================================================================
# Engine Operations Integration Tests
# =============================================================================


class TestEngineOperations(EngineLifecycleTestBase):
    """Test engine operations through the service layer."""

    @integration
    def test_synthesize_with_mock_engine(self, mock_engine_router):
        """Verify synthesis operation through service layer."""
        from backend.services.engine_service import EngineService

        mock_engine = self._create_mock_engine("xtts_v2")
        mock_engine.synthesize.return_value = {
            "audio_path": "/tmp/test_output.wav",
            "duration": 2.5,
        }
        # Clear side_effect and set return_value
        mock_engine_router.get_engine.side_effect = None
        mock_engine_router.get_engine.return_value = mock_engine

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        result = service.synthesize(
            engine_id="xtts_v2",
            text="Hello, world!",
            voice_id="test_voice",
        )

        assert "audio_path" in result or "error" not in result
        mock_engine.synthesize.assert_called_once()

    @integration
    def test_synthesize_engine_not_found(self, mock_engine_router):
        """Verify synthesis returns error for non-existent engine."""
        from backend.services.engine_service import EngineService

        # Clear side_effect and set return_value
        mock_engine_router.get_engine.side_effect = None
        mock_engine_router.get_engine.return_value = None

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        result = service.synthesize(
            engine_id="nonexistent",
            text="Hello, world!",
        )

        assert "error" in result
        assert "not found" in result["error"].lower()

    @integration
    def test_transcribe_with_mock_engine(self, mock_engine_router):
        """Verify transcription operation through service layer."""
        from backend.services.engine_service import EngineService

        mock_engine = self._create_mock_engine("whisper")
        mock_engine.transcribe.return_value = {
            "text": "Transcribed text here",
            "language": "en",
            "confidence": 0.95,
        }
        mock_engine_router.get_engine.return_value = mock_engine

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        result = service.transcribe(
            engine_id="whisper",
            audio_path="/tmp/test_audio.wav",
            language="en",
        )

        assert "text" in result or "error" not in result

    @integration
    def test_voice_clone_with_mock_engine(self, mock_engine_router):
        """Verify voice cloning operation through service layer."""
        from backend.services.engine_service import EngineService

        mock_engine = self._create_mock_engine("xtts_v2")
        mock_engine.clone_voice.return_value = {
            "audio_path": "/tmp/cloned_output.wav",
            "similarity_score": 0.92,
        }
        mock_engine_router.get_engine.return_value = mock_engine

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        result = service.clone_voice(
            engine_id="xtts_v2",
            reference_audio="/tmp/reference.wav",
            text="Clone this voice!",
        )

        assert "audio_path" in result or "error" not in result


# =============================================================================
# Concurrent Engine Access Tests
# =============================================================================


class TestConcurrentEngineAccess(AsyncIntegrationTestBase):
    """Test concurrent access to engine services."""

    @pytest.fixture
    def mock_engine_router(self):
        """Create mock router for concurrent tests."""
        mock_router = MagicMock()
        mock_router.list_engines.return_value = [
            {"id": "xtts_v2", "name": "XTTS v2", "status": "ready"},
        ]
        return mock_router

    @pytest.mark.asyncio
    @integration
    async def test_concurrent_engine_status_checks(self, mock_engine_router):
        """Verify concurrent status checks don't cause issues."""
        from backend.services.engine_service import EngineService

        mock_engine = MagicMock()
        mock_engine.id = "xtts_v2"
        mock_engine.ready = True
        mock_engine_router.get_engine.return_value = mock_engine

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        async def check_status():
            await asyncio.sleep(0.001)  # Small delay
            return service.get_engine_status("xtts_v2")

        # Run multiple concurrent checks
        tasks = [check_status() for _ in range(10)]
        results = await asyncio.gather(*tasks)

        assert all(r["status"] == "available" for r in results)

    @pytest.mark.asyncio
    @integration
    async def test_concurrent_engine_list(self, mock_engine_router):
        """Verify concurrent engine listing doesn't cause issues."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engine_router = mock_engine_router
        service._engines_loaded = True

        async def list_engines():
            await asyncio.sleep(0.001)
            return service.list_engines()

        # Run multiple concurrent lists
        tasks = [list_engines() for _ in range(10)]
        results = await asyncio.gather(*tasks)

        # All should return same result
        assert all(len(r) == 1 for r in results)


# =============================================================================
# Engine Performance Metrics Tests
# =============================================================================


class TestEnginePerformanceMetrics(EngineLifecycleTestBase):
    """Test engine performance metrics functionality."""

    @integration
    def test_get_engine_performance_metrics_unavailable(self):
        """Verify performance metrics handles unavailable module."""
        from backend.services.engine_service import EngineService

        service = EngineService()
        service._engines_loaded = True

        with patch.dict("sys.modules", {"app.core.engines.performance_metrics": None}):
            metrics = service.get_engine_performance_metrics()

            # Should return error dict, not raise
            assert "error" in metrics

    @integration
    def test_get_engine_performance_metrics_mock(self, mock_engine_router):
        """Verify performance metrics are collected."""
        from backend.services.engine_service import EngineService

        mock_metrics = MagicMock()
        mock_metrics.get_summary.return_value = {
            "total_calls": 100,
            "avg_latency_ms": 250,
        }
        mock_metrics.get_all_stats.return_value = []

        # Patch at the import location used by the method
        with patch(
            "app.core.engines.performance_metrics.get_engine_metrics",
            return_value=mock_metrics,
        ):
            service = EngineService()
            service._engines_loaded = True

            metrics = service.get_engine_performance_metrics()

            # With proper mocking, should have summary
            assert "summary" in metrics
            assert metrics["summary"]["total_calls"] == 100
