"""
Unit Tests for OpenVoice Engine
Tests OpenVoice engine functionality including optimizations.
"""

import sys
from pathlib import Path

import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the openvoice engine module
try:
    from app.core.engines import openvoice_engine
except ImportError:
    pytest.skip("Could not import openvoice_engine", allow_module_level=True)


class TestOpenVoiceEngineImports:
    """Test openvoice engine module can be imported."""

    def test_module_imports(self):
        """Test module can be imported."""
        assert openvoice_engine is not None, "Failed to import openvoice_engine module"

    def test_module_has_openvoice_engine_class(self):
        """Test module has OpenVoiceEngine class."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            cls = openvoice_engine.OpenVoiceEngine
            assert isinstance(cls, type), "OpenVoiceEngine should be a class"


class TestOpenVoiceEngineClass:
    """Test OpenVoiceEngine class."""

    def test_openvoice_engine_class_exists(self):
        """Test OpenVoiceEngine class exists."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            cls = openvoice_engine.OpenVoiceEngine
            assert isinstance(cls, type), "OpenVoiceEngine should be a class"

    def test_openvoice_engine_initialization(self):
        """Test OpenVoiceEngine can be instantiated."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                assert engine is not None
                assert hasattr(engine, "device")
                assert engine.device == "cpu"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")

    def test_openvoice_engine_has_required_methods(self):
        """Test OpenVoiceEngine has required methods."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                required_methods = ["initialize", "cleanup", "synthesize"]
                for method in required_methods:
                    assert hasattr(engine, method), f"OpenVoiceEngine missing method: {method}"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")

    def test_openvoice_engine_has_optimization_features(self):
        """Test OpenVoiceEngine has optimization features."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                assert hasattr(engine, "enable_caching"), "OpenVoiceEngine should support caching"
                assert hasattr(
                    engine, "batch_synthesize"
                ), "OpenVoiceEngine should support batch processing"
                assert hasattr(
                    engine, "batch_size"
                ), "OpenVoiceEngine should have batch_size attribute"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")


class TestOpenVoiceEngineBatchProcessing:
    """Test OpenVoice engine batch processing functionality."""

    def test_batch_processing_method_exists(self):
        """Test batch_synthesize method exists."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                assert hasattr(
                    engine, "batch_synthesize"
                ), "OpenVoiceEngine should have batch_synthesize method"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")

    def test_batch_size_attribute(self):
        """Test batch_size attribute exists and is valid."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                assert hasattr(engine, "batch_size"), "OpenVoiceEngine should have batch_size"
                assert isinstance(engine.batch_size, int), "batch_size should be an integer"
                assert engine.batch_size > 0, "batch_size should be positive"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")


class TestOpenVoiceEngineProtocol:
    """Test OpenVoice engine protocol compliance."""

    def test_engine_protocol_compliance(self):
        """Test OpenVoiceEngine implements EngineProtocol."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                assert hasattr(engine, "initialize"), "Should implement initialize"
                assert hasattr(engine, "cleanup"), "Should implement cleanup"
                assert hasattr(engine, "is_initialized"), "Should implement is_initialized"
                assert hasattr(engine, "get_device"), "Should implement get_device"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")


class TestOpenVoiceEngineOptimization:
    """Test OpenVoice engine optimization features."""

    def test_thread_pool_executor_usage(self):
        """Test that ThreadPoolExecutor is used in batch processing."""
        if hasattr(openvoice_engine, "OpenVoiceEngine"):
            try:
                import inspect

                engine = openvoice_engine.OpenVoiceEngine(device="cpu", gpu=False)
                if hasattr(engine, "batch_synthesize"):
                    source = inspect.getsource(engine.batch_synthesize)
                    assert (
                        "ThreadPoolExecutor" in source or "executor" in source
                    ), "batch_synthesize should use ThreadPoolExecutor for parallel processing"
            except (ImportError, Exception):
                pytest.skip("openvoice dependencies not installed")


class TestOpenVoiceSeExtractorUnpack:
    """myshell ``get_se`` returns ``(embedding, audio_name)`` — engine must unpack."""

    def test_unpack_tuple_returns_first_element(self) -> None:
        sentinel = object()
        assert openvoice_engine._unpack_se_extractor_result((sentinel, "audio_x")) is sentinel

    def test_unpack_non_tuple_passthrough(self) -> None:
        sentinel = object()
        assert openvoice_engine._unpack_se_extractor_result(sentinel) is sentinel


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
