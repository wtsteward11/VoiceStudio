"""GAP-031: multitrack mixdown, solo/mute, import-from-project, timeline track PUT."""

from __future__ import annotations

from typing import Any

import numpy as np
import pytest
import soundfile as sf
from fastapi import FastAPI
from fastapi.testclient import TestClient

from backend.api.deps import get_track_store_dep


@pytest.fixture(autouse=True)
def reset_timeline_state():
    from backend.api.routes import timeline

    timeline._timeline_state = None
    timeline._undo_stack = []
    timeline._redo_stack = []
    yield
    timeline._timeline_state = None
    timeline._undo_stack = []
    timeline._redo_stack = []


@pytest.fixture
def timeline_client():
    from backend.api.routes.timeline import router

    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


@pytest.mark.asyncio
async def test_fade_in_reduces_attack_sample(tmp_path):
    """Linear fade-in should attenuate the first samples of a constant clip."""
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    p = tmp_path / "flat.wav"
    sf.write(str(p), np.ones(4800, dtype=np.float32), 48000)
    clip = Clip(
        track_id="t1",
        start_time=0,
        end_time=0.05,
        source_path=str(p),
        fade_in_seconds=0.01,
        fade_out_seconds=0.0,
    )
    t1 = Track(id="t1", name="A", type="audio", order=0, clips=[clip])
    state = TimelineState(duration=0.05, sample_rate=48000, tracks=[t1])
    mix = await _render_timeline_audio(state, 48000)
    assert mix is not None
    # 0.05s @ 48kHz => 2400 samples; last index is 2399 (compare to mid-clip, fully ramped).
    assert float(mix[0]) < float(mix[len(mix) // 2])


@pytest.mark.asyncio
async def test_multitrack_mix_sums_both_tracks(tmp_path):
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    p1 = tmp_path / "a.wav"
    p2 = tmp_path / "b.wav"
    sf.write(str(p1), np.full(2400, 0.5, dtype=np.float32), 48000)
    sf.write(str(p2), np.full(2400, 0.5, dtype=np.float32), 48000)
    t1 = Track(id="t1", name="A", type="audio", order=0, clips=[Clip(track_id="t1", start_time=0, end_time=0.05, source_path=str(p1))])
    t2 = Track(id="t2", name="B", type="audio", order=1, clips=[Clip(track_id="t2", start_time=0, end_time=0.05, source_path=str(p2))])
    state = TimelineState(duration=0.05, sample_rate=48000, tracks=[t1, t2])
    mix = await _render_timeline_audio(state, 48000)
    assert mix is not None
    assert float(np.max(np.abs(mix))) > 0.2


@pytest.mark.asyncio
async def test_muted_track_excluded(tmp_path):
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    p1 = tmp_path / "m1.wav"
    p2 = tmp_path / "m2.wav"
    sf.write(str(p1), np.ones(800, dtype=np.float32), 48000)
    sf.write(str(p2), np.ones(800, dtype=np.float32) * 0.25, 48000)
    t1 = Track(
        id="t1",
        name="A",
        type="audio",
        order=0,
        muted=True,
        clips=[Clip(track_id="t1", start_time=0, end_time=0.015, source_path=str(p1))],
    )
    t2 = Track(
        id="t2",
        name="B",
        type="audio",
        order=1,
        muted=False,
        clips=[Clip(track_id="t2", start_time=0, end_time=0.015, source_path=str(p2))],
    )
    state = TimelineState(duration=0.02, sample_rate=48000, tracks=[t1, t2])
    mix = await _render_timeline_audio(state, 48000)
    assert mix is not None
    # Only B contributes; expect peak near 0.25 (volume-normalized at write may scale — check B dominates)
    assert float(np.max(mix)) < 0.26


@pytest.mark.asyncio
async def test_solo_restricts_to_solo_track(tmp_path):
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    loud = tmp_path / "l.wav"
    quiet = tmp_path / "q.wav"
    sf.write(str(loud), np.ones(800, dtype=np.float32), 48000)
    sf.write(str(quiet), np.ones(800, dtype=np.float32) * 0.1, 48000)
    t1 = Track(
        id="t1",
        name="L",
        type="audio",
        order=0,
        solo=True,
        clips=[Clip(track_id="t1", start_time=0, end_time=0.015, source_path=str(loud))],
    )
    t2 = Track(
        id="t2",
        name="Q",
        type="audio",
        order=1,
        solo=False,
        clips=[Clip(track_id="t2", start_time=0, end_time=0.015, source_path=str(quiet))],
    )
    state = TimelineState(duration=0.02, sample_rate=48000, tracks=[t1, t2])
    mix = await _render_timeline_audio(state, 48000)
    assert mix is not None
    assert float(np.max(mix)) > 0.5


@pytest.mark.asyncio
async def test_solo_and_muted_track_is_silent(tmp_path):
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    p = tmp_path / "solo_mute.wav"
    sf.write(str(p), np.ones(800, dtype=np.float32), 48000)
    t1 = Track(
        id="t1",
        name="A",
        type="audio",
        order=0,
        solo=True,
        muted=True,
        clips=[Clip(track_id="t1", start_time=0, end_time=0.015, source_path=str(p))],
    )
    t2 = Track(
        id="t2",
        name="B",
        type="audio",
        order=1,
        solo=False,
        muted=False,
        clips=[Clip(track_id="t2", start_time=0, end_time=0.015, source_path=str(p), volume=0.01)],
    )
    state = TimelineState(duration=0.02, sample_rate=48000, tracks=[t1, t2])
    mix = await _render_timeline_audio(state, 48000)
    # Solo excludes B; A is solo but muted — no audible contribution.
    assert mix is None


@pytest.mark.asyncio
async def test_deterministic_track_order_sampling(tmp_path):
    """Same order/id; list order in JSON may vary but ordering sorts by order then id."""
    from backend.api.routes.timeline import Clip, TimelineState, Track, _render_timeline_audio

    p1 = tmp_path / "d1.wav"
    p2 = tmp_path / "d2.wav"
    sf.write(str(p1), np.zeros(400, dtype=np.float32), 48000)
    sf.write(str(p2), np.ones(400, dtype=np.float32) * 0.3, 48000)
    t_lo = Track(
        id="zzz",
        name="second",
        type="audio",
        order=0,
        clips=[Clip(track_id="zzz", start_time=0, end_time=0.008, source_path=str(p1))],
    )
    t_hi = Track(
        id="aaa",
        name="first",
        type="audio",
        order=1,
        clips=[Clip(track_id="aaa", start_time=0, end_time=0.008, source_path=str(p2))],
    )
    state = TimelineState(duration=0.01, sample_rate=48000, tracks=[t_hi, t_lo])
    a = await _render_timeline_audio(state, 48000)
    state2 = TimelineState(duration=0.01, sample_rate=48000, tracks=[t_lo, t_hi])
    b = await _render_timeline_audio(state2, 48000)
    assert a is not None and b is not None
    assert np.allclose(a, b)


def test_put_timeline_track_updates_mute_solo(timeline_client):
    timeline_client.post("/api/timeline/tracks", json={"name": "Mix", "type": "audio"})
    st = timeline_client.get("/api/timeline/state").json()
    tid = st["tracks"][0]["id"]
    r = timeline_client.put(
        f"/api/timeline/tracks/{tid}",
        json={"muted": True, "solo": True},
    )
    assert r.status_code == 200
    data = r.json()
    assert data["muted"] is True
    assert data["solo"] is True


def test_import_from_project_builds_tracks(monkeypatch, tmp_path):
    from backend.api.routes.timeline import router

    wav = tmp_path / "reg.wav"
    sf.write(str(wav), np.ones(600, dtype=np.float32) * 0.2, 48000)

    monkeypatch.setattr(
        "backend.services.audio_artifacts.registry.AudioRegistry.get_path",
        staticmethod(lambda aid: str(wav) if aid == "aid-test-1" else None),
    )

    class FakeStore:
        def list_tracks(self, project_id: str) -> list[dict[str, Any]]:
            return [
                {
                    "id": "tr-a",
                    "name": "One",
                    "project_id": project_id,
                    "track_number": 1,
                    "clips": [],
                    "is_muted": False,
                    "is_solo": False,
                },
                {
                    "id": "tr-b",
                    "name": "Two",
                    "project_id": project_id,
                    "track_number": 2,
                    "clips": [
                        {
                            "id": "c1",
                            "name": "c",
                            "profile_id": "p",
                            "audio_id": "aid-test-1",
                            "audio_url": "",
                            "duration_seconds": 0.012,
                            "start_time": 0.0,
                        }
                    ],
                    "is_muted": True,
                    "is_solo": False,
                },
            ]

    app = FastAPI()
    app.include_router(router)
    app.dependency_overrides[get_track_store_dep] = lambda: FakeStore()
    try:
        client = TestClient(app)
        r = client.post("/api/timeline/import-from-project", json={"project_id": "proj-mix-1"})
        assert r.status_code == 200, r.text
        body = r.json()
        assert len(body["tracks"]) == 2
        muted_ids = {t["id"]: t["muted"] for t in body["tracks"]}
        assert muted_ids.get("tr-b") is True
    finally:
        app.dependency_overrides.clear()
