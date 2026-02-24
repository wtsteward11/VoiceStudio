"""
Tests for API Version Client Integration

These tests verify that the C# BackendClient correctly interacts with
the version API endpoints. This file tests the Python backend endpoints
that the C# client will call.
"""

import sys
from pathlib import Path

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from backend.api.routes.version import router
    from backend.api.versioning import VERSION_HEADER, APIVersion
except ImportError:
    pytest.skip("Could not import version route module", allow_module_level=True)


@pytest.fixture
def app():
    """Create a FastAPI app with version routes."""
    app = FastAPI()
    app.include_router(router)
    return app


@pytest.fixture
def client(app):
    """Create a test client."""
    return TestClient(app)


class TestCompatibilityEndpoint:
    """Tests for /api/version/compatibility endpoint used by C# client."""

    def test_compatibility_without_header_returns_compatible(self, client):
        """Test compatibility check without X-API-Version header."""
        response = client.get("/api/version/compatibility")
        assert response.status_code == 200

        data = response.json()
        assert data["compatible"] is True
        assert "server_version" in data
        assert "supported_versions" in data
        assert isinstance(data["supported_versions"], list)

    def test_compatibility_with_v2_header_is_compatible(self, client):
        """Test compatibility check with v2 header."""
        response = client.get("/api/version/compatibility", headers={VERSION_HEADER: "v2"})
        assert response.status_code == 200

        data = response.json()
        assert data["compatible"] is True
        # Server returns its own version (v3), not the client's requested version
        assert data["server_version"] == "v3"
        assert "v2" in data["supported_versions"]

    def test_compatibility_with_v1_header_is_compatible(self, client):
        """Test compatibility check with v1 header."""
        response = client.get("/api/version/compatibility", headers={VERSION_HEADER: "v1"})
        assert response.status_code == 200

        data = response.json()
        assert data["compatible"] is True

    def test_compatibility_with_unsupported_version_returns_incompatible(self, client):
        """Test compatibility check with unsupported version."""
        response = client.get("/api/version/compatibility", headers={VERSION_HEADER: "v99"})
        assert response.status_code == 200

        data = response.json()
        assert data["compatible"] is False
        assert "recommendation" in data

    def test_compatibility_response_includes_supported_versions(self, client):
        """Test that compatibility response includes all supported versions."""
        response = client.get("/api/version/compatibility")
        assert response.status_code == 200

        data = response.json()
        supported = data["supported_versions"]

        # Verify both v1 and v2 are in supported versions
        assert "v1" in supported
        assert "v2" in supported

    def test_compatibility_includes_version_header_in_response(self, client):
        """Test that response includes X-API-Version header."""
        response = client.get("/api/version/compatibility")
        assert response.status_code == 200
        assert VERSION_HEADER in response.headers


class TestVersionListEndpoint:
    """Tests for /api/version/ endpoint."""

    def test_get_versions_returns_all_versions(self, client):
        """Test listing all supported versions."""
        response = client.get("/api/version/")
        assert response.status_code == 200

        data = response.json()
        assert "current_version" in data
        assert "default_version" in data
        assert "supported_versions" in data
        assert "versions" in data

        # Verify version details are included
        assert len(data["versions"]) >= 2

    def test_versions_include_metadata(self, client):
        """Test that versions include status metadata."""
        response = client.get("/api/version/")
        assert response.status_code == 200

        data = response.json()
        for v in data["versions"]:
            assert "version" in v
            assert "status" in v
            assert "deprecated" in v


class TestCurrentVersionEndpoint:
    """Tests for /api/version/current endpoint."""

    def test_get_current_version(self, client):
        """Test getting current API version."""
        response = client.get("/api/version/current")
        assert response.status_code == 200

        data = response.json()
        assert data["version"] == APIVersion.current().value
        assert "timestamp" in data
        assert "status" in data


class TestNegotiateEndpoint:
    """Tests for /api/version/negotiate endpoint."""

    def test_negotiate_with_v2_preferred(self, client):
        """Test version negotiation preferring v2."""
        response = client.post("/api/version/negotiate", json={"preferred_versions": ["v2", "v1"]})
        assert response.status_code == 200

        data = response.json()
        assert data["negotiated_version"] == "v2"
        assert data["matched"] is True

    def test_negotiate_with_only_v1(self, client):
        """Test version negotiation with only v1."""
        response = client.post("/api/version/negotiate", json={"preferred_versions": ["v1"]})
        assert response.status_code == 200

        data = response.json()
        assert data["negotiated_version"] == "v1"
        assert data["matched"] is True

    def test_negotiate_with_unsupported_version_falls_back(self, client):
        """Test version negotiation with unsupported version falls back."""
        response = client.post("/api/version/negotiate", json={"preferred_versions": ["v99", "v2"]})
        assert response.status_code == 200

        data = response.json()
        # Should negotiate to v2 since v99 is unsupported
        assert data["negotiated_version"] == "v2"
        assert data["matched"] is True  # v2 was in preferred list

    def test_negotiate_with_no_match_falls_back_to_default(self, client):
        """Test version negotiation with no matching versions falls back to default."""
        response = client.post(
            "/api/version/negotiate", json={"preferred_versions": ["v99", "v100"]}
        )
        assert response.status_code == 200

        data = response.json()
        # Falls back to default version, marked as not matched
        assert data["matched"] is False
        assert data["negotiated_version"] in ["v1", "v2"]  # Default


class TestVersionEndpointsEndpoint:
    """Tests for /api/version/endpoints endpoint."""

    def test_get_all_versioned_endpoints(self, client):
        """Test getting all versioned endpoints."""
        response = client.get("/api/version/endpoints")
        assert response.status_code == 200

        data = response.json()
        assert "endpoints" in data
        assert "timestamp" in data

    def test_filter_endpoints_by_version(self, client):
        """Test filtering endpoints by version."""
        response = client.get("/api/version/endpoints?version=v2")
        assert response.status_code == 200

        data = response.json()
        assert "endpoints" in data


class TestClientStartupValidation:
    """
    Tests that verify the expected behavior for C# BackendClient startup validation.
    These tests ensure the API provides the information needed for the client to
    perform version checks during initialization.
    """

    def test_compatibility_endpoint_returns_all_required_fields(self, client):
        """Test that compatibility endpoint returns all fields needed by C# client."""
        response = client.get("/api/version/compatibility")
        assert response.status_code == 200

        data = response.json()

        # These fields are expected by the C# BackendClient
        assert "compatible" in data
        assert "server_version" in data
        assert "supported_versions" in data

        # Optional but useful fields
        if not data["compatible"]:
            assert "recommendation" in data

    def test_version_info_endpoint_returns_all_required_fields(self, client):
        """Test that version info endpoint returns all fields needed by C# client."""
        response = client.get("/api/version/")
        assert response.status_code == 200

        data = response.json()

        # These fields are expected by the C# BackendClient.GetApiVersionInfoAsync
        assert "current_version" in data
        assert "default_version" in data
        assert "supported_versions" in data

    def test_endpoints_are_accessible_without_authentication(self, client):
        """Test that version endpoints don't require auth."""
        # These should all work without any auth headers
        endpoints = [
            "/api/version/",
            "/api/version/current",
            "/api/version/compatibility",
        ]

        for endpoint in endpoints:
            response = client.get(endpoint)
            assert response.status_code == 200, f"Endpoint {endpoint} should be accessible"
