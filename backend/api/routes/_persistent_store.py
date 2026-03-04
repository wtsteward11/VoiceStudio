"""Deprecated: re-export PersistentStore from services layer.

Use: from backend.services.persistent_store import PersistentStore
"""

from backend.services.persistent_store import PersistentStore

__all__ = ["PersistentStore"]
