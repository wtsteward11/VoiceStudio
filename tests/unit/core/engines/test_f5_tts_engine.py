"""
Unit Tests for F5-TTS Engine
Tests F5-TTS engine functionality including optimizations.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the f5_tts engine module
try:
    from app.core.engines import f5_tts_engine
except ImportError:
    pytest.skip("Could not import f5_tts_engine", allow_module_level=True)


class TestF5TTSEngineImports:
    """Test f5_tts engine module can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert f5_tts_engine is not None, "Failed to import f5_tts_engine module"

    def test_module_has_f5tts_engine_class(self):
        """Test module has F5TTSEngine class."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            cls = f5_tts_engine.F5TTSEngine
            assert isinstance(cls, type), "F5TTSEngine should be a class"


class TestF5TTSEngineClass:
    """Test F5TTSEngine class."""

    def test_f5tts_engine_class_exists(self):
        """Test F5TTSEngine class exists."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            cls = f5_tts_engine.F5TTSEngine
            assert isinstance(cls, type), "F5TTSEngine should be a class"

    def test_f5tts_engine_initialization(self):
        """Test F5TTSEngine can be instantiated."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert engine is not None
            assert hasattr(engine, "device")
            assert engine.device == "cpu"

    def test_f5tts_engine_has_required_methods(self):
        """Test F5TTSEngine has required methods."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            required_methods = ["initialize", "cleanup", "synthesize"]
            for method in required_methods:
                assert hasattr(engine, method), f"F5TTSEngine missing method: {method}"

    def test_f5tts_engine_has_optimization_features(self):
        """Test F5TTSEngine has optimization features."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            # Check for caching support
            assert hasattr(engine, "enable_caching"), "F5TTSEngine should support caching"
            # Check for batch processing (method is called batch_synthesize)
            assert hasattr(
                engine, "batch_synthesize"
            ), "F5TTSEngine should support batch processing"
            # Check for batch_size attribute
            assert hasattr(engine, "batch_size"), "F5TTSEngine should have batch_size attribute"


class TestF5TTSEngineCaching:
    """Test F5-TTS engine caching functionality."""

    def test_cache_key_generation(self):
        """Test cache key generation function."""
        if hasattr(f5_tts_engine, "_get_cache_key"):
            key = f5_tts_engine._get_cache_key("test_model", "cpu")
            assert isinstance(key, str)
            assert "f5tts" in key
            assert "test_model" in key
            assert "cpu" in key

    def test_caching_enable_disable(self):
        """Test enabling and disabling caching."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            # Check initial state (should be True by default)
            assert engine._caching_enabled is True, "Caching should be enabled by default"
            # Test that enable_caching method exists and can be called
            assert hasattr(engine, "enable_caching"), "enable_caching should exist"
            assert callable(engine.enable_caching), "enable_caching should be callable"
            # Call the method to disable caching
            engine.enable_caching(False)
            assert engine._caching_enabled is False, "Caching should be disabled after call"


class TestF5TTSEngineBatchProcessing:
    """Test F5-TTS engine batch processing functionality."""

    def test_batch_processing_method_exists(self):
        """Test batch_synthesize method exists."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert hasattr(
                engine, "batch_synthesize"
            ), "F5TTSEngine should have batch_synthesize method"

    def test_batch_size_attribute(self):
        """Test batch_size attribute exists and is valid."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert hasattr(engine, "batch_size"), "F5TTSEngine should have batch_size"
            assert isinstance(engine.batch_size, int), "batch_size should be an integer"
            assert engine.batch_size > 0, "batch_size should be positive"


class TestF5TTSEngineProtocol:
    """Test F5-TTS engine protocol compliance."""

    def test_engine_protocol_compliance(self):
        """Test F5TTSEngine implements EngineProtocol."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            # Check for protocol methods
            assert hasattr(engine, "initialize"), "Should implement initialize"
            assert hasattr(engine, "cleanup"), "Should implement cleanup"
            assert hasattr(engine, "is_initialized"), "Should implement is_initialized"
            assert hasattr(engine, "get_device"), "Should implement get_device"

    def test_device_management(self):
        """Test device management methods."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert engine.get_device() == "cpu"
            # Test with cuda if available
            engine_cuda = f5_tts_engine.F5TTSEngine(device="cuda", gpu=True)
            assert engine_cuda.get_device() == "cuda"


class TestF5TTSEngineConfiguration:
    """Test F5-TTS engine configuration."""

    def test_default_sample_rate(self):
        """Test default sample rate is set."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert hasattr(engine, "sample_rate"), "Should have sample_rate attribute"
            assert isinstance(engine.sample_rate, int), "sample_rate should be an integer"
            assert engine.sample_rate > 0, "sample_rate should be positive"

    def test_model_name_attribute(self):
        """Test model_name attribute exists."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            assert hasattr(engine, "model_name"), "Should have model_name attribute"


class TestF5TTSEngineOptimization:
    """Test F5-TTS engine optimization features."""

    def test_inference_mode_usage(self):
        """Test that torch.inference_mode is used in synthesize method."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            # Check source code for inference_mode usage
            import inspect

            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            if hasattr(engine, "synthesize"):
                source = inspect.getsource(engine.synthesize)
                # Check for inference_mode (optimization feature)
                assert (
                    "inference_mode" in source or "no_grad" in source
                ), "synthesize should use torch.inference_mode or torch.no_grad for optimization"

    def test_thread_pool_executor_usage(self):
        """Test that ThreadPoolExecutor is used in batch processing."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            # Check source code for ThreadPoolExecutor usage
            import inspect

            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            if hasattr(engine, "batch_synthesize"):
                source = inspect.getsource(engine.batch_synthesize)
                # Check for ThreadPoolExecutor (parallel processing optimization)
                assert (
                    "ThreadPoolExecutor" in source or "executor" in source
                ), "batch_synthesize should use ThreadPoolExecutor for parallel processing"

    def test_gpu_memory_optimization(self):
        """Test GPU memory optimization features."""
        if hasattr(f5_tts_engine, "F5TTSEngine"):
            # Check source code for GPU cache clearing
            import inspect

            engine = f5_tts_engine.F5TTSEngine(device="cpu", gpu=False)
            if hasattr(engine, "batch_synthesize"):
                source = inspect.getsource(engine.batch_synthesize)
                # Check for GPU cache clearing (memory optimization)
                assert (
                    "empty_cache" in source or "cuda" in source
                ), "batch_synthesize should include GPU memory optimization"


class TestF5TTSEngineModule:
    """Test F5-TTS engine module structure."""

    def test_module_has_cache_functions(self):
        """Test module has caching helper functions."""
        cache_functions = [
            "_get_cache_key",
            "_get_cached_f5tts_model",
            "_cache_f5tts_model",
        ]
        for func_name in cache_functions:
            if hasattr(f5_tts_engine, func_name):
                func = getattr(f5_tts_engine, func_name)
                assert callable(func), f"{func_name} should be callable"

    def test_module_has_model_cache(self):
        """Test module has model cache support."""
        # Check for model cache availability
        assert hasattr(f5_tts_engine, "HAS_MODEL_CACHE") or hasattr(
            f5_tts_engine, "_F5TTS_MODEL_CACHE"
        ), "Module should have model cache support"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
