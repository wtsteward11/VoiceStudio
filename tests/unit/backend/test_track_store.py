"""
Unit tests for TrackStore (SQLite authority + legacy JSON import).
"""

from __future__ import annotations

import asyncio
import json
import shutil
import tempfile
from pathlib import Path

from backend.infrastructure.adapters.database import (
    close_database_adapter,
    get_database_adapter,
    reset_database_adapter_singleton,
)
from backend.infrastructure.migrations.initial_schema import run_migrations
from backend.project.tracks.track_store import TrackStore, reset_track_store


class TestTrackStore:
    """Tests for TrackStore class."""

    def setup_method(self) -> None:
        self.temp_dir = tempfile.mkdtemp()

        async def _setup() -> None:
            await close_database_adapter()
            reset_database_adapter_singleton()
            reset_track_store()
            dbp = f"sqlite:///{Path(self.temp_dir) / 'tracks_test.db'}"
            await run_migrations(db_path=dbp)
            db = get_database_adapter(dbp)
            await db.connect()

        asyncio.run(_setup())
        self.store = TrackStore(projects_dir=self.temp_dir)
        self.project_id = "test_project"

    def teardown_method(self) -> None:
        reset_track_store()
        asyncio.run(close_database_adapter())
        reset_database_adapter_singleton()
        asyncio.set_event_loop(asyncio.new_event_loop())
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_initialization(self) -> None:
        """Test store initializes correctly."""
        store = TrackStore()
        assert store is not None

    def test_save_track(self) -> None:
        """Test saving a track."""
        track_data = {
            "id": "track_1",
            "name": "Test Track",
            "clips": [],
        }

        result = self.store.save_track(self.project_id, track_data)

        assert result == "track_1"

    def test_get_track(self) -> None:
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

    def test_get_track_not_found(self) -> None:
        """Test getting non-existent track returns None."""
        result = self.store.get_track(self.project_id, "nonexistent")

        assert result is None

    def test_list_tracks(self) -> None:
        """Test listing all tracks for a project."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Track 1"})
        self.store.save_track(self.project_id, {"id": "t2", "name": "Track 2"})
        self.store.save_track(self.project_id, {"id": "t3", "name": "Track 3"})

        tracks = self.store.list_tracks(self.project_id)

        assert len(tracks) == 3

    def test_list_tracks_empty(self) -> None:
        """Test listing tracks when none exist."""
        tracks = self.store.list_tracks("empty_project")

        assert tracks == []

    def test_update_track(self) -> None:
        """Test updating an existing track."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Original"})

        result = self.store.update_track(self.project_id, "t1", {"name": "Updated"})

        assert result is not None
        assert result.get("name") == "Updated"

    def test_update_track_not_found(self) -> None:
        """Test updating non-existent track."""
        result = self.store.update_track(self.project_id, "nonexistent", {"name": "New"})

        assert result is None

    def test_delete_track(self) -> None:
        """Test deleting a track."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "To Delete"})

        result = self.store.delete_track(self.project_id, "t1")

        assert result is True
        assert self.store.get_track(self.project_id, "t1") is None

    def test_delete_track_not_found(self) -> None:
        """Test deleting non-existent track."""
        result = self.store.delete_track(self.project_id, "nonexistent")

        assert result is False

    def test_project_isolation(self) -> None:
        """Test tracks are isolated by project."""
        self.store.save_track("project_a", {"id": "t1", "name": "Track A"})
        self.store.save_track("project_b", {"id": "t1", "name": "Track B"})

        track_a = self.store.get_track("project_a", "t1")
        track_b = self.store.get_track("project_b", "t1")

        assert track_a.get("name") == "Track A"
        assert track_b.get("name") == "Track B"

    def test_track_with_clips(self) -> None:
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

    def test_update_adds_clip(self) -> None:
        """Test updating track to add a clip."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Track", "clips": []})

        new_clips = [{"id": "clip_new", "audio_id": "audio_new"}]
        result = self.store.update_track(self.project_id, "t1", {"clips": new_clips})

        assert len(result.get("clips", [])) == 1

    def test_list_tracks_multiple_projects(self) -> None:
        """Test listing tracks doesn't mix projects."""
        self.store.save_track("project_x", {"id": "t1", "name": "X Track 1"})
        self.store.save_track("project_x", {"id": "t2", "name": "X Track 2"})
        self.store.save_track("project_y", {"id": "t1", "name": "Y Track 1"})

        x_tracks = self.store.list_tracks("project_x")
        y_tracks = self.store.list_tracks("project_y")

        assert len(x_tracks) == 2
        assert len(y_tracks) == 1

    def test_save_overwrites_existing(self) -> None:
        """Test saving with same ID overwrites."""
        self.store.save_track(self.project_id, {"id": "t1", "name": "Original"})
        self.store.save_track(self.project_id, {"id": "t1", "name": "Overwritten"})

        result = self.store.get_track(self.project_id, "t1")

        assert result.get("name") == "Overwritten"

    def test_legacy_json_import_into_sqlite(self) -> None:
        """Strategy A: legacy tracks/*.json is imported once then served from SQLite."""
        pid = "legacy_proj"
        tdir = Path(self.temp_dir) / pid / "tracks"
        tdir.mkdir(parents=True)
        (tdir / "disk_only.json").write_text(
            json.dumps({"id": "disk_only", "name": "From Disk", "track_number": 1}),
            encoding="utf-8",
        )

        tracks = self.store.list_tracks(pid)
        assert len(tracks) == 1
        assert tracks[0]["name"] == "From Disk"

        loaded = self.store.get_track(pid, "disk_only")
        assert loaded is not None
        assert loaded["name"] == "From Disk"
