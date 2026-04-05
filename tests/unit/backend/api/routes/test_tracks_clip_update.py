"""
GAP-046: PUT clip update audio_id swap + ArtifactRefCounter + validation.
"""

from __future__ import annotations

import sys
from pathlib import Path
from unittest.mock import MagicMock

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

project_root = Path(__file__).resolve().parents[5]
if str(project_root) not in sys.path:
    sys.path.insert(0, str(project_root))

from backend.api import deps
from backend.api.routes import tracks


class FakeTrackStore:
    def __init__(self, initial: dict[tuple[str, str], dict]):
        self._data = {k: dict(v) for k, v in initial.items()}
        for (_p, _t), track in self._data.items():
            track.setdefault("clips", [])
            # Deep-copy clip dicts so routes mutate the same objects we inspect
            track["clips"] = [dict(c) for c in track["clips"]]

    def get_track(self, project_id: str, track_id: str):
        key = (project_id, track_id)
        t = self._data.get(key)
        if t is None:
            return None
        return t

    def update_track(self, project_id: str, track_id: str, updates: dict):
        t = self.get_track(project_id, track_id)
        if t is None:
            return None
        t.update(updates)
        return t


def _clip(cid: str, audio_id: str, profile_id: str = "p1") -> dict:
    return {
        "id": cid,
        "name": "n",
        "profile_id": profile_id,
        "audio_id": audio_id,
        "audio_url": f"/{audio_id}",
        "duration_seconds": 3.0,
        "start_time": 0.0,
    }


class TestTracksClipUpdate:
    def test_swap_audio_id_calls_decrement_and_increment(self):
        pid, tid, cid = "proj-u1", "trk-1", "clip-1"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip(cid, "audio-old")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"audio_id": "audio-new"},
        )
        assert resp.status_code == 200
        ref.decrement.assert_called_once_with("audio-old", cid)
        ref.increment.assert_called_once_with("audio-new", cid)
        updated = next(c for c in store.get_track(pid, tid)["clips"] if c["id"] == cid)
        assert updated["audio_id"] == "audio-new"

    def test_same_audio_id_no_ref_churn(self):
        pid, tid, cid = "proj-u2", "trk-1", "clip-1"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip(cid, "audio-same")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"audio_id": "audio-same"},
        )
        assert resp.status_code == 200
        ref.decrement.assert_not_called()
        ref.increment.assert_not_called()

    def test_first_assignment_from_empty_increments_only(self):
        pid, tid, cid = "proj-u3", "trk-1", "clip-1"
        clip = _clip(cid, "")
        clip["audio_id"] = ""
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [clip]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"audio_id": "audio-first"},
        )
        assert resp.status_code == 200
        ref.decrement.assert_not_called()
        ref.increment.assert_called_once_with("audio-first", cid)

    def test_audio_url_and_duration_persist(self):
        pid, tid, cid = "proj-u4", "trk-1", "clip-1"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip(cid, "a1")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"audio_url": "https://x/wav", "duration_seconds": 9.5},
        )
        assert resp.status_code == 200
        data = resp.json()
        assert data["audio_url"] == "https://x/wav"
        assert data["duration_seconds"] == 9.5

    def test_clip_not_found_404(self):
        pid, tid = "proj-u5", "trk-1"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip("other", "a")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/missing-clip",
            json={"audio_id": "x"},
        )
        assert resp.status_code == 404

    def test_track_not_found_404(self):
        pid, tid, cid = "proj-u6", "trk-missing", "clip-1"
        store = FakeTrackStore({})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"audio_id": "x"},
        )
        assert resp.status_code == 404

    def test_negative_duration_400(self):
        pid, tid, cid = "proj-u7", "trk-1", "clip-1"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip(cid, "a1")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"duration_seconds": -1.0},
        )
        assert resp.status_code == 400

    def test_put_derived_from_clip_id_persists(self):
        """GAP-040: optional lineage field round-trips on clip update."""
        pid, tid, cid = "proj-dfc", "trk-1", "clip-right"
        store = FakeTrackStore({(pid, tid): {"id": tid, "project_id": pid, "clips": [_clip(cid, "a1")]}})
        ref = MagicMock()
        app = FastAPI()
        app.include_router(tracks.router)
        app.dependency_overrides[deps.get_track_store_dep] = lambda: store
        app.dependency_overrides[deps.get_ref_counter_dep] = lambda: ref

        client = TestClient(app)
        resp = client.put(
            f"/api/projects/{pid}/tracks/{tid}/clips/{cid}",
            json={"derived_from_clip_id": "clip-left"},
        )
        assert resp.status_code == 200
        body = resp.json()
        assert body.get("derived_from_clip_id") == "clip-left"
        updated = next(c for c in store.get_track(pid, tid)["clips"] if c["id"] == cid)
        assert updated.get("derived_from_clip_id") == "clip-left"
