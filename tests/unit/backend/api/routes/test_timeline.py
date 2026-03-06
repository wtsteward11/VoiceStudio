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
def reset_timeline_state():
    """Reset timeline state before each test."""
    # Reset the module-level state before each test
    from backend.api.routes import timeline

    timeline._timeline_state = None
    timeline._undo_stack = []
    timeline._redo_stack = []
    yield
    # Clean up after test
    timeline._timeline_state = None
    timeline._undo_stack = []
    timeline._redo_stack = []


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

    def test_export_timeline(self, timeline_client):
        """Test POST /api/timeline/export exports timeline."""
        response = timeline_client.post(
            "/api/timeline/export",
            json={
                "output_path": "/output/timeline.wav",
                "format": "wav",
                "sample_rate": 48000,
            },
        )
        assert response.status_code == 200
        data = response.json()
        assert data["success"] is True
        # Path may be sanitized to safe dir (relative/repo paths refused)
        assert isinstance(data["output_path"], str)
        assert data["output_path"].endswith(".wav")
        assert "duration" in data


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
