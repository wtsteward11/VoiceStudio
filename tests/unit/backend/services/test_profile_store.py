"""
Unit tests for ProfileStore (Phase 21.3).

Tests persistent voice profile storage.
"""

import asyncio
import shutil
import tempfile
from pathlib import Path

import pytest

from backend.infrastructure.adapters.database import (
    close_database_adapter,
    get_database_adapter,
    reset_database_adapter_singleton,
)
from backend.infrastructure.migrations.initial_schema import run_migrations
from backend.project.tracks.track_store import reset_track_store


class TestProfileStore:
    """Tests for ProfileStore."""

    @pytest.fixture
    def temp_dir(self):
        """Create a temporary directory for test storage."""
        path = tempfile.mkdtemp()
        yield path
        shutil.rmtree(path, ignore_errors=True)

    def test_import(self):
        """Test that ProfileStore can be imported."""
        from backend.services.profile_store import ProfileStore

        assert ProfileStore is not None

    def test_create_store(self, temp_dir):
        """Test creating a profile store."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)
        assert store is not None

    def test_save_profile(self, temp_dir):
        """Test saving a profile."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        profile = {
            "name": "Test Voice",
            "language": "en",
            "quality_score": 4.5,
            "tags": ["test", "demo"],
        }

        profile_id = store.save(profile)

        assert profile_id is not None
        assert len(profile_id) > 0

    def test_get_profile(self, temp_dir):
        """Test retrieving a saved profile."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        profile = {
            "name": "Test Voice",
            "language": "en",
        }

        profile_id = store.save(profile)
        retrieved = store.get(profile_id)

        assert retrieved is not None
        assert retrieved["name"] == "Test Voice"
        assert retrieved["language"] == "en"

    def test_get_nonexistent_profile(self, temp_dir):
        """Test getting a profile that doesn't exist."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        result = store.get("nonexistent-id")

        assert result is None

    def test_delete_profile(self, temp_dir):
        """Test deleting a profile."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        profile = {"name": "To Delete", "language": "en"}
        profile_id = store.save(profile)

        assert store.get(profile_id) is not None

        result = store.delete(profile_id)

        assert result is True
        assert store.get(profile_id) is None

    def test_list_profiles(self, temp_dir):
        """Test listing profiles."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        # Save multiple profiles
        for i in range(5):
            store.save({"name": f"Profile {i}", "language": "en"})

        profiles = store.list_profiles(limit=10)

        assert len(profiles) == 5

    def test_list_profiles_with_filter(self, temp_dir):
        """Test listing profiles with language filter."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        store.save({"name": "English Voice", "language": "en"})
        store.save({"name": "Spanish Voice", "language": "es"})
        store.save({"name": "French Voice", "language": "fr"})

        en_profiles = store.list_profiles(language="en")

        assert len(en_profiles) == 1
        assert en_profiles[0]["language"] == "en"

    def test_count(self, temp_dir):
        """Test counting profiles."""
        from backend.services.profile_store import ProfileStore

        store = ProfileStore(base_dir=temp_dir)

        assert store.count() == 0

        store.save({"name": "Test", "language": "en"})

        assert store.count() == 1

    def test_persistence(self, temp_dir):
        """Test that profiles persist across store instances."""
        from backend.services.profile_store import ProfileStore

        # Create and save with first instance
        store1 = ProfileStore(base_dir=temp_dir)
        profile_id = store1.save({"name": "Persistent", "language": "en"})

        # Create new instance and verify
        store2 = ProfileStore(base_dir=temp_dir)
        retrieved = store2.get(profile_id)

        assert retrieved is not None
        assert retrieved["name"] == "Persistent"


class TestTrackStore:
    """Tests for TrackStore (SQLite-backed; requires connected DatabaseAdapter)."""

    def setup_method(self) -> None:
        self._temp_db_dir = tempfile.mkdtemp()

        async def _setup() -> None:
            await close_database_adapter()
            reset_database_adapter_singleton()
            reset_track_store()
            dbp = f"sqlite:///{Path(self._temp_db_dir) / 'profile_store_track_test.db'}"
            await run_migrations(db_path=dbp)
            db = get_database_adapter(dbp)
            await db.connect()

        asyncio.run(_setup())

    def teardown_method(self) -> None:
        reset_track_store()
        asyncio.run(close_database_adapter())
        reset_database_adapter_singleton()
        asyncio.set_event_loop(asyncio.new_event_loop())
        shutil.rmtree(self._temp_db_dir, ignore_errors=True)

    def test_import(self) -> None:
        """Test that TrackStore can be imported."""
        from backend.services.track_store import TrackStore

        assert TrackStore is not None

    def test_create_store(self) -> None:
        """Test creating a track store."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)
        assert store is not None

    def test_save_track(self) -> None:
        """Test saving a track."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)

        track = {
            "name": "Voice Track 1",
            "type": "voice",
            "track_number": 1,
        }

        track_id = store.save_track("project-1", track)

        assert track_id is not None

    def test_get_track(self) -> None:
        """Test retrieving a track."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)

        track = {"name": "Test Track", "type": "audio"}
        track_id = store.save_track("project-1", track)

        retrieved = store.get_track("project-1", track_id)

        assert retrieved is not None
        assert retrieved["name"] == "Test Track"

    def test_list_tracks(self) -> None:
        """Test listing tracks for a project."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)

        store.save_track("project-1", {"name": "Track 1", "track_number": 1})
        store.save_track("project-1", {"name": "Track 2", "track_number": 2})

        tracks = store.list_tracks("project-1")

        assert len(tracks) == 2

    def test_delete_track(self) -> None:
        """Test deleting a track."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)

        track_id = store.save_track("project-1", {"name": "To Delete"})

        result = store.delete_track("project-1", track_id)

        assert result is True
        assert store.get_track("project-1", track_id) is None

    def test_disconnected_adapter_save_raises(self) -> None:
        """Regression: TrackStore requires a connected global DatabaseAdapter."""
        from backend.services.track_store import TrackStore

        async def _leave_disconnected() -> None:
            await close_database_adapter()
            reset_database_adapter_singleton()
            reset_track_store()
            _ = get_database_adapter(
                f"sqlite:///{Path(self._temp_db_dir) / 'disconnected.db'}",
            )

        asyncio.run(_leave_disconnected())
        store = TrackStore(projects_dir=self._temp_db_dir)
        with pytest.raises(RuntimeError, match="Database not connected"):
            store.save_track("project-1", {"name": "x"})

    def test_connected_store_round_trips(self) -> None:
        """Regression: connected adapter supports save → get → delete → miss."""
        from backend.services.track_store import TrackStore

        store = TrackStore(projects_dir=self._temp_db_dir)
        pid = "project-roundtrip"
        tid = store.save_track(pid, {"name": "RoundTrip", "type": "audio"})
        got = store.get_track(pid, tid)
        assert got is not None
        assert got["name"] == "RoundTrip"
        assert store.delete_track(pid, tid) is True
        assert store.get_track(pid, tid) is None


class TestArtifactRefCounter:
    """Tests for ArtifactRefCounter."""

    @pytest.fixture
    def temp_dir(self):
        """Create a temporary directory."""
        path = tempfile.mkdtemp()
        yield path
        shutil.rmtree(path, ignore_errors=True)

    def test_import(self):
        """Test that ArtifactRefCounter can be imported."""
        from backend.services.artifact_ref_counter import ArtifactRefCounter

        assert ArtifactRefCounter is not None

    def test_increment(self, temp_dir):
        """Test incrementing reference count."""
        from backend.services.artifact_ref_counter import ArtifactRefCounter

        counter = ArtifactRefCounter(data_dir=temp_dir)

        count = counter.increment("artifact-1", "clip-1")

        assert count == 1

    def test_decrement(self, temp_dir):
        """Test decrementing reference count."""
        from backend.services.artifact_ref_counter import ArtifactRefCounter

        counter = ArtifactRefCounter(data_dir=temp_dir)

        counter.increment("artifact-1", "clip-1")
        counter.increment("artifact-1", "clip-2")

        count = counter.decrement("artifact-1", "clip-1")

        assert count == 1

    def test_get_count(self, temp_dir):
        """Test getting reference count."""
        from backend.services.artifact_ref_counter import ArtifactRefCounter

        counter = ArtifactRefCounter(data_dir=temp_dir)

        counter.increment("artifact-1", "clip-1")
        counter.increment("artifact-1", "clip-2")

        count = counter.get_count("artifact-1")

        assert count == 2

    def test_get_zero_ref_artifacts(self, temp_dir):
        """Test getting artifacts with zero references."""
        from backend.services.artifact_ref_counter import ArtifactRefCounter

        counter = ArtifactRefCounter(data_dir=temp_dir)

        # Add and remove all references
        counter.increment("artifact-1", "clip-1")
        counter.decrement("artifact-1", "clip-1")

        # The entry should now be removed (no zero-ref tracking)
        zero_refs = counter.get_zero_ref_artifacts()

        # Should be empty since we delete entries with no refs
        assert isinstance(zero_refs, list)
