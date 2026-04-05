"""GAP-043: model download manager — routes + service behavior."""

from __future__ import annotations

import asyncio
import json
from datetime import datetime
from unittest.mock import MagicMock

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

import backend.data.repositories.job_repository as job_repo_mod
from backend.api.auth import require_auth_if_enabled
from backend.api.routes import models as models_routes
from backend.data.repositories.job_repository import (
    InMemoryJobRepository,
    JobEntity,
    JobStatus,
    JobType,
    reset_job_repository,
)


@pytest.fixture
def client(monkeypatch):
    reset_job_repository()
    job_repo_mod._job_repository = InMemoryJobRepository()
    monkeypatch.setattr(
        "backend.services.model_download_service.schedule_model_download",
        lambda *args, **kwargs: None,
    )
    app = FastAPI()
    app.include_router(models_routes.router)
    app.dependency_overrides[require_auth_if_enabled] = lambda: None
    with TestClient(app) as c:
        yield c
    app.dependency_overrides.clear()
    reset_job_repository()


def test_validate_model_download_url_rejects_file_scheme():
    from backend.services.model_download_service import validate_model_download_url

    with pytest.raises(ValueError, match="scheme"):
        validate_model_download_url("file:///tmp/x.zip")


def test_post_download_returns_job_id(client):
    body = {
        "url": "https://example.com/model.zip",
        "engine": "xtts_v2",
        "model_name": "demo",
        "version": "1.0",
    }
    r = client.post("/api/models/download", json=body)
    assert r.status_code == 200
    data = r.json()
    assert "job_id" in data
    assert len(data["job_id"]) > 8


def test_post_download_409_when_active_exists(client):
    repo = job_repo_mod.get_job_repository()

    async def _seed():
        ent = JobEntity(
            id="existing-dl",
            job_type=JobType.DOWNLOAD.value,
            name="Download xtts_v2/demo",
            status=JobStatus.RUNNING.value,
            progress=0.1,
            metadata=json.dumps(
                {
                    "url": "https://example.com/a.zip",
                    "engine_id": "xtts_v2",
                    "model_name": "demo",
                    "version": "1.0",
                }
            ),
        )
        await repo.create(ent)

    asyncio.run(_seed())

    r = client.post(
        "/api/models/download",
        json={
            "url": "https://example.com/b.zip",
            "engine": "xtts_v2",
            "model_name": "demo",
            "version": "1.0",
        },
    )
    assert r.status_code == 409
    assert r.json()["detail"]["job_id"] == "existing-dl"


def test_post_download_400_bad_url_scheme(client):
    r = client.post(
        "/api/models/download",
        json={
            "url": "ftp://example.com/x.zip",
            "engine": "xtts_v2",
            "model_name": "demo",
            "version": "1.0",
        },
    )
    assert r.status_code == 400


@pytest.mark.asyncio
async def test_run_model_download_job_checksum_mismatch_fails_job(monkeypatch, tmp_path):
    reset_job_repository()
    job_repo_mod._job_repository = InMemoryJobRepository()
    repo = job_repo_mod.get_job_repository()

    job_id = "dl-checksum"
    ent = JobEntity(
        id=job_id,
        job_type=JobType.DOWNLOAD.value,
        name="Download",
        status=JobStatus.PENDING.value,
        metadata=json.dumps(
            {
                "url": "https://example.com/blob",
                "engine_id": "e1",
                "model_name": "m1",
                "version": "1.0",
                "expected_sha256": "a" * 64,
            }
        ),
        created_at=datetime.now(),
        updated_at=datetime.now(),
    )
    await repo.create(ent)

    class _StreamCtx:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        def raise_for_status(self):
            pass

        headers = {"content-length": "5"}

        async def aiter_bytes(self, chunk_size: int = 65536):
            yield b"hello"

    class _ClientCtx:
        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            return False

        def stream(self, method: str, url: str):
            return _StreamCtx()

    fake_client = _ClientCtx()
    monkeypatch.setattr(
        "backend.services.model_download_service.httpx.AsyncClient",
        lambda *a, **k: fake_client,
    )

    storage = MagicMock()
    storage.base_dir = str(tmp_path)

    from backend.services.model_download_service import run_model_download_job

    await run_model_download_job(job_id, model_storage=storage)

    updated = await repo.get_by_id(job_id)
    assert updated is not None
    assert updated.status == JobStatus.FAILED.value
    assert updated.error is not None
    assert "Checksum" in updated.error
    storage.register_model.assert_not_called()
