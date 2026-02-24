"""
Tests for API v2 health routes.
"""

import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from backend.api.versioning import VERSION_HEADER, APIVersion
    from backend.api.routes.v2 import health_router
except ImportError:
    pytest.skip("Could not import v2 health route module", allow_module_level=True)


class TestV2HealthRoutes:
    """Tests for v2 health endpoints."""

    @pytest.fixture
    def client(self):
        """Create test client with v2 routes."""
        from fastapi import FastAPI

        app = FastAPI()
        app.include_router(health_router)

        return TestClient(app)

    def test_health_v2_returns_ok(self, client):
        """Test v2 health endpoint returns healthy status."""
        response = client.get("/api/v2/health/")

        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "healthy"
        assert "timestamp" in data
        assert "version" in data

    def test_health_v2_includes_version_header(self, client):
        """Test v2 health endpoint includes version header."""
        response = client.get("/api/v2/health/")

        assert response.status_code == 200
        assert response.headers.get(VERSION_HEADER) == APIVersion.V2.value

    def test_health_v2_version_info(self, client):
        """Test v2 health endpoint includes version information."""
        response = client.get("/api/v2/health/")

        assert response.status_code == 200
        data = response.json()
        assert data["version"]["api"] == "v2"
        assert data["version"]["current"] == APIVersion.current().value

    def test_health_detailed_v2_returns_ok(self, client):
        """Test v2 detailed health endpoint returns healthy status."""
        response = client.get("/api/v2/health/detailed")

        assert response.status_code == 200
        data = response.json()
        assert data["status"] == "healthy"
        assert "timestamp" in data
        assert "version" in data
        assert "components" in data

    def test_health_detailed_v2_version_header(self, client):
        """Test v2 detailed health endpoint includes version header."""
        response = client.get("/api/v2/health/detailed")

        assert response.status_code == 200
        assert response.headers.get(VERSION_HEADER) == APIVersion.V2.value

    def test_health_detailed_v2_supported_versions(self, client):
        """Test v2 detailed health shows supported versions."""
        response = client.get("/api/v2/health/detailed")

        assert response.status_code == 200
        data = response.json()
        supported = data["version"]["supported"]
        assert "v1" in supported
        assert "v2" in supported

    def test_timestamp_is_iso_format(self, client):
        """Test timestamp is in ISO 8601 format with Z suffix."""
        response = client.get("/api/v2/health/")

        assert response.status_code == 200
        data = response.json()
        timestamp = data["timestamp"]
        # Should end with Z for UTC
        assert timestamp.endswith("Z")
        # Should be parseable as ISO format
        from datetime import datetime

        # Remove Z suffix for parsing
        datetime.fromisoformat(timestamp.rstrip("Z"))
