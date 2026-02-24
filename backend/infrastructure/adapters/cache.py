"""
Cache Adapter.

Task 3.2.4: Adapter for caching operations.
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import Callable
from dataclasses import dataclass
from typing import Any

from backend.infrastructure.adapters.base import Adapter

logger = logging.getLogger(__name__)


@dataclass
class CacheEntry:
    """A cache entry with expiration."""

    value: Any
    expires_at: float

    @property
    def is_expired(self) -> bool:
        return time.time() >= self.expires_at


class CacheAdapter(Adapter):
    """
    Adapter for caching operations.

    Provides in-memory caching with TTL support.
    Can be extended to use Redis or other backends.
    """

    def __init__(
        self,
        default_ttl: int = 300,
        max_size: int = 1000,
    ):
        """
        Initialize cache adapter.

        Args:
            default_ttl: Default TTL in seconds
            max_size: Maximum cache entries
        """
        super().__init__("Cache")

        self._default_ttl = default_ttl
        self._max_size = max_size
        self._cache: dict[str, CacheEntry] = {}
        self._hits = 0
        self._misses = 0
        self._lock = asyncio.Lock()

    async def connect(self) -> bool:
        """Initialize cache."""
        self._cache.clear()
        self._connected = True
        return True

    async def disconnect(self) -> bool:
        """Clear cache."""
        self._cache.clear()
        self._connected = False
        return True

    async def health_check(self) -> dict[str, Any]:
        """Check cache health."""
        return {
            "connected": self._connected,
            "entries": len(self._cache),
            "max_size": self._max_size,
            "hits": self._hits,
            "misses": self._misses,
            "hit_rate": (
                self._hits / (self._hits + self._misses) if (self._hits + self._misses) > 0 else 0
            ),
        }

    async def get(self, key: str) -> Any | None:
        """
        Get a value from cache.

        Args:
            key: Cache key

        Returns:
            Cached value or None
        """
        async with self._lock:
            entry = self._cache.get(key)

            if entry is None:
                self._misses += 1
                return None

            if entry.is_expired:
                del self._cache[key]
                self._misses += 1
                return None

            self._hits += 1
            return entry.value

    async def set(
        self,
        key: str,
        value: Any,
        ttl: int | None = None,
    ) -> None:
        """
        Set a value in cache.

        Args:
            key: Cache key
            value: Value to cache
            ttl: TTL in seconds (optional)
        """
        async with self._lock:
            # Evict if at max size
            if len(self._cache) >= self._max_size:
                await self._evict_expired()

                if len(self._cache) >= self._max_size:
                    # Remove oldest entry
                    oldest_key = next(iter(self._cache))
                    del self._cache[oldest_key]

            ttl = ttl or self._default_ttl
            expires_at = time.time() + ttl

            self._cache[key] = CacheEntry(
                value=value,
                expires_at=expires_at,
            )

    async def delete(self, key: str) -> bool:
        """
        Delete a value from cache.

        Args:
            key: Cache key

        Returns:
            True if deleted
        """
        async with self._lock:
            if key in self._cache:
                del self._cache[key]
                return True
            return False

    async def clear(self) -> None:
        """Clear all cache entries."""
        async with self._lock:
            self._cache.clear()

    async def _evict_expired(self) -> int:
        """
        Remove expired entries.

        Returns:
            Number of entries removed
        """
        expired_keys = [k for k, v in self._cache.items() if v.is_expired]

        for key in expired_keys:
            del self._cache[key]

        return len(expired_keys)

    async def get_or_set(
        self,
        key: str,
        factory: Callable[..., Any],
        ttl: int | None = None,
    ) -> Any:
        """
        Get from cache or compute and cache.

        Args:
            key: Cache key
            factory: Async function to compute value
            ttl: TTL in seconds

        Returns:
            Cached or computed value
        """
        value = await self.get(key)

        if value is not None:
            return value

        # Compute value
        if asyncio.iscoroutinefunction(factory):
            value = await factory()
        else:
            value = factory()

        await self.set(key, value, ttl)
        return value

    def get_stats(self) -> dict[str, Any]:
        """Get cache statistics."""
        return {
            "entries": len(self._cache),
            "hits": self._hits,
            "misses": self._misses,
            "hit_rate": (
                self._hits / (self._hits + self._misses) if (self._hits + self._misses) > 0 else 0
            ),
        }
