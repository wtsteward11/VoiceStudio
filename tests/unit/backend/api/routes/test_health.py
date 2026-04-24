"""
Unit Tests for Health API Routes.

Tests all 14 health check endpoints with comprehensive coverage.
"""

from datetime import datetime
from unittest.mock import MagicMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture
def mock_health_checker():
    """Create mock health checker."""
    mock_checker = MagicMock()
    mock_checker.check.return_value = MagicMock(
        status="healthy",
        checks={"database": True, "gpu": True, "engines": True},
    )
    return mock_checker


@pytest.fixture
def mock_engine_service():
    """Create mock engine service."""
    mock_service = MagicMock()
    mock_service.list_engines.return_value = [
        {"id": "xtts", "name": "XTTS", "status": "available"},
        {"id": "piper", "name": "Piper", "status": "available"},
    ]
    return mock_service


@pytest.fixture
def mock_breaker_stats():
    """Create mock circuit breaker stats."""
    return {
        "xtts": {
            "state": "closed",
            "failure_count": 0,
            "success_count": 10,
            "last_failure": None,
        },
        "piper": {
            "state": "closed",
            "failure_count": 0,
            "success_count": 5,
            "last_failure": None,
        },
    }


@pytest.fixture
def health_client(mock_health_checker, mock_engine_service, mock_breaker_stats):
    """Create test client with mocked dependencies."""
    with (
        patch(
            "backend.api.routes.health.get_health_checker",
            return_value=mock_health_checker,
        ),
        patch(
            "backend.api.routes.health.get_engine_service",
            return_value=mock_engine_service,
        ),
        patch(
            "backend.api.routes.health.get_engine_breaker_stats",
            return_value=mock_breaker_stats,
        ),
        patch(
            "backend.api.routes.health._check_database",
            return_value=True,
        ),
        patch(
            "backend.api.routes.health._check_gpu",
            return_value={
                "status": "healthy",
                "available": True,
                "device_count": 1,
                "device_name": "NVIDIA GeForce RTX 3080",
            },
        ),
        patch(
            "backend.api.routes.health._check_engines",
            return_value={
                "status": "healthy",
                "available_count": 2,
                "engines": ["xtts", "piper"],
            },
        ),
    ):
        from backend.api.routes.health import router

        app = FastAPI()
        app.include_router(router)
        yield TestClient(app)


# =============================================================================
# Basic Health Check Tests
# =============================================================================


class TestHealthCheck:
    """Tests for the main health check endpoint."""

    def test_health_check(self, health_client):
        """Test GET /api/health/ returns healthy status."""
        response = health_client.get("/api/health/")
        assert response.status_code == 200
        data = response.json()
        assert "status" in data
        assert "timestamp" in data

    def test_simple_health_check(self, health_client):
        """Test GET /api/health/simple returns minimal status."""
        response = health_client.get("/api/health/simple")
        assert response.status_code == 200
        data = response.json()
        assert "status" in data
        assert data["status"] == "ok"


# =============================================================================
# Kubernetes Probe Tests
# =============================================================================


class TestKubernetesProbes:
    """Tests for Kubernetes readiness and liveness probes."""

    def test_readiness_check(self, health_client):
        """Test GET /api/health/readiness endpoint."""
        response = health_client.get("/api/health/readiness")
        assert response.status_code == 200
        data = response.json()
        assert "ready" in data or "status" in data

    def test_ready_check_alias(self, health_client):
        """Test GET /api/health/ready (alias) endpoint."""
        response = health_client.get("/api/health/ready")
        assert response.status_code == 200

    def test_liveness_check(self, health_client):
        """Test GET /api/health/liveness endpoint."""
        response = health_client.get("/api/health/liveness")
        assert response.status_code == 200
        data = response.json()
        assert "alive" in data or "status" in data

    def test_live_check_alias(self, health_client):
        """Test GET /api/health/live (alias) endpoint."""
        response = health_client.get("/api/health/live")
        assert response.status_code == 200


# =============================================================================
# Detailed Health Tests
# =============================================================================


class TestDetailedHealth:
    """Tests for detailed health information endpoints."""

    def test_detailed_health_check(self, health_client):
        """Test GET /api/health/detailed returns comprehensive info."""
        response = health_client.get("/api/health/detailed")
        assert response.status_code == 200
        data = response.json()
        assert "status" in data

    def test_preflight_check(self, health_client):
        """Test GET /api/health/preflight endpoint."""
        response = health_client.get("/api/health/preflight")
        assert response.status_code == 200
        data = response.json()
        assert "status" in data or "checks" in data
        checks = data.get("checks") or {}
        wcpp = checks.get("whisper_cpp")
        assert wcpp is not None, "checks.whisper_cpp must be present"
        assert wcpp.get("ok") is not None, "Slice 22: checks.whisper_cpp.ok must never be null"
        assert isinstance(wcpp.get("ok"), bool)


# =============================================================================
# Resource Health Tests
# =============================================================================


class TestResourceHealth:
    """Tests for resource-specific health endpoints."""

    def test_resources_endpoint(self, health_client):
        """Test GET /api/health/resources returns system resource info."""
        response = health_client.get("/api/health/resources")
        assert response.status_code == 200
        data = response.json()
        # Should return resource information
        assert isinstance(data, dict)

    def test_engines_endpoint(self, health_client):
        """Test GET /api/health/engines returns engine status."""
        response = health_client.get("/api/health/engines")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, dict)

    def test_circuit_breakers_endpoint(self, health_client):
        """Test GET /api/health/circuit-breakers returns breaker states."""
        response = health_client.get("/api/health/circuit-breakers")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, dict)


# =============================================================================
# Performance Health Tests
# =============================================================================


class TestPerformanceHealth:
    """Tests for performance-related health endpoints."""

    def test_performance_metrics(self, health_client):
        """Test GET /api/health/performance returns metrics."""
        response = health_client.get("/api/health/performance")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, dict)

    def test_endpoint_performance_metrics(self, health_client):
        """Test GET /api/health/performance/{endpoint} returns endpoint-specific metrics."""
        response = health_client.get("/api/health/performance/voice/synthesize")
        # May return 200 with data, 404 if endpoint not tracked, or 503 if service unavailable
        assert response.status_code in [200, 404, 503]


# =============================================================================
# Feature Health Tests
# =============================================================================


class TestFeatureHealth:
    """Tests for feature availability endpoint."""

    def test_features_endpoint(self, health_client):
        """Test GET /api/health/features returns available features."""
        response = health_client.get("/api/health/features")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, dict)
