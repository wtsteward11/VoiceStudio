"""
Unit tests for whisper.cpp engine (Slice 25 — deterministic; no assertion→skip laundering).
"""

from __future__ import annotations

import logging
import sys
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest

project_root = Path(__file__).parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from app.core.engines import whisper_cpp_engine
except ImportError:
    pytest.skip("Could not import whisper_cpp_engine", allow_module_level=True)


class TestWhisperCPPEngineImports:
    def test_module_imports(self) -> None:
        assert whisper_cpp_engine is not None

    def test_module_has_whisper_cpp_engine_class(self) -> None:
        cls = whisper_cpp_engine.WhisperCPPEngine
        assert isinstance(cls, type)


class TestWhisperCPPEngineSurface:
    """API surface without optional native stacks (no broad except → skip)."""

    def test_cache_key_generation(self) -> None:
        if hasattr(whisper_cpp_engine, "_get_cache_key"):
            key = whisper_cpp_engine._get_cache_key("test_model.bin", "en")
            assert isinstance(key, str)
            assert "whisper_cpp" in key
            assert "test_model" in key
            assert "en" in key

    def test_instance_layout_with_temp_model_path(self, tmp_path: Path) -> None:
        gguf = tmp_path / "stub.gguf"
        gguf.write_bytes(b"x")
        engine = whisper_cpp_engine.WhisperCPPEngine(
            model_path=str(gguf),
            language="en",
        )
        assert engine.model_path == str(gguf)
        assert engine.language == "en"
        assert engine.batch_size == 4
        assert hasattr(engine, "_transcription_cache")
        assert hasattr(engine, "batch_transcribe")
        assert hasattr(engine, "clear_transcription_cache")
        assert hasattr(engine, "_caching_enabled")


class TestWhisperCPPEngineSlice23NoFallback:
    """Slice 23: no cross-engine substitution into faster-whisper (WhisperEngine)."""

    def test_perform_transcription_returns_none_without_binding_or_cli(
        self, monkeypatch: pytest.MonkeyPatch, caplog: pytest.LogCaptureFixture
    ) -> None:
        monkeypatch.setattr(whisper_cpp_engine, "HAS_WHISPER_CPP", False)
        monkeypatch.setattr(whisper_cpp_engine, "HAS_NUMPY", True)

        engine = whisper_cpp_engine.WhisperCPPEngine(
            model_path="/nonexistent/whisper-medium.en.gguf",
            language="en",
        )
        engine._initialized = True
        engine._ctx = None

        audio = np.zeros(16000, dtype=np.float32)
        caplog.set_level(logging.ERROR)
        with patch("app.core.engines.whisper_engine.WhisperEngine") as mock_whisper_engine_cls:
            with patch.object(engine, "_check_whisper_cpp_binary", return_value=False):
                result = engine._perform_transcription(audio, 16000, "en")

        assert result is None
        mock_whisper_engine_cls.assert_not_called()
        assert any(
            "engine_id=whisper_cpp" in r.getMessage() for r in caplog.records
        ), "failure logs must attribute to whisper_cpp"
