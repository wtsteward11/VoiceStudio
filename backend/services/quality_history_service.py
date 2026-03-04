"""
Quality history store for analytics and quality routes.

Provides shared access to quality history without route-to-route imports.
Owns CRUD and cleanup policy.
"""

from __future__ import annotations

import logging
from typing import Any

logger = logging.getLogger(__name__)

# Module-level store; quality route populates via store_entry, analytics reads via get_*
_quality_history: dict[str, list[Any]] = {}

_MAX_HISTORY_ENTRIES_PER_PROFILE = 1000
_MAX_TOTAL_ENTRIES = 10000


def get_quality_history() -> dict[str, list[Any]]:
    """Get the quality history store (profile_id -> list of entries)."""
    return _quality_history


def get_entries(profile_id: str) -> list[Any]:
    """Get entries for a profile."""
    return _quality_history.get(profile_id, [])


def get_all_entries_flat() -> list[Any]:
    """Get all entries across all profiles as a flat list."""
    result: list[Any] = []
    for entries in _quality_history.values():
        result.extend(entries)
    return result


def store_entry(profile_id: str, entry: Any) -> None:
    """Append entry for profile and run cleanup if needed."""
    if profile_id not in _quality_history:
        _quality_history[profile_id] = []
    _quality_history[profile_id].append(entry)
    # Cleanup periodically (every 100 entries per profile)
    if len(_quality_history[profile_id]) % 100 == 0:
        cleanup_old_entries()


def cleanup_old_entries() -> None:
    """
    Clean up old quality history entries to prevent memory accumulation.

    Removes oldest entries when limits are exceeded.
    """
    global _quality_history

    # First, clean up per-profile limits
    for profile_id, entries in list(_quality_history.items()):
        if len(entries) > _MAX_HISTORY_ENTRIES_PER_PROFILE:
            entries.sort(key=lambda e: getattr(e, "timestamp", ""))
            excess = len(entries) - _MAX_HISTORY_ENTRIES_PER_PROFILE
            _quality_history[profile_id] = entries[excess:]
            logger.debug(
                "Cleaned up %s old quality history entries for profile %s",
                excess,
                profile_id,
            )

    # Then, clean up total limit across all profiles
    total_entries = sum(len(entries) for entries in _quality_history.values())
    if total_entries > _MAX_TOTAL_ENTRIES:
        all_entries: list[tuple[str, Any]] = []
        for profile_id, entries in _quality_history.items():
            for entry in entries:
                all_entries.append((profile_id, entry))

        all_entries.sort(key=lambda x: getattr(x[1], "timestamp", ""))

        excess = total_entries - _MAX_TOTAL_ENTRIES
        removed = 0
        for profile_id, entry in all_entries[:excess]:
            if profile_id in _quality_history:
                try:
                    _quality_history[profile_id].remove(entry)
                    removed += 1
                except ValueError:
                    pass

        new_content = {pid: entries for pid, entries in _quality_history.items() if entries}
        _quality_history.clear()
        _quality_history.update(new_content)

        logger.debug("Cleaned up %s old quality history entries globally", removed)
