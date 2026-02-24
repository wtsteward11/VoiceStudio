"""
Unit tests for EngineService.

Tests engine initialization, selection, synthesis, and lifecycle behavior
with mocked engine dependencies.
"""

from unittest.mock import MagicMock, patch

import pytest

from backend.services.engine_service import (
    EngineService,
    IEngineService,
    get_engine_by_id,
    get_engine_service,
)

# -------------------------------------------------------------------------
# Fixtures
# -------------------------------------------------------------------------


@pytest.fixture
def mock_engine_router() -> MagicMock:
    """Create a mock engine router for testing."""
    router = MagicMock()
    router.list_engines.return_value = [
        {"id": "xtts_v2", "name": "XTTS v2", "capabilities": ["synthesis", "voice_cloning"]},
        {"id": "piper", "name": "Piper", "capabilities": ["synthesis"]},
    ]
    return router


@pytest.fixture
def mock_engine() -> MagicMock:
    """Create a mock engine instance for synthesis and transcription."""
    engine = MagicMock()
    engine.synthesize.return_value = {"audio_path": "/tmp/test_output.wav"}
    engine.clone_voice.return_value = {"audio_path": "/tmp/cloned.wav"}
    engine.transcribe.return_value = {"text": "Hello world"}
    engine.ready = True
    engine.list_voices.return_value = [{"id": "voice1", "name": "Test Voice"}]
    return engine


@pytest.fixture
def mock_quality_metrics() -> MagicMock:
    """Create a mock quality metrics module."""
    metrics = MagicMock()
    metrics.calculate_all_metrics.return_value = {"mos": 4.2, "snr": 25.0}
    metrics.calculate_similarity.return_value = 0.85
    metrics.calculate_mos_score.return_value = 4.0
    metrics.calculate_snr.return_value = 22.5
    metrics.calculate_naturalness.return_value = 0.9
    metrics.detect_artifacts.return_value = {"clipping": False, "distortion": False}
    return metrics


@pytest.fixture
def mock_quality_presets() -> MagicMock:
    """Create a mock quality presets module."""
    presets = MagicMock()
    presets.list_quality_presets.return_value = [
        {"name": "high", "description": "High quality preset"},
    ]
    presets.get_synthesis_params_from_preset.return_value = {"speed": 1.0, "temperature": 0.7}
    return presets


@pytest.fixture
def engine_service(
    mock_engine_router: MagicMock,
    mock_engine: MagicMock,
    mock_quality_metrics: MagicMock,
    mock_quality_presets: MagicMock,
) -> EngineService:
    """Create EngineService with mocked engine dependencies."""
    mock_engine_router.get_engine.return_value = mock_engine
    mock_engine_router.list_voices.return_value = [{"id": "voice1", "name": "Test Voice"}]
    mock_engine_router.synthesize.return_value = {"audio_path": "/tmp/routed.wav"}

    service = EngineService()
    service._engine_router = mock_engine_router
    service._quality_metrics = mock_quality_metrics
    service._quality_optimizer = MagicMock()
    service._quality_presets = mock_quality_presets
    service._engines_loaded = True

    return service


# -------------------------------------------------------------------------
# Engine Initialization Tests
# -------------------------------------------------------------------------


class TestEngineServiceInitialization:
    """Tests for engine service initialization and lazy loading."""

    def test_engine_service_implements_interface(self) -> None:
        """EngineService implements IEngineService interface."""
        assert issubclass(EngineService, IEngineService)

    def test_initialization_sets_defaults(self) -> None:
        """EngineService initializes with correct default state."""
        service = EngineService()
        assert service._engine_router is None
        assert service._quality_metrics is None
        assert service._engines_loaded is False
        assert service._circuit_breakers == {}

    def test_list_engines_returns_empty_when_router_unavailable(self) -> None:
        """list_engines returns empty list when router cannot be loaded."""
        service = EngineService()
        service._engines_loaded = True
        service._engine_router = None
        result = service.list_engines()
        assert result == []


# -------------------------------------------------------------------------
# Engine Selection Tests
# -------------------------------------------------------------------------


class TestEngineSelection:
    """Tests for engine discovery and selection."""

    def test_list_engines_delegates_to_router(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
    ) -> None:
        """list_engines delegates to router and returns engine list."""
        result = engine_service.list_engines()
        mock_engine_router.list_engines.assert_called_once()
        assert len(result) == 2
        assert result[0]["id"] == "xtts_v2"
        assert result[1]["id"] == "piper"

    def test_get_engine_returns_engine_instance(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
        mock_engine: MagicMock,
    ) -> None:
        """get_engine returns engine when available."""
        result = engine_service.get_engine("xtts_v2")
        mock_engine_router.get_engine.assert_called_once_with("xtts_v2")
        assert result is mock_engine

    def test_get_engine_returns_none_when_unavailable(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
    ) -> None:
        """get_engine returns None when engine not found."""
        mock_engine_router.get_engine.return_value = None
        result = engine_service.get_engine("nonexistent")
        assert result is None

    def test_is_engine_available_true_when_engine_exists(
        self,
        engine_service: EngineService,
    ) -> None:
        """is_engine_available returns True when engine is present."""
        assert engine_service.is_engine_available("xtts_v2") is True

    def test_is_engine_available_false_when_engine_missing(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
    ) -> None:
        """is_engine_available returns False when engine not found."""
        mock_engine_router.get_engine.return_value = None
        assert engine_service.is_engine_available("nonexistent") is False

    def test_get_engine_status_returns_available(
        self,
        engine_service: EngineService,
        mock_engine: MagicMock,
    ) -> None:
        """get_engine_status returns correct status for available engine."""
        result = engine_service.get_engine_status("xtts_v2")
        assert result["status"] == "available"
        assert result["engine_id"] == "xtts_v2"
        assert result["ready"] is True


# -------------------------------------------------------------------------
# Synthesis Tests
# -------------------------------------------------------------------------


class TestSynthesis:
    """Tests for synthesis operations."""

    def test_synthesize_success_returns_audio_path(
        self,
        engine_service: EngineService,
        mock_engine: MagicMock,
    ) -> None:
        """synthesize returns audio path on success."""
        result = engine_service.synthesize(
            engine_id="xtts_v2",
            text="Hello world",
            voice_id="voice1",
        )
        mock_engine.synthesize.assert_called_once_with("Hello world", voice_id="voice1")
        assert "audio_path" in result
        assert result["audio_path"] == "/tmp/test_output.wav"
        assert "error" not in result

    def test_synthesize_passes_kwargs_to_engine(
        self,
        engine_service: EngineService,
        mock_engine: MagicMock,
    ) -> None:
        """synthesize forwards extra kwargs to engine."""
        engine_service.synthesize(
            engine_id="xtts_v2",
            text="Test",
            speed=1.2,
            language="en",
        )
        mock_engine.synthesize.assert_called_once()
        call_kwargs = mock_engine.synthesize.call_args[1]
        assert call_kwargs.get("speed") == 1.2
        assert call_kwargs.get("language") == "en"

    def test_synthesize_returns_error_when_router_unavailable(self) -> None:
        """synthesize returns error when engine router not loaded."""
        service = EngineService()
        service._engines_loaded = True
        service._engine_router = None
        result = service.synthesize(engine_id="xtts_v2", text="Hello")
        assert "error" in result
        assert "degraded" in result
        assert "Engine router not available" in result["error"]

    def test_synthesize_returns_error_when_all_engines_fail(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
    ) -> None:
        """synthesize returns error dict when all engines in chain fail."""
        mock_engine_router.get_engine.return_value = None
        result = engine_service.synthesize(
            engine_id="unknown_engine",
            text="Hello",
        )
        assert "error" in result
        assert "degraded" in result
        assert "engines_tried" in result


# -------------------------------------------------------------------------
# Transcription Tests
# -------------------------------------------------------------------------


class TestTranscription:
    """Tests for transcription operations."""

    def test_transcribe_success_returns_text(
        self,
        engine_service: EngineService,
        mock_engine: MagicMock,
    ) -> None:
        """transcribe returns text on success."""
        result = engine_service.transcribe(
            engine_id="whisper",
            audio_path="/tmp/audio.wav",
            language="en",
        )
        mock_engine.transcribe.assert_called_once()
        call_kwargs = mock_engine.transcribe.call_args[1]
        assert call_kwargs["audio_path"] == "/tmp/audio.wav"
        assert call_kwargs["language"] == "en"
        assert result["text"] == "Hello world"

    def test_transcribe_returns_error_when_engine_unavailable(
        self,
        engine_service: EngineService,
        mock_engine_router: MagicMock,
    ) -> None:
        """transcribe returns error when engine not found."""
        mock_engine_router.get_engine.return_value = None
        result = engine_service.transcribe(
            engine_id="nonexistent",
            audio_path="/tmp/audio.wav",
        )
        assert "error" in result
        assert "not found" in result["error"]


# -------------------------------------------------------------------------
# Singleton and Factory Tests
# -------------------------------------------------------------------------


class TestEngineServiceFactory:
    """Tests for get_engine_service and get_engine_by_id."""

    def test_get_engine_service_returns_singleton(self) -> None:
        """get_engine_service returns same instance on repeated calls."""
        import backend.services.engine_service as engine_module

        # Reset singleton for test isolation
        orig = engine_module._engine_service_instance
        engine_module._engine_service_instance = None

        try:
            svc1 = get_engine_service()
            svc2 = get_engine_service()
            assert svc1 is svc2
        finally:
            engine_module._engine_service_instance = orig

    def test_get_engine_by_id_delegates_to_service(self) -> None:
        """get_engine_by_id delegates to engine service."""
        with patch("backend.services.engine_service.get_engine_service") as mock_get_service:
            mock_svc = MagicMock()
            mock_engine = MagicMock()
            mock_svc.get_engine.return_value = mock_engine
            mock_get_service.return_value = mock_svc

            result = get_engine_by_id("xtts_v2")
            mock_svc.get_engine.assert_called_once_with("xtts_v2")
            assert result is mock_engine


# -------------------------------------------------------------------------
# Circuit Breaker / Health Tests
# -------------------------------------------------------------------------


class TestEngineServiceHealth:
    """Tests for engine health and circuit breaker integration."""

    def test_get_engine_health_returns_circuit_state(
        self,
        engine_service: EngineService,
    ) -> None:
        """get_engine_health returns circuit breaker state for engine."""
        result = engine_service.get_engine_health("xtts_v2")
        assert "engine_id" in result
        assert result["engine_id"] == "xtts_v2"
        assert "circuit_state" in result
        assert "is_healthy" in result

    def test_get_all_engine_health_returns_dict(
        self,
        engine_service: EngineService,
    ) -> None:
        """get_all_engine_health returns dict of engine health."""
        # Trigger circuit breaker creation via synthesize
        engine_service.synthesize(engine_id="xtts_v2", text="test")
        result = engine_service.get_all_engine_health()
        assert isinstance(result, dict)
