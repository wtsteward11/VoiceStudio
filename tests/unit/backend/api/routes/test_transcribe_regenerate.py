"""
GAP-046: POST /api/transcribe/regenerate-segment validation + 202 acceptance.

Isolated from legacy test_transcribe.py (module-level skip). Uses FastAPI TestClient
with dependency overrides and patches for create_job / background task.
"""

from __future__ import annotations

import sys
import uuid
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parents[5]
if str(project_root) not in sys.path:
    sys.path.insert(0, str(project_root))

from backend.api import deps
from backend.api.routes import transcribe


class FakeTranscriptionRepo:
    def __init__(self, by_id: dict):
        self._by_id = by_id

    async def get_transcription(self, transcription_id: str):
        return self._by_id.get(transcription_id)


class FakeTrackStore:
    def __init__(self, tracks: dict[tuple[str, str], dict]):
        self._tracks = tracks

    def get_track(self, project_id: str, track_id: str):
        return self._tracks.get((project_id, track_id))


@pytest.fixture
def base_track_and_transcription():
    tid = "tr-" + uuid.uuid4().hex[:8]
    pid = "proj-1"
    track_id = "track-a"
    clip_id = "clip-1"
    seg_id = "seg-1"
    transcription_row = {
        "id": tid,
        "segments": [
            {"id": seg_id, "text": "Hello regenerate", "start": 0.0, "end": 1.0},
        ],
    }
    track_row = {
        "id": track_id,
        "project_id": pid,
        "clips": [
            {
                "id": clip_id,
                "name": "c1",
                "profile_id": "profile-x",
                "audio_id": "audio-old",
                "audio_url": "/old",
                "duration_seconds": 2.0,
                "start_time": 0.0,
            },
        ],
    }
    return {
        "transcription_id": tid,
        "project_id": pid,
        "track_id": track_id,
        "clip_id": clip_id,
        "segment_id": seg_id,
        "transcription_row": transcription_row,
        "track_row": track_row,
    }


class TestRegenerateSegmentRoute:
    def test_valid_request_returns_202_and_job_id(self, base_track_and_transcription):
        b = base_track_and_transcription
        repo = FakeTranscriptionRepo({b["transcription_id"]: b["transcription_row"]})
        store = FakeTrackStore({(b["project_id"], b["track_id"]): b["track_row"]})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        create_job_mock = AsyncMock(return_value=None)

        async def _noop_background(*_args, **_kwargs):
            return None

        with (
            patch.object(transcribe, "get_transcription_repository", return_value=repo),
            patch(
                "backend.services.canonical_job_lifecycle.create_job",
                create_job_mock,
            ),
            patch(
                "backend.services.transcript_segment_regeneration.run_transcript_segment_regeneration_job",
                _noop_background,
            ),
        ):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 202
        body = resp.json()
        assert "job_id" in body
        assert body.get("status") == "pending"
        assert len(body["job_id"]) > 0
        create_job_mock.assert_awaited_once()

    def test_transcription_not_found_404(self, base_track_and_transcription):
        b = base_track_and_transcription
        repo = FakeTranscriptionRepo({})
        store = FakeTrackStore({(b["project_id"], b["track_id"]): b["track_row"]})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 404
        detail = resp.json().get("detail")
        assert isinstance(detail, dict)
        assert detail.get("code") == "TRANSCRIPTION_NOT_FOUND"

    def test_segment_not_found_400(self, base_track_and_transcription):
        b = base_track_and_transcription
        row = {
            **b["transcription_row"],
            "segments": [{"id": "other-seg", "text": "x", "start": 0.0, "end": 1.0}],
        }
        repo = FakeTranscriptionRepo({b["transcription_id"]: row})
        store = FakeTrackStore({(b["project_id"], b["track_id"]): b["track_row"]})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 400
        detail = resp.json().get("detail")
        assert detail.get("code") == "SEGMENT_NOT_FOUND"

    def test_track_not_found_404(self, base_track_and_transcription):
        b = base_track_and_transcription
        repo = FakeTranscriptionRepo({b["transcription_id"]: b["transcription_row"]})
        store = FakeTrackStore({})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 404
        assert resp.json()["detail"]["code"] == "TRACK_NOT_FOUND"

    def test_clip_not_on_track_404(self, base_track_and_transcription):
        b = base_track_and_transcription
        repo = FakeTranscriptionRepo({b["transcription_id"]: b["transcription_row"]})
        track_other_clip = {
            **b["track_row"],
            "clips": [
                {
                    "id": "wrong-clip",
                    "name": "c",
                    "profile_id": "profile-x",
                    "audio_id": "a",
                    "audio_url": "/a",
                    "duration_seconds": 1.0,
                    "start_time": 0.0,
                },
            ],
        }
        store = FakeTrackStore({(b["project_id"], b["track_id"]): track_other_clip})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 404
        assert resp.json()["detail"]["code"] == "CLIP_NOT_FOUND"

    def test_profile_required_400(self, base_track_and_transcription):
        b = base_track_and_transcription
        repo = FakeTranscriptionRepo({b["transcription_id"]: b["transcription_row"]})
        bad_clip_track = {
            **b["track_row"],
            "clips": [
                {
                    **b["track_row"]["clips"][0],
                    "profile_id": "",
                },
            ],
        }
        store = FakeTrackStore({(b["project_id"], b["track_id"]): bad_clip_track})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 400
        assert resp.json()["detail"]["code"] == "PROFILE_REQUIRED"

    def test_empty_segment_text_400(self, base_track_and_transcription):
        b = base_track_and_transcription
        row = {
            **b["transcription_row"],
            "segments": [
                {"id": b["segment_id"], "text": "   ", "start": 0.0, "end": 1.0},
            ],
        }
        repo = FakeTranscriptionRepo({b["transcription_id"]: row})
        store = FakeTrackStore({(b["project_id"], b["track_id"]): b["track_row"]})
        app = FastAPI()
        app.include_router(transcribe.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store

        with patch.object(transcribe, "get_transcription_repository", return_value=repo):
            client = TestClient(app)
            resp = client.post(
                "/api/transcribe/regenerate-segment",
                json={
                    "project_id": b["project_id"],
                    "track_id": b["track_id"],
                    "clip_id": b["clip_id"],
                    "transcription_id": b["transcription_id"],
                    "segment_id": b["segment_id"],
                },
            )

        assert resp.status_code == 400
        assert resp.json()["detail"]["code"] == "EMPTY_TEXT"
