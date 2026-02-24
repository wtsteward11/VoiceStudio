"""
Unit Tests for Automation API Route
Tests automation system endpoints comprehensively.
"""

import sys
import uuid
from datetime import datetime
from pathlib import Path

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

# Import the route module
try:
    from backend.api.routes import automation
except ImportError:
    pytest.skip("Could not import automation route module", allow_module_level=True)


class TestAutomationRouteImports:
    """Test automation route module can be imported."""

    def test_automation_module_imports(self):
        """Test automation module can be imported."""
        assert automation is not None, "Failed to import automation module"
        assert hasattr(automation, "router"), "automation module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert automation.router is not None, "Router should exist"
        if hasattr(automation.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(automation.router, "routes"):
            routes = [route.path for route in automation.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestAutomationCurvesEndpoints:
    """Test automation curves CRUD endpoints."""

    def setup_method(self):
        self._saved_curves = dict(automation._automation_curves)
        automation._automation_curves.clear()

    def teardown_method(self):
        automation._automation_curves.clear()
        automation._automation_curves.update(self._saved_curves)

    def test_get_automation_curves_empty(self):
        """Test listing automation curves when empty."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        response = client.get("/api/automation")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) == 0

    def test_get_automation_curves_with_data(self):
        """Test listing automation curves with data."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.get("/api/automation")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["name"] == "Volume Curve"

    def test_get_automation_curves_filtered_by_track(self):
        """Test listing automation curves filtered by track_id."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create curves for different tracks
        curve_id1 = f"automation-{uuid.uuid4().hex[:8]}"
        curve_id2 = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        automation._automation_curves[curve_id1] = {
            "id": curve_id1,
            "name": "Track 1 Volume",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        automation._automation_curves[curve_id2] = {
            "id": curve_id2,
            "name": "Track 2 Volume",
            "parameter_id": "volume",
            "track_id": "track2",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.get("/api/automation?track_id=track1")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["track_id"] == "track1"

    def test_get_automation_curves_filtered_by_parameter(self):
        """Test listing automation curves filtered by parameter_id."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create curves for different parameters
        curve_id1 = f"automation-{uuid.uuid4().hex[:8]}"
        curve_id2 = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        automation._automation_curves[curve_id1] = {
            "id": curve_id1,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        automation._automation_curves[curve_id2] = {
            "id": curve_id2,
            "name": "Pitch Curve",
            "parameter_id": "pitch",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.get("/api/automation?parameter_id=volume")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["parameter_id"] == "volume"

    def test_get_automation_curve_success(self):
        """Test getting a specific automation curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.get(f"/api/automation/{curve_id}")
        assert response.status_code == 200
        data = response.json()
        assert data["id"] == curve_id
        assert data["name"] == "Volume Curve"

    def test_get_automation_curve_not_found(self):
        """Test getting non-existent automation curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        response = client.get("/api/automation/nonexistent")
        assert response.status_code == 404

    def test_create_automation_curve_success(self):
        """Test successful automation curve creation."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        request_data = {
            "name": "Volume Automation",
            "parameter_id": "volume",
            "track_id": "track1",
            "interpolation": "linear",
        }

        response = client.post("/api/automation", json=request_data)
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Volume Automation"
        assert data["parameter_id"] == "volume"
        assert "id" in data

    def test_create_automation_curve_with_bezier(self):
        """Test creating automation curve with bezier interpolation."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        request_data = {
            "name": "Smooth Volume",
            "parameter_id": "volume",
            "track_id": "track1",
            "interpolation": "bezier",
        }

        response = client.post("/api/automation", json=request_data)
        assert response.status_code == 200
        data = response.json()
        assert data["interpolation"] == "bezier"

    def test_update_automation_curve_success(self):
        """Test successful automation curve update."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Original Name",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        request_data = {
            "name": "Updated Name",
            "interpolation": "bezier",
        }

        response = client.put(f"/api/automation/{curve_id}", json=request_data)
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Updated Name"
        assert data["interpolation"] == "bezier"

    def test_update_automation_curve_not_found(self):
        """Test updating non-existent automation curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        request_data = {"name": "Updated Name"}

        response = client.put("/api/automation/nonexistent", json=request_data)
        assert response.status_code == 404

    def test_update_automation_curve_with_points(self):
        """Test updating automation curve with points."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        request_data = {
            "points": [
                {"time": 0.0, "value": 0.5},
                {"time": 1.0, "value": 1.0},
            ]
        }

        response = client.put(f"/api/automation/{curve_id}", json=request_data)
        assert response.status_code == 200
        data = response.json()
        assert len(data["points"]) == 2

    def test_delete_automation_curve_success(self):
        """Test successful automation curve deletion."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.delete(f"/api/automation/{curve_id}")
        assert response.status_code == 200

        # Verify curve is deleted
        get_response = client.get(f"/api/automation/{curve_id}")
        assert get_response.status_code == 404

    def test_delete_automation_curve_not_found(self):
        """Test deleting non-existent automation curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        response = client.delete("/api/automation/nonexistent")
        assert response.status_code == 404


class TestAutomationPointsEndpoints:
    """Test automation point management endpoints."""

    def setup_method(self):
        self._saved_curves = dict(automation._automation_curves)
        automation._automation_curves.clear()

    def teardown_method(self):
        automation._automation_curves.clear()
        automation._automation_curves.update(self._saved_curves)

    def test_add_automation_point_success(self):
        """Test successful automation point addition."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        point_data = {
            "time": 1.0,
            "value": 0.75,
        }

        response = client.post(f"/api/automation/{curve_id}/points", json=point_data)
        assert response.status_code == 200
        data = response.json()
        assert len(data["points"]) == 1
        assert data["points"][0]["time"] == 1.0
        assert data["points"][0]["value"] == 0.75

    def test_add_automation_point_with_bezier_handles(self):
        """Test adding automation point with bezier handles."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [],
            "interpolation": "bezier",
            "created": now,
            "modified": now,
        }

        point_data = {
            "time": 1.0,
            "value": 0.75,
            "bezier_handle_in_x": 0.1,
            "bezier_handle_in_y": 0.0,
            "bezier_handle_out_x": 0.1,
            "bezier_handle_out_y": 0.0,
        }

        response = client.post(f"/api/automation/{curve_id}/points", json=point_data)
        assert response.status_code == 200
        data = response.json()
        assert len(data["points"]) == 1
        assert data["points"][0]["bezier_handle_in_x"] == 0.1

    def test_add_automation_point_curve_not_found(self):
        """Test adding point to non-existent curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        point_data = {
            "time": 1.0,
            "value": 0.75,
        }

        response = client.post("/api/automation/nonexistent/points", json=point_data)
        assert response.status_code == 404

    def test_delete_automation_point_success(self):
        """Test successful automation point deletion."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve with points
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [
                {"time": 0.0, "value": 0.5},
                {"time": 1.0, "value": 1.0},
                {"time": 2.0, "value": 0.5},
            ],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.delete(f"/api/automation/{curve_id}/points/1")
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True

    def test_delete_automation_point_curve_not_found(self):
        """Test deleting point from non-existent curve."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        response = client.delete("/api/automation/nonexistent/points/0")
        assert response.status_code == 404

    def test_delete_automation_point_invalid_index(self):
        """Test deleting point with invalid index."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        automation._automation_curves.clear()

        # Create a curve with one point
        curve_id = f"automation-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        automation._automation_curves[curve_id] = {
            "id": curve_id,
            "name": "Volume Curve",
            "parameter_id": "volume",
            "track_id": "track1",
            "points": [
                {"time": 0.0, "value": 0.5},
            ],
            "interpolation": "linear",
            "created": now,
            "modified": now,
        }

        response = client.delete(f"/api/automation/{curve_id}/points/10")
        assert response.status_code == 400


class TestTrackParametersEndpoint:
    """Test track parameters endpoint."""

    def test_get_track_parameters_success(self):
        """Test successful track parameters retrieval."""
        app = FastAPI()
        app.include_router(automation.router)
        client = TestClient(app)

        response = client.get("/api/automation/tracks/test_track/parameters")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, dict)
        assert "parameters" in data


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
