"""
Profile storage service: path resolution for profile data (M8).

Single source of truth for profile directory and reference audio paths.
Sanitizes profile_id to prevent path traversal. Uses get_path("profiles") for
storage root (outside repo).
"""

from __future__ import annotations

import re
from pathlib import Path

from backend.services.path_service import PathService

# Fallback filenames for reference audio (checked in order)
_REFERENCE_AUDIO_CANDIDATES = ("reference_audio.wav", "reference.wav", "audio.wav")

# Allow alnum, underscore, hyphen only
_PROFILE_ID_PATTERN = re.compile(r"^[a-zA-Z0-9_-]+$")


def _sanitize_profile_id(profile_id: str) -> str:
    """Validate profile_id; raise ValueError if invalid."""
    if not profile_id or not isinstance(profile_id, str):
        raise ValueError("profile_id must be a non-empty string")
    if ".." in profile_id or "/" in profile_id or "\\" in profile_id:
        raise ValueError("profile_id must not contain path traversal or separators")
    if not _PROFILE_ID_PATTERN.match(profile_id):
        raise ValueError(
            "profile_id must contain only letters, digits, underscore, or hyphen"
        )
    return profile_id


def get_profile_dir(profile_id: str) -> Path:
    """
    Return the directory for a profile's data.

    Args:
        profile_id: Profile ID (directory name under profiles/)

    Returns:
        Path to profile directory (may not exist)

    Raises:
        ValueError: If profile_id is invalid
    """
    safe_id = _sanitize_profile_id(profile_id)
    return PathService.get_profiles_dir() / safe_id


def get_reference_audio_path(profile_id: str) -> Path:
    """
    Resolve the path to a profile's reference audio file.

    Checks fallback filenames (reference_audio.wav, reference.wav, audio.wav).
    Returns the first existing file, or the default path if none exist.

    Args:
        profile_id: Profile ID (directory name under profiles/)

    Returns:
        Path to the reference audio file. Caller should check path.exists().

    Raises:
        ValueError: If profile_id is invalid
    """
    profile_dir = get_profile_dir(profile_id)
    for name in _REFERENCE_AUDIO_CANDIDATES:
        candidate = profile_dir / name
        if candidate.exists():
            return candidate
    return profile_dir / _REFERENCE_AUDIO_CANDIDATES[0]


def exists_reference_audio(profile_id: str) -> bool:
    """Return True if any reference audio file exists for the profile."""
    return get_reference_audio_path(profile_id).exists()


def ensure_profile_dir(profile_id: str) -> Path:
    """
    Create profile directory if needed; return path.

    Raises:
        ValueError: If profile_id is invalid
    """
    profile_dir = get_profile_dir(profile_id)
    profile_dir.mkdir(parents=True, exist_ok=True)
    return profile_dir


_storage: ProfileStorageService | None = None


class ProfileStorageService:
    """Facade for profile path resolution (singleton)."""

    def get_profile_dir(self, profile_id: str) -> Path:
        return get_profile_dir(profile_id)

    def get_reference_audio_path(self, profile_id: str) -> Path:
        return get_reference_audio_path(profile_id)

    def exists_reference_audio(self, profile_id: str) -> bool:
        return exists_reference_audio(profile_id)

    def ensure_profile_dir(self, profile_id: str) -> Path:
        return ensure_profile_dir(profile_id)


def get_profile_storage() -> ProfileStorageService:
    """Get the ProfileStorageService singleton."""
    global _storage
    if _storage is None:
        _storage = ProfileStorageService()
    return _storage
