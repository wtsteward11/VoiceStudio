"""
Unit tests for ProfileStore (Phase 21.3)
"""

import shutil
import tempfile

from backend.services.profile_store import ProfileStore


class TestProfileStore:
    """Tests for ProfileStore class."""

    def setup_method(self):
        """Set up test fixtures."""
        self.temp_dir = tempfile.mkdtemp()
        self.store = ProfileStore(base_dir=self.temp_dir)

    def teardown_method(self):
        """Clean up test fixtures."""
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_initialization(self):
        """Test store initializes correctly."""
        store = ProfileStore()
        assert store is not None

    def test_save(self):
        """Test saving a profile."""
        profile_data = {
            "id": "profile_1",
            "name": "Test Voice",
            "voice_id": "voice_123",
            "engine": "xtts_v2",
        }

        result = self.store.save(profile_data)

        # save() returns the profile_id string, not the profile dict
        assert result == "profile_1"

    def test_get(self):
        """Test getting a profile by ID."""
        profile_data = {
            "id": "profile_2",
            "name": "Another Voice",
            "voice_id": "voice_456",
        }
        self.store.save(profile_data)

        result = self.store.get("profile_2")

        assert result is not None
        assert result.get("name") == "Another Voice"

    def test_get_not_found(self):
        """Test getting non-existent profile returns None."""
        result = self.store.get("nonexistent")

        assert result is None

    def test_list_profiles(self):
        """Test listing all profiles."""
        self.store.save({"id": "p1", "name": "Voice 1"})
        self.store.save({"id": "p2", "name": "Voice 2"})
        self.store.save({"id": "p3", "name": "Voice 3"})

        profiles = self.store.list_profiles()

        assert len(profiles) == 3

    def test_list_profiles_empty(self):
        """Test listing profiles when none exist."""
        profiles = self.store.list_profiles()

        assert profiles == []

    def test_update_profile_via_save(self):
        """Test updating an existing profile by re-saving with same ID."""
        self.store.save({"id": "p1", "name": "Original"})

        # Update by calling save again with the same ID
        result = self.store.save({"id": "p1", "name": "Updated"})

        assert result == "p1"
        # Verify the update persisted
        updated = self.store.get("p1")
        assert updated is not None
        assert updated.get("name") == "Updated"

    def test_delete(self):
        """Test deleting a profile."""
        self.store.save({"id": "p1", "name": "To Delete"})

        result = self.store.delete("p1")

        assert result is True
        assert self.store.get("p1") is None

    def test_delete_not_found(self):
        """Test deleting non-existent profile."""
        result = self.store.delete("nonexistent")

        assert result is False

    def test_profile_isolation(self):
        """Test profiles are isolated correctly."""
        self.store.save({"id": "p1", "name": "Voice 1", "user": "user1"})
        self.store.save({"id": "p2", "name": "Voice 2", "user": "user2"})

        p1 = self.store.get("p1")
        p2 = self.store.get("p2")

        assert p1.get("user") != p2.get("user")

    def test_profile_with_metadata(self):
        """Test profile with complex metadata."""
        profile_data = {
            "id": "p_meta",
            "name": "Complex Profile",
            "metadata": {
                "created_at": "2024-01-01",
                "settings": {"pitch": 1.0, "speed": 1.2},
                "tags": ["demo", "test"],
            },
        }

        self.store.save(profile_data)
        result = self.store.get("p_meta")

        assert result.get("metadata") is not None
        assert result["metadata"]["settings"]["speed"] == 1.2

    def test_save_overwrites_existing(self):
        """Test saving with same ID overwrites."""
        self.store.save({"id": "p1", "name": "Original"})
        self.store.save({"id": "p1", "name": "Overwritten"})

        result = self.store.get("p1")

        assert result.get("name") == "Overwritten"

    def test_list_profiles_with_language_filter(self):
        """Test listing profiles with language filter criteria."""
        self.store.save({"id": "p1", "name": "Voice 1", "language": "en"})
        self.store.save({"id": "p2", "name": "Voice 2", "language": "es"})
        self.store.save({"id": "p3", "name": "Voice 3", "language": "en"})

        # Use the language parameter supported by list_profiles
        en_profiles = self.store.list_profiles(language="en")

        assert len(en_profiles) == 2
