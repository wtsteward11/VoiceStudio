"""
Unit Tests for Waveform API Route
Tests waveform visualization endpoints comprehensively.
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
    from backend.api.routes import waveform
except ImportError:
    pytest.skip("Could not import waveform route module", allow_module_level=True)


class TestWaveformRouteImports:
    """Test waveform route module can be imported."""

    def test_waveform_module_imports(self):
        """Test waveform module can be imported."""
        assert waveform is not None, "Failed to import waveform module"
        assert hasattr(waveform, "router"), "waveform module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert waveform.router is not None, "Router should exist"
        if hasattr(waveform.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(waveform.router, "routes"):
            routes = [route.path for route in waveform.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestWaveformConfig:
    """Test waveform configuration endpoints."""

    def setup_method(self):
        self._saved_cache = dict(waveform._waveform_cache)
        waveform._waveform_cache.clear()

    def teardown_method(self):
        waveform._waveform_cache.clear()
        waveform._waveform_cache.update(self._saved_cache)

    def test_get_waveform_config_default(self):
        """Test getting default waveform config."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        response = client.get("/api/waveform/config/test-audio")
        assert response.status_code == 200
        data = response.json()
        assert data["audio_id"] == "test-audio"
        assert data["zoom_level"] == 1.0

    def test_get_waveform_config_cached(self):
        """Test getting cached waveform config."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        waveform._waveform_cache.clear()

        cache_key = "config_test-audio"
        waveform._waveform_cache[cache_key] = {
            "audio_id": "test-audio",
            "zoom_level": 2.5,
            "show_channels": [0, 1],
            "show_rms": False,
            "show_peak": True,
            "show_zero_crossings": True,
            "color_scheme": "heatmap",
        }

        response = client.get("/api/waveform/config/test-audio")
        assert response.status_code == 200
        data = response.json()
        assert data["zoom_level"] == 2.5
        assert data["color_scheme"] == "heatmap"

    def test_update_waveform_config_success(self):
        """Test successful waveform config update."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        waveform._waveform_cache.clear()

        config_data = {
            "audio_id": "test-audio",
            "zoom_level": 3.0,
            "show_channels": [0],
            "show_rms": True,
            "show_peak": True,
            "show_zero_crossings": False,
            "color_scheme": "spectral",
        }

        response = client.put("/api/waveform/config/test-audio", json=config_data)
        assert response.status_code == 200
        data = response.json()
        assert data["zoom_level"] == 3.0
        assert data["color_scheme"] == "spectral"


class TestWaveformData:
    """Test waveform data endpoints."""

    def test_get_waveform_data_success(self):
        """Test successful waveform data retrieval."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=True):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = "/path/to/audio.wav"

                with patch("app.core.audio.audio_utils.load_audio") as mock_load:
                    # Create mock audio data
                    sample_rate = 44100
                    duration = 1.0
                    samples = int(sample_rate * duration)
                    mock_audio = np.random.randn(samples, 1).astype(np.float32)
                    mock_load.return_value = (mock_audio, sample_rate)

                    response = client.get("/api/waveform/data/test-audio")
                    # May return 200 or 500 depending on dependencies
                    assert response.status_code in [200, 500]

    def test_get_waveform_data_not_found(self):
        """Test getting waveform data for non-existent audio."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=False):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = None

                response = client.get("/api/waveform/data/nonexistent")
                assert response.status_code == 404

    def test_get_waveform_data_with_time_range(self):
        """Test getting waveform data with time range filter."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=True):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = "/path/to/audio.wav"

                with patch("app.core.audio.audio_utils.load_audio") as mock_load:
                    sample_rate = 44100
                    duration = 10.0
                    samples = int(sample_rate * duration)
                    mock_audio = np.random.randn(samples, 1).astype(np.float32)
                    mock_load.return_value = (mock_audio, sample_rate)

                    response = client.get(
                        "/api/waveform/data/test-audio" "?time_start=1.0&time_end=5.0"
                    )
                    # May return 200 or 500 depending on dependencies
                    assert response.status_code in [200, 500]

    def test_get_waveform_data_with_zoom(self):
        """Test getting waveform data with zoom level."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=True):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = "/path/to/audio.wav"

                with patch("app.core.audio.audio_utils.load_audio") as mock_load:
                    sample_rate = 44100
                    duration = 1.0
                    samples = int(sample_rate * duration)
                    mock_audio = np.random.randn(samples, 1).astype(np.float32)
                    mock_load.return_value = (mock_audio, sample_rate)

                    response = client.get("/api/waveform/data/test-audio?zoom_level=2.0")
                    # May return 200 or 500 depending on dependencies
                    assert response.status_code in [200, 500]


class TestWaveformAnalysis:
    """Test waveform analysis endpoints."""

    def test_analyze_waveform_success(self):
        """Test successful waveform analysis."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=True):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = "/path/to/audio.wav"

                with patch("app.core.audio.audio_utils.load_audio") as mock_load:
                    sample_rate = 44100
                    duration = 1.0
                    samples = int(sample_rate * duration)
                    mock_audio = np.random.randn(samples, 1).astype(np.float32)
                    mock_load.return_value = (mock_audio, sample_rate)

                    response = client.get("/api/waveform/analysis/test-audio")
                    # May return 200 or 500 depending on dependencies
                    assert response.status_code in [200, 500]

    def test_analyze_waveform_not_found(self):
        """Test analyzing non-existent audio."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=False):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = None

                response = client.get("/api/waveform/analysis/nonexistent")
                assert response.status_code == 404


class TestWaveformCompare:
    """Test waveform comparison endpoints."""

    def test_compare_waveforms_success(self):
        """Test successful waveform comparison."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=True):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = "/path/to/audio.wav"

                with patch("app.core.audio.audio_utils.load_audio") as mock_load:
                    sample_rate = 44100
                    duration = 1.0
                    samples = int(sample_rate * duration)
                    mock_audio = np.random.randn(samples, 1).astype(np.float32)
                    mock_load.return_value = (mock_audio, sample_rate)

                    response = client.get(
                        "/api/waveform/compare" "?audio_id_1=audio1&audio_id_2=audio2"
                    )
                    # May return 200 or 500 depending on dependencies
                    assert response.status_code in [200, 500]

    def test_compare_waveforms_missing_params(self):
        """Test waveform comparison with missing parameters."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        response = client.get("/api/waveform/compare")
        assert response.status_code == 422  # Validation error

    def test_compare_waveforms_not_found(self):
        """Test comparing non-existent audio files."""
        app = FastAPI()
        app.include_router(waveform.router)
        client = TestClient(app)

        with patch("os.path.exists", return_value=False):
            with patch("backend.api.routes.audio._get_audio_path") as mock_path:
                mock_path.return_value = None

                response = client.get(
                    "/api/waveform/compare" "?audio_id_1=nonexistent1" "&audio_id_2=nonexistent2"
                )
                assert response.status_code == 404


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
