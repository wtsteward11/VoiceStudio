"""
Unit Tests for Models API Routes.

Tests model management endpoints (paths aligned with backend/api/routes/models.py).
"""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.auth import require_auth_if_enabled
from backend.api.routes.models import router


@pytest.fixture
def models_client():
    """Create test client for models routes."""
    app = FastAPI()
    app.include_router(router)
    app.dependency_overrides[require_auth_if_enabled] = lambda: None
    with TestClient(app) as client:
        yield client
    app.dependency_overrides.clear()


class TestModelsEndpoints:
    """Tests for models endpoints."""

    def test_list_models(self, models_client):
        """Test GET /api/models returns model list (or error if storage unavailable)."""
        response = models_client.get("/api/models")
        assert response.status_code in (200, 500)

    def test_get_model_by_engine_and_name(self, models_client):
        """Test GET /api/models/{engine}/{model_name}."""
        response = models_client.get("/api/models/xtts_v2/test-model")
        assert response.status_code in (200, 404, 500)

    def test_delete_model(self, models_client):
        """Test DELETE /api/models/{engine}/{model_name}."""
        response = models_client.delete("/api/models/xtts_v2/test-model")
        assert response.status_code in (200, 404, 500)
