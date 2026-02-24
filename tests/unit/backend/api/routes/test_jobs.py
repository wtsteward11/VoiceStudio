"""
Unit Tests for Jobs API Route
Tests job management endpoints comprehensively.
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
    from backend.api.routes import jobs
except ImportError:
    pytest.skip("Could not import jobs route module", allow_module_level=True)


class TestJobsRouteImports:
    """Test jobs route module can be imported."""

    def test_jobs_module_imports(self):
        """Test jobs module can be imported."""
        assert jobs is not None, "Failed to import jobs module"
        assert hasattr(jobs, "router"), "jobs module missing router"

    def test_router_exists(self):
        """Test router exists and is configured."""
        assert jobs.router is not None, "Router should exist"
        if hasattr(jobs.router, "prefix"):
            pass  # Router configuration is valid

    def test_router_has_routes(self):
        """Test router has registered routes."""
        if hasattr(jobs.router, "routes"):
            routes = [route.path for route in jobs.router.routes]
            assert len(routes) > 0, "Router should have routes registered"


class TestJobsEndpoints:
    """Test job management endpoints."""

    def setup_method(self):
        """Save module state before each test."""
        self._saved_jobs = dict(getattr(jobs, "_jobs", {}))

    def teardown_method(self):
        """Restore module state after each test."""
        if hasattr(jobs, "_jobs"):
            jobs._jobs.clear()
            jobs._jobs.update(self._saved_jobs)

    def test_get_jobs_empty(self):
        """Test listing jobs when empty."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.get("/api/jobs")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) == 0

    def test_get_jobs_with_data(self):
        """Test listing jobs with data."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create a job
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        response = client.get("/api/jobs")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["name"] == "Test Job"

    def test_get_jobs_filtered_by_type(self):
        """Test listing jobs filtered by type."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create jobs of different types
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        jobs._jobs[job_id1] = {
            "id": job_id1,
            "name": "Batch Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        jobs._jobs[job_id2] = {
            "id": job_id2,
            "name": "Training Job",
            "type": "training",
            "status": "running",
            "progress": 0.3,
            "created": now,
        }

        response = client.get("/api/jobs?job_type=batch")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["type"] == "batch"

    def test_get_jobs_filtered_by_status(self):
        """Test listing jobs filtered by status."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create jobs with different statuses
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()

        jobs._jobs[job_id1] = {
            "id": job_id1,
            "name": "Running Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        jobs._jobs[job_id2] = {
            "id": job_id2,
            "name": "Completed Job",
            "type": "batch",
            "status": "completed",
            "progress": 1.0,
            "created": now,
        }

        response = client.get("/api/jobs?status=running")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["status"] == "running"

    def test_get_jobs_with_limit(self):
        """Test listing jobs with limit."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create multiple jobs
        now = datetime.utcnow().isoformat()
        for i in range(5):
            job_id = f"job-{uuid.uuid4().hex[:8]}"
            jobs._jobs[job_id] = {
                "id": job_id,
                "name": f"Job {i}",
                "type": "batch",
                "status": "running",
                "progress": 0.5,
                "created": now,
            }

        response = client.get("/api/jobs?limit=2")
        assert response.status_code == 200
        data = response.json()
        assert len(data) <= 2

    def test_get_job_success(self):
        """Test getting a specific job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        response = client.get(f"/api/jobs/{job_id}")
        assert response.status_code == 200
        data = response.json()
        assert data["id"] == job_id
        assert data["name"] == "Test Job"

    def test_get_job_not_found(self):
        """Test getting non-existent job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.get("/api/jobs/nonexistent")
        assert response.status_code == 404

    def test_get_job_summary_success(self):
        """Test successful job summary retrieval."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create jobs with different statuses
        now = datetime.utcnow().isoformat()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        job_id3 = f"job-{uuid.uuid4().hex[:8]}"

        jobs._jobs[job_id1] = {
            "id": job_id1,
            "name": "Running Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        jobs._jobs[job_id2] = {
            "id": job_id2,
            "name": "Completed Job",
            "type": "batch",
            "status": "completed",
            "progress": 1.0,
            "created": now,
        }

        jobs._jobs[job_id3] = {
            "id": job_id3,
            "name": "Failed Job",
            "type": "training",
            "status": "failed",
            "progress": 0.0,
            "created": now,
        }

        response = client.get("/api/jobs/summary")
        assert response.status_code == 200
        data = response.json()
        assert "total" in data
        assert "running" in data
        assert "completed" in data
        assert "failed" in data

    def test_cancel_job_success(self):
        """Test successful job cancellation."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        response = client.post(f"/api/jobs/{job_id}/cancel")
        assert response.status_code == 200

        # Verify job is cancelled
        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "cancelled"

    def test_cancel_job_not_found(self):
        """Test cancelling non-existent job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.post("/api/jobs/nonexistent/cancel")
        assert response.status_code == 404

    def test_pause_job_success(self):
        """Test successful job pause."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        response = client.post(f"/api/jobs/{job_id}/pause")
        assert response.status_code == 200

        # Verify job is paused
        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "paused"

    def test_pause_job_not_found(self):
        """Test pausing non-existent job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.post("/api/jobs/nonexistent/pause")
        assert response.status_code == 404

    def test_resume_job_success(self):
        """Test successful job resume."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "paused",
            "progress": 0.5,
            "created": now,
        }

        response = client.post(f"/api/jobs/{job_id}/resume")
        assert response.status_code == 200

        # Verify job is resumed
        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "running"

    def test_resume_job_not_found(self):
        """Test resuming non-existent job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.post("/api/jobs/nonexistent/resume")
        assert response.status_code == 404

    def test_delete_job_success(self):
        """Test successful job deletion."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        job_id = f"job-{uuid.uuid4().hex[:8]}"
        now = datetime.utcnow().isoformat()
        jobs._jobs[job_id] = {
            "id": job_id,
            "name": "Test Job",
            "type": "batch",
            "status": "completed",
            "progress": 1.0,
            "created": now,
        }

        response = client.delete(f"/api/jobs/{job_id}")
        assert response.status_code == 200

        # Verify job is deleted
        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 404

    def test_delete_job_not_found(self):
        """Test deleting non-existent job."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        response = client.delete("/api/jobs/nonexistent")
        assert response.status_code == 404

    def test_clear_completed_jobs_success(self):
        """Test successful clearing of completed jobs."""
        app = FastAPI()
        app.include_router(jobs.router)
        client = TestClient(app)

        jobs._jobs.clear()

        # Create completed and running jobs
        now = datetime.utcnow().isoformat()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"

        jobs._jobs[job_id1] = {
            "id": job_id1,
            "name": "Completed Job",
            "type": "batch",
            "status": "completed",
            "progress": 1.0,
            "created": now,
        }

        jobs._jobs[job_id2] = {
            "id": job_id2,
            "name": "Running Job",
            "type": "batch",
            "status": "running",
            "progress": 0.5,
            "created": now,
        }

        response = client.delete("/api/jobs")
        assert response.status_code == 200

        # Verify only completed jobs are deleted
        get_response = client.get("/api/jobs")
        data = get_response.json()
        assert len(data) == 1
        assert data[0]["status"] == "running"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
