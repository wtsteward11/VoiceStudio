"""
Profile service for creating profiles without route-to-route imports.

Extracts create_profile business logic for use by voice_cloning_wizard and other callers.
Provides resolve_reference_audio_path as single source of truth for profile reference paths.
"""

from __future__ import annotations

import logging
import shutil
import time
import uuid
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


def resolve_reference_audio_path(profile_id: str) -> Path:
    """
    Resolve the path to a profile's reference audio file.

    Delegates to ProfileStorageService (sanitizes profile_id, uses canonical
    profiles directory). Single source of truth for profile reference paths.

    Args:
        profile_id: Profile ID (directory name under profiles/)

    Returns:
        Path to the reference audio file. The file may not exist; caller should
        check path.exists() before use.

    Raises:
        ValueError: If profile_id is invalid (path traversal, invalid chars)
    """
    from backend.services.profile_storage_service import get_reference_audio_path

    return get_reference_audio_path(profile_id)


def create_profile_from_request(
    name: str,
    language: str = "en",
    emotion: str | None = None,
    tags: list[str] | None = None,
    avatar_url: str | None = None,
    description: str | None = None,
    reference_audio_source: str | Path | None = None,
) -> dict[str, Any]:
    """
    Create a new voice profile and save to store.

    When ``reference_audio_source`` is set, the file is copied to the canonical
    profile directory as ``reference_audio.wav`` **before** persist — if copy
    fails, no profile row is saved.

    Returns the created profile dict with id, name, language, etc.
    """
    from backend.project.management.profile_store import get_profile_store
    from backend.services.profile_storage_service import ensure_profile_dir

    if not name or not str(name).strip():
        raise ValueError("Profile name is required and cannot be empty")
    if not language or not str(language).strip():
        raise ValueError("Language is required and cannot be empty")

    profile_id = str(uuid.uuid4())
    tags_list = [str(t).strip() for t in tags] if tags else []

    profile_data: dict[str, Any] = {
        "id": profile_id,
        "name": str(name).strip(),
        "language": str(language).strip(),
        "emotion": str(emotion).strip() if emotion else None,
        "tags": tags_list,
        "quality_score": 0.0,
        "avatar_url": avatar_url,
        "description": description,
        "created_at": time.time(),
        "owner_user_id": "local",
    }

    if reference_audio_source is not None:
        source = Path(reference_audio_source)
        if not source.is_file():
            raise ValueError(f"reference_audio_source does not exist or is not a file: {source}")
        dest_dir = ensure_profile_dir(profile_id)
        dest = dest_dir / "reference_audio.wav"
        shutil.copy2(source, dest)
        profile_data["reference_audio_url"] = str(dest)
        profile_data["reference_audio_bound"] = True
    else:
        profile_data["reference_audio_bound"] = False

    store = get_profile_store()
    store.save(profile_data)

    profile_data["profile_id"] = profile_id  # Alias for callers expecting profile_id
    logger.info(f"Created profile: {profile_id} - {profile_data['name']}")
    return profile_data
