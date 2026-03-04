"""
Unit tests for ProfileStorageService (M8).

Verifies profile_id sanitization and path resolution.
"""

from pathlib import Path

import pytest

from backend.services.profile_storage_service import (
    ensure_profile_dir,
    exists_reference_audio,
    get_profile_dir,
    get_profile_storage,
    get_reference_audio_path,
)


class TestProfileStorageServiceSanitization:
    """Sanitization must reject invalid profile_ids."""

    def test_sanitize_rejects_traversal(self):
        """get_profile_dir('../../etc') raises ValueError."""
        with pytest.raises(ValueError, match="path traversal"):
            get_profile_dir("../../etc")

    def test_sanitize_rejects_special_chars(self):
        """get_profile_dir('foo/bar') raises ValueError."""
        with pytest.raises(ValueError):
            get_profile_dir("foo/bar")

    def test_sanitize_rejects_backslash(self):
        """get_profile_dir('foo\\bar') raises ValueError."""
        with pytest.raises(ValueError):
            get_profile_dir("foo\\bar")

    def test_sanitize_allows_alnum_underscore_dash(self):
        """get_profile_dir('abc_123-X') returns path under profiles root."""
        result = get_profile_dir("abc_123-X")
        profiles_root = result.parent
        assert result.name == "abc_123-X"
        assert "profiles" in str(profiles_root).lower() or "profiles" in str(profiles_root)

    def test_sanitize_rejects_empty(self):
        """get_profile_dir('') raises ValueError."""
        with pytest.raises(ValueError):
            get_profile_dir("")


class TestProfileStorageServicePaths:
    """Path resolution must be under profiles root."""

    def test_reference_path_under_profiles_root(self):
        """get_reference_audio_path('valid-id') returns Path under profiles root."""
        result = get_reference_audio_path("valid-id")
        assert isinstance(result, Path)
        # Path should end with one of the candidate filenames
        assert result.name in ("reference_audio.wav", "reference.wav", "audio.wav")
        # Parent should be the profile dir
        assert result.parent.name == "valid-id"

    def test_get_profile_storage_singleton(self):
        """get_profile_storage returns same instance."""
        a = get_profile_storage()
        b = get_profile_storage()
        assert a is b

    def test_ensure_profile_dir_returns_path(self):
        """ensure_profile_dir returns Path consistent with get_profile_dir."""
        result = ensure_profile_dir("test_profile_ensure_123")
        assert result.name == "test_profile_ensure_123"
        assert result == get_profile_dir("test_profile_ensure_123")


class TestProfileStorageServiceExists:
    """exists_reference_audio behavior."""

    def test_exists_returns_bool(self):
        """exists_reference_audio returns bool."""
        result = exists_reference_audio("nonexistent_profile_xyz_123")
        assert isinstance(result, bool)
