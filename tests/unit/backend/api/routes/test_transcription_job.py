"""Unit tests for POST /api/transcribe/jobs (simulation + job contract)."""

from __future__ import annotations

import asyncio
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient
from httpx import ASGITransport, AsyncClient

from backend.api.routes import transcribe as transcribe_routes
from backend.ml.models.model_preflight import PreflightError as MLPreflightError
from tests.unit.backend.api.routes.test_dialogue import StoringFakeTranscriptionRepository


@pytest.fixture
def transcribe_client():
    app = FastAPI()
    app.include_router(transcribe_routes.router)
    return TestClient(app)


@pytest.fixture
def fake_repo():
    return StoringFakeTranscriptionRepository({})


@pytest.fixture
def in_memory_jobs(monkeypatch):
    """Canonical job store for async transcription jobs (no SQLite dependency)."""
    from backend.data.repositories import job_repository as jr

    mem = jr.InMemoryJobRepository()
    monkeypatch.setattr(jr, "_job_repository", mem)
    monkeypatch.setattr(jr, "_job_repository_init_attempted", True)
    yield mem


class TestTranscriptionJobRoute:
    def test_simulate_returns_is_simulated_true(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            r = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "a1", "simulate": True},
            )
        assert r.status_code == 200, r.text
        body = r.json()
        assert body["is_simulated"] is True
        assert body["mode"] == "simulation"
        assert body["real_transcription_performed"] is False

    def test_simulate_never_sets_real_transcription_performed(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            r = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "x", "simulate": True, "engine": "whisper"},
            )
        assert r.status_code == 200
        assert r.json()["real_transcription_performed"] is False

    def test_job_id_always_present(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            r = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "jid", "simulate": True},
            )
        assert r.status_code == 200
        assert r.json().get("job_id")

    def test_unknown_audio_id_simulation_still_works(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            r = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "not-in-registry-zzz", "simulate": True},
            )
        assert r.status_code == 200
        assert r.json()["transcript_id"]

    def test_engine_unavailable_returns_blocker(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            with patch.object(
                transcribe_routes,
                "transcribe_audio",
                new=AsyncMock(side_effect=MLPreflightError({"code": "ENGINE_UNAVAILABLE"}, status_code=503)),
            ):
                r = transcribe_client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a1", "simulate": False},
                )
        assert r.status_code == 200
        body = r.json()
        assert body["status"] == "unavailable"
        assert body["mode"] == "unavailable"
        assert body["is_simulated"] is False
        assert body["real_transcription_performed"] is False
        assert body.get("blocker")

    def test_failure_response_is_json(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            with patch.object(
                transcribe_routes,
                "transcribe_audio",
                new=AsyncMock(side_effect=MLPreflightError({"code": "X"}, status_code=503)),
            ):
                r = transcribe_client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a1", "simulate": False},
                )
        assert r.headers.get("content-type", "").startswith("application/json")
        data = r.json()
        assert "blocker" in data

    def test_transcript_id_queryable_after_simulate(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            jr = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "q1", "simulate": True},
            )
            tid = jr.json()["transcript_id"]
            gr = transcribe_client.get(f"/api/transcribe/{tid}")
        assert gr.status_code == 200
        seg = gr.json()["segments"]
        assert len(seg) >= 1
        assert jr.json()["transcript"]["segments"][0]["text"] == seg[0]["text"]

    def test_simulation_empty_segments_returns_structured_422(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            with patch(
                "backend.api.routes.transcribe.build_simulation_transcript",
                return_value={
                    "id": "t-empty",
                    "audio_id": "a",
                    "text": "",
                    "language": "en",
                    "duration": 0.0,
                    "segments": [],
                    "word_timestamps": [],
                    "created": "2026-01-01T00:00:00+00:00",
                    "engine": "simulation",
                },
            ):
                r = transcribe_client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a", "simulate": True},
                )
        assert r.status_code == 422
        err = r.json()
        assert err.get("detail", {}).get("code") == "EMPTY_TRANSCRIPT"

    def test_real_path_service_error_returns_failed_contract(self, transcribe_client, fake_repo):
        from backend.core.exceptions import ServiceError

        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            with patch.object(
                transcribe_routes,
                "transcribe_audio",
                new=AsyncMock(side_effect=ServiceError(404, "Audio file not found")),
            ):
                r = transcribe_client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "missing", "simulate": False},
                )
        assert r.status_code == 200
        b = r.json()
        assert b["status"] == "failed"
        assert b["mode"] == "real"
        assert b["blocker"]


class TestTranscriptionJobAsyncRoute:
    """Durable async_mode + GET /api/transcribe/jobs/{job_id} polling contract."""

    async def test_async_simulate_returns_pending_immediately(self, fake_repo, in_memory_jobs):
        app = FastAPI()
        app.include_router(transcribe_routes.router)
        transport = ASGITransport(app=app)
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            async with AsyncClient(transport=transport, base_url="http://test") as client:
                r = await client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a1", "simulate": True, "async_mode": True},
                )
        assert r.status_code == 200, r.text
        body = r.json()
        assert body["status"] == "pending"
        assert body["job_id"]
        assert body["progress"] == 0.0

    async def test_async_simulate_job_status_eventually_completed(self, fake_repo, in_memory_jobs):
        app = FastAPI()
        app.include_router(transcribe_routes.router)
        transport = ASGITransport(app=app)
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            async with AsyncClient(transport=transport, base_url="http://test") as client:
                r = await client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a1", "simulate": True, "async_mode": True},
                )
                assert r.status_code == 200
                jid = r.json()["job_id"]
                gr = None
                for _ in range(100):
                    await asyncio.sleep(0.01)
                    gr = await client.get(f"/api/transcribe/jobs/{jid}")
                    if gr.status_code == 200 and gr.json().get("status") == "completed":
                        break
                assert gr is not None
                assert gr.status_code == 200
                data = gr.json()
                assert data["status"] == "completed"
                assert data["transcript_id"]
                assert data["mode"] == "simulation"

    async def test_async_real_unavailable_returns_pending_then_unavailable(
        self, fake_repo, in_memory_jobs
    ):
        app = FastAPI()
        app.include_router(transcribe_routes.router)
        transport = ASGITransport(app=app)
        with (
            patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo),
            patch.object(
                transcribe_routes,
                "transcribe_audio",
                new=AsyncMock(side_effect=MLPreflightError({"code": "ENGINE_UNAVAILABLE"}, status_code=503)),
            ),
        ):
            async with AsyncClient(transport=transport, base_url="http://test") as client:
                r = await client.post(
                    "/api/transcribe/jobs",
                    json={"audio_id": "a1", "simulate": False, "async_mode": True},
                )
                assert r.status_code == 200
                jid = r.json()["job_id"]
                gr = None
                for _ in range(100):
                    await asyncio.sleep(0.01)
                    gr = await client.get(f"/api/transcribe/jobs/{jid}")
                    if gr.status_code == 200 and gr.json().get("status") == "unavailable":
                        break
                assert gr is not None
                assert gr.json()["status"] == "unavailable"
                assert gr.json()["mode"] == "unavailable"
                assert gr.json().get("blocker")

    def test_sync_path_unchanged_with_async_mode_false(self, transcribe_client, fake_repo):
        with patch.object(transcribe_routes, "get_transcription_repository", return_value=fake_repo):
            r = transcribe_client.post(
                "/api/transcribe/jobs",
                json={"audio_id": "a1", "simulate": True, "async_mode": False},
            )
        assert r.status_code == 200
        body = r.json()
        assert body["status"] == "completed"
        assert body["mode"] == "simulation"
        assert body["transcript_id"]

    def test_poll_missing_job_returns_404(self, transcribe_client):
        r = transcribe_client.get("/api/transcribe/jobs/00000000-0000-0000-0000-000000000001")
        assert r.status_code == 404
        assert r.headers.get("content-type", "").startswith("application/json")
