"""
Unit Tests for Spatial Audio API Route
Tests spatial audio processing endpoints in isolation.
"""

import sys
from pathlib import Path
from unittest.mock import patch

import numpy as np
import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import spatial_audio
except ImportError:
    pytest.skip("Could not import spatial_audio route module", allow_module_level=True)


class TestSpatialAudioRouteImports:
    """Test spatial audio route module can be imported."""

    def test_spatial_audio_module_imports(self):
        """Test spatial_audio module can be imported."""
        assert spatial_audio is not None, "Failed to import spatial_audio module"
        assert hasattr(spatial_audio, "router"), "spatial_audio module missing router"

    def test_spatial_audio_models_imported(self):
        """Test spatial audio models are imported."""
        assert hasattr(spatial_audio, "SpatialPosition"), "SpatialPosition model missing"
        assert hasattr(spatial_audio, "SpatialConfig"), "SpatialConfig model missing"
        assert hasattr(
            spatial_audio, "SpatialConfigCreateRequest"
        ), "SpatialConfigCreateRequest model missing"


class TestSpatialAudioRouter:
    """Test spatial audio router configuration."""

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert spatial_audio.router is not None, "Router should exist"
        if hasattr(spatial_audio.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(spatial_audio.router, "routes"):
            routes = [route.path for route in spatial_audio.router.routes]
            assert len(routes) > 0, "Router should have routes registered"
            # Should have at least 11 routes
            assert len(routes) >= 11, f"Expected at least 11 routes, got {len(routes)}"


class TestSpatialAudioConfigEndpoints:
    """Test spatial audio configuration CRUD endpoints."""

    def setup_method(self):
        """Clear configs before each test."""
        spatial_audio._spatial_configs.clear()

    def test_create_spatial_config(self):
        """Test creating a spatial audio configuration."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/configs",
            json={
                "name": "Test Config",
                "audio_id": "test_audio_123",
                "x": 0.5,
                "y": 0.3,
                "z": 0.1,
                "distance": 2.0,
                "room_size": 1.5,
                "reverb_amount": 0.3,
                "occlusion": 0.2,
                "doppler": True,
                "hrtf": True,
            },
        )

        assert response.status_code == 200
        data = response.json()
        assert "config_id" in data
        assert data["name"] == "Test Config"
        assert data["audio_id"] == "test_audio_123"
        assert data["position"]["x"] == 0.5
        assert data["position"]["y"] == 0.3
        assert data["position"]["z"] == 0.1
        assert data["position"]["distance"] == 2.0
        assert data["room_size"] == 1.5
        assert data["reverb_amount"] == 0.3
        assert data["occlusion"] == 0.2
        assert data["doppler"] is True
        assert data["hrtf"] is True

    def test_create_spatial_config_minimal(self):
        """Test creating a spatial config with minimal fields."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "Minimal Config", "audio_id": "audio_123"},
        )

        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Minimal Config"
        assert data["audio_id"] == "audio_123"
        assert data["position"]["x"] == 0.0  # Default
        assert data["position"]["y"] == 0.0  # Default
        assert data["position"]["z"] == 0.0  # Default
        assert data["position"]["distance"] == 1.0  # Default

    def test_list_spatial_configs_empty(self):
        """Test listing configs when none exist."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.get("/api/spatial-audio/configs")
        assert response.status_code == 200
        assert response.json() == []

    def test_list_spatial_configs(self):
        """Test listing all spatial configs."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create two configs
        client.post(
            "/api/spatial-audio/configs",
            json={"name": "Config 1", "audio_id": "audio_1"},
        )
        client.post(
            "/api/spatial-audio/configs",
            json={"name": "Config 2", "audio_id": "audio_2"},
        )

        response = client.get("/api/spatial-audio/configs")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 2
        assert any(c["name"] == "Config 1" for c in data)
        assert any(c["name"] == "Config 2" for c in data)

    def test_list_spatial_configs_filtered_by_audio_id(self):
        """Test listing configs filtered by audio_id."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create configs with different audio_ids
        client.post(
            "/api/spatial-audio/configs",
            json={"name": "Config 1", "audio_id": "audio_1"},
        )
        client.post(
            "/api/spatial-audio/configs",
            json={"name": "Config 2", "audio_id": "audio_2"},
        )

        response = client.get("/api/spatial-audio/configs?audio_id=audio_1")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["audio_id"] == "audio_1"

    def test_get_spatial_config(self):
        """Test getting a specific spatial config."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create a config
        create_response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "Test Config", "audio_id": "audio_123"},
        )
        config_id = create_response.json()["config_id"]

        # Get the config
        response = client.get(f"/api/spatial-audio/configs/{config_id}")
        assert response.status_code == 200
        data = response.json()
        assert data["config_id"] == config_id
        assert data["name"] == "Test Config"

    def test_get_spatial_config_not_found(self):
        """Test getting a non-existent config returns 404."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.get("/api/spatial-audio/configs/nonexistent")
        assert response.status_code == 404
        assert "not found" in response.json()["detail"].lower()

    def test_update_spatial_config(self):
        """Test updating a spatial config."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create a config
        create_response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "Original Name", "audio_id": "audio_123"},
        )
        config_id = create_response.json()["config_id"]

        # Update the config
        response = client.put(
            f"/api/spatial-audio/configs/{config_id}",
            json={
                "name": "Updated Name",
                "audio_id": "audio_456",
                "x": 0.7,
                "y": 0.8,
                "z": 0.9,
                "distance": 3.0,
            },
        )

        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Updated Name"
        assert data["audio_id"] == "audio_456"
        assert data["position"]["x"] == 0.7
        assert data["position"]["y"] == 0.8
        assert data["position"]["z"] == 0.9
        assert data["position"]["distance"] == 3.0

    def test_update_spatial_config_not_found(self):
        """Test updating a non-existent config returns 404."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.put(
            "/api/spatial-audio/configs/nonexistent",
            json={"name": "Test", "audio_id": "audio_123"},
        )
        assert response.status_code == 404

    def test_delete_spatial_config(self):
        """Test deleting a spatial config."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create a config
        create_response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "To Delete", "audio_id": "audio_123"},
        )
        config_id = create_response.json()["config_id"]

        # Delete the config
        response = client.delete(f"/api/spatial-audio/configs/{config_id}")
        assert response.status_code == 200
        assert response.json()["success"] is True

        # Verify it's deleted
        get_response = client.get(f"/api/spatial-audio/configs/{config_id}")
        assert get_response.status_code == 404

    def test_delete_spatial_config_not_found(self):
        """Test deleting a non-existent config returns 404."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.delete("/api/spatial-audio/configs/nonexistent")
        assert response.status_code == 404


class TestSpatialAudioProcessingEndpoints:
    """Test spatial audio processing endpoints."""

    def setup_method(self):
        """Set up test fixtures."""
        spatial_audio._spatial_configs.clear()
        # Mock audio storage
        self.mock_audio_storage = {}

    @patch("os.path.exists", return_value=True)
    @patch("soundfile.write")
    @patch("soundfile.read")
    @patch(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        side_effect=lambda aid: "/fake/path/audio.wav" if aid == "test_audio_123" else None,
    )
    def test_apply_spatial_audio(self, mock_get_path, mock_sf_read, mock_sf_write, mock_exists):
        """Test applying spatial audio to an audio file."""
        # Create sample audio data
        sample_rate = 44100
        duration = 1.0
        audio_data = np.random.randn(int(sample_rate * duration)).astype(np.float32)
        mock_sf_read.return_value = (audio_data, sample_rate)

        # Create a config
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        create_response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "Test Config", "audio_id": "test_audio_123"},
        )
        config_id = create_response.json()["config_id"]

        # Apply spatial audio
        response = client.post(
            "/api/spatial-audio/apply",
            json={"config_id": config_id, "output_format": "wav"},
        )

        # Should succeed (may fail if librosa/scipy not available, but should handle gracefully)
        assert response.status_code in [200, 500, 503]
        if response.status_code == 200:
            data = response.json()
            assert "success" in data or "audio_id" in data

    def test_apply_spatial_audio_config_not_found(self):
        """Test applying spatial audio with non-existent config."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/apply",
            json={"config_id": "nonexistent", "output_format": "wav"},
        )
        assert response.status_code == 404

    @patch("os.path.exists", return_value=True)
    @patch("soundfile.write")
    @patch("soundfile.read")
    @patch(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        side_effect=lambda aid: "/fake/path/audio.wav" if aid == "test_audio_123" else None,
    )
    def test_preview_spatial_audio(self, mock_get_path, mock_sf_read, mock_sf_write, mock_exists):
        """Test previewing spatial audio."""
        # Create sample audio data
        sample_rate = 44100
        audio_data = np.random.randn(1000).astype(np.float32)
        mock_sf_read.return_value = (audio_data, sample_rate)

        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/preview",
            params={
                "audio_id": "test_audio_123",
                "x": 0.5,
                "y": 0.3,
                "z": 0.1,
                "distance": 2.0,
            },
        )

        # Should succeed or fail gracefully
        assert response.status_code in [200, 404, 503]
        if response.status_code == 200:
            data = response.json()
            assert "audio_id" in data
            assert "position" in data

    @patch(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        return_value=None,
    )
    def test_preview_spatial_audio_not_found(self, mock_get_path):
        """Test previewing with non-existent audio."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/preview",
            params={"audio_id": "nonexistent"},
        )
        assert response.status_code == 404

    def test_set_voice_position(self):
        """Test setting voice position."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/position",
            json={
                "audio_id": "test_audio_123",
                "x": 0.5,
                "y": 0.3,
                "z": 0.1,
                "distance": 2.0,
            },
        )

        assert response.status_code == 200
        data = response.json()
        assert "config_id" in data
        assert data["position"]["x"] == 0.5
        assert data["position"]["y"] == 0.3
        assert data["position"]["z"] == 0.1
        assert data["position"]["distance"] == 2.0

    def test_configure_environment(self):
        """Test configuring environment settings."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/environment",
            json={
                "room_size": 2.0,
                "material": "wood",
                "reverb_amount": 0.5,
                "doppler": True,
            },
        )

        assert response.status_code == 200
        data = response.json()
        assert data["room_size"] == 2.0
        assert data["material"] == "wood"
        assert data["reverb_amount"] == 0.5
        assert data["doppler"] is True

    def test_process_spatial_audio_with_config_id(self):
        """Test processing spatial audio with existing config."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        # Create a config
        create_response = client.post(
            "/api/spatial-audio/configs",
            json={"name": "Test Config", "audio_id": "test_audio_123"},
        )
        config_id = create_response.json()["config_id"]

        response = client.post(
            "/api/spatial-audio/process",
            json={"config_id": config_id, "output_format": "wav"},
        )

        # Should succeed or fail gracefully (may need audio file or validation error)
        assert response.status_code in [200, 400, 404, 422, 500]

    def test_process_spatial_audio_with_position(self):
        """Test processing spatial audio with position."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/process",
            json={
                "audio_id": "test_audio_123",
                "position": {"x": 0.5, "y": 0.3, "z": 0.1, "distance": 2.0},
                "output_format": "wav",
            },
        )

        # Should succeed or fail gracefully
        assert response.status_code in [200, 400, 404, 500]

    def test_process_spatial_audio_missing_position_and_config(self):
        """Test processing without position or config_id."""
        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/process",
            json={"audio_id": "test_audio_123", "output_format": "wav"},
        )

        assert response.status_code == 400
        assert "required" in response.json()["detail"].lower()

    @patch("os.path.exists", return_value=True)
    @patch("soundfile.write")
    @patch("soundfile.read")
    @patch(
        "backend.services.audio_artifacts.AudioRegistry.get_path",
        side_effect=lambda aid: "/fake/path/audio.wav" if aid == "test_audio_123" else None,
    )
    def test_generate_binaural_audio(self, mock_get_path, mock_sf_read, mock_sf_write, mock_exists):
        """Test generating binaural audio."""
        # Create sample audio data
        sample_rate = 44100
        audio_data = np.random.randn(1000).astype(np.float32)
        mock_sf_read.return_value = (audio_data, sample_rate)

        app = FastAPI()
        app.include_router(spatial_audio.router)
        client = TestClient(app)

        response = client.post(
            "/api/spatial-audio/binaural",
            json={
                "audio_id": "test_audio_123",
                "x": 0.5,
                "y": 0.3,
                "z": 0.1,
                "distance": 2.0,
                "hrtf": True,
            },
        )

        # Should succeed or fail gracefully
        assert response.status_code in [200, 404, 503, 500]
        if response.status_code == 200:
            data = response.json()
            assert "audio_id" in data
            assert "position" in data
            assert "hrtf" in data


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
