"""
Unit tests for Tacotron 2 TTS Engine.

Tests engine initialization, synthesis, and cleanup without requiring
the actual Coqui TTS library (uses mocks).
"""

from unittest.mock import MagicMock, patch

import pytest


class TestTacotron2EngineInit:
    """Tests for Tacotron2Engine initialization."""

    def test_engine_has_correct_id(self):
        """Engine should have ENGINE_ID = 'tacotron2'."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        assert Tacotron2Engine.ENGINE_ID == "tacotron2"

    def test_supported_languages_list(self):
        """Engine should support multiple languages."""
        from app.core.engines.tacotron2_engine import SUPPORTED_LANGUAGES

        assert "en" in SUPPORTED_LANGUAGES
        assert "de" in SUPPORTED_LANGUAGES
        assert "fr" in SUPPORTED_LANGUAGES
        assert len(SUPPORTED_LANGUAGES) >= 9

    def test_available_models_has_all_languages(self):
        """Available models should cover all supported languages."""
        from app.core.engines.tacotron2_engine import (
            AVAILABLE_MODELS,
            SUPPORTED_LANGUAGES,
        )

        for lang in SUPPORTED_LANGUAGES:
            assert lang in AVAILABLE_MODELS
            assert "tacotron2" in AVAILABLE_MODELS[lang].lower()

    def test_init_sets_default_device(self):
        """Engine should default to cuda device when gpu=True and CUDA available."""
        from app.core.engines.base import EngineProtocol
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        # Mock CUDA detection to return cuda
        with patch.object(EngineProtocol, "_detect_cuda_device", return_value="cuda"):
            engine = Tacotron2Engine(gpu=True)
            assert engine.device == "cuda"

    def test_init_respects_cpu_device(self):
        """Engine should use cpu when gpu=False."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine(gpu=False)
        assert engine.device == "cpu"

    def test_init_respects_explicit_device(self):
        """Engine should use explicitly specified device."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine(device="cpu")
        assert engine.device == "cpu"

    def test_not_initialized_by_default(self):
        """Engine should not be initialized after construction."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()
        assert not engine.is_initialized()


class TestTacotron2EngineWithMocks:
    """Tests using mocked TTS library."""

    def test_initialize_creates_tts_instance(self):
        """Initialize should create TTS instance."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        # Setup mock TTS module
        mock_tts = MagicMock()
        mock_tts.synthesizer = MagicMock()
        mock_tts.synthesizer.output_sample_rate = 22050

        mock_tts_class = MagicMock(return_value=mock_tts)
        mock_tts_api = MagicMock()
        mock_tts_api.TTS = mock_tts_class

        engine = Tacotron2Engine(device="cpu")

        # Mock the TTS.api module that is imported inside initialize()
        with patch.dict("sys.modules", {"TTS": MagicMock(), "TTS.api": mock_tts_api}):
            result = engine.initialize()

        assert result is True
        assert engine.is_initialized()

    def test_synthesize_raises_if_not_initialized(self):
        """Synthesize should raise error if not initialized."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()

        with pytest.raises(RuntimeError, match="not initialized"):
            engine.synthesize("Hello world")

    def test_get_supported_languages_returns_copy(self):
        """get_supported_languages should return a copy of the list."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()
        langs1 = engine.get_supported_languages()
        langs2 = engine.get_supported_languages()

        assert langs1 == langs2
        assert langs1 is not langs2  # Different list objects

    def test_get_info_contains_engine_id(self):
        """get_info should include engine_id."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()
        info = engine.get_info()

        assert "engine_id" in info
        assert info["engine_id"] == "tacotron2"

    def test_health_check_false_when_not_initialized(self):
        """health_check should return healthy=False when not initialized."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()
        result = engine.health_check()
        assert result["healthy"] is False
        assert result["engine"] == "tacotron2"

    def test_get_health_details_structure(self):
        """get_health_details should return proper structure."""
        from app.core.engines.tacotron2_engine import Tacotron2Engine

        engine = Tacotron2Engine()
        details = engine.get_health_details()

        assert "healthy" in details
        assert "initialized" in details
        assert "model_loaded" in details
        assert "device" in details


class TestTacotron2EngineManifest:
    """Tests for engine manifest file."""

    def test_manifest_exists(self):
        """Manifest file should exist."""
        from pathlib import Path

        manifest_path = Path("engines/audio/tacotron2/engine.manifest.json")
        assert manifest_path.exists()

    def test_manifest_has_required_fields(self):
        """Manifest should have all required fields."""
        import json
        from pathlib import Path

        manifest_path = Path("engines/audio/tacotron2/engine.manifest.json")
        manifest = json.loads(manifest_path.read_text())

        required_fields = [
            "engine_id",
            "name",
            "type",
            "subtype",
            "version",
            "venv_family",
            "entry_point",
        ]

        for field in required_fields:
            assert field in manifest, f"Missing field: {field}"

    def test_manifest_engine_id_matches(self):
        """Manifest engine_id should match Python class."""
        import json
        from pathlib import Path

        from app.core.engines.tacotron2_engine import Tacotron2Engine

        manifest_path = Path("engines/audio/tacotron2/engine.manifest.json")
        manifest = json.loads(manifest_path.read_text())

        assert manifest["engine_id"] == Tacotron2Engine.ENGINE_ID

    def test_manifest_venv_family_is_core_tts(self):
        """Manifest should specify venv_core_tts family."""
        import json
        from pathlib import Path

        manifest_path = Path("engines/audio/tacotron2/engine.manifest.json")
        manifest = json.loads(manifest_path.read_text())

        assert manifest["venv_family"] == "venv_core_tts"


class TestVenvFamilyRegistration:
    """Tests for venv family registration."""

    def test_tacotron2_in_core_tts_family(self):
        """tacotron2 should be in venv_core_tts family."""
        from app.core.runtime.venv_family_manager import (
            ENGINE_TO_FAMILY,
            VenvFamily,
        )

        assert "tacotron2" in ENGINE_TO_FAMILY
        assert ENGINE_TO_FAMILY["tacotron2"] == VenvFamily.CORE_TTS
