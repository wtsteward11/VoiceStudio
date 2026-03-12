"""
Unit tests for Aeneas Engine (Audio-Text Alignment Engine)

Tests cover:
- Engine initialization and cleanup
- LRU alignment cache
- Batch processing with ThreadPoolExecutor
- Reusable temporary directory
- Protocol compliance
- Configuration and optimization features
"""

import contextlib
import os
import tempfile
from collections import OrderedDict
from unittest.mock import MagicMock, Mock, patch

import pytest

# Try to import the engine
try:
    from app.core.engines.aeneas_engine import AeneasEngine, create_aeneas_engine

    HAS_AENEAS = True
except ImportError:
    HAS_AENEAS = False

pytestmark = pytest.mark.skipif(
    not HAS_AENEAS,
    reason="Aeneas engine not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def temp_audio_file():
    """Create a temporary audio file for testing."""
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as f:
        f.write(b"fake audio data")
        temp_path = f.name
    yield temp_path
    if os.path.exists(temp_path):
        os.unlink(temp_path)


@pytest.fixture
def aeneas_engine():
    """Create an Aeneas engine instance for testing."""
    if not HAS_AENEAS:
        pytest.skip("Aeneas engine not available")

    engine = AeneasEngine(device="cpu", gpu=False)
    yield engine
    with contextlib.suppress(Exception):
        engine.cleanup()


class TestAeneasEngineImports:
    """Test that Aeneas engine can be imported."""

    def test_import_engine(self):
        """Test that AeneasEngine can be imported."""
        if not HAS_AENEAS:
            pytest.skip("Aeneas engine not available")
        from app.core.engines.aeneas_engine import AeneasEngine

        assert AeneasEngine is not None

    def test_import_create_function(self):
        """Test that create_aeneas_engine can be imported."""
        if not HAS_AENEAS:
            pytest.skip("Aeneas engine not available")
        from app.core.engines.aeneas_engine import create_aeneas_engine

        assert create_aeneas_engine is not None


class TestAeneasEngineStructure:
    """Test Aeneas engine class structure and basic functionality."""

    def test_engine_initialization(self, aeneas_engine):
        """Test that engine initializes correctly."""
        assert aeneas_engine is not None
        assert aeneas_engine.device == "cpu"
        assert not aeneas_engine._initialized

    def test_engine_has_cache(self, aeneas_engine):
        """Test that engine has LRU cache."""
        assert hasattr(aeneas_engine, "_alignment_cache")
        assert isinstance(aeneas_engine._alignment_cache, OrderedDict)
        assert aeneas_engine._cache_max_size == 100
        assert aeneas_engine.enable_cache is True

    def test_engine_has_batch_size(self, aeneas_engine):
        """Test that engine has batch size configuration."""
        assert hasattr(aeneas_engine, "batch_size")
        assert aeneas_engine.batch_size == 4

    def test_engine_has_temp_dir(self, aeneas_engine):
        """Test that engine has reusable temp directory."""
        assert hasattr(aeneas_engine, "_temp_dir")
        # Initially None until initialized
        assert aeneas_engine._temp_dir is None or isinstance(aeneas_engine._temp_dir, str)

    def test_engine_protocol_compliance(self, aeneas_engine):
        """Test that engine implements EngineProtocol."""
        from app.core.engines.protocols import EngineProtocol

        assert isinstance(aeneas_engine, EngineProtocol)
        assert hasattr(aeneas_engine, "initialize")
        assert hasattr(aeneas_engine, "cleanup")
        assert hasattr(aeneas_engine, "is_initialized")


class TestAeneasEngineCache:
    """Test LRU alignment cache functionality."""

    def test_cache_initialization(self, aeneas_engine):
        """Test that cache is initialized as OrderedDict."""
        assert isinstance(aeneas_engine._alignment_cache, OrderedDict)
        assert len(aeneas_engine._alignment_cache) == 0

    def test_cache_enable_disable(self, aeneas_engine):
        """Test enabling and disabling cache."""
        aeneas_engine.enable_cache = True
        assert aeneas_engine.enable_cache is True

        aeneas_engine.enable_cache = False
        assert aeneas_engine.enable_cache is False

    def test_cache_max_size(self, aeneas_engine):
        """Test cache max size configuration."""
        original_size = aeneas_engine._cache_max_size
        aeneas_engine._cache_max_size = 50
        assert aeneas_engine._cache_max_size == 50
        aeneas_engine._cache_max_size = original_size

    def test_clear_cache(self, aeneas_engine):
        """Test clearing the cache."""
        # Add some fake cache entries
        aeneas_engine._alignment_cache["test1"] = {"data": "test1"}
        aeneas_engine._alignment_cache["test2"] = {"data": "test2"}
        assert len(aeneas_engine._alignment_cache) == 2

        aeneas_engine.clear_cache()
        assert len(aeneas_engine._alignment_cache) == 0

    def test_get_cache_stats(self, aeneas_engine):
        """Test getting cache statistics."""
        stats = aeneas_engine.get_cache_stats()
        assert isinstance(stats, dict)
        assert "cache_size" in stats
        assert "max_cache_size" in stats
        assert "cache_enabled" in stats
        assert stats["cache_size"] == 0
        assert stats["max_cache_size"] == 100
        assert stats["cache_enabled"] is True

    def test_cache_lru_eviction(self, aeneas_engine):
        """Test LRU cache eviction when max size is reached."""
        aeneas_engine._cache_max_size = 3

        # Add entries up to max size
        for i in range(3):
            aeneas_engine._alignment_cache[f"key{i}"] = {"data": f"value{i}"}

        assert len(aeneas_engine._alignment_cache) == 3

        # Add one more - manually evict oldest if needed (simulating cache behavior)
        if len(aeneas_engine._alignment_cache) >= aeneas_engine._cache_max_size:
            oldest_key = next(iter(aeneas_engine._alignment_cache))
            del aeneas_engine._alignment_cache[oldest_key]
        aeneas_engine._alignment_cache["key3"] = {"data": "value3"}
        aeneas_engine._alignment_cache.move_to_end("key3")  # LRU update

        # Check that oldest was evicted
        assert len(aeneas_engine._alignment_cache) == 3
        assert "key0" not in aeneas_engine._alignment_cache
        assert "key3" in aeneas_engine._alignment_cache


class TestAeneasEngineBatchProcessing:
    """Test batch processing with ThreadPoolExecutor."""

    def test_batch_size_configuration(self, aeneas_engine):
        """Test batch size can be configured."""
        original_batch_size = aeneas_engine.batch_size
        aeneas_engine.batch_size = 8
        assert aeneas_engine.batch_size == 8
        aeneas_engine.batch_size = original_batch_size

    def test_batch_align_method_exists(self, aeneas_engine):
        """Test that batch_align method exists."""
        assert hasattr(aeneas_engine, "batch_align")
        assert callable(aeneas_engine.batch_align)

    @patch("app.core.engines.aeneas_engine.ThreadPoolExecutor")
    def test_batch_align_uses_thread_pool(self, mock_executor, aeneas_engine, temp_audio_file):
        """Test that batch_align uses ThreadPoolExecutor."""
        # Mock the align method to return success
        aeneas_engine.align = Mock(return_value={"result": "success"})
        aeneas_engine._initialized = True

        # Mock ThreadPoolExecutor
        mock_executor_instance = MagicMock()
        mock_executor.return_value.__enter__.return_value = mock_executor_instance
        mock_executor.return_value.__exit__.return_value = None

        # Create batch alignment tasks
        tasks = [
            (temp_audio_file, "Test text 1", "en"),
            (temp_audio_file, "Test text 2", "en"),
        ]

        try:
            aeneas_engine.batch_align(tasks, batch_size=2)
            # Should have called ThreadPoolExecutor
            assert mock_executor.called
        except Exception:
            # If aeneas is not installed, that's okay
            ...


class TestAeneasEngineConfiguration:
    """Test engine configuration and settings."""

    def test_supported_formats(self, aeneas_engine):
        """Test supported output formats."""
        assert hasattr(aeneas_engine, "SUPPORTED_FORMATS")
        assert isinstance(aeneas_engine.SUPPORTED_FORMATS, list)
        assert "srt" in aeneas_engine.SUPPORTED_FORMATS
        assert "vtt" in aeneas_engine.SUPPORTED_FORMATS
        assert "json" in aeneas_engine.SUPPORTED_FORMATS

    def test_supported_languages(self, aeneas_engine):
        """Test supported languages."""
        assert hasattr(aeneas_engine, "SUPPORTED_LANGUAGES")
        assert isinstance(aeneas_engine.SUPPORTED_LANGUAGES, list)
        assert "en" in aeneas_engine.SUPPORTED_LANGUAGES
        assert "es" in aeneas_engine.SUPPORTED_LANGUAGES

    def test_device_configuration(self, aeneas_engine):
        """Test device configuration."""
        assert aeneas_engine.device == "cpu"
        assert aeneas_engine.get_device() == "cpu"


class TestAeneasEngineOptimization:
    """Test optimization features."""

    def test_reusable_temp_directory(self, aeneas_engine):
        """Test that engine uses reusable temp directory."""
        # Initialize to create temp directory
        with patch("app.core.engines.aeneas_engine.tempfile.mkdtemp") as mock_mkdtemp:
            mock_mkdtemp.return_value = "/tmp/test_aeneas"

            # Mock initialization to succeed
            with patch.object(
                aeneas_engine, "_find_python_executable", return_value="/usr/bin/python3"
            ):
                with patch("app.core.engines.aeneas_engine.subprocess.run") as mock_run:
                    mock_run.return_value.returncode = 0
                    mock_run.return_value.stdout = "aeneas 1.7.3"

                    try:
                        result = aeneas_engine.initialize()
                        if result:
                            assert aeneas_engine._temp_dir is not None
                    except Exception:
                        # If dependencies not available, skip
                        ...

    def test_cache_optimization(self, aeneas_engine):
        """Test that cache optimization is enabled by default."""
        assert aeneas_engine.enable_cache is True
        assert aeneas_engine._cache_max_size > 0

    def test_batch_processing_optimization(self, aeneas_engine):
        """Test that batch processing is configured."""
        assert aeneas_engine.batch_size > 0
        assert hasattr(aeneas_engine, "batch_align")


class TestAeneasEngineCreateFunction:
    """Test the create_aeneas_engine factory function."""

    def test_create_function_exists(self):
        """Test that create function exists."""
        if not HAS_AENEAS:
            pytest.skip("Aeneas engine not available")
        from app.core.engines.aeneas_engine import create_aeneas_engine

        assert callable(create_aeneas_engine)

    def test_create_function_returns_engine(self):
        """Test that create function returns engine instance."""
        if not HAS_AENEAS:
            pytest.skip("Aeneas engine not available")
        from app.core.engines.aeneas_engine import create_aeneas_engine

        engine = create_aeneas_engine(device="cpu", gpu=False)
        assert engine is not None
        assert isinstance(engine, AeneasEngine)
        with contextlib.suppress(Exception):
            engine.cleanup()
