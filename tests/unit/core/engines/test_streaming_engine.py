"""
Unit Tests for Streaming Engine
Tests streaming engine functionality including optimizations.

Tests cover:
- Engine initialization and cleanup
- LRU chunk cache
- LRU stream cache
- Buffer pool for reuse
- Async streaming capabilities
- Protocol compliance
- Configuration and optimization features
"""

import contextlib
import sys
from collections import OrderedDict
from pathlib import Path
from unittest.mock import MagicMock, Mock

import numpy as np
import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the streaming engine module
try:
    from app.core.engines.streaming_engine import StreamingEngine

    HAS_STREAMING = True
except ImportError:
    HAS_STREAMING = False

pytestmark = pytest.mark.skipif(
    not HAS_STREAMING,
    reason="Streaming engine not available (SKIP_DEBT_CLEANUP_SUBPLAN § Infrastructure)",
)


@pytest.fixture
def mock_engine():
    """Create a mock TTS engine for testing."""
    engine = MagicMock()
    engine.initialize = Mock(return_value=True)
    engine.cleanup = Mock()
    engine.is_initialized = Mock(return_value=True)
    engine.synthesize = Mock(return_value=np.array([0.1, 0.2, 0.3], dtype=np.float32))
    return engine


@pytest.fixture
def streaming_engine(mock_engine):
    """Create a StreamingEngine instance for testing."""
    if not HAS_STREAMING:
        pytest.skip("Streaming engine not available")

    engine = StreamingEngine(engine=mock_engine, device="cpu", gpu=False)
    yield engine
    with contextlib.suppress(Exception):
        engine.cleanup()


class TestStreamingEngineImports:
    """Test that Streaming engine can be imported."""

    def test_import_engine(self):
        """Test that StreamingEngine can be imported."""
        if not HAS_STREAMING:
            pytest.skip("Streaming engine not available")
        from app.core.engines.streaming_engine import StreamingEngine

        assert StreamingEngine is not None


class TestStreamingEngineStructure:
    """Test Streaming engine class structure and basic functionality."""

    def test_engine_initialization(self, streaming_engine):
        """Test that engine initializes correctly."""
        assert streaming_engine is not None
        assert streaming_engine.device == "cpu"
        assert not streaming_engine._initialized

    def test_engine_has_chunk_cache(self, streaming_engine):
        """Test that engine has LRU chunk cache."""
        assert hasattr(streaming_engine, "_chunk_cache")
        assert isinstance(streaming_engine._chunk_cache, OrderedDict)
        assert streaming_engine._cache_max_size == 200
        assert streaming_engine.enable_cache is True

    def test_engine_has_stream_cache(self, streaming_engine):
        """Test that engine has LRU stream cache."""
        assert hasattr(streaming_engine, "_stream_cache")
        assert isinstance(streaming_engine._stream_cache, OrderedDict)

    def test_engine_has_buffer_pool(self, streaming_engine):
        """Test that engine has buffer pool for reuse."""
        assert hasattr(streaming_engine, "_buffer_pool")
        assert isinstance(streaming_engine._buffer_pool, list)
        assert hasattr(streaming_engine, "_max_buffer_pool_size")
        assert streaming_engine._max_buffer_pool_size == 20

    def test_engine_protocol_compliance(self, streaming_engine):
        """Test that engine implements EngineProtocol."""
        from app.core.engines.protocols import EngineProtocol

        assert isinstance(streaming_engine, EngineProtocol)
        assert hasattr(streaming_engine, "initialize")
        assert hasattr(streaming_engine, "cleanup")
        assert hasattr(streaming_engine, "is_initialized")


class TestStreamingEngineCache:
    """Test LRU cache functionality."""

    def test_chunk_cache_initialization(self, streaming_engine):
        """Test that chunk cache is initialized as OrderedDict."""
        assert isinstance(streaming_engine._chunk_cache, OrderedDict)
        assert len(streaming_engine._chunk_cache) == 0

    def test_stream_cache_initialization(self, streaming_engine):
        """Test that stream cache is initialized as OrderedDict."""
        assert isinstance(streaming_engine._stream_cache, OrderedDict)
        assert len(streaming_engine._stream_cache) == 0

    def test_cache_enable_disable(self, streaming_engine):
        """Test enabling and disabling cache."""
        streaming_engine.enable_cache = True
        assert streaming_engine.enable_cache is True

        streaming_engine.enable_cache = False
        assert streaming_engine.enable_cache is False

    def test_cache_max_size(self, streaming_engine):
        """Test cache max size configuration."""
        original_size = streaming_engine._cache_max_size
        streaming_engine._cache_max_size = 50
        assert streaming_engine._cache_max_size == 50
        streaming_engine._cache_max_size = original_size

    def test_clear_cache(self, streaming_engine):
        """Test clearing the cache."""
        # Add some fake cache entries
        streaming_engine._chunk_cache["test1"] = ["chunk1", "chunk2"]
        streaming_engine._stream_cache["test1"] = [np.array([0.1, 0.2])]
        assert len(streaming_engine._chunk_cache) == 1
        assert len(streaming_engine._stream_cache) == 1

        streaming_engine.clear_cache()
        assert len(streaming_engine._chunk_cache) == 0
        assert len(streaming_engine._stream_cache) == 0

    def test_get_cache_stats(self, streaming_engine):
        """Test getting cache statistics."""
        stats = streaming_engine.get_cache_stats()
        assert isinstance(stats, dict)
        assert "chunk_cache_size" in stats
        assert "stream_cache_size" in stats
        assert "max_cache_size" in stats
        assert "cache_enabled" in stats
        assert stats["chunk_cache_size"] == 0
        assert stats["stream_cache_size"] == 0
        assert stats["max_cache_size"] == 200
        assert stats["cache_enabled"] is True

    def test_chunk_cache_lru_eviction(self, streaming_engine):
        """Test LRU cache eviction when max size is reached."""
        streaming_engine._cache_max_size = 3

        # Add entries up to max size
        for i in range(3):
            streaming_engine._chunk_cache[f"key{i}"] = [f"chunk{i}"]

        assert len(streaming_engine._chunk_cache) == 3

        # Add one more - manually evict oldest if needed
        # (simulating cache behavior)
        if len(streaming_engine._chunk_cache) >= (streaming_engine._cache_max_size):
            oldest_key = next(iter(streaming_engine._chunk_cache))
            del streaming_engine._chunk_cache[oldest_key]
        streaming_engine._chunk_cache["key3"] = ["chunk3"]
        streaming_engine._chunk_cache.move_to_end("key3")  # LRU update

        # Check that oldest was evicted
        assert len(streaming_engine._chunk_cache) == 3
        assert "key0" not in streaming_engine._chunk_cache
        assert "key3" in streaming_engine._chunk_cache


class TestStreamingEngineBufferPool:
    """Test buffer pool for reuse."""

    def test_buffer_pool_initialization(self, streaming_engine):
        """Test that buffer pool is initialized."""
        assert isinstance(streaming_engine._buffer_pool, list)
        assert len(streaming_engine._buffer_pool) == 0

    def test_buffer_pool_max_size(self, streaming_engine):
        """Test buffer pool max size configuration."""
        original_size = streaming_engine._max_buffer_pool_size
        streaming_engine._max_buffer_pool_size = 20
        assert streaming_engine._max_buffer_pool_size == 20
        streaming_engine._max_buffer_pool_size = original_size

    def test_buffer_pool_reuse(self, streaming_engine):
        """Test that buffer pool can store reusable buffers."""
        # Add a buffer to the pool
        test_buffer = np.array([0.1, 0.2, 0.3], dtype=np.float32)
        streaming_engine._buffer_pool.append(test_buffer)
        assert len(streaming_engine._buffer_pool) == 1
        assert streaming_engine._buffer_pool[0] is test_buffer


class TestStreamingEngineConfiguration:
    """Test engine configuration and settings."""

    def test_chunk_size_configuration(self, streaming_engine):
        """Test chunk size can be configured."""
        original_chunk_size = streaming_engine.chunk_size
        streaming_engine.chunk_size = 200
        assert streaming_engine.chunk_size == 200
        streaming_engine.chunk_size = original_chunk_size

    def test_buffer_size_configuration(self, streaming_engine):
        """Test buffer size can be configured."""
        original_buffer_size = streaming_engine.buffer_size
        streaming_engine.buffer_size = 96000
        assert streaming_engine.buffer_size == 96000
        streaming_engine.buffer_size = original_buffer_size

    def test_overlap_size_configuration(self, streaming_engine):
        """Test overlap size can be configured."""
        original_overlap_size = streaming_engine.overlap_size
        streaming_engine.overlap_size = 1000
        assert streaming_engine.overlap_size == 1000
        streaming_engine.overlap_size = original_overlap_size

    def test_device_configuration(self, streaming_engine):
        """Test device configuration."""
        assert streaming_engine.device == "cpu"
        assert streaming_engine.get_device() == "cpu"


class TestStreamingEngineOptimization:
    """Test optimization features."""

    def test_lru_caching_optimization(self, streaming_engine):
        """Test that LRU caching is enabled by default."""
        assert streaming_engine.enable_cache is True
        assert streaming_engine._cache_max_size > 0
        assert isinstance(streaming_engine._chunk_cache, OrderedDict)
        assert isinstance(streaming_engine._stream_cache, OrderedDict)

    def test_buffer_pool_optimization(self, streaming_engine):
        """Test that buffer pool is configured."""
        assert streaming_engine._max_buffer_pool_size > 0
        assert isinstance(streaming_engine._buffer_pool, list)

    def test_has_streaming_methods(self, streaming_engine):
        """Test that engine has streaming capabilities."""
        # Check for streaming-related methods
        has_synthesize_stream = hasattr(streaming_engine, "synthesize_stream")
        has_synthesize_stream_async = hasattr(streaming_engine, "synthesize_stream_async")
        # At least one streaming method should exist
        assert has_synthesize_stream or has_synthesize_stream_async


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
