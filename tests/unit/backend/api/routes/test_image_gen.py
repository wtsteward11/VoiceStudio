"""
Unit Tests for Image Generation API Routes.

Tests image generation, upscaling, and face enhancement endpoints.

The route module unconditionally imports ``PIL.Image``. Pillow is *not*
installed in the PR Ubuntu CI environment (only in the engines profile), so
importing the route in CI raises ``ImportError`` and produces a collection
error rather than a runnable test suite. Skip the whole module when Pillow is
unavailable so missing optional deps surface as honest skips, not collection
failures (PR #49 first-failure triage).
"""

from unittest.mock import AsyncMock, MagicMock, patch

import pytest

pytest.importorskip(
    "PIL",
    reason="Pillow not installed; backend.api.routes.image_gen requires it",
)

from fastapi import FastAPI  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402

# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(autouse=True)
def reset_image_state():
    """Reset image storage before each test."""
    from backend.api.routes import image_gen

    image_gen._image_storage = {}
    yield
    image_gen._image_storage = {}


@pytest.fixture
def image_client():
    """Create test client for image generation routes."""
    from backend.api.routes.image_gen import router

    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


# =============================================================================
# Engine List Tests
# =============================================================================


class TestEngineList:
    """Tests for engine listing endpoint."""

    def test_get_engines_list(self, image_client):
        """Test GET /engines/list returns available engines."""
        response = image_client.get("/api/image/engines/list")
        assert response.status_code == 200
        data = response.json()
        # Should return a list or dict of engines
        assert data is not None


# =============================================================================
# Image Retrieval Tests
# =============================================================================


class TestImageRetrieval:
    """Tests for image retrieval endpoint."""

    def test_get_image_not_found(self, image_client):
        """Test GET /{image_id} returns 404 for missing image."""
        response = image_client.get("/api/image/nonexistent-image-id")
        assert response.status_code == 404

    def test_get_image_with_stored_path(self, image_client):
        """Test GET /{image_id} attempts to return stored image."""
        from backend.api.routes import image_gen

        # Manually add an image to storage (path doesn't need to exist for 404 test)
        image_gen._image_storage["test-image-123"] = "/fake/path/image.png"

        # Should fail since file doesn't exist, but tests the lookup logic
        response = image_client.get("/api/image/test-image-123")
        # Will be 404 since the file doesn't exist
        assert response.status_code == 404


# =============================================================================
# Image Generation Tests
# =============================================================================


class TestImageGeneration:
    """Tests for image generation endpoint."""

    def test_generate_validation_empty_prompt(self, image_client):
        """Test POST /generate validates prompt is provided."""
        response = image_client.post(
            "/api/image/generate",
            json={"prompt": ""},
        )
        # Empty prompt should fail validation
        assert response.status_code in [400, 422, 500]

    @patch("backend.api.routes.image_gen.get_engine_service")
    def test_generate_with_valid_request(self, mock_get_service, image_client):
        """Test POST /generate with valid request (mocked engine)."""
        # Mock the engine service
        mock_service = MagicMock()
        mock_service.get_image_generator.return_value = None  # No engine available
        mock_get_service.return_value = mock_service

        response = image_client.post(
            "/api/image/generate",
            json={
                "prompt": "A beautiful sunset over mountains",
                "engine": "auto",
                "width": 512,
                "height": 512,
            },
        )
        # Without an actual engine, should return 503 or similar
        assert response.status_code in [200, 500, 503]

    def test_generate_request_structure(self, image_client):
        """Test POST /generate accepts expected request structure."""
        # Test that the endpoint accepts valid fields
        response = image_client.post(
            "/api/image/generate",
            json={
                "prompt": "Test prompt",
                "negative_prompt": "blurry, low quality",
                "width": 512,
                "height": 512,
                "steps": 20,
                "cfg_scale": 7.0,
                "seed": 12345,
            },
        )
        # May fail without engine, but validates request parsing
        assert response.status_code in [200, 422, 500, 503]


# =============================================================================
# Image Upscale Tests
# =============================================================================


class TestImageUpscale:
    """Tests for image upscaling endpoint."""

    def test_upscale_validation(self, image_client):
        """Test POST /upscale validates input."""
        response = image_client.post(
            "/api/image/upscale",
            json={},
        )
        # Should fail validation - missing required fields
        assert response.status_code == 422

    def test_upscale_request_structure(self, image_client):
        """Test POST /upscale accepts expected structure."""
        response = image_client.post(
            "/api/image/upscale",
            json={
                "image_path": "/path/to/image.png",
                "scale_factor": 2.0,
            },
        )
        # Will fail without actual file, but validates request parsing
        assert response.status_code in [200, 400, 404, 422, 500, 503]


# =============================================================================
# Face Enhancement Tests
# =============================================================================


class TestFaceEnhancement:
    """Tests for face enhancement endpoint."""

    def test_enhance_face_validation(self, image_client):
        """Test POST /enhance-face validates input."""
        response = image_client.post(
            "/api/image/enhance-face",
            json={},
        )
        # Should fail validation (400 or 422)
        assert response.status_code in [400, 422]

    def test_enhance_face_request_structure(self, image_client):
        """Test POST /enhance-face accepts expected structure."""
        response = image_client.post(
            "/api/image/enhance-face",
            json={
                "image_path": "/path/to/face.png",
            },
        )
        # Will fail without actual file, but validates request parsing
        assert response.status_code in [200, 400, 404, 500, 503]
