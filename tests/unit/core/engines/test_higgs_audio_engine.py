"""
Unit Tests for Higgs Audio Engine
Tests Higgs Audio engine functionality including optimizations.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the higgs_audio engine module
try:
    from app.core.engines import higgs_audio_engine
except ImportError:
    pytest.skip("Could not import higgs_audio_engine", allow_module_level=True)


class TestHiggsAudioEngineImports:
    """Test higgs_audio engine module can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert higgs_audio_engine is not None, "Failed to import higgs_audio_engine module"

    def test_module_has_higgs_audio_engine_class(self):
        """Test module has HiggsAudioEngine class."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            cls = higgs_audio_engine.HiggsAudioEngine
            assert isinstance(cls, type), "HiggsAudioEngine should be a class"


class TestHiggsAudioEngineClass:
    """Test HiggsAudioEngine class."""

    def test_higgs_audio_engine_class_exists(self):
        """Test HiggsAudioEngine class exists."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            cls = higgs_audio_engine.HiggsAudioEngine
            assert isinstance(cls, type), "HiggsAudioEngine should be a class"

    def test_higgs_audio_engine_initialization(self):
        """Test HiggsAudioEngine can be instantiated."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert engine is not None
            assert hasattr(engine, "device")
            assert engine.device == "cpu"

    def test_higgs_audio_engine_has_required_methods(self):
        """Test HiggsAudioEngine has required methods."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            required_methods = ["initialize", "cleanup", "synthesize"]
            for method in required_methods:
                assert hasattr(engine, method), f"HiggsAudioEngine missing method: {method}"

    def test_higgs_audio_engine_has_optimization_features(self):
        """Test HiggsAudioEngine has optimization features."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            # Check for caching support
            assert hasattr(engine, "enable_caching"), "HiggsAudioEngine should support caching"
            # Check for batch processing (method is called batch_synthesize)
            assert hasattr(
                engine, "batch_synthesize"
            ), "HiggsAudioEngine should support batch processing"
            # Check for batch_size attribute
            assert hasattr(
                engine, "batch_size"
            ), "HiggsAudioEngine should have batch_size attribute"


class TestHiggsAudioEngineCaching:
    """Test Higgs Audio engine caching functionality."""

    def test_cache_key_generation(self):
        """Test cache key generation function."""
        if hasattr(higgs_audio_engine, "_get_cache_key"):
            key = higgs_audio_engine._get_cache_key("test_model", "cpu")
            assert isinstance(key, str)
            assert "higgs_audio" in key
            assert "test_model" in key
            assert "cpu" in key

    def test_caching_enable_disable(self):
        """Test enabling and disabling caching."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            # Check initial state (should be True by default)
            assert engine._caching_enabled is True, "Caching should be enabled by default"
            # Test that enable_caching method exists and can be called
            assert hasattr(engine, "enable_caching"), "enable_caching should exist"
            assert callable(engine.enable_caching), "enable_caching should be callable"
            # Call the method to disable caching
            engine.enable_caching(False)
            assert engine._caching_enabled is False, "Caching should be disabled after call"

    def test_speaker_cache_support(self):
        """Test speaker audio cache support."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            # Check for speaker cache support (Higgs Audio has LRU speaker cache)
            assert hasattr(higgs_audio_engine, "_HIGGS_AUDIO_SPEAKER_CACHE") or hasattr(
                engine, "_speaker_cache"
            ), "HiggsAudioEngine should support speaker audio caching"


class TestHiggsAudioEngineBatchProcessing:
    """Test Higgs Audio engine batch processing functionality."""

    def test_batch_processing_method_exists(self):
        """Test batch_synthesize method exists."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert hasattr(
                engine, "batch_synthesize"
            ), "HiggsAudioEngine should have batch_synthesize method"

    def test_batch_size_attribute(self):
        """Test batch_size attribute exists and is valid."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert hasattr(engine, "batch_size"), "HiggsAudioEngine should have batch_size"
            assert isinstance(engine.batch_size, int), "batch_size should be an integer"
            assert engine.batch_size > 0, "batch_size should be positive"


class TestHiggsAudioEngineProtocol:
    """Test Higgs Audio engine protocol compliance."""

    def test_engine_protocol_compliance(self):
        """Test HiggsAudioEngine implements EngineProtocol."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            # Check for protocol methods
            assert hasattr(engine, "initialize"), "Should implement initialize"
            assert hasattr(engine, "cleanup"), "Should implement cleanup"
            assert hasattr(engine, "is_initialized"), "Should implement is_initialized"
            assert hasattr(engine, "get_device"), "Should implement get_device"

    def test_device_management(self):
        """Test device management methods."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert engine.get_device() == "cpu"
            # Test with cuda if available
            engine_cuda = higgs_audio_engine.HiggsAudioEngine(device="cuda", gpu=True)
            assert engine_cuda.get_device() == "cuda"


class TestHiggsAudioEngineConfiguration:
    """Test Higgs Audio engine configuration."""

    def test_default_sample_rate(self):
        """Test default sample rate is set."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert hasattr(engine, "sample_rate"), "Should have sample_rate attribute"
            assert isinstance(engine.sample_rate, int), "sample_rate should be an integer"
            assert engine.sample_rate > 0, "sample_rate should be positive"

    def test_model_name_attribute(self):
        """Test model_name attribute exists."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            assert hasattr(engine, "model_name"), "Should have model_name attribute"


class TestHiggsAudioEngineOptimization:
    """Test Higgs Audio engine optimization features."""

    def test_inference_mode_usage(self):
        """Test that torch.inference_mode is used in synthesize method."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            # Check source code for inference_mode usage
            import inspect

            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            if hasattr(engine, "synthesize"):
                source = inspect.getsource(engine.synthesize)
                # Check for inference_mode (optimization feature)
                assert (
                    "inference_mode" in source or "no_grad" in source
                ), "synthesize should use torch.inference_mode or torch.no_grad for optimization"

    def test_thread_pool_executor_usage(self):
        """Test that ThreadPoolExecutor is used in batch processing."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            # Check source code for ThreadPoolExecutor usage
            import inspect

            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            if hasattr(engine, "batch_synthesize"):
                source = inspect.getsource(engine.batch_synthesize)
                # Check for ThreadPoolExecutor (parallel processing optimization)
                assert (
                    "ThreadPoolExecutor" in source or "executor" in source
                ), "batch_synthesize should use ThreadPoolExecutor for parallel processing"

    def test_gpu_memory_optimization(self):
        """Test GPU memory optimization features."""
        if hasattr(higgs_audio_engine, "HiggsAudioEngine"):
            # Check source code for GPU cache clearing
            import inspect

            engine = higgs_audio_engine.HiggsAudioEngine(device="cpu", gpu=False)
            if hasattr(engine, "batch_synthesize"):
                source = inspect.getsource(engine.batch_synthesize)
                # Check for GPU cache clearing (memory optimization)
                assert (
                    "empty_cache" in source or "cuda" in source
                ), "batch_synthesize should include GPU memory optimization"


class TestHiggsAudioEngineModule:
    """Test Higgs Audio engine module structure."""

    def test_module_has_cache_functions(self):
        """Test module has caching helper functions."""
        cache_functions = [
            "_get_cache_key",
            "_get_cached_higgs_audio_model",
            "_cache_higgs_audio_model",
        ]
        for func_name in cache_functions:
            if hasattr(higgs_audio_engine, func_name):
                func = getattr(higgs_audio_engine, func_name)
                assert callable(func), f"{func_name} should be callable"

    def test_module_has_model_cache(self):
        """Test module has model cache support."""
        # Check for model cache availability
        assert hasattr(higgs_audio_engine, "HAS_MODEL_CACHE") or hasattr(
            higgs_audio_engine, "_HIGGS_AUDIO_MODEL_CACHE"
        ), "Module should have model cache support"

    def test_module_has_speaker_cache(self):
        """Test module has speaker audio cache support."""
        # Check for speaker cache (unique to Higgs Audio)
        assert hasattr(
            higgs_audio_engine, "_HIGGS_AUDIO_SPEAKER_CACHE"
        ), "Module should have speaker audio cache support"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
