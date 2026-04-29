"""
Unit Tests for Timeline API Routes.

Tests all 15 timeline endpoints with comprehensive coverage:
- Timeline state management
- Track CRUD operations
- Clip CRUD operations
- Clip editing (move, trim, split)
- Playback controls (playhead, loop)
- Export functionality
- Undo/Redo operations
"""

import pytest
from fastapi import FastAPI
from fastapi.testclient import TestClient

# =============================================================================
# Fixtures
# =============================================================================


@pytest.fixture(autouse=True)
def reset_timeline_state(tmp_path):
    """Reset SQLite-backed timeline session before each test (D-001)."""
    import asyncio

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
    db_path = tmp_path / "timeline_route_unit.db"
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
def timeline_client():
    """Create test client for timeline routes."""
    from backend.api.routes.timeline import router

    app = FastAPI()
    app.include_router(router)
    return TestClient(app)


# =============================================================================
# Timeline State Tests
# =============================================================================


class TestTimelineState:
    """Tests for timeline state management."""

    def test_get_timeline_state(self, timeline_client):
        """Test GET /api/timeline/state returns timeline."""
        response = timeline_client.get("/api/timeline/state")
        assert response.status_code == 200
        data = response.json()
        assert "id" in data
        assert "name" in data
        assert "tracks" in data
        assert "duration" in data
        assert "playhead_position" in data
        assert "revision" in data
        assert isinstance(data["revision"], int)

    def test_create_timeline(self, timeline_client):
        """Test POST /api/timeline/create creates new timeline."""
        response = timeline_client.post(
            "/api/timeline/create",
            json={"name": "Test Timeline", "sample_rate": 44100},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Test Timeline"
        assert data["sample_rate"] == 44100
        assert data["tracks"] == []

    def test_create_timeline_with_defaults(self, timeline_client):
        """Test creating timeline with default values."""
        response = timeline_client.post(
            "/api/timeline/create",
            json={},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Untitled Timeline"
        assert data["sample_rate"] == 48000


# =============================================================================
# Track CRUD Tests
# =============================================================================


class TestTrackOperations:
    """Tests for track CRUD operations."""

    def test_add_track(self, timeline_client):
        """Test POST /api/timeline/tracks adds a track."""
        response = timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Audio Track 1", "type": "audio"},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Audio Track 1"
        assert data["type"] == "audio"
        assert "id" in data

    def test_add_track_defaults(self, timeline_client):
        """Test adding track with default values."""
        response = timeline_client.post(
            "/api/timeline/tracks",
            json={},
        )
        assert response.status_code == 200
        data = response.json()
        assert "Track" in data["name"]
        assert data["type"] == "audio"

    def test_delete_track(self, timeline_client):
        """Test POST /api/timeline/tracks/delete deletes a track."""
        # First create a track
        add_response = timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Track to Delete"},
        )
        track_id = add_response.json()["id"]

        # Delete the track
        response = timeline_client.post(
            "/api/timeline/tracks/delete",
            json={"id": track_id},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert data["deleted_id"] == track_id

    def test_delete_nonexistent_track(self, timeline_client):
        """Test deleting a track that doesn't exist."""
        response = timeline_client.post(
            "/api/timeline/tracks/delete",
            json={"id": "nonexistent-track-id"},
        )
        assert response.status_code == 404


# =============================================================================
# Clip CRUD Tests
# =============================================================================


class TestClipOperations:
    """Tests for clip CRUD operations."""

    @pytest.fixture
    def setup_track(self, timeline_client):
        """Create a track for clip tests."""
        response = timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Test Track"},
        )
        return response.json()["id"]

    def test_add_clip(self, timeline_client, setup_track):
        """Test POST /api/timeline/clips adds a clip."""
        response = timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": setup_track,
                "source_path": "/path/to/audio.wav",
                "start_time": 0.0,
                "duration": 5.0,
                "name": "Test Clip",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert data["name"] == "Test Clip"
        assert data["start_time"] == 0.0
        assert data["end_time"] == 5.0
        assert data["source_path"] == "/path/to/audio.wav"

    def test_add_clip_to_nonexistent_track(self, timeline_client):
        """Test adding clip to non-existent track."""
        response = timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": "nonexistent-track",
                "start_time": 0.0,
                "duration": 1.0,
            },
        )
        assert response.status_code == 404

    def test_delete_clip(self, timeline_client, setup_track):
        """Test POST /api/timeline/clips/delete deletes a clip."""
        # First add a clip
        add_response = timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": setup_track,
                "start_time": 0.0,
                "duration": 1.0,
            },
        )
        clip_id = add_response.json()["id"]

        # Delete the clip
        response = timeline_client.post(
            "/api/timeline/clips/delete",
            json={"id": clip_id},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert data["deleted_id"] == clip_id

    def test_delete_nonexistent_clip(self, timeline_client):
        """Test deleting a clip that doesn't exist."""
        response = timeline_client.post(
            "/api/timeline/clips/delete",
            json={"id": "nonexistent-clip-id"},
        )
        assert response.status_code == 404


# =============================================================================
# Clip Editing Tests
# =============================================================================


class TestClipEditing:
    """Tests for clip editing operations."""

    @pytest.fixture
    def setup_clip(self, timeline_client):
        """Create a track and clip for editing tests."""
        # Create track
        track_response = timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Edit Track"},
        )
        track_id = track_response.json()["id"]

        # Create clip
        clip_response = timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": track_id,
                "start_time": 0.0,
                "duration": 10.0,
                "name": "Edit Clip",
            },
        )
        return {"track_id": track_id, "clip": clip_response.json()}

    def test_move_clip(self, timeline_client, setup_clip):
        """Test PUT /api/timeline/clips/{id}/move moves a clip."""
        clip_id = setup_clip["clip"]["id"]

        response = timeline_client.put(
            f"/api/timeline/clips/{clip_id}/move",
            json={"new_start_time": 5.0},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["start_time"] == 5.0
        assert data["end_time"] == 15.0  # Original duration preserved

    def test_move_clip_to_different_track(self, timeline_client, setup_clip):
        """Test moving clip to a different track."""
        clip_id = setup_clip["clip"]["id"]

        # Create second track
        track2_response = timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Target Track"},
        )
        track2_id = track2_response.json()["id"]

        response = timeline_client.put(
            f"/api/timeline/clips/{clip_id}/move",
            json={"new_start_time": 2.0, "new_track_id": track2_id},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["track_id"] == track2_id

    def test_move_nonexistent_clip(self, timeline_client):
        """Test moving a clip that doesn't exist."""
        response = timeline_client.put(
            "/api/timeline/clips/nonexistent/move",
            json={"new_start_time": 5.0},
        )
        assert response.status_code == 404

    def test_trim_clip(self, timeline_client, setup_clip):
        """Test PUT /api/timeline/clips/{id}/trim trims a clip."""
        clip_id = setup_clip["clip"]["id"]

        response = timeline_client.put(
            f"/api/timeline/clips/{clip_id}/trim",
            json={"new_start": 2.0, "new_end": 8.0},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["start_time"] == 2.0
        assert data["end_time"] == 8.0

    def test_trim_clip_start_only(self, timeline_client, setup_clip):
        """Test trimming only the start of a clip."""
        clip_id = setup_clip["clip"]["id"]

        response = timeline_client.put(
            f"/api/timeline/clips/{clip_id}/trim",
            json={"new_start": 1.0},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["start_time"] == 1.0
        assert data["end_time"] == 10.0  # Original end preserved
        assert data.get("source_start", 0.0) == pytest.approx(1.0)

    def test_set_clip_fade(self, timeline_client, setup_clip):
        """PUT /clips/{id}/fade sets fade metadata."""
        clip_id = setup_clip["clip"]["id"]
        response = timeline_client.put(
            f"/api/timeline/clips/{clip_id}/fade",
            json={"fade_in_seconds": 0.2, "fade_out_seconds": 0.3},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["fade_in_seconds"] == pytest.approx(0.2)
        assert data["fade_out_seconds"] == pytest.approx(0.3)

    def test_split_clip(self, timeline_client, setup_clip):
        """Test POST /api/timeline/clips/{id}/split splits a clip."""
        clip_id = setup_clip["clip"]["id"]

        response = timeline_client.post(
            f"/api/timeline/clips/{clip_id}/split",
            json={"split_position": 5.0},
        )
        assert response.status_code == 200
        data = response.json()
        assert "clip_before" in data
        assert "clip_after" in data
        assert data["clip_before"]["end_time"] == 5.0
        assert data["clip_after"]["start_time"] == 5.0

    def test_split_clip_invalid_position(self, timeline_client, setup_clip):
        """Test splitting at invalid position."""
        clip_id = setup_clip["clip"]["id"]

        # Split outside clip bounds
        response = timeline_client.post(
            f"/api/timeline/clips/{clip_id}/split",
            json={"split_position": 15.0},  # Clip ends at 10.0
        )
        assert response.status_code == 400


# =============================================================================
# Playback Control Tests
# =============================================================================


class TestPlaybackControls:
    """Tests for playback control endpoints."""

    def test_set_playhead(self, timeline_client):
        """Test POST /api/timeline/playhead sets position."""
        response = timeline_client.post(
            "/api/timeline/playhead",
            json={"Position": 5.5},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True

        # Verify playhead was set
        state = timeline_client.get("/api/timeline/state").json()
        assert state["playhead_position"] == 5.5

    def test_set_playhead_negative_clamped(self, timeline_client):
        """Test that negative playhead is clamped to zero."""
        response = timeline_client.post(
            "/api/timeline/playhead",
            json={"Position": -5.0},
        )
        assert response.status_code == 200

        state = timeline_client.get("/api/timeline/state").json()
        assert state["playhead_position"] == 0.0

    def test_set_loop(self, timeline_client):
        """Test POST /api/timeline/loop sets loop region."""
        response = timeline_client.post(
            "/api/timeline/loop",
            json={"Start": 2.0, "End": 8.0},
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True

        # Verify loop was set
        state = timeline_client.get("/api/timeline/state").json()
        assert state["loop_start"] == 2.0
        assert state["loop_end"] == 8.0


# =============================================================================
# Export Tests
# =============================================================================


class TestExport:
    """Tests for timeline export."""

    def test_export_timeline(self, timeline_client, tmp_path):
        """GAP-031: export requires audible timeline or valid fallback (no silent empty success)."""
        import numpy as np
        import soundfile as sf

        wav_path = tmp_path / "clip.wav"
        sf.write(str(wav_path), np.zeros(800, dtype=np.float32), 48000)

        timeline_client.post("/api/timeline/tracks", json={"name": "T1", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.01,
                "name": "c1",
            },
        )

        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "sample_rate": 48000,
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        assert isinstance(data["output_path"], str)
        assert data["output_path"].endswith(".wav")
        assert "duration" in data

    def test_export_empty_timeline_returns_400_without_fallback(self, timeline_client):
        """GAP-031: empty mix fails closed when fallback does not apply."""
        timeline_client.post("/api/timeline/create", json={"name": "Empty", "sample_rate": 48000})
        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/empty.wav",
                "format": "wav",
                "sample_rate": 48000,
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 400
        detail = response.json().get("detail", "")
        assert "no audible" in detail.lower() or "timeline" in detail.lower()

    def test_export_apply_effects_requires_chain(self, timeline_client):
        """GAP-029: apply_effects without effect_chain_id is rejected."""
        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "apply_effects": True,
                "project_id": "proj-x",
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 422

    def test_export_apply_effects_unknown_chain(self, timeline_client, tmp_path):
        """GAP-029: unknown chain returns 404 (not silent success)."""
        import numpy as np
        import soundfile as sf

        wav_path = tmp_path / "unk_chain.wav"
        sf.write(str(wav_path), np.ones(1200, dtype=np.float32) * 0.1, 48000)
        timeline_client.post("/api/timeline/create", json={"name": "UnkFx", "sample_rate": 48000})
        timeline_client.post("/api/timeline/tracks", json={"name": "T", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.02,
                "name": "clip1",
            },
        )

        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "apply_effects": True,
                "project_id": "proj-x",
                "effect_chain_id": "nonexistent-chain-12345",
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 404

    def test_export_with_effect_bake_success(self, timeline_client, tmp_path):
        """Apply enabled chain during export when chain exists (GAP-029)."""
        from datetime import datetime
        from uuid import uuid4

        import numpy as np
        import soundfile as sf

        from backend.audio.effects.effect_chain_store import get_effect_chain_store

        wav_path = tmp_path / "bake_clip.wav"
        sf.write(str(wav_path), np.zeros(1200, dtype=np.float32), 48000)
        timeline_client.post("/api/timeline/tracks", json={"name": "FX", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.02,
                "name": "fxclip",
            },
        )

        pid = f"proj-fx-{uuid4().hex[:8]}"
        cid = str(uuid4())
        now = datetime.utcnow().isoformat()
        get_effect_chain_store().save(
            {
                "id": cid,
                "name": "BakeChain",
                "description": None,
                "project_id": pid,
                "effects": [
                    {
                        "id": "e1",
                        "type": "eq",
                        "name": "EQ",
                        "enabled": True,
                        "order": 0,
                        "parameters": [
                            {"name": "low_gain", "value": 0.0, "min_value": -12.0, "max_value": 12.0}
                        ],
                    }
                ],
                "created": now,
                "modified": now,
            }
        )

        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/bake.wav",
                "format": "wav",
                "project_id": pid,
                "apply_effects": True,
                "effect_chain_id": cid,
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 200, response.text
        data = response.json()
        assert data["success"] is True
        assert data["output_path"].endswith(".wav")

    def test_export_invalid_lufs_preset_returns_422(self, timeline_client, tmp_path):
        """GAP/LUFS lane: unknown lufs_preset must not silently coerce."""
        import numpy as np
        import soundfile as sf

        wav_path = tmp_path / "lufs_bad.wav"
        sf.write(str(wav_path), np.ones(800, dtype=np.float32) * 0.1, 48000)
        timeline_client.post("/api/timeline/create", json={"name": "Lufs", "sample_rate": 48000})
        timeline_client.post("/api/timeline/tracks", json={"name": "T", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.015,
                "name": "c",
            },
        )

        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "lufs_preset": "not_a_real_preset_id_12345",
            },
        )
        assert response.status_code == 422

    def test_export_lufs_neutral_skips_normalize(self, timeline_client, monkeypatch, tmp_path):
        """Neutral preset must not invoke pyloudnorm path (patch raises if called)."""
        import numpy as np
        import soundfile as sf

        wav_path = tmp_path / "neutral.wav"
        sf.write(str(wav_path), np.zeros(400, dtype=np.float32), 48000)
        timeline_client.post("/api/timeline/tracks", json={"name": "N", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.01,
                "name": "n1",
            },
        )

        def _should_not_run(*_args, **_kwargs):
            raise AssertionError("normalize_lufs_for_export must not run for neutral preset")

        monkeypatch.setattr(
            "backend.services.timeline_export_loudness.normalize_lufs_for_export",
            _should_not_run,
        )
        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "lufs_preset": "neutral",
            },
        )
        assert response.status_code == 200, response.text

    def test_export_lufs_normalization_unavailable_returns_503(self, timeline_client, monkeypatch, tmp_path):
        """When normalization is required but the LUFS path fails, return 503 (no silent wav)."""
        import numpy as np
        import soundfile as sf

        wav_path = tmp_path / "lufs503.wav"
        sf.write(str(wav_path), np.zeros(400, dtype=np.float32), 48000)
        timeline_client.post("/api/timeline/tracks", json={"name": "L", "type": "audio"})
        st = timeline_client.get("/api/timeline/state").json()
        tid = st["tracks"][0]["id"]
        timeline_client.post(
            "/api/timeline/clips",
            json={
                "track_id": tid,
                "source_path": str(wav_path),
                "start_time": 0.0,
                "duration": 0.01,
                "name": "l1",
            },
        )

        def _boom(*_args, **_kwargs):
            raise ImportError("pyloudnorm unavailable")

        monkeypatch.setattr(
            "backend.services.timeline_export_loudness.normalize_lufs_for_export",
            _boom,
        )
        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "lufs_preset": "broadcast",
            },
        )
        assert response.status_code == 503


# =============================================================================
# Undo/Redo Tests
# =============================================================================


class TestUndoRedo:
    """Tests for undo/redo functionality."""

    def test_undo_after_action(self, timeline_client):
        """Test undo reverses last action."""
        # Create timeline
        timeline_client.post("/api/timeline/create", json={"name": "Original"})

        # Add a track (creates undo state)
        timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "New Track"},
        )

        # Verify track exists
        state = timeline_client.get("/api/timeline/state").json()
        assert len(state["tracks"]) == 1

        # Undo
        response = timeline_client.post("/api/timeline/undo")
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True

        # Verify track is removed
        state = timeline_client.get("/api/timeline/state").json()
        assert len(state["tracks"]) == 0

    def test_redo_after_undo(self, timeline_client):
        """Test redo restores undone action."""
        # Create timeline with track
        timeline_client.post("/api/timeline/create", json={"name": "Test"})
        timeline_client.post(
            "/api/timeline/tracks",
            json={"name": "Track 1"},
        )

        # Undo
        timeline_client.post("/api/timeline/undo")

        # Redo
        response = timeline_client.post("/api/timeline/redo")
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True

        # Verify track is restored
        state = timeline_client.get("/api/timeline/state").json()
        assert len(state["tracks"]) == 1

    def test_undo_empty_stack(self, timeline_client):
        """Test undo with empty stack."""
        response = timeline_client.post("/api/timeline/undo")
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is False

    def test_redo_empty_stack(self, timeline_client):
        """Test redo with empty stack."""
        response = timeline_client.post("/api/timeline/redo")
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is False

    def test_get_undo_redo_state(self, timeline_client):
        """Test GET /api/timeline/undo-redo-state."""
        # Initially should have no undo/redo
        response = timeline_client.get("/api/timeline/undo-redo-state")
        assert response.status_code == 200
        data = response.json()
        assert data["can_undo"] is False
        assert data["can_redo"] is False

        # Create timeline first (establishes initial state)
        timeline_client.post("/api/timeline/create", json={"name": "Test"})

        # Add first track (saves pre-track state to undo stack)
        timeline_client.post("/api/timeline/tracks", json={"name": "Track 1"})

        # Add second track (saves pre-second-track state to undo stack)
        timeline_client.post("/api/timeline/tracks", json={"name": "Track 2"})

        # Now should have undo available (2 items in undo stack)
        response = timeline_client.get("/api/timeline/undo-redo-state")
        data = response.json()
        assert data["can_undo"] is True
        assert data["can_redo"] is False

        # Undo once (removes Track 2)
        timeline_client.post("/api/timeline/undo")

        # Should still have undo (1 item left) and now have redo available
        response = timeline_client.get("/api/timeline/undo-redo-state")
        data = response.json()
        assert data["can_undo"] is True  # Still has 1 undo
        assert data["can_redo"] is True  # Has 1 redo


class TestTimelineDurabilityHardening:
    """Session scoping, concurrency conflict surface, lazy DB (no lifespan)."""

    def test_session_scoped_tracks_isolated(self, timeline_client):
        """Different session_id query values do not share tracks."""
        r1 = timeline_client.post(
            "/api/timeline/tracks?session_id=proj-alpha",
            json={"name": "A", "type": "audio"},
        )
        assert r1.status_code == 200
        r2 = timeline_client.post(
            "/api/timeline/tracks?session_id=proj-beta",
            json={"name": "B", "type": "audio"},
        )
        assert r2.status_code == 200
        sa = timeline_client.get("/api/timeline/state?session_id=proj-alpha").json()
        sb = timeline_client.get("/api/timeline/state?session_id=proj-beta").json()
        assert len(sa["tracks"]) == 1
        assert len(sb["tracks"]) == 1
        assert sa["tracks"][0]["name"] == "A"
        assert sb["tracks"][0]["name"] == "B"

    def test_concurrent_write_conflict_returns_409(self, timeline_client, monkeypatch):
        """409 when persist uses a stale base revision (simulated via hydrate skew)."""
        from backend.api.routes import timeline as tl_mod
        from backend.project.timeline.session_repository import DEFAULT_SESSION_ID

        real = tl_mod._hydrate

        async def always_base_zero(sid: str = DEFAULT_SESSION_ID):
            s, u, r, _br = await real(sid)
            return s, u, r, 0

        monkeypatch.setattr(tl_mod, "_hydrate", always_base_zero)
        r1 = timeline_client.post("/api/timeline/tracks", json={"name": "T1", "type": "audio"})
        assert r1.status_code == 200
        r2 = timeline_client.post("/api/timeline/tracks", json={"name": "T2", "type": "audio"})
        assert r2.status_code == 409
        body = r2.json()
        assert body["detail"]["code"] == "TIMELINE_CONFLICT"

    def test_testclient_without_lifespan_lazy_connects_timeline_state(self, timeline_client):
        """Security-style TestClient still reads timeline (session_repository lazy-connect)."""
        r = timeline_client.get("/api/timeline/state")
        assert r.status_code == 200
        assert "tracks" in r.json()
