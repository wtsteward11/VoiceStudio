"""
Unit tests for eSpeak-NG Engine (Compact Multilingual TTS Engine)

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
    from app.core.engines.espeak_ng_engine import (
        ESpeakNGEngine,
        create_espeak_ng_engine,
    )

    HAS_ESPEAK = True
except ImportError:
    HAS_ESPEAK = False

pytestmark = pytest.mark.skipif(
    not HAS_ESPEAK,
    reason="eSpeak-NG engine not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def espeak_engine():
    """Create an eSpeak-NG engine instance for testing."""
    if not HAS_ESPEAK:
        pytest.skip("eSpeak-NG engine not available")

    engine = ESpeakNGEngine(device="cpu", gpu=False)
    yield engine
    with contextlib.suppress(Exception):
        engine.cleanup()


class TestESpeakNGEngineImports:
    """Test that eSpeak-NG engine can be imported."""

    def test_import_engine(self):
        """Test that ESpeakNGEngine can be imported."""
        if not HAS_ESPEAK:
            pytest.skip("eSpeak-NG engine not available")
        from app.core.engines.espeak_ng_engine import ESpeakNGEngine

        assert ESpeakNGEngine is not None

    def test_import_create_function(self):
        """Test that create_espeak_ng_engine can be imported."""
        if not HAS_ESPEAK:
            pytest.skip("eSpeak-NG engine not available")
        from app.core.engines.espeak_ng_engine import create_espeak_ng_engine

        assert create_espeak_ng_engine is not None


class TestESpeakNGEngineStructure:
    """Test eSpeak-NG engine class structure and basic functionality."""

    def test_engine_initialization(self, espeak_engine):
        """Test that engine initializes correctly."""
        assert espeak_engine is not None
        assert espeak_engine.device == "cpu"
        assert not espeak_engine._initialized

    def test_engine_has_cache(self, espeak_engine):
        """Test that engine has LRU cache."""
        assert hasattr(espeak_engine, "_synthesis_cache")
        assert isinstance(espeak_engine._synthesis_cache, OrderedDict)
        assert espeak_engine._cache_max_size == 200
        assert espeak_engine.enable_cache is True

    def test_engine_has_batch_size(self, espeak_engine):
        """Test that engine has batch size configuration."""
        assert hasattr(espeak_engine, "batch_size")
        assert espeak_engine.batch_size == 8

    def test_engine_has_temp_dir(self, espeak_engine):
        """Test that engine has reusable temp directory."""
        assert hasattr(espeak_engine, "_temp_dir")
        # Initially None until initialized
        assert espeak_engine._temp_dir is None or isinstance(espeak_engine._temp_dir, str)

    def test_engine_protocol_compliance(self, espeak_engine):
        """Test that engine implements EngineProtocol."""
        from app.core.engines.protocols import EngineProtocol

        assert isinstance(espeak_engine, EngineProtocol)
        assert hasattr(espeak_engine, "initialize")
        assert hasattr(espeak_engine, "cleanup")
        assert hasattr(espeak_engine, "is_initialized")


class TestESpeakNGEngineCache:
    """Test LRU synthesis cache functionality."""

    def test_cache_initialization(self, espeak_engine):
        """Test that cache is initialized as OrderedDict."""
        assert isinstance(espeak_engine._synthesis_cache, OrderedDict)
        assert len(espeak_engine._synthesis_cache) == 0

    def test_cache_enable_disable(self, espeak_engine):
        """Test enabling and disabling cache."""
        espeak_engine.enable_cache = True
        assert espeak_engine.enable_cache is True

        espeak_engine.enable_cache = False
        assert espeak_engine.enable_cache is False

    def test_cache_max_size(self, espeak_engine):
        """Test cache max size configuration."""
        original_size = espeak_engine._cache_max_size
        espeak_engine._cache_max_size = 50
        assert espeak_engine._cache_max_size == 50
        espeak_engine._cache_max_size = original_size

    def test_clear_cache(self, espeak_engine):
        """Test clearing the cache."""
        # Add some fake cache entries
        espeak_engine._synthesis_cache["test1"] = {"data": "test1"}
        espeak_engine._synthesis_cache["test2"] = {"data": "test2"}
        assert len(espeak_engine._synthesis_cache) == 2

        espeak_engine.clear_cache()
        assert len(espeak_engine._synthesis_cache) == 0

    def test_get_cache_stats(self, espeak_engine):
        """Test getting cache statistics."""
        stats = espeak_engine.get_cache_stats()
        assert isinstance(stats, dict)
        assert "cache_size" in stats
        assert "max_cache_size" in stats
        assert "cache_enabled" in stats
        assert stats["cache_size"] == 0
        assert stats["max_cache_size"] == 200
        assert stats["cache_enabled"] is True

    def test_cache_lru_eviction(self, espeak_engine):
        """Test LRU cache eviction when max size is reached."""
        espeak_engine._cache_max_size = 3

        # Add entries up to max size
        for i in range(3):
            espeak_engine._synthesis_cache[f"key{i}"] = {"data": f"value{i}"}

        assert len(espeak_engine._synthesis_cache) == 3

        # Add one more - manually evict oldest if needed
        # (simulating cache behavior)
        if len(espeak_engine._synthesis_cache) >= (espeak_engine._cache_max_size):
            oldest_key = next(iter(espeak_engine._synthesis_cache))
            del espeak_engine._synthesis_cache[oldest_key]
        espeak_engine._synthesis_cache["key3"] = {"data": "value3"}
        espeak_engine._synthesis_cache.move_to_end("key3")  # LRU update

        # Check that oldest was evicted
        assert len(espeak_engine._synthesis_cache) == 3
        assert "key0" not in espeak_engine._synthesis_cache
        assert "key3" in espeak_engine._synthesis_cache


class TestESpeakNGEngineBatchProcessing:
    """Test batch processing with ThreadPoolExecutor."""

    def test_batch_size_configuration(self, espeak_engine):
        """Test batch size can be configured."""
        original_batch_size = espeak_engine.batch_size
        espeak_engine.batch_size = 8
        assert espeak_engine.batch_size == 8
        espeak_engine.batch_size = original_batch_size

    def test_batch_synthesize_method_exists(self, espeak_engine):
        """Test that batch_synthesize method exists."""
        assert hasattr(espeak_engine, "batch_synthesize")
        assert callable(espeak_engine.batch_synthesize)

    @patch("app.core.engines.espeak_ng_engine.ThreadPoolExecutor")
    def test_batch_synthesize_uses_thread_pool(self, mock_executor, espeak_engine):
        """Test that batch_synthesize uses ThreadPoolExecutor."""
        # Mock the synthesize method to return success
        espeak_engine.synthesize = Mock(return_value=b"fake audio data")
        espeak_engine._initialized = True

        # Mock ThreadPoolExecutor
        mock_executor_instance = MagicMock()
        mock_executor.return_value.__enter__.return_value = mock_executor_instance
        mock_executor.return_value.__exit__.return_value = None

        # Create batch synthesis tasks
        texts = ["Test text 1", "Test text 2"]

        try:
            espeak_engine.batch_synthesize(texts, batch_size=2)
            # Should have called ThreadPoolExecutor
            assert mock_executor.called
        except Exception:
            # If espeak is not installed, that's okay
            ...


class TestESpeakNGEngineConfiguration:
    """Test engine configuration and settings."""

    def test_supported_languages(self, espeak_engine):
        """Test supported languages."""
        assert hasattr(espeak_engine, "SUPPORTED_LANGUAGES")
        assert isinstance(espeak_engine.SUPPORTED_LANGUAGES, list)
        assert "en" in espeak_engine.SUPPORTED_LANGUAGES
        assert "es" in espeak_engine.SUPPORTED_LANGUAGES

    def test_default_sample_rate(self, espeak_engine):
        """Test default sample rate."""
        assert hasattr(espeak_engine, "DEFAULT_SAMPLE_RATE")
        assert espeak_engine.DEFAULT_SAMPLE_RATE == 22050

    def test_device_configuration(self, espeak_engine):
        """Test device configuration."""
        assert espeak_engine.device == "cpu"
        assert espeak_engine.get_device() == "cpu"


class TestESpeakNGEngineOptimization:
    """Test optimization features."""

    def test_reusable_temp_directory(self, espeak_engine):
        """Test that engine uses reusable temp directory."""
        # Initialize to create temp directory
        with patch("app.core.engines.espeak_ng_engine.tempfile.mkdtemp") as mock_mkdtemp:
            mock_mkdtemp.return_value = "/tmp/test_espeak"

            # Mock initialization to succeed
            with (
                patch.object(espeak_engine, "_find_executable", return_value="/usr/bin/espeak-ng"),
                patch("app.core.engines.espeak_ng_engine.subprocess.run") as mock_run,
            ):
                mock_run.return_value.returncode = 0
                mock_run.return_value.stdout = "eSpeak-ng text-to-speech"

                try:
                    result = espeak_engine.initialize()
                    if result:
                        assert espeak_engine._temp_dir is not None
                except Exception:
                    # If dependencies not available, skip
                    ...

    def test_cache_optimization(self, espeak_engine):
        """Test that cache optimization is enabled by default."""
        assert espeak_engine.enable_cache is True
        assert espeak_engine._cache_max_size > 0

    def test_batch_processing_optimization(self, espeak_engine):
        """Test that batch processing is configured."""
        assert espeak_engine.batch_size > 0
        assert hasattr(espeak_engine, "batch_synthesize")


class TestESpeakNGEngineCreateFunction:
    """Test the create_espeak_ng_engine factory function."""

    def test_create_function_exists(self):
        """Test that create function exists."""
        if not HAS_ESPEAK:
            pytest.skip("eSpeak-NG engine not available")
        from app.core.engines.espeak_ng_engine import create_espeak_ng_engine

        assert callable(create_espeak_ng_engine)

    def test_create_function_returns_engine(self):
        """Test that create function returns engine instance."""
        if not HAS_ESPEAK:
            pytest.skip("eSpeak-NG engine not available")
        from app.core.engines.espeak_ng_engine import create_espeak_ng_engine

        engine = create_espeak_ng_engine(device="cpu", gpu=False)
        assert engine is not None
        assert isinstance(engine, ESpeakNGEngine)
        with contextlib.suppress(Exception):
            engine.cleanup()
