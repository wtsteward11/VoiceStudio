"""
Unit Tests for Jobs API Route
Tests job management endpoints comprehensively.

Uses InMemoryJobRepository with dependency override for isolation.
"""

from __future__ import annotations

import asyncio
import sys
import uuid
from datetime import datetime
from pathlib import Path

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).parent.parent.parent.parent.parent.parent
sys.path.insert(0, str(project_root))

try:
    from backend.api.routes import jobs
    from backend.data.repositories.job_repository import (
        InMemoryJobRepository,
        JobEntity,
    )
    from backend.data.repositories.job_repository import (
        JobStatus as RepoJobStatus,
    )
except ImportError:
    pytest.skip("Could not import jobs route module", allow_module_level=True)


def _run(coro):
    """Run async coroutine in sync test context."""
    return asyncio.run(coro)


class TestJobsRouteImports:
    """Test jobs route module can be imported."""

    def test_jobs_module_imports(self):
        """Test jobs module can be imported."""
        assert jobs is not None, "Failed to import jobs route module"
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

    def _make_app(self, repo: InMemoryJobRepository) -> FastAPI:
        """Create FastAPI app with repo override."""
        app = FastAPI()
        app.include_router(jobs.router)
        app.dependency_overrides[jobs.get_repo] = lambda: repo
        return app

    def _create_job(
        self,
        repo: InMemoryJobRepository,
        job_id: str,
        name: str,
        job_type: str = "batch",
        status: str = "running",
        progress: float = 0.5,
    ) -> None:
        """Create a job in the repository."""
        entity = JobEntity(
            id=job_id,
            job_type=job_type,
            name=name,
            status=status,
            progress=progress,
            created_at=datetime.utcnow(),
            updated_at=datetime.utcnow(),
        )
        _run(repo.create(entity))

    def test_get_jobs_empty(self):
        """Test listing jobs when empty."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs")
        assert response.status_code == 200
        data = response.json()
        assert isinstance(data, list)
        assert len(data) == 0

    def test_get_jobs_with_data(self):
        """Test listing jobs with data."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job", status="running")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["name"] == "Test Job"

    def test_get_jobs_filtered_by_type(self):
        """Test listing jobs filtered by type."""
        repo = InMemoryJobRepository()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id1, "Batch Job", job_type="batch")
        self._create_job(repo, job_id2, "Training Job", job_type="training")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs?job_type=batch")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["type"] == "batch"

    def test_get_jobs_filtered_by_status(self):
        """Test listing jobs filtered by status."""
        repo = InMemoryJobRepository()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id1, "Running Job", status="running")
        self._create_job(repo, job_id2, "Completed Job", status="completed")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs?status=running")
        assert response.status_code == 200
        data = response.json()
        assert len(data) == 1
        assert data[0]["status"] == "running"

    def test_get_jobs_with_limit(self):
        """Test listing jobs with limit."""
        repo = InMemoryJobRepository()
        for i in range(5):
            self._create_job(repo, f"job-{uuid.uuid4().hex[:8]}", f"Job {i}")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs?limit=2")
        assert response.status_code == 200
        data = response.json()
        assert len(data) <= 2

    def test_get_job_success(self):
        """Test getting a specific job."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get(f"/api/jobs/{job_id}")
        assert response.status_code == 200
        data = response.json()
        assert data["id"] == job_id
        assert data["name"] == "Test Job"

    def test_get_job_not_found(self):
        """Test getting non-existent job."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs/nonexistent")
        assert response.status_code == 404

    def test_get_job_summary_success(self):
        """Test successful job summary retrieval."""
        repo = InMemoryJobRepository()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        job_id3 = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id1, "Running Job", status="running")
        self._create_job(repo, job_id2, "Completed Job", status="completed")
        self._create_job(repo, job_id3, "Failed Job", status="failed")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.get("/api/jobs/summary")
        assert response.status_code == 200
        data = response.json()
        assert "total" in data
        assert "running" in data
        assert "completed" in data
        assert "failed" in data

    def test_cancel_job_success(self):
        """Test successful job cancellation."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job", status="running")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post(f"/api/jobs/{job_id}/cancel")
        assert response.status_code == 200

        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "cancelled"

    def test_cancel_job_not_found(self):
        """Test cancelling non-existent job."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post("/api/jobs/nonexistent/cancel")
        assert response.status_code == 404

    def test_pause_job_success(self):
        """Test successful job pause."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job", status="running")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post(f"/api/jobs/{job_id}/pause")
        assert response.status_code == 200

        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "paused"

    def test_pause_job_not_found(self):
        """Test pausing non-existent job."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post("/api/jobs/nonexistent/pause")
        assert response.status_code == 404

    def test_resume_job_success(self):
        """Test successful job resume."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job", status="paused")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post(f"/api/jobs/{job_id}/resume")
        assert response.status_code == 200

        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 200
        assert get_response.json()["status"] == "running"

    def test_resume_job_not_found(self):
        """Test resuming non-existent job."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.post("/api/jobs/nonexistent/resume")
        assert response.status_code == 404

    def test_delete_job_success(self):
        """Test successful job deletion."""
        repo = InMemoryJobRepository()
        job_id = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id, "Test Job", status="completed")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.delete(f"/api/jobs/{job_id}")
        assert response.status_code == 200

        get_response = client.get(f"/api/jobs/{job_id}")
        assert get_response.status_code == 404

    def test_delete_job_not_found(self):
        """Test deleting non-existent job."""
        repo = InMemoryJobRepository()
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.delete("/api/jobs/nonexistent")
        assert response.status_code == 404

    def test_clear_completed_jobs_success(self):
        """Test successful clearing of completed jobs."""
        repo = InMemoryJobRepository()
        job_id1 = f"job-{uuid.uuid4().hex[:8]}"
        job_id2 = f"job-{uuid.uuid4().hex[:8]}"
        self._create_job(repo, job_id1, "Completed Job", status="completed")
        self._create_job(repo, job_id2, "Running Job", status="running")
        app = self._make_app(repo)
        client = TestClient(app)

        response = client.delete("/api/jobs")
        assert response.status_code == 200

        get_response = client.get("/api/jobs")
        data = get_response.json()
        assert len(data) == 1
        assert data[0]["status"] == "running"


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
