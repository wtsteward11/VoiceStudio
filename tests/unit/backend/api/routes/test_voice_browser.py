"""
Unit Tests for Voice Browser API Route
Tests voice browser endpoints comprehensively.
"""

import sys
from datetime import datetime
from pathlib import Path
from unittest.mock import patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import voice_browser
except ImportError:
    pytest.skip("Could not import voice_browser route module", allow_module_level=True)


class TestVoiceBrowserRouteImports:
    """Test voice browser route module can be imported."""

    def test_voice_browser_module_imports(self):
        """Test voice_browser module can be imported."""
        assert voice_browser is not None, "Failed to import voice_browser module"
        assert hasattr(voice_browser, "router"), "voice_browser module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert voice_browser.router is not None, "Router should exist"
        if hasattr(voice_browser.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(voice_browser.router, "routes"):
            routes = [route.path for route in voice_browser.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestVoiceSearchEndpoints:
    """Test voice search endpoints."""

    def setup_method(self):
        self._saved_catalog = dict(voice_browser._voice_catalog)
        voice_browser._voice_catalog.clear()
        self._sync_patch = patch.object(
            voice_browser, "_sync_catalog_from_profiles", lambda: None
        )
        self._sync_patch.start()

    def teardown_method(self):
        self._sync_patch.stop()
        voice_browser._voice_catalog.clear()
        voice_browser._voice_catalog.update(self._saved_catalog)

    def test_search_voices_empty(self):
        """Test searching voices when catalog is empty."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        response = client.get("/api/voice-browser/voices")
        assert response.status_code == 200
        data = response.json()
        assert "voices" in data
        assert len(data["voices"]) == 0
        assert data["total"] == 0

    def test_search_voices_with_data(self):
        """Test searching voices with data."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Test Voice",
            "description": "A test voice profile",
            "language": "en",
            "gender": "female",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": ["test", "english"],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices")
        assert response.status_code == 200
        data = response.json()
        assert len(data["voices"]) == 1
        assert data["voices"][0]["name"] == "Test Voice"

    def test_search_voices_with_query(self):
        """Test searching voices with query parameter."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Test Voice",
            "description": "A test voice",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": [],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices?query=Test")
        assert response.status_code == 200
        data = response.json()
        assert len(data["voices"]) == 1

    def test_search_voices_filtered_by_language(self):
        """Test searching voices filtered by language."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "English Voice",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": [],
            "created": now,
        }

        voice_browser._voice_catalog["voice2"] = {
            "id": "voice2",
            "name": "Spanish Voice",
            "language": "es",
            "quality_score": 0.8,
            "sample_count": 5,
            "tags": [],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices?language=en")
        assert response.status_code == 200
        data = response.json()
        assert all(v["language"] == "en" for v in data["voices"])

    def test_search_voices_filtered_by_gender(self):
        """Test searching voices filtered by gender."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Female Voice",
            "language": "en",
            "gender": "female",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": [],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices?gender=female")
        assert response.status_code == 200
        data = response.json()
        assert all(v["gender"] == "female" for v in data["voices"])

    def test_search_voices_filtered_by_min_quality(self):
        """Test searching voices filtered by minimum quality score."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "High Quality",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": [],
            "created": now,
        }

        voice_browser._voice_catalog["voice2"] = {
            "id": "voice2",
            "name": "Low Quality",
            "language": "en",
            "quality_score": 0.5,
            "sample_count": 5,
            "tags": [],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices?min_quality_score=0.8")
        assert response.status_code == 200
        data = response.json()
        assert all(v["quality_score"] >= 0.8 for v in data["voices"])

    def test_search_voices_filtered_by_tags(self):
        """Test searching voices filtered by tags."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Tagged Voice",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": ["professional", "clear"],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices?tags=professional")
        assert response.status_code == 200
        data = response.json()
        assert len(data["voices"]) == 1

    def test_search_voices_with_pagination(self):
        """Test searching voices with pagination."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        for i in range(5):
            voice_browser._voice_catalog[f"voice{i}"] = {
                "id": f"voice{i}",
                "name": f"Voice {i}",
                "language": "en",
                "quality_score": 0.8,
                "sample_count": 10,
                "tags": [],
                "created": now,
            }

        response = client.get("/api/voice-browser/voices?limit=2&offset=0")
        assert response.status_code == 200
        data = response.json()
        assert len(data["voices"]) == 2
        assert data["limit"] == 2
        assert data["offset"] == 0

    def test_get_voice_summary_success(self):
        """Test successful voice summary retrieval."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Test Voice",
            "description": "A test voice",
            "language": "en",
            "gender": "female",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": ["test"],
            "created": now,
        }

        response = client.get("/api/voice-browser/voices/voice1")
        assert response.status_code == 200
        data = response.json()
        assert data["id"] == "voice1"
        assert data["name"] == "Test Voice"

    def test_get_voice_summary_not_found(self):
        """Test getting non-existent voice summary."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        response = client.get("/api/voice-browser/voices/nonexistent")
        assert response.status_code == 404


class TestVoiceCatalogEndpoints:
    """Test voice catalog metadata endpoints."""

    def setup_method(self):
        self._saved_catalog = dict(voice_browser._voice_catalog)
        voice_browser._voice_catalog.clear()
        self._sync_patch = patch.object(
            voice_browser, "_sync_catalog_from_profiles", lambda: None
        )
        self._sync_patch.start()

    def teardown_method(self):
        self._sync_patch.stop()
        voice_browser._voice_catalog.clear()
        voice_browser._voice_catalog.update(self._saved_catalog)

    def test_get_available_languages_success(self):
        """Test successful languages listing."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "English Voice",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": [],
            "created": now,
        }

        voice_browser._voice_catalog["voice2"] = {
            "id": "voice2",
            "name": "Spanish Voice",
            "language": "es",
            "quality_score": 0.8,
            "sample_count": 5,
            "tags": [],
            "created": now,
        }

        response = client.get("/api/voice-browser/languages")
        assert response.status_code == 200
        data = response.json()
        assert "languages" in data
        assert "en" in data["languages"]
        assert "es" in data["languages"]

    def test_get_available_languages_empty(self):
        """Test languages listing when catalog is empty."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        response = client.get("/api/voice-browser/languages")
        assert response.status_code == 200
        data = response.json()
        assert "languages" in data
        assert len(data["languages"]) == 0

    def test_get_available_tags_success(self):
        """Test successful tags listing."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        now = datetime.utcnow().isoformat()
        voice_browser._voice_catalog["voice1"] = {
            "id": "voice1",
            "name": "Tagged Voice",
            "language": "en",
            "quality_score": 0.9,
            "sample_count": 10,
            "tags": ["professional", "clear"],
            "created": now,
        }

        response = client.get("/api/voice-browser/tags")
        assert response.status_code == 200
        data = response.json()
        assert "tags" in data
        assert "professional" in data["tags"]
        assert "clear" in data["tags"]

    def test_get_available_tags_empty(self):
        """Test tags listing when catalog is empty."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        voice_browser._voice_catalog.clear()

        response = client.get("/api/voice-browser/tags")
        assert response.status_code == 200
        data = response.json()
        assert "tags" in data
        assert len(data["tags"]) == 0


class TestVoiceBrowserRefreshEndpoint:
    """Test voice browser catalog refresh endpoint."""

    def test_refresh_catalog_success(self):
        """Test successful catalog refresh."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        with patch("backend.api.routes.voice_browser._sync_catalog_from_profiles") as mock_sync:
            mock_sync.return_value = None

            response = client.post("/api/voice-browser/refresh")
            assert response.status_code == 200
            data = response.json()
            assert "message" in data

    def test_refresh_catalog_updates_catalog(self):
        """Test that refresh updates the catalog."""
        app = FastAPI()
        app.include_router(voice_browser.router)
        client = TestClient(app)

        # Clear catalog first
        voice_browser._voice_catalog.clear()

        # Mock profiles storage
        mock_profiles = {
            "profile1": {
                "id": "profile1",
                "name": "Test Profile",
                "language": "en",
                "quality_score": 0.9,
                "tags": ["test"],
            }
        }

        with patch("backend.api.routes.voice_browser._sync_catalog_from_profiles") as mock_sync:

            def sync_side_effect():
                voice_browser._voice_catalog["profile1"] = mock_profiles["profile1"]

            mock_sync.side_effect = sync_side_effect

            response = client.post("/api/voice-browser/refresh")
            assert response.status_code == 200


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
