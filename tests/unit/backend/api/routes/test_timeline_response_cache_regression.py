"""Regression: timeline GET must not be masked by API response cache after mutations."""

from __future__ import annotations

import uuid

from fastapi.testclient import TestClient

from backend.api.main import app


def test_timeline_state_reflects_track_immediately_after_post() -> None:
    """
    Response cache middleware caches JSON GETs. Timeline mutates via POST without
    invalidating those cache entries, so a second GET could incorrectly return the
    pre-mutation empty state. Timeline routes must bypass GET caching.
    """
    session_id = f"pytest-timeline-cache-{uuid.uuid4().hex}"
    client = TestClient(app, raise_server_exceptions=True)

    r0 = client.get("/api/timeline/state", params={"session_id": session_id})
    assert r0.status_code == 200
    assert r0.headers.get("X-Cache") != "HIT"

    r_track = client.post(
        "/api/timeline/tracks",
        json={"name": "Proof Track", "type": "audio"},
        params={"session_id": session_id},
    )
    assert r_track.status_code == 200
    track_id = r_track.json()["id"]

    r1 = client.get("/api/timeline/state", params={"session_id": session_id})
    assert r1.status_code == 200
    assert r1.headers.get("X-Cache") != "HIT"
    tracks = r1.json().get("tracks", [])
    assert any(t.get("id") == track_id for t in tracks), r1.json()
