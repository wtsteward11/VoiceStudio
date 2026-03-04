"""
Marker store service for timeline markers.

Provides centralized access to markers for search and markers route.
Replaces route-to-route imports from markers.
"""

from __future__ import annotations

from backend.services.persistent_store import PersistentStore

_marker_store: PersistentStore | None = None


def get_marker_store() -> PersistentStore:
    """Get the global marker store singleton."""
    global _marker_store
    if _marker_store is None:
        _marker_store = PersistentStore("markers")
    return _marker_store
