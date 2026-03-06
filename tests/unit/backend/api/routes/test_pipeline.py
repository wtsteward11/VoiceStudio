"""
Unit Tests for Pipeline API Routes.

Tests pipeline processing, providers, and metrics endpoints.
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(autouse=True)
def reset_pipeline_state():
    """Reset pipeline state before each test."""
    from backend.api.routes import pipeline

    pipeline._sessions = {}
    yield
    pipeline._sessions = {}


@pytest.fixture
def pipeline_client():
    """Create test client for pipeline routes."""
    from backend.api.routes.pipeline import router

    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


# =============================================================================
# Metrics Tests
# =============================================================================


class TestPipelineMetrics:
    """Tests for pipeline metrics endpoint."""

    def test_get_pipeline_metrics(self, pipeline_client):
        """Test GET /api/pipeline/metrics returns metrics."""
        response = pipeline_client.get("/api/pipeline/metrics")
        assert response.status_code == 200
        data = response.json()
        assert "active_sessions" in data
        assert "total_sessions" in data
        assert "avg_latency_ms" in data
        assert data["active_sessions"] == 0

    def test_metrics_endpoint_active(self, pipeline_client):
        """Test metrics endpoint reports active status."""
        response = pipeline_client.get("/api/pipeline/metrics")
        assert response.status_code == 200
        data = response.json()
        assert "message" in data
        assert "active" in data["message"].lower()


# =============================================================================
# Providers Tests
# =============================================================================


class TestPipelineProviders:
    """Tests for pipeline providers endpoint."""

    def test_get_providers(self, pipeline_client):
        """Test GET /api/pipeline/providers returns available providers."""
        response = pipeline_client.get("/api/pipeline/providers")
        assert response.status_code == 200
        data = response.json()

        # Should have stt, llm, tts sections
        assert "stt" in data
        assert "llm" in data
        assert "tts" in data

    def test_providers_has_stt_defaults(self, pipeline_client):
        """Test STT provider section has expected structure."""
        response = pipeline_client.get("/api/pipeline/providers")
        assert response.status_code == 200
        data = response.json()

        assert "available" in data["stt"]
        assert "default" in data["stt"]
        assert isinstance(data["stt"]["available"], list)

    def test_providers_has_tts_defaults(self, pipeline_client):
        """Test TTS provider section has expected structure."""
        response = pipeline_client.get("/api/pipeline/providers")
        assert response.status_code == 200
        data = response.json()

        assert "available" in data["tts"]
        assert "default" in data["tts"]
        assert isinstance(data["tts"]["available"], list)


# =============================================================================
# Pipeline Process Tests
# =============================================================================


class TestPipelineProcess:
    """Tests for pipeline processing endpoint."""

    def test_process_pipeline_validation(self, pipeline_client):
        """Test POST /api/pipeline/process validates input."""
        # Missing required 'text' field
        response = pipeline_client.post(
            "/api/pipeline/process",
            json={},
        )
        assert response.status_code == 422

    @patch("backend.pipeline.facade.PipelineOrchestrator")
    def test_process_pipeline_init_failure(self, mock_orchestrator_class, pipeline_client):
        """Test pipeline returns 503 when initialization fails."""
        # Mock orchestrator that fails to initialize
        mock_orchestrator = MagicMock()
        mock_orchestrator.initialize = AsyncMock(return_value=False)
        mock_orchestrator_class.return_value = mock_orchestrator

        response = pipeline_client.post(
            "/api/pipeline/process",
            json={"text": "Hello"},
        )
        assert response.status_code == 503
        assert "initialization failed" in response.json()["detail"].lower()

    @patch("backend.pipeline.facade.PipelineOrchestrator")
    def test_process_pipeline_success(self, mock_orchestrator_class, pipeline_client):
        """Test successful pipeline processing."""
        # Mock successful orchestrator
        mock_orchestrator = MagicMock()
        mock_orchestrator.initialize = AsyncMock(return_value=True)
        mock_orchestrator.process_text = AsyncMock(
            return_value={
                "response": "Hello back!",
                "audio": None,
                "metrics": {"latency_ms": 100},
            }
        )
        mock_orchestrator.cleanup = AsyncMock()
        mock_orchestrator_class.return_value = mock_orchestrator

        response = pipeline_client.post(
            "/api/pipeline/process",
            json={"text": "Hello"},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["response_text"] == "Hello back!"
        assert "session_id" in data
        assert data["session_id"].startswith("sess-")

    @patch("backend.pipeline.facade.PipelineOrchestrator")
    def test_process_pipeline_with_options(self, mock_orchestrator_class, pipeline_client):
        """Test pipeline processing with custom options."""
        mock_orchestrator = MagicMock()
        mock_orchestrator.initialize = AsyncMock(return_value=True)
        mock_orchestrator.process_text = AsyncMock(
            return_value={
                "response": "Custom response",
                "audio": None,
            }
        )
        mock_orchestrator.cleanup = AsyncMock()
        mock_orchestrator_class.return_value = mock_orchestrator

        response = pipeline_client.post(
            "/api/pipeline/process",
            json={
                "text": "Test",
                "mode": "batch",
                "stt_engine": "whisper",
                "llm_provider": "ollama",
                "tts_engine": "piper",
                "language": "en",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert data["response_text"] == "Custom response"

    @patch("backend.pipeline.facade.PipelineOrchestrator")
    def test_process_pipeline_error_handling(self, mock_orchestrator_class, pipeline_client):
        """Test pipeline handles processing errors gracefully."""
        mock_orchestrator = MagicMock()
        mock_orchestrator.initialize = AsyncMock(return_value=True)
        mock_orchestrator.process_text = AsyncMock(side_effect=Exception("Processing failed"))
        mock_orchestrator.cleanup = AsyncMock()
        mock_orchestrator_class.return_value = mock_orchestrator

        response = pipeline_client.post(
            "/api/pipeline/process",
            json={"text": "Hello"},
        )
        assert response.status_code == 500
        assert "processing failed" in response.json()["detail"].lower()
