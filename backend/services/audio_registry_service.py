"""
Canonical audio registry service (M9).

Single spine for audio_id resolution, removal, caching, and purge.
Routes and other modules use this service instead of backend.audio.processing.*.
"""

from __future__ import annotations

import logging
from pathlib import Path

from backend.config.path_config import get_path
from backend.services.audio_artifacts.errors import ArtifactNotFoundError
from backend.services.audio_artifacts.registry_db import AudioRegistryDB

logger = logging.getLogger(__name__)

_registry_instance: AudioRegistryDB | None = None


def get_registry() -> AudioRegistryDB:
    """Return the canonical AudioRegistryDB singleton."""
    global _registry_instance
    if _registry_instance is None:
        _registry_instance = AudioRegistryDB()
    return _registry_instance


def resolve_audio_path(audio_id: str) -> str | Path | None:
    """
    Resolve audio_id to file path.

    Returns:
        Path as str or Path, or None if not found.
    """
    try:
        registry = get_registry()
        return registry.resolve_path(audio_id)
    except ArtifactNotFoundError:
        return None


def remove_audio_id(audio_id: str) -> None:
    """Remove audio_id from registry. Does not delete the file."""
    registry = get_registry()
    registry.delete(audio_id)


def ensure_cached(source_path: Path) -> Path:
    """
    Place source file into content-addressed cache; return cached path.

    Only this module imports get_audio_cache. Routes must use this service.
    """
    return _get_cache().ensure_cached(source_path)


def get_cache_dir() -> str:
    """Return cache root for diagnostics (e.g. health check)."""
    return str(_get_cache().cache_dir)


def get_cache():
    """Return the content-addressed cache instance (for deps/diagnostics)."""
    return _get_cache()


def _get_cache():
    """Lazy cache instance."""
    from backend.audio.processing.content_addressed_audio_cache import get_audio_cache

    return get_audio_cache(cache_dir=str(get_path("cache")))


def purge_old_entries(max_age_seconds: int, max_count: int) -> int:
    """
    Remove old registry entries (mapping only; does not delete files).

    Removes:
    - Entries older than max_age_seconds
    - Excess entries beyond max_count (oldest first)

    Returns:
        Number of entries removed.
    """
    import time

    registry = get_registry()
    entries = registry.list_entries_sorted_by_age(limit=max_count + 10000)
    current_time = time.time()
    to_remove: list[str] = []

    for audio_id, created_at in entries:
        age = current_time - created_at
        if age > max_age_seconds:
            to_remove.append(audio_id)

    if len(entries) > max_count:
        excess = len(entries) - max_count
        for audio_id, _ in entries[:excess]:
            if audio_id not in to_remove:
                to_remove.append(audio_id)

    for audio_id in to_remove:
        try:
            registry.delete(audio_id)
        except Exception as e:
            logger.debug("Failed to remove audio_id from registry: %s", e)

    if to_remove:
        logger.info("Purged %d old audio entries from registry", len(to_remove))

    return len(to_remove)


def get_registry_db_path() -> Path:
    """Return the registry database path for diagnostics."""
    return get_path("data") / "audio_registry.db"


def reset_registry_for_testing() -> None:
    """Reset the global registry instance (for test isolation)."""
    global _registry_instance
    _registry_instance = None
