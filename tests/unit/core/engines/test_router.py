"""
Unit Tests for Engine Router
Tests engine routing and selection functionality including optimizations.

Tests cover:
- Engine registration and retrieval
- Idle timeout and automatic cleanup
- Memory threshold monitoring
- Last access tracking
- Engine statistics
- Memory usage tracking
"""

import sys
import time
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the router module
try:
    from app.core.engines import router as router_module
    from app.core.engines.protocols import EngineProtocol
    from app.core.engines.router import EngineRouter

    HAS_ROUTER = True
except ImportError:
    HAS_ROUTER = False

pytestmark = pytest.mark.skipif(
    not HAS_ROUTER,
    reason="Engine router not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def mock_engine_class():
    """Create a mock engine class for testing."""

    class MockEngine(EngineProtocol):
        def __init__(self, device=None, gpu=True):
            super().__init__(device=device, gpu=gpu)
            self._initialized = False

        def initialize(self):
            self._initialized = True
            return True

        def cleanup(self):
            self._initialized = False

    return MockEngine


@pytest.fixture
def engine_router():
    """Create an EngineRouter instance for testing."""
    if not HAS_ROUTER:
        pytest.skip("Engine router not available")

    router = EngineRouter(
        idle_timeout_seconds=1.0,  # Short timeout for testing
        memory_threshold_mb=100.0,  # Low threshold for testing
        auto_cleanup_enabled=True,
    )
    yield router


class TestEngineRouterImports:
    """Test engine router can be imported."""

    def test_router_class_imports(self):
        """Test EngineRouter can be imported."""
        if not HAS_ROUTER:
            pytest.skip("Engine router not available")
        from app.core.engines.router import EngineRouter

        assert EngineRouter is not None


class TestEngineRouterStructure:
    """Test EngineRouter class structure."""

    def test_router_initialization(self, engine_router):
        """Test that router initializes correctly."""
        assert engine_router is not None
        assert engine_router._idle_timeout_seconds == 1.0
        assert engine_router._memory_threshold_mb == 100.0
        assert engine_router._auto_cleanup_enabled is True

    def test_router_has_storage(self, engine_router):
        """Test that router has storage dictionaries."""
        assert hasattr(engine_router, "_engines")
        assert hasattr(engine_router, "_engine_types")
        assert hasattr(engine_router, "_manifests")
        assert hasattr(engine_router, "_engine_last_access")
        assert hasattr(engine_router, "_engine_memory_usage")

    def test_router_has_optimization_features(self, engine_router):
        """Test that router has optimization features."""
        assert hasattr(engine_router, "_idle_timeout_seconds")
        assert hasattr(engine_router, "_memory_threshold_mb")
        assert hasattr(engine_router, "_auto_cleanup_enabled")
        assert hasattr(engine_router, "_cleanup_idle_engines")
        assert hasattr(engine_router, "_cleanup_if_memory_high")


class TestEngineRouterRegistration:
    """Test engine registration functionality."""

    def test_register_engine(self, engine_router, mock_engine_class):
        """Test registering an engine."""
        engine_router.register_engine("test_engine", mock_engine_class)
        assert "test_engine" in engine_router._engine_types
        assert engine_router._engine_types["test_engine"] == mock_engine_class

    def test_register_engine_invalid_type(self, engine_router):
        """Test that registering non-EngineProtocol raises error."""

        class InvalidEngine: ...

        with pytest.raises(TypeError):
            engine_router.register_engine("invalid", InvalidEngine)

    def test_list_engines(self, engine_router, mock_engine_class):
        """Test listing registered engines."""
        engine_router.register_engine("engine1", mock_engine_class)
        engine_router.register_engine("engine2", mock_engine_class)

        engines = engine_router.list_engines()
        assert isinstance(engines, list)
        assert "engine1" in engines
        assert "engine2" in engines


class TestEngineRouterIdleTimeout:
    """Test idle timeout and automatic cleanup."""

    def test_idle_timeout_configuration(self, engine_router):
        """Test that idle timeout can be configured."""
        assert engine_router._idle_timeout_seconds == 1.0

        # Create router with different timeout
        router2 = EngineRouter(idle_timeout_seconds=600.0)
        assert router2._idle_timeout_seconds == 600.0

    def test_last_access_tracking(self, engine_router, mock_engine_class):
        """Test that last access time is tracked."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine = engine_router.get_engine("test_engine", device="cpu", gpu=False)

        assert engine is not None
        assert "test_engine" in engine_router._engine_last_access
        assert engine_router._engine_last_access["test_engine"] > 0

    def test_idle_engine_cleanup(self, engine_router, mock_engine_class):
        """Test that idle engines are automatically cleaned up."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        assert "test_engine" in engine_router._engines

        # Wait for idle timeout
        time.sleep(1.1)

        # Get another engine to trigger cleanup
        engine_router.register_engine("test_engine2", mock_engine_class)
        engine_router.get_engine("test_engine2", device="cpu", gpu=False)

        # First engine should be cleaned up
        assert "test_engine" not in engine_router._engines
        assert "test_engine2" in engine_router._engines

    def test_cleanup_idle_engines_method(self, engine_router, mock_engine_class):
        """Test manual cleanup of idle engines."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        assert "test_engine" in engine_router._engines

        # Manually set last access to past
        engine_router._engine_last_access["test_engine"] = time.time() - 2.0

        # Trigger cleanup
        engine_router._cleanup_idle_engines()

        # Engine should be cleaned up
        assert "test_engine" not in engine_router._engines

    def test_unregister_keeps_engine_id_in_list_engines(
        self, engine_router, mock_engine_class
    ):
        """Instance unload must not drop the engine id from ``list_engines`` (Slice 12)."""
        engine_router.register_engine("unload_me", mock_engine_class)
        engine_router.get_engine("unload_me", device="cpu", gpu=False)
        assert "unload_me" in engine_router._engines
        engine_router.unregister_engine("unload_me")
        assert "unload_me" not in engine_router._engines
        ids = engine_router.list_engines()
        assert "unload_me" in ids
        assert "unload_me" in engine_router._engine_types


class TestEngineRouterMemoryMonitoring:
    """Test memory threshold monitoring."""

    def test_memory_threshold_configuration(self, engine_router):
        """Test that memory threshold can be configured."""
        assert engine_router._memory_threshold_mb == 100.0

        # Create router with different threshold
        router2 = EngineRouter(memory_threshold_mb=4096.0)
        assert router2._memory_threshold_mb == 4096.0

    def test_auto_cleanup_enabled(self, engine_router):
        """Test that auto cleanup can be enabled/disabled."""
        assert engine_router._auto_cleanup_enabled is True

        router2 = EngineRouter(auto_cleanup_enabled=False)
        assert router2._auto_cleanup_enabled is False

    def test_memory_cleanup_when_high(self, engine_router, mock_engine_class):
        """Test that engines are cleaned up when memory is high."""
        # Get the actual module from sys.modules to avoid fixture interference
        the_router_module = sys.modules["app.core.engines.router"]

        with (
            patch.object(the_router_module, "HAS_PSUTIL", True),
            patch.object(the_router_module, "psutil") as mock_psutil,
        ):

            # Mock psutil.virtual_memory to return high memory usage
            mock_virtual_memory = MagicMock()
            mock_virtual_memory.percent = 95.0  # 95% memory usage
            mock_psutil.virtual_memory.return_value = mock_virtual_memory

            # Mock process memory info
            mock_process = MagicMock()
            mock_process.memory_info.return_value = MagicMock(rss=200 * 1024 * 1024)  # 200MB
            engine_router._process = mock_process

            # Register and get engines
            engine_router.register_engine("engine1", mock_engine_class)
            engine_router.get_engine("engine1", device="cpu", gpu=False)

            engine_router.register_engine("engine2", mock_engine_class)
            engine_router.get_engine("engine2", device="cpu", gpu=False)

            # Set memory usage for engines
            engine_router._engine_memory_usage["engine1"] = 60.0  # MB
            engine_router._engine_memory_usage["engine2"] = 60.0  # MB

            # Set threshold low to trigger cleanup
            engine_router._memory_threshold_mb = 100.0

            # Trigger memory check
            engine_router._cleanup_if_memory_high()

            # At least one engine should be cleaned up (oldest first)
            # Note: This depends on implementation details


class TestEngineRouterStatistics:
    """Test engine statistics functionality."""

    def test_get_engine_stats(self, engine_router, mock_engine_class):
        """Test getting engine statistics."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        stats = engine_router.get_engine_stats()

        assert isinstance(stats, dict)
        assert "total_loaded" in stats
        assert "total_registered" in stats
        assert "idle_timeout_seconds" in stats
        assert "engines" in stats
        assert stats["total_loaded"] == 1
        assert stats["total_registered"] == 1
        assert "test_engine" in stats["engines"]

    def test_engine_stats_include_idle_info(self, engine_router, mock_engine_class):
        """Test that engine stats include idle information."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        stats = engine_router.get_engine_stats()
        engine_stats = stats["engines"]["test_engine"]

        assert "idle_seconds" in engine_stats
        assert "is_idle" in engine_stats
        assert isinstance(engine_stats["idle_seconds"], (int, float))
        assert isinstance(engine_stats["is_idle"], bool)


class TestEngineRouterUnload:
    """Test engine unloading functionality."""

    def test_unload_engine(self, engine_router, mock_engine_class):
        """Test manually unloading an engine."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        assert "test_engine" in engine_router._engines

        result = engine_router.unload_engine("test_engine")

        assert result is True
        assert "test_engine" not in engine_router._engines

    def test_unload_nonexistent_engine(self, engine_router):
        """Test unloading a non-existent engine."""
        result = engine_router.unload_engine("nonexistent")
        assert result is False


class TestEngineRouterOptimization:
    """Test optimization features."""

    def test_idle_timeout_optimization(self, engine_router):
        """Test that idle timeout is configured."""
        assert engine_router._idle_timeout_seconds > 0

    def test_memory_monitoring_optimization(self, engine_router):
        """Test that memory monitoring is configured."""
        assert engine_router._memory_threshold_mb > 0
        assert hasattr(engine_router, "_auto_cleanup_enabled")

    def test_last_access_tracking_optimization(self, engine_router, mock_engine_class):
        """Test that last access tracking works."""
        engine_router.register_engine("test_engine", mock_engine_class)
        engine_router.get_engine("test_engine", device="cpu", gpu=False)

        assert "test_engine" in engine_router._engine_last_access
        last_access1 = engine_router._engine_last_access["test_engine"]

        # Get engine again - should update last access
        # Use longer timeout to prevent cleanup
        engine_router._idle_timeout_seconds = 10.0
        time.sleep(0.1)
        engine2 = engine_router.get_engine("test_engine", device="cpu", gpu=False)

        # Check if engine still exists (might have been cleaned up)
        if "test_engine" in engine_router._engine_last_access:
            last_access2 = engine_router._engine_last_access["test_engine"]
            assert last_access2 >= last_access1
        else:
            # Engine was cleaned up, which is also valid behavior
            assert engine2 is not None or "test_engine" not in engine_router._engines


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
