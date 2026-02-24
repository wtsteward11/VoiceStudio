"""
Unit tests for TrackStore (Phase 21.3)
"""

import shutil
import tempfile

from backend.services.track_store import TrackStore


class TestTrackStore:
    """Tests for TrackStore class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.temp_dir = tempfile.mkdtemp()
        self.store = TrackStore(projects_dir=self.temp_dir)
        self.project_id = "test_project"

    def teardown_method(self):
        """Clean up test fixtures."""
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_initialization(self):
        """Test store initializes correctly."""
        store = TrackStore()
        assert store is not None

    def test_save_track(self):
        """Test saving a track."""
        track_data = {
            "id": "track_1",
            "name": "Test Track",
            "clips": [],
        }

        result = self.store.save_track(self.project_id, track_data)

        # save_track returns the track_id string, not the track dict
        assert result == "track_1"

    def test_get_track(self):
        """Test getting a track by ID."""
        track_data = {
            "id": "track_2",
            "name": "Another Track",
            "clips": [],
        }
        self.store.save_track(self.project_id, track_data)

        result = self.store.get_track(self.project_id, "track_2")

        assert result is not None
        assert result.get("name") == "Another Track"

    def test_get_track_not_found(self):
        """Test getting non-existent track returns None."""
        result = self.store.get_track(self.project_id, "nonexistent")

        assert result is None

    def test_list_tracks(self):
        """Test listing all tracks for a project."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Track 1"})
        self.store.save_track(self.project_id, {"id": "t2", "name": "Track 2"})
        self.store.save_track(self.project_id, {"id": "t3", "name": "Track 3"})

        tracks = self.store.list_tracks(self.project_id)

        assert len(tracks) == 3

    def test_list_tracks_empty(self):
        """Test listing tracks when none exist."""
        tracks = self.store.list_tracks("empty_project")

        assert tracks == []

    def test_update_track(self):
        """Test updating an existing track."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Original"})

        result = self.store.update_track(self.project_id, "t1", {"name": "Updated"})

        assert result is not None
        assert result.get("name") == "Updated"

    def test_update_track_not_found(self):
        """Test updating non-existent track."""
        result = self.store.update_track(self.project_id, "nonexistent", {"name": "New"})

        assert result is None

    def test_delete_track(self):
        """Test deleting a track."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "To Delete"})

        result = self.store.delete_track(self.project_id, "t1")

        assert result is True
        assert self.store.get_track(self.project_id, "t1") is None

    def test_delete_track_not_found(self):
        """Test deleting non-existent track."""
        result = self.store.delete_track(self.project_id, "nonexistent")

        assert result is False

    def test_project_isolation(self):
        """Test tracks are isolated by project."""
        self.store.save_track("project_a", {"id": "t1", "name": "Track A"})
        self.store.save_track("project_b", {"id": "t1", "name": "Track B"})

        track_a = self.store.get_track("project_a", "t1")
        track_b = self.store.get_track("project_b", "t1")

        assert track_a.get("name") == "Track A"
        assert track_b.get("name") == "Track B"

    def test_track_with_clips(self):
        """Test track with clips."""
        track_data = {
            "id": "track_clips",
            "name": "Track with Clips",
            "clips": [
                {"id": "clip_1", "audio_id": "audio_1", "start_ms": 0, "end_ms": 1000},
                {"id": "clip_2", "audio_id": "audio_2", "start_ms": 1000, "end_ms": 2000},
            ],
        }

        self.store.save_track(self.project_id, track_data)
        result = self.store.get_track(self.project_id, "track_clips")

        assert len(result.get("clips", [])) == 2
        assert result["clips"][0]["id"] == "clip_1"

    def test_update_adds_clip(self):
        """Test updating track to add a clip."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Track", "clips": []})

        new_clips = [{"id": "clip_new", "audio_id": "audio_new"}]
        result = self.store.update_track(self.project_id, "t1", {"clips": new_clips})

        assert len(result.get("clips", [])) == 1

    def test_list_tracks_multiple_projects(self):
        """Test listing tracks doesn't mix projects."""
        self.store.save_track("project_x", {"id": "t1", "name": "X Track 1"})
        self.store.save_track("project_x", {"id": "t2", "name": "X Track 2"})
        self.store.save_track("project_y", {"id": "t1", "name": "Y Track 1"})

        x_tracks = self.store.list_tracks("project_x")
        y_tracks = self.store.list_tracks("project_y")

        assert len(x_tracks) == 2
        assert len(y_tracks) == 1

    def test_save_overwrites_existing(self):
        """Test saving with same ID overwrites."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Original"})
        self.store.save_track(self.project_id, {"id": "t1", "name": "Overwritten"})

        result = self.store.get_track(self.project_id, "t1")

        assert result.get("name") == "Overwritten"
