"""
Persistent Voice Profile Store for VoiceStudio (Phase 13.1.1-13.1.3)

Migrates voice profiles from in-memory storage to durable disk-backed
JsonFileStore. Profiles persist across restarts with fast index lookup.
"""

from __future__ import annotations

import json
import logging
import os
import threading
import time
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class ProfileStore:
    """
    Disk-backed voice profile storage.

    Stores profiles as individual JSON files organized by ID.
    Maintains an in-memory index for fast lookups without
    loading all profile data.

    Storage layout:
        ~/.voicestudio/profiles/
        ├── index.json          # Fast lookup index
        ├── {profile_id}/
        │   ├── profile.json    # Profile metadata
        │   └── reference/      # Reference audio files
    """

    def __init__(
        self,
        base_dir: str | None = None,
        max_profiles: int = 5000,
    ):
        self._base_dir = Path(
            base_dir
            or os.getenv("VOICESTUDIO_PROFILES_PATH", "")
            or Path.home() / ".voicestudio" / "profiles"
        )
        self._base_dir.mkdir(parents=True, exist_ok=True)
        self._max_profiles = max_profiles
        self._index: dict[str, dict[str, Any]] = {}
        self._lock = threading.RLock()
        self._load_index()

    def get(self, profile_id: str) -> dict[str, Any] | None:
        """Get a profile by ID."""
        with self._lock:
            if profile_id not in self._index:
                return None

            profile_dir = self._base_dir / profile_id
            profile_file = profile_dir / "profile.json"

            if not profile_file.exists():
                return self._index.get(profile_id)

            try:
                with open(profile_file, encoding="utf-8") as f:
                    return json.load(f)
            except (json.JSONDecodeError, OSError) as exc:
                logger.warning(f"Failed to load profile {profile_id}: {exc}")
                return self._index.get(profile_id)

    def save(self, profile: dict[str, Any]) -> str:
        """
        Save a voice profile.

        Args:
            profile: Profile dictionary (must include 'id').

        Returns:
            Profile ID.
        """
        profile_id = profile.get("id", "")
        if not profile_id:
            import uuid

            profile_id = f"prof-{uuid.uuid4().hex[:8]}"
            profile["id"] = profile_id

        with self._lock:
            # Create profile directory
            profile_dir = self._base_dir / profile_id
            profile_dir.mkdir(parents=True, exist_ok=True)

            # Write profile data
            profile_file = profile_dir / "profile.json"
            tmp_file = profile_file.with_suffix(".tmp")
            try:
                with open(tmp_file, "w", encoding="utf-8") as f:
                    json.dump(profile, f, indent=2, ensure_ascii=False)
                os.replace(str(tmp_file), str(profile_file))
            except OSError as exc:
                logger.error(f"Failed to save profile {profile_id}: {exc}")
                raise

            # Update index
            self._index[profile_id] = {
                "id": profile_id,
                "name": profile.get("name", ""),
                "language": profile.get("language", "en"),
                "quality_score": profile.get("quality_score", 0.0),
                "tags": profile.get("tags", []),
                "updated_at": time.time(),
            }
            self._save_index()

        logger.info(f"Profile saved: {profile_id}")
        return profile_id

    def delete(self, profile_id: str) -> bool:
        """Delete a profile."""
        with self._lock:
            if profile_id not in self._index:
                return False

            profile_dir = self._base_dir / profile_id
            if profile_dir.exists():
                import shutil

                try:
                    shutil.rmtree(profile_dir)
                except OSError as exc:
                    logger.error(f"Failed to delete profile directory {profile_id}: {exc}")

            del self._index[profile_id]
            self._save_index()

        logger.info(f"Profile deleted: {profile_id}")
        return True

    def list_profiles(
        self,
        limit: int = 100,
        offset: int = 0,
        language: str | None = None,
        search: str | None = None,
    ) -> list[dict[str, Any]]:
        """List profiles from the index with optional filtering."""
        with self._lock:
            profiles = list(self._index.values())

        # Filter
        if language:
            profiles = [p for p in profiles if p.get("language") == language]
        if search:
            search_lower = search.lower()
            profiles = [
                p
                for p in profiles
                if search_lower in p.get("name", "").lower()
                or search_lower in str(p.get("tags", "")).lower()
            ]

        # Sort by name
        profiles.sort(key=lambda p: p.get("name", "").lower())

        return profiles[offset : offset + limit]

    def count(self) -> int:
        """Get total number of profiles."""
        return len(self._index)

    def _load_index(self) -> None:
        """Load the profile index from disk."""
        index_file = self._base_dir / "index.json"
        if index_file.exists():
            try:
                with open(index_file, encoding="utf-8") as f:
                    self._index = json.load(f)
                logger.info(f"Loaded profile index: {len(self._index)} profiles")
                return
            except (json.JSONDecodeError, OSError) as exc:
                logger.warning(f"Failed to load profile index: {exc}")

        # Rebuild index from directories
        self._rebuild_index()

    def _rebuild_index(self) -> None:
        """Rebuild the index from profile directories."""
        self._index.clear()
        for item in self._base_dir.iterdir():
            if item.is_dir() and (item / "profile.json").exists():
                try:
                    with open(item / "profile.json", encoding="utf-8") as f:
                        profile = json.load(f)
                    self._index[item.name] = {
                        "id": item.name,
                        "name": profile.get("name", ""),
                        "language": profile.get("language", "en"),
                        "quality_score": profile.get("quality_score", 0.0),
                        "tags": profile.get("tags", []),
                    }
                except (json.JSONDecodeError, OSError):
                    continue

        self._save_index()
        logger.info(f"Rebuilt profile index: {len(self._index)} profiles")

    def _save_index(self) -> None:
        """Save the index to disk atomically."""
        index_file = self._base_dir / "index.json"
        tmp_file = index_file.with_suffix(".tmp")
        try:
            with open(tmp_file, "w", encoding="utf-8") as f:
                json.dump(self._index, f, indent=2)
            os.replace(str(tmp_file), str(index_file))
        except OSError as exc:
            logger.warning(f"Failed to save profile index: {exc}")


# Singleton
_store: ProfileStore | None = None


def get_profile_store() -> ProfileStore:
    """Get the global profile store singleton."""
    global _store
    if _store is None:
        _store = ProfileStore()
    return _store
