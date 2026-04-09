"""
GAP-015: Runtime-proof assertion — training export must not succeed for simulation / incomplete jobs.

ASGI full-app test (Grade I): same process as production routes, no live uvicorn.
Complements Grade S unit tests in tests/unit/backend/services/test_training_simulation_honesty.py.
"""
from __future__ import annotations

import uuid

import pytest
from httpx import ASGITransport, AsyncClient

from backend.api.main import app
from backend.services import training_service as ts


@pytest.mark.asyncio
async def test_export_rejects_simulation_complete_status() -> None:
    """POST /api/training/export returns 404 when job status is simulation_complete."""
    tid = f"rt-proof-sim-{uuid.uuid4().hex[:8]}"
    key = f"training_{tid}"
    ts._training_jobs_store[key] = {
        "id": tid,
        "status": ts.SIMULATION_STATUS,
        "output_path": "/tmp/fake-export-path",
        "dataset_id": "d1",
        "profile_id": "p1",
        "engine": "xtts",
    }
    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            response = await client.post(
                "/api/training/export",
                json={
                    "training_id": tid,
                    "profile_id": "p1",
                    "include_metadata": True,
                },
            )
        assert response.status_code == 404, (
            f"Expected 404 for simulation job export, got {response.status_code}: {response.text[:300]}"
        )
        body = response.json()
        detail = str(body.get("detail", body))
        lowered = detail.lower()
        assert "not completed" in lowered or "not found" in lowered
    finally:
        if key in ts._training_jobs_store:
            del ts._training_jobs_store[key]


@pytest.mark.asyncio
async def test_export_rejects_running_status() -> None:
    """POST /api/training/export returns 404 when job is not completed."""
    tid = f"rt-proof-run-{uuid.uuid4().hex[:8]}"
    key = f"training_{tid}"
    ts._training_jobs_store[key] = {
        "id": tid,
        "status": "running",
        "dataset_id": "d1",
        "profile_id": "p1",
        "engine": "xtts",
        "progress": 0.5,
    }
    try:
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            response = await client.post(
                "/api/training/export",
                json={"training_id": tid, "profile_id": "p1", "include_metadata": True},
            )
        assert response.status_code == 404
    finally:
        if key in ts._training_jobs_store:
            del ts._training_jobs_store[key]
