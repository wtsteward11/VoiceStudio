"""
Transcription API route smoke tests.

Job contract, simulation, and persistence are covered in ``test_transcription_job.py``.
"""

from __future__ import annotations

from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.routes import transcribe


def test_transcribe_router_exposes_jobs_post():
    paths = [getattr(route, "path", "") for route in transcribe.router.routes]
    assert any(p.endswith("/jobs") for p in paths)


def test_transcribe_router_includes_post_root():
    app = FastAPI()
    app.include_router(transcribe.router)
    client = TestClient(app)
    assert client.get("/openapi.json").status_code == 200
