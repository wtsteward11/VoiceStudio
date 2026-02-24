"""Thread-safe state management for route-level in-memory dicts.

All route files that use module-level dicts as data stores should
use a per-module asyncio.Lock to guard concurrent access. This module
provides a helper to create and retrieve named locks.

Usage in a route file:

    from ._state import get_state_lock

    _lock = get_state_lock(__name__)

    @router.post("/items")
    async def create_item(...):
        async with _lock:
            _items[item_id] = item_data

Long-term, these dicts should be replaced with database-backed
repositories (see BLOCKER-1 in the architecture review).
"""

import asyncio
from typing import Dict

_locks: Dict[str, asyncio.Lock] = {}


def get_state_lock(module_name: str) -> asyncio.Lock:
    """Get or create a named asyncio.Lock for a route module."""
    if module_name not in _locks:
        _locks[module_name] = asyncio.Lock()
    return _locks[module_name]
