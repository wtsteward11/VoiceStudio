"""
API key store for routes that need API key access without route-to-route imports.
"""

from __future__ import annotations

from backend.services.persistent_store import PersistentStore

_api_keys_store: PersistentStore | None = None


def get_api_keys_store() -> PersistentStore:
    """Get the API keys persistent store."""
    global _api_keys_store
    if _api_keys_store is None:
        _api_keys_store = PersistentStore("api_keys")
    return _api_keys_store
