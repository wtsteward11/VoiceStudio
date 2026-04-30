"""
Unit tests for Dialogue Timeline Regeneration v1 (POST/GET/edit/regenerate).

Uses patched transcription repository, in-memory library, mocked synthesis,
SQLite-backed timeline (D-001), and audio forensics for export proof.
"""

from __future__ import annotations

import asyncio
import copy
import math
import struct
import uuid
import wave
from pathlib import Path
from typing import Any
from unittest.mock import AsyncMock, patch

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.models_additional import VoiceSynthesizeResponse


def _write_non_silent_wav16_mono(path: Path, *, seconds: float = 2.6, freq_hz: float = 440.0) -> None:
    sample_rate = 48000
    n = max(1, int(sample_rate * seconds))
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(sample_rate)
        frames = bytearray()
        for i in range(n):
            sample = int(12000 * math.sin(2 * math.pi * freq_hz * i / sample_rate))
            frames.extend(struct.pack("<h", max(-32768, min(32767, sample))))
        w.writeframes(bytes(frames))


class FakeTranscriptionRepository:
    """Minimal async repo matching dialogue workflow expectations."""

    def __init__(self, rows: dict[str, dict[str, Any]]):
        self._rows = copy.deepcopy(rows)

    async def get_transcription(self, transcription_id: str) -> dict[str, Any] | None:
        row = self._rows.get(transcription_id)
        return copy.deepcopy(row) if row else None

    async def update_transcription(
        self,
        transcription_id: str,
        text: str | None = None,
        segments: list[Any] | None = None,
        word_timestamps: list[Any] | None = None,
    ) -> dict[str, Any] | None:
        row = self._rows.get(transcription_id)
        if row is None:
            return None
        if text is not None:
            row["text"] = text
        if segments is not None:
            row["segments"] = segments
        if word_timestamps is not None:
            row["word_timestamps"] = word_timestamps
        self._rows[transcription_id] = row
        r = copy.deepcopy(row)
        return {
            "id": r["id"],
            "audio_id": r.get("audio_id", ""),
            "text": r.get("text", ""),
            "language": r.get("language", "en"),
            "duration": r.get("duration", 0.0),
            "segments": r.get("segments", []),
            "word_timestamps": r.get("word_timestamps", []),
            "created": r.get("created", "2026-01-01T00:00:00"),
            "engine": r.get("engine", "unknown"),
            "project_id": r.get("project_id"),
        }


@pytest.fixture(autouse=True)
def reset_timeline_state(tmp_path):
    """Reset SQLite-backed timeline session before each test (D-001)."""
    from backend.infrastructure.adapters.database import (
        get_database_adapter,
        reset_database_adapter_singleton,
    )
    from backend.project.timeline.session_repository import (
        DEFAULT_SESSION_ID,
        delete_session_timeline,
        ensure_session_timeline_table,
    )

    reset_database_adapter_singleton()
    db_path = tmp_path / "dialogue_timeline_unit.db"
    db = get_database_adapter(connection_string=f"sqlite:///{db_path.resolve().as_posix()}")

    async def setup() -> None:
        connected = await db.connect()
        assert connected is True
        await ensure_session_timeline_table(db)
        await delete_session_timeline(DEFAULT_SESSION_ID, db=db)

    asyncio.run(setup())
    yield

    async def teardown() -> None:
        await delete_session_timeline(DEFAULT_SESSION_ID, db=db)
        await db.disconnect()
        reset_database_adapter_singleton()

    asyncio.run(teardown())


@pytest.fixture
def dialogue_client(tmp_path):
    from backend.api.routes.dialogue import router as dialogue_router
    from backend.api.routes.timeline import router as timeline_router

    app = FastAPI()
    app.include_router(dialogue_router)
    app.include_router(timeline_router)
    return TestClient(app)


def _seed_transcription() -> tuple[str, FakeTranscriptionRepository]:
    tid = "transcript-dialogue-1"
    repo = FakeTranscriptionRepository(
        {
            tid: {
                "id": tid,
                "audio_id": "audio-src-1",
                "text": "full",
                "language": "en",
                "duration": 10.0,
                "segments": [],
                "word_timestamps": [],
                "created": "2026-04-29T12:00:00",
                "engine": "whisper",
                "project_id": "proj-1",
            }
        }
    )
    return tid, repo


def _seed_transcription_with_empty_segment() -> tuple[str, str, FakeTranscriptionRepository]:
    tid = "transcript-empty"
    sid = "seg-empty"
    repo = FakeTranscriptionRepository(
        {
            tid: {
                "id": tid,
                "audio_id": "a",
                "text": "",
                "language": "en",
                "duration": 1.0,
                "segments": [{"id": sid, "text": "", "start": 0.0, "end": 1.0, "status": "raw"}],
                "word_timestamps": [],
                "created": "2026-04-29T12:00:00",
                "engine": "whisper",
                "project_id": None,
            }
        }
    )
    return tid, sid, repo


def _patches(fake_repo: FakeTranscriptionRepository, lib_repo: Any, tmp_wav: Path):
    async def _synth(req: Any, request: Any, config_service: Any) -> VoiceSynthesizeResponse:
        from backend.services.audio_artifacts import AudioRegistry

        aid = f"sdlg_{uuid.uuid4().hex}"
        AudioRegistry.register(
            aid,
            str(tmp_wav),
            project_id=getattr(req, "project_id", None),
            source="dialogue_unit_test",
            model_used="piper",
            duration_seconds=2.5,
        )
        return VoiceSynthesizeResponse(
            audio_id=aid,
            audio_url=f"/audio/{aid}",
            generated_audio_id=aid,
            profile_id=req.profile_id,
            duration=2.5,
            quality_score=0.9,
            routed_engine="piper",
        )

    from backend.services import dialogue_segment_workflow as dsw

    return (
        patch.object(dsw, "get_transcription_repository", return_value=fake_repo),
        patch.object(dsw, "get_library_asset_repository", return_value=lib_repo),
        patch.object(dsw.SynthesisService, "synthesize", new=AsyncMock(side_effect=_synth)),
    )


@pytest.fixture
def lib_repo():
    from backend.data.repositories.library_repository import InMemoryLibraryAssetRepository

    return InMemoryLibraryAssetRepository()


class TestDialogueSegmentContract:
    def test_create_get_stable_id(self, dialogue_client):
        tid, fake_repo = _seed_transcription()
        from backend.services import dialogue_segment_workflow as dsw

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            r = dialogue_client.post(
                "/api/dialogue/segments",
                json={
                    "transcript_id": tid,
                    "text": "hello world",
                    "start": 1.0,
                    "end": 2.5,
                },
            )
        assert r.status_code == 200, r.text
        body = r.json()
        sid = body["id"]
        assert body["text"] == "hello world"
        assert body["start"] == 1.0
        assert body["end"] == 2.5
        assert body["status"] == "raw"

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            r2 = dialogue_client.get(f"/api/dialogue/segments/{sid}", params={"transcript_id": tid})
        assert r2.status_code == 200
        assert r2.json()["id"] == sid

    def test_edit_persistence_timing_unchanged(self, dialogue_client):
        tid, fake_repo = _seed_transcription()
        from backend.services import dialogue_segment_workflow as dsw

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            sid = dialogue_client.post(
                "/api/dialogue/segments",
                json={"transcript_id": tid, "text": "orig", "start": 3.0, "end": 4.0},
            ).json()["id"]
            r = dialogue_client.put(
                f"/api/dialogue/segments/{sid}/edit",
                params={"transcript_id": tid},
                json={"edited_text": "modified line"},
            )
        assert r.status_code == 200, r.text
        d = r.json()
        assert d["edited_text"] == "modified line"
        assert d["text"] == "orig"
        assert d["start"] == 3.0
        assert d["end"] == 4.0
        assert d["status"] == "edited"


class TestDialogueRegenerateChain:
    def test_regenerate_returns_ids_and_provenance(self, dialogue_client, tmp_path, lib_repo):
        tid, fake_repo = _seed_transcription()
        wav = tmp_path / "unit.wav"
        _write_non_silent_wav16_mono(wav)
        from backend.services import dialogue_segment_workflow as dsw
        from backend.services.audio_artifacts import AudioRegistry

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            sid = dialogue_client.post(
                "/api/dialogue/segments",
                json={"transcript_id": tid, "text": "x", "start": 0.5, "end": 1.0},
            ).json()["id"]
            dialogue_client.put(
                f"/api/dialogue/segments/{sid}/edit",
                params={"transcript_id": tid},
                json={"edited_text": "spoken text"},
            )
            tr = dialogue_client.post("/api/timeline/tracks", json={"name": "A1", "type": "audio"})
            track_id = tr.json()["id"]

        p1, p2, p3 = _patches(fake_repo, lib_repo, wav)
        with p1, p2, p3:
            reg = dialogue_client.post(
                f"/api/dialogue/segments/{sid}/regenerate",
                json={
                    "transcript_id": tid,
                    "profile_id": "profile-1",
                    "track_id": track_id,
                    "engine": "piper",
                    "project_id": "proj-1",
                    "replace_existing_clip": False,
                },
            )
        assert reg.status_code == 200, reg.text
        out = reg.json()
        assert out["audio_id"]
        assert out["generated_audio_id"]
        assert out["library_asset_id"]
        assert out["timeline_clip_id"]
        assert out["routed_engine"] == "piper"
        seg = out["segment"]
        assert seg["timeline_clip_id"] == out["timeline_clip_id"]
        assert seg["library_asset_id"] == out["library_asset_id"]
        assert seg["generated_audio_id"] == out["generated_audio_id"]
        assert seg["dialogue_provenance"]["transcript_id"] == tid
        assert seg["dialogue_provenance"]["edited_text"] == "spoken text"
        assert seg["dialogue_provenance"]["routed_engine"] == "piper"
        assert seg["status"] == "regenerated"

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            st = dialogue_client.get("/api/timeline/state").json()
        clip_ids = [c["id"] for t in st["tracks"] for c in t["clips"]]
        assert out["timeline_clip_id"] in clip_ids
        clip = next(
            c
            for t in st["tracks"]
            for c in t["clips"]
            if c["id"] == out["timeline_clip_id"]
        )
        assert clip["metadata"]["segment_id"] == sid
        assert clip["metadata"]["transcript_id"] == tid

    def test_regenerate_replace_dedupes_clip(self, dialogue_client, tmp_path, lib_repo):
        tid, fake_repo = _seed_transcription()
        wav = tmp_path / "unit.wav"
        _write_non_silent_wav16_mono(wav)
        from backend.services import dialogue_segment_workflow as dsw

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            sid = dialogue_client.post(
                "/api/dialogue/segments",
                json={"transcript_id": tid, "text": "a", "start": 0.0, "end": 0.5},
            ).json()["id"]
            dialogue_client.put(
                f"/api/dialogue/segments/{sid}/edit",
                params={"transcript_id": tid},
                json={"edited_text": "line one"},
            )
            track_id = dialogue_client.post(
                "/api/timeline/tracks", json={"name": "T", "type": "audio"}
            ).json()["id"]

        p1, p2, p3 = _patches(fake_repo, lib_repo, wav)
        with p1, p2, p3:
            first = dialogue_client.post(
                f"/api/dialogue/segments/{sid}/regenerate",
                json={
                    "transcript_id": tid,
                    "profile_id": "p1",
                    "track_id": track_id,
                    "engine": "piper",
                    "replace_existing_clip": False,
                },
            ).json()["timeline_clip_id"]
            second = dialogue_client.post(
                f"/api/dialogue/segments/{sid}/regenerate",
                json={
                    "transcript_id": tid,
                    "profile_id": "p1",
                    "track_id": track_id,
                    "engine": "piper",
                    "replace_existing_clip": True,
                },
            ).json()["timeline_clip_id"]
        assert second != first
        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            seg = dialogue_client.get(
                f"/api/dialogue/segments/{sid}", params={"transcript_id": tid}
            ).json()
        assert seg["timeline_clip_id"] == second
        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            st = dialogue_client.get("/api/timeline/state").json()
        clips = [c for t in st["tracks"] for c in t["clips"] if c["metadata"].get("segment_id") == sid]
        assert len(clips) == 1
        assert clips[0]["id"] == second

    def test_export_wav_non_silent_riff(self, dialogue_client, tmp_path, lib_repo):
        tid, fake_repo = _seed_transcription()
        wav = tmp_path / "unit.wav"
        _write_non_silent_wav16_mono(wav)
        from backend.services import dialogue_segment_workflow as dsw
        from scripts.proof.audio_forensics import analyze_wav_bytes

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            sid = dialogue_client.post(
                "/api/dialogue/segments",
                json={"transcript_id": tid, "text": "z", "start": 0.0, "end": 1.0},
            ).json()["id"]
            dialogue_client.put(
                f"/api/dialogue/segments/{sid}/edit",
                params={"transcript_id": tid},
                json={"edited_text": "export me"},
            )
            track_id = dialogue_client.post(
                "/api/timeline/tracks", json={"name": "E", "type": "audio"}
            ).json()["id"]

        p1, p2, p3 = _patches(fake_repo, lib_repo, wav)
        with p1, p2, p3:
            dialogue_client.post(
                f"/api/dialogue/segments/{sid}/regenerate",
                json={
                    "transcript_id": tid,
                    "profile_id": "p1",
                    "track_id": track_id,
                    "engine": "piper",
                },
            )
        out_file = tmp_path / "mixdown.wav"
        ex = dialogue_client.post(
            "/api/timeline/export",
            json={"output_path": str(out_file), "format": "wav"},
        )
        assert ex.status_code == 200, ex.text
        data = out_file.read_bytes()
        report = analyze_wav_bytes(data)
        assert report["is_wav"] is True
        assert report.get("non_silent") is True

    def test_missing_segment_404(self, dialogue_client, lib_repo):
        tid, fake_repo = _seed_transcription()
        from backend.services import dialogue_segment_workflow as dsw

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            r = dialogue_client.get(
                "/api/dialogue/segments/does-not-exist", params={"transcript_id": tid}
            )
        assert r.status_code == 404

    def test_empty_text_regenerate_422(self, dialogue_client, tmp_path, lib_repo):
        tid, sid, fake_repo = _seed_transcription_with_empty_segment()
        wav = tmp_path / "unit.wav"
        _write_non_silent_wav16_mono(wav)
        from backend.services import dialogue_segment_workflow as dsw

        with patch.object(dsw, "get_transcription_repository", return_value=fake_repo):
            track_id = dialogue_client.post(
                "/api/timeline/tracks", json={"name": "X", "type": "audio"}
            ).json()["id"]

        p1, p2, p3 = _patches(fake_repo, lib_repo, wav)
        with p1, p2, p3:
            r = dialogue_client.post(
                f"/api/dialogue/segments/{sid}/regenerate",
                json={
                    "transcript_id": tid,
                    "profile_id": "p1",
                    "track_id": track_id,
                    "engine": "piper",
                },
            )
        assert r.status_code == 422
