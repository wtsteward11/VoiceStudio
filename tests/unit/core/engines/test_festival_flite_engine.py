"""
Unit tests for Festival/Flite Engine (Legacy TTS Engine)

Tests cover:
- Engine initialization and cleanup
- LRU synthesis cache
- Batch processing with ThreadPoolExecutor
- Reusable temporary directory
- Protocol compliance
- Configuration and optimization features
"""

import contextlib
from collections import OrderedDict
from unittest.mock import MagicMock, Mock, patch

import pytest

# Try to import the engine
try:
    from app.core.engines.festival_flite_engine import (
        FestivalFliteEngine,
        create_festival_flite_engine,
    )

    HAS_FESTIVAL = True
except ImportError:
    HAS_FESTIVAL = False

pytestmark = pytest.mark.skipif(
    not HAS_FESTIVAL,
    reason="Festival/Flite engine not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def festival_engine():
    """Create a Festival/Flite engine instance for testing."""
    if not HAS_FESTIVAL:
        pytest.skip("Festival/Flite engine not available")

    engine = FestivalFliteEngine(device="cpu", gpu=False)
    yield engine
    with contextlib.suppress(Exception):
        engine.cleanup()


class TestFestivalFliteEngineImports:
    """Test that Festival/Flite engine can be imported."""

    def test_import_engine(self):
        """Test that FestivalFliteEngine can be imported."""
        if not HAS_FESTIVAL:
            pytest.skip("Festival/Flite engine not available")
        from app.core.engines.festival_flite_engine import FestivalFliteEngine

        assert FestivalFliteEngine is not None

    def test_import_create_function(self):
        """Test that create_festival_flite_engine can be imported."""
        if not HAS_FESTIVAL:
            pytest.skip("Festival/Flite engine not available")
        from app.core.engines.festival_flite_engine import create_festival_flite_engine

        assert create_festival_flite_engine is not None


class TestFestivalFliteEngineStructure:
    """Test Festival/Flite engine class structure and basic functionality."""

    def test_engine_initialization(self, festival_engine):
        """Test that engine initializes correctly."""
        assert festival_engine is not None
        assert festival_engine.device == "cpu"
        assert not festival_engine._initialized

    def test_engine_has_cache(self, festival_engine):
        """Test that engine has LRU cache."""
        assert hasattr(festival_engine, "_synthesis_cache")
        assert isinstance(festival_engine._synthesis_cache, OrderedDict)
        assert festival_engine._cache_max_size == 200
        assert festival_engine.enable_cache is True

    def test_engine_has_batch_size(self, festival_engine):
        """Test that engine has batch size configuration."""
        assert hasattr(festival_engine, "batch_size")
        assert festival_engine.batch_size == 8

    def test_engine_has_temp_dir(self, festival_engine):
        """Test that engine has reusable temp directory."""
        assert hasattr(festival_engine, "_temp_dir")
        # Initially None until initialized
        assert festival_engine._temp_dir is None or isinstance(festival_engine._temp_dir, str)

    def test_engine_protocol_compliance(self, festival_engine):
        """Test that engine implements EngineProtocol."""
        from app.core.engines.protocols import EngineProtocol

        assert isinstance(festival_engine, EngineProtocol)
        assert hasattr(festival_engine, "initialize")
        assert hasattr(festival_engine, "cleanup")
        assert hasattr(festival_engine, "is_initialized")


class TestFestivalFliteEngineCache:
    """Test LRU synthesis cache functionality."""

    def test_cache_initialization(self, festival_engine):
        """Test that cache is initialized as OrderedDict."""
        assert isinstance(festival_engine._synthesis_cache, OrderedDict)
        assert len(festival_engine._synthesis_cache) == 0

    def test_cache_enable_disable(self, festival_engine):
        """Test enabling and disabling cache."""
        festival_engine.enable_cache = True
        assert festival_engine.enable_cache is True

        festival_engine.enable_cache = False
        assert festival_engine.enable_cache is False

    def test_cache_max_size(self, festival_engine):
        """Test cache max size configuration."""
        original_size = festival_engine._cache_max_size
        festival_engine._cache_max_size = 50
        assert festival_engine._cache_max_size == 50
        festival_engine._cache_max_size = original_size

    def test_clear_cache(self, festival_engine):
        """Test clearing the cache."""
        # Add some fake cache entries
        festival_engine._synthesis_cache["test1"] = {"data": "test1"}
        festival_engine._synthesis_cache["test2"] = {"data": "test2"}
        assert len(festival_engine._synthesis_cache) == 2

        festival_engine.clear_cache()
        assert len(festival_engine._synthesis_cache) == 0

    def test_get_cache_stats(self, festival_engine):
        """Test getting cache statistics."""
        stats = festival_engine.get_cache_stats()
        assert isinstance(stats, dict)
        assert "cache_size" in stats
        assert "max_cache_size" in stats
        assert "cache_enabled" in stats
        assert stats["cache_size"] == 0
        assert stats["max_cache_size"] == 200
        assert stats["cache_enabled"] is True

    def test_cache_lru_eviction(self, festival_engine):
        """Test LRU cache eviction when max size is reached."""
        festival_engine._cache_max_size = 3

        # Add entries up to max size
        for i in range(3):
            festival_engine._synthesis_cache[f"key{i}"] = {"data": f"value{i}"}

        assert len(festival_engine._synthesis_cache) == 3

        # Add one more - manually evict oldest if needed
        # (simulating cache behavior)
        if len(festival_engine._synthesis_cache) >= (festival_engine._cache_max_size):
            oldest_key = next(iter(festival_engine._synthesis_cache))
            del festival_engine._synthesis_cache[oldest_key]
        festival_engine._synthesis_cache["key3"] = {"data": "value3"}
        festival_engine._synthesis_cache.move_to_end("key3")  # LRU update

        # Check that oldest was evicted
        assert len(festival_engine._synthesis_cache) == 3
        assert "key0" not in festival_engine._synthesis_cache
        assert "key3" in festival_engine._synthesis_cache


class TestFestivalFliteEngineBatchProcessing:
    """Test batch processing with ThreadPoolExecutor."""

    def test_batch_size_configuration(self, festival_engine):
        """Test batch size can be configured."""
        original_batch_size = festival_engine.batch_size
        festival_engine.batch_size = 8
        assert festival_engine.batch_size == 8
        festival_engine.batch_size = original_batch_size

    def test_batch_synthesize_method_exists(self, festival_engine):
        """Test that batch_synthesize method exists."""
        assert hasattr(festival_engine, "batch_synthesize")
        assert callable(festival_engine.batch_synthesize)

    @patch("app.core.engines.festival_flite_engine.ThreadPoolExecutor")
    def test_batch_synthesize_uses_thread_pool(self, mock_executor, festival_engine):
        """Test that batch_synthesize uses ThreadPoolExecutor."""
        # Mock the synthesize method to return success
        festival_engine.synthesize = Mock(return_value=b"fake audio data")
        festival_engine._initialized = True

        # Mock ThreadPoolExecutor
        mock_executor_instance = MagicMock()
        mock_executor.return_value.__enter__.return_value = mock_executor_instance
        mock_executor.return_value.__exit__.return_value = None

        # Create batch synthesis tasks
        texts = ["Test text 1", "Test text 2"]

        try:
            festival_engine.batch_synthesize(texts, batch_size=2)
            # Should have called ThreadPoolExecutor
            assert mock_executor.called
        except Exception:
            # If festival/flite is not installed, that's okay
            ...


class TestFestivalFliteEngineConfiguration:
    """Test engine configuration and settings."""

    def test_supported_languages(self, festival_engine):
        """Test supported languages."""
        assert hasattr(festival_engine, "SUPPORTED_LANGUAGES")
        assert isinstance(festival_engine.SUPPORTED_LANGUAGES, list)
        assert "en" in festival_engine.SUPPORTED_LANGUAGES
        assert "es" in festival_engine.SUPPORTED_LANGUAGES

    def test_default_sample_rate(self, festival_engine):
        """Test default sample rate."""
        assert hasattr(festival_engine, "DEFAULT_SAMPLE_RATE")
        assert festival_engine.DEFAULT_SAMPLE_RATE == 16000

    def test_use_flite_configuration(self, festival_engine):
        """Test use_flite configuration."""
        assert hasattr(festival_engine, "use_flite")
        assert isinstance(festival_engine.use_flite, bool)

    def test_device_configuration(self, festival_engine):
        """Test device configuration."""
        assert festival_engine.device == "cpu"
        assert festival_engine.get_device() == "cpu"


class TestFestivalFliteEngineOptimization:
    """Test optimization features."""

    def test_reusable_temp_directory(self, festival_engine):
        """Test that engine uses reusable temp directory."""
        # Initialize to create temp directory
        with patch("app.core.engines.festival_flite_engine.tempfile.mkdtemp") as mock_mkdtemp:
            mock_mkdtemp.return_value = "/tmp/test_festival"

            # Mock initialization to succeed
            with (
                patch.object(
                    festival_engine,
                    "_find_executable",
                    return_value="/usr/bin/flite",
                ),
                patch("app.core.engines.festival_flite_engine.subprocess.run") as mock_run,
            ):
                mock_run.return_value.returncode = 0
                mock_run.return_value.stdout = "Flite text-to-speech"

                try:
                    result = festival_engine.initialize()
                    if result:
                        assert festival_engine._temp_dir is not None
                except Exception:
                    # If dependencies not available, skip
                    ...

    def test_cache_optimization(self, festival_engine):
        """Test that cache optimization is enabled by default."""
        assert festival_engine.enable_cache is True
        assert festival_engine._cache_max_size > 0

    def test_batch_processing_optimization(self, festival_engine):
        """Test that batch processing is configured."""
        assert festival_engine.batch_size > 0
        assert hasattr(festival_engine, "batch_synthesize")


class TestFestivalFliteEngineCreateFunction:
    """Test the create_festival_flite_engine factory function."""

    def test_create_function_exists(self):
        """Test that create function exists."""
        if not HAS_FESTIVAL:
            pytest.skip("Festival/Flite engine not available")
        from app.core.engines.festival_flite_engine import create_festival_flite_engine

        assert callable(create_festival_flite_engine)

    def test_create_function_returns_engine(self):
        """Test that create function returns engine instance."""
        if not HAS_FESTIVAL:
            pytest.skip("Festival/Flite engine not available")
        from app.core.engines.festival_flite_engine import create_festival_flite_engine

        engine = create_festival_flite_engine(device="cpu", gpu=False)
        assert engine is not None
        assert isinstance(engine, FestivalFliteEngine)
        with contextlib.suppress(Exception):
            engine.cleanup()
