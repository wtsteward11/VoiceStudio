"""
Tests for API version routes.
"""

import sys
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from backend.api.routes.version import router
    from backend.api.versioning import VERSION_HEADER, APIVersion
except ImportError:
    pytest.skip("Could not import version route module", allow_module_level=True)


class TestVersionRoutes:
    """Tests for version information endpoints."""

    @pytest.fixture
    def client(self):
        """Create test client with version routes."""
        from fastapi import FastAPI

        app = FastAPI()
        app.include_router(router)

        return TestClient(app)

    def test_get_versions(self, client):
        """Test listing all API versions."""
        response = client.get("/api/version/")

        assert response.status_code == 200
        data = response.json()
        assert data["current_version"] == "v3"
        assert data["default_version"] == "v1"
        assert "v1" in data["supported_versions"]
        assert "v2" in data["supported_versions"]
        assert "v3" in data["supported_versions"]
        assert "versions" in data
        assert "timestamp" in data

    def test_get_versions_includes_metadata(self, client):
        """Test version listing includes metadata."""
        response = client.get("/api/version/")

        assert response.status_code == 200
        data = response.json()

        for version_info in data["versions"]:
            assert "version" in version_info
            assert "status" in version_info
            assert "deprecated" in version_info

    def test_get_current_version(self, client):
        """Test getting current API version."""
        response = client.get("/api/version/current")

        assert response.status_code == 200
        data = response.json()
        assert data["version"] == APIVersion.current().value
        assert data["status"] == "current"
        assert "timestamp" in data

    def test_detect_version_default(self, client):
        """Test version detection with no version info."""
        response = client.get("/api/version/detect")

        assert response.status_code == 200
        data = response.json()
        assert data["detected_version"] == APIVersion.default().value
        assert data["detection_method"] == "default"

    def test_detect_version_from_header(self, client):
        """Test version detection from header."""
        response = client.get(
            "/api/version/detect",
            headers={VERSION_HEADER: "v2"},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["detected_version"] == "v2"
        assert data["detection_method"] == "header"

    def test_negotiate_version_match(self, client):
        """Test version negotiation with matching version."""
        response = client.post(
            "/api/version/negotiate",
            json={"preferred_versions": ["v2", "v1"]},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["negotiated_version"] == "v2"
        assert data["matched"] is True

    def test_negotiate_version_fallback(self, client):
        """Test version negotiation with no matching version."""
        response = client.post(
            "/api/version/negotiate",
            json={"preferred_versions": ["v99", "v100"]},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["negotiated_version"] == APIVersion.default().value
        assert data["matched"] is False

    def test_negotiate_version_order(self, client):
        """Test version negotiation respects preference order."""
        response = client.post(
            "/api/version/negotiate",
            json={"preferred_versions": ["v1", "v2"]},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["negotiated_version"] == "v1"  # First preference that's supported

    def test_get_versioned_endpoints(self, client):
        """Test getting versioned endpoints."""
        response = client.get("/api/version/endpoints")

        assert response.status_code == 200
        data = response.json()
        assert "endpoints" in data
        assert "count" in data
        assert "timestamp" in data

    def test_get_versioned_endpoints_filter(self, client):
        """Test filtering endpoints by version."""
        response = client.get("/api/version/endpoints?version=v1")

        assert response.status_code == 200
        data = response.json()
        assert data["filter"] == "v1"

    def test_get_versioned_endpoints_invalid_version(self, client):
        """Test filtering with invalid version returns error."""
        response = client.get("/api/version/endpoints?version=v999")

        assert response.status_code == 400

    def test_compatibility_check_no_header(self, client):
        """Test compatibility check without client version."""
        response = client.get("/api/version/compatibility")

        assert response.status_code == 200
        data = response.json()
        assert data["client_version"] is None
        assert data["compatible"] is True
        assert "server_version" in data
        assert "supported_versions" in data

    def test_compatibility_check_with_valid_version(self, client):
        """Test compatibility check with valid client version."""
        response = client.get(
            "/api/version/compatibility",
            headers={VERSION_HEADER: "v3"},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["client_version"] == "v3"
        assert data["compatible"] is True
        assert data["is_current"] is True

    def test_compatibility_check_with_old_version(self, client):
        """Test compatibility check with old but supported version."""
        response = client.get(
            "/api/version/compatibility",
            headers={VERSION_HEADER: "v1"},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["client_version"] == "v1"
        assert data["compatible"] is True
        assert data["is_current"] is False
        assert "recommendation" in data

    def test_compatibility_check_with_invalid_version(self, client):
        """Test compatibility check with invalid client version."""
        response = client.get(
            "/api/version/compatibility",
            headers={VERSION_HEADER: "invalid"},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["compatible"] is False
        assert "recommendation" in data

    def test_version_header_in_response(self, client):
        """Test that version header is included in responses."""
        response = client.get("/api/version/")

        assert VERSION_HEADER in response.headers
        assert response.headers[VERSION_HEADER] == APIVersion.current().value

    def test_timestamp_format(self, client):
        """Test timestamp is in ISO 8601 format."""
        response = client.get("/api/version/")

        assert response.status_code == 200
        data = response.json()
        timestamp = data["timestamp"]
        assert timestamp.endswith("Z")

        from datetime import datetime

        datetime.fromisoformat(timestamp.rstrip("Z"))
