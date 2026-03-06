"""
Unit tests for XTTS Engine optimizations.

Tests model caching, lazy loading, batch processing, and GPU optimizations.
"""

import os
from unittest.mock import Mock, patch

import pytest

from app.core.engines.xtts_engine import (
    _MODEL_CACHE,
    XTTSEngine,
    _cache_model,
    _get_cache_key,
    _get_cached_model,
)


# Fixture to allow XTTS model loading in tests (bypasses license check)
@pytest.fixture(autouse=True)
def allow_xtts_loading(monkeypatch):
    """Set COQUI_TOS_AGREED to bypass license check in tests."""
    monkeypatch.setenv("COQUI_TOS_AGREED", "1")


class TestModelCaching:
    """Tests for model caching system."""

    def test_get_cache_key(self):
        """Test cache key generation."""
        key1 = _get_cache_key("model1", "cuda")
        key2 = _get_cache_key("model1", "cuda")
        key3 = _get_cache_key("model2", "cuda")

        assert key1 == key2
        assert key1 != key3

    @patch("app.core.engines.xtts_engine.HAS_MODEL_CACHE", False)
    @patch("app.core.engines.xtts_engine._model_cache", None)
    def test_cache_model(self):
        """Test caching a model using XTTS-specific fallback cache."""
        # Clear cache first
        _MODEL_CACHE.clear()

        model = Mock()
        _cache_model("model1", "cuda", model)

        assert len(_MODEL_CACHE) == 1
        cached = _get_cached_model("model1", "cuda")
        assert cached == model

    def test_get_cached_model_nonexistent(self):
        """Test getting non-existent cached model."""
        _MODEL_CACHE.clear()

        cached = _get_cached_model("nonexistent", "cuda")
        assert cached is None

    def test_cache_lru_eviction(self):
        """Test LRU eviction when cache is full."""
        _MODEL_CACHE.clear()

        # Set max cache size to 2
        from app.core.engines import xtts_engine

        original_max = xtts_engine._MAX_CACHE_SIZE
        xtts_engine._MAX_CACHE_SIZE = 2

        try:
            model1 = Mock()
            model2 = Mock()
            model3 = Mock()

            _cache_model("model1", "cuda", model1)
            _cache_model("model2", "cuda", model2)
            _cache_model("model3", "cuda", model3)  # Should evict model1

            assert _get_cached_model("model1", "cuda") is None
            assert _get_cached_model("model2", "cuda") == model2
            assert _get_cached_model("model3", "cuda") == model3
        finally:
            xtts_engine._MAX_CACHE_SIZE = original_max
            _MODEL_CACHE.clear()


class TestXTTSEngineOptimization:
    """Tests for XTTS Engine optimization features."""

    @patch("app.core.engines.xtts_engine.TTS")
    def test_enable_caching(self, mock_tts_class):
        """Test enabling/disabling caching (XTTS uses set_caching_enabled)."""
        engine = XTTSEngine()

        engine.set_caching_enabled(True)
        assert engine._use_cache is True

        engine.set_caching_enabled(False)
        assert engine._use_cache is False

    @patch("app.core.engines.xtts_engine.TTS")
    def test_set_batch_size(self, mock_tts_class):
        """Test setting batch size."""
        engine = XTTSEngine()

        engine.set_batch_size(10)
        assert engine._batch_size == 10

        engine.set_batch_size(0)  # Should be clamped to 1
        assert engine._batch_size == 1

    @patch("app.core.engines.xtts_engine.TTS")
    @patch("app.core.engines.xtts_engine.torch")
    @patch("app.core.engines.base._get_torch")
    def test_get_memory_usage_cuda(self, mock_get_torch, mock_torch, mock_tts_class):
        """Test getting GPU memory usage."""
        # Configure the mock torch module for both xtts_engine and base module
        mock_torch.cuda.is_available.return_value = True
        mock_torch.cuda.memory_allocated.return_value = 1024 * 1024 * 100  # 100 MB
        mock_torch.cuda.memory_reserved.return_value = 1024 * 1024 * 150  # 150 MB
        mock_torch.cuda.max_memory_allocated.return_value = 1024 * 1024 * 200  # 200 MB

        # Mock device properties
        mock_props = Mock()
        mock_props.name = "Test GPU"
        mock_props.total_memory = 4 * 1024**3  # 4GB
        mock_torch.cuda.get_device_properties.return_value = mock_props

        # Make _get_torch return our mock
        mock_get_torch.return_value = mock_torch

        engine = XTTSEngine(device="cuda")
        memory = engine.get_memory_usage()

        assert memory["gpu_available"] is True
        assert memory["allocated_mb"] == 100.0
        assert memory["reserved_mb"] == 150.0
        assert memory["max_allocated_mb"] == 200.0

    @patch("app.core.engines.xtts_engine.TTS")
    @patch("app.core.engines.xtts_engine.torch")
    def test_get_memory_usage_cpu(self, mock_torch, mock_tts_class):
        """Test getting memory usage on CPU."""
        mock_torch.cuda.is_available.return_value = False

        engine = XTTSEngine(device="cpu")
        memory = engine.get_memory_usage()

        assert memory["gpu_available"] is False

    @patch("app.core.engines.xtts_engine.TTS")
    def test_lazy_loading(self, mock_tts_class):
        """Test lazy loading initialization."""
        engine = XTTSEngine()

        # Initialize with lazy loading
        result = engine.initialize(lazy=True)

        assert result is True
        assert engine._initialized is True
        assert engine._lazy_load is True
        assert engine.tts is None  # Model not loaded yet

    @patch("app.core.engines.xtts_engine.TTS")
    def test_model_caching_on_initialize(self, mock_tts_class):
        """Test that model is cached on initialization."""
        _MODEL_CACHE.clear()

        mock_tts_instance = Mock()
        mock_tts_class.return_value = mock_tts_instance

        engine1 = XTTSEngine(model_name="test_model")
        engine1.initialize()

        # Second engine should use cached model
        engine2 = XTTSEngine(model_name="test_model", device="cuda")
        engine2.initialize()

        # Both should have the same model instance
        assert engine1.tts == engine2.tts

    @patch("app.core.engines.xtts_engine.HAS_MODEL_CACHE", False)
    @patch("app.core.engines.xtts_engine._model_cache", None)
    @patch("app.core.engines.xtts_engine.TTS")
    def test_cleanup_with_cache(self, mock_tts_class):
        """Test cleanup when model is cached (using XTTS-specific fallback cache)."""
        _MODEL_CACHE.clear()

        mock_tts_instance = Mock()
        mock_tts_class.return_value = mock_tts_instance

        engine = XTTSEngine()
        engine.initialize()

        # Model should be cached in fallback cache
        cache_key = _get_cache_key(engine.model_name, engine.device)
        assert cache_key in _MODEL_CACHE

        # Cleanup should not delete cached model
        engine.cleanup(clear_cache=False)
        assert cache_key in _MODEL_CACHE  # Still cached

        # Cleanup with clear_cache should remove from cache
        engine.cleanup(clear_cache=True)
        assert cache_key not in _MODEL_CACHE

    @patch("app.core.engines.xtts_engine.TTS")
    @patch("app.core.engines.xtts_engine.torch")
    def test_batch_synthesize_optimization(self, mock_torch, mock_tts_class):
        """Test batch synthesis optimizations."""
        mock_torch.cuda.is_available.return_value = True
        mock_torch.cuda.empty_cache = Mock()
        mock_torch.inference_mode = Mock(
            return_value=Mock(__enter__=Mock(), __exit__=Mock(return_value=None))
        )

        mock_tts_instance = Mock()
        mock_tts_instance.tts.return_value = [0.0] * 1000
        mock_tts_class.return_value = mock_tts_instance

        engine = XTTSEngine()
        engine.initialize()
        engine.set_batch_size(2)

        texts = ["Text 1", "Text 2", "Text 3"]
        results = engine.batch_synthesize(texts, "speaker.wav", batch_size=2)

        assert len(results) == 3
        # Should have called empty_cache for batch processing
        assert mock_torch.cuda.empty_cache.called


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
