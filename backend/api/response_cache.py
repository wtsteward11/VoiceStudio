"""
API Response Caching System
LRU cache with TTL for GET endpoint responses

Compatible with:
- Python 3.10+
- FastAPI
- All GET endpoints
"""

from __future__ import annotations

import hashlib
import json
import logging
import time
from collections import OrderedDict
from collections.abc import Callable
from typing import Any

from fastapi import Request, Response
from starlette.responses import JSONResponse

logger = logging.getLogger(__name__)


class ResponseCache:
    """
    LRU cache with TTL for API responses.

    Features:
    - LRU eviction policy
    - TTL (Time To Live) support
    - Automatic cache invalidation
    - Cache statistics
    """

    def __init__(
        self,
        max_size: int = 1000,
        default_ttl: int = 300,  # 5 minutes default
        cleanup_interval: int = 60,  # Cleanup every 60 seconds
        max_memory_mb: float | None = None,  # Optional memory limit
    ):
        """
        Initialize response cache.

        Args:
            max_size: Maximum number of cached responses
            default_ttl: Default TTL in seconds
            cleanup_interval: Interval for cleanup in seconds
            max_memory_mb: Maximum memory usage in MB (None = unlimited)
        """
        self.max_size = max_size
        self.default_ttl = default_ttl
        self.cleanup_interval = cleanup_interval
        self.max_memory_mb = max_memory_mb

        # Cache storage: {cache_key: (response_data, timestamp, ttl, size_bytes, tags)}
        # tags: list of strings for invalidation (e.g., ["profiles", "user:123"])
        self._cache: OrderedDict[str, tuple[Any, float, int, int, list]] = OrderedDict()

        # Index for fast invalidation by tag
        self._tag_index: dict[str, set] = {}  # tag -> set of cache_keys

        # Statistics
        self._hits = 0
        self._misses = 0
        self._evictions = 0
        self._last_cleanup = time.time()
        self._current_memory_bytes = 0

    def _generate_cache_key(
        self, request: Request, include_query: bool = True, include_headers: bool = False
    ) -> str:
        """
        Generate cache key from request.

        Args:
            request: FastAPI request
            include_query: Whether to include query parameters
            include_headers: Whether to include relevant headers (for user-specific caching)

        Returns:
            Cache key string
        """
        key_parts = [request.method, request.url.path]

        if include_query and request.query_params:
            # Sort query params for consistent keys
            sorted_params = sorted(request.query_params.items())
            key_parts.append(str(sorted_params))

        # Include relevant headers for user-specific caching
        if include_headers:
            # Include Authorization header for user-specific responses
            auth_header = request.headers.get("Authorization")
            if auth_header:
                # Use hash of auth header for privacy
                auth_hash = hashlib.sha256(auth_header.encode()).hexdigest()[:16]
                key_parts.append(f"auth:{auth_hash}")

        key_string = "|".join(key_parts)
        return hashlib.md5(key_string.encode()).hexdigest()

    def _estimate_size_bytes(self, response_data: Any) -> int:
        """
        Estimate size of response data in bytes.

        Args:
            response_data: Response data to estimate

        Returns:
            Estimated size in bytes
        """
        try:
            # Serialize to JSON to estimate size
            json_str = json.dumps(response_data)
            return len(json_str.encode("utf-8"))
        except Exception:
            # Fallback: rough estimate
            return 1024  # 1KB default estimate

    def get(self, cache_key: str) -> Any | None:
        """
        Get cached response.

        Args:
            cache_key: Cache key

        Returns:
            Cached response data or None if not found/expired
        """
        if cache_key not in self._cache:
            self._misses += 1
            return None

        # Check TTL
        response_data, timestamp, ttl, _size_bytes, _tags = self._cache[cache_key]
        current_time = time.time()
        age = current_time - timestamp

        if age > ttl:
            # Expired, remove from cache
            self._remove_from_cache(cache_key)
            self._misses += 1
            return None

        # Move to end (LRU update)
        self._cache.move_to_end(cache_key)
        self._hits += 1
        return response_data

    def _remove_from_cache(self, cache_key: str) -> None:
        """Remove entry from cache and update indices."""
        if cache_key not in self._cache:
            return

        _, _, _, size_bytes, tags = self._cache[cache_key]

        # Remove from tag index
        for tag in tags:
            if tag in self._tag_index:
                self._tag_index[tag].discard(cache_key)
                if not self._tag_index[tag]:
                    del self._tag_index[tag]

        # Update memory stats
        self._current_memory_bytes -= size_bytes

        # Remove from cache
        del self._cache[cache_key]

    def set(
        self,
        cache_key: str,
        response_data: Any,
        ttl: int | None = None,
        tags: list | None = None,
    ) -> None:
        """
        Cache response.

        Args:
            cache_key: Cache key
            response_data: Response data to cache
            ttl: TTL in seconds (uses default if None)
            tags: List of tags for invalidation (e.g., ["profiles", "user:123"])
        """
        # Cleanup if needed
        self._cleanup_if_needed()

        # Estimate size
        size_bytes = self._estimate_size_bytes(response_data)

        # Check memory limit
        if self.max_memory_mb is not None:
            size_bytes / (1024 * 1024)
            # Evict until we have enough space
            while (
                self._current_memory_bytes + size_bytes > self.max_memory_mb * 1024 * 1024
                and self._cache
            ):
                oldest_key = next(iter(self._cache))
                self._remove_from_cache(oldest_key)
                self._evictions += 1

        # Remove oldest entries if cache is full (by count)
        if len(self._cache) >= self.max_size:
            oldest_key = next(iter(self._cache))
            self._remove_from_cache(oldest_key)
            self._evictions += 1

        # Remove existing entry if present
        if cache_key in self._cache:
            self._remove_from_cache(cache_key)

        # Store with timestamp, TTL, size, and tags
        actual_ttl = ttl if ttl is not None else self.default_ttl
        actual_tags = tags or []
        self._cache[cache_key] = (
            response_data,
            time.time(),
            actual_ttl,
            size_bytes,
            actual_tags,
        )
        self._cache.move_to_end(cache_key)  # LRU update

        # Update memory stats
        self._current_memory_bytes += size_bytes

        # Update tag index
        for tag in actual_tags:
            if tag not in self._tag_index:
                self._tag_index[tag] = set()
            self._tag_index[tag].add(cache_key)

    def invalidate(
        self,
        pattern: str | None = None,
        tags: list | None = None,
        path_prefix: str | None = None,
    ) -> int:
        """
        Invalidate cache entries.

        Args:
            pattern: Optional pattern to match in cache key (if None, uses other criteria)
            tags: Optional list of tags to invalidate (all entries with any matching tag)
            path_prefix: Optional path prefix to invalidate (e.g., "/api/profiles")

        Returns:
            Number of entries invalidated
        """
        if pattern is None and tags is None and path_prefix is None:
            # Clear all
            count = len(self._cache)
            self._cache.clear()
            self._tag_index.clear()
            self._current_memory_bytes = 0
            return count

        to_remove = set()

        # Invalidate by tags
        if tags:
            for tag in tags:
                if tag in self._tag_index:
                    to_remove.update(self._tag_index[tag])

        # Invalidate by path prefix
        if path_prefix:
            # We need to check stored paths, but we only have cache keys
            # For now, we'll use pattern matching on keys
            # In a real implementation, we'd store path separately
            for key in self._cache:
                # Cache keys are MD5 hashes, so we can't match paths directly
                # This is a limitation - we'd need to store path metadata
                ...

        # Invalidate by pattern
        if pattern:
            for key in self._cache:
                if pattern in key:
                    to_remove.add(key)

        # Remove entries
        count = len(to_remove)
        for key in to_remove:
            self._remove_from_cache(key)

        return count

    def invalidate_by_tag(self, tag: str) -> int:
        """
        Invalidate all cache entries with a specific tag.

        Args:
            tag: Tag to invalidate

        Returns:
            Number of entries invalidated
        """
        return self.invalidate(tags=[tag])

    def invalidate_by_tags(self, tags: list) -> int:
        """
        Invalidate all cache entries with any of the specified tags.

        Args:
            tags: List of tags to invalidate

        Returns:
            Number of entries invalidated
        """
        return self.invalidate(tags=tags)

    def _cleanup_if_needed(self) -> None:
        """Clean up expired entries if cleanup interval has passed."""
        current_time = time.time()
        if current_time - self._last_cleanup < self.cleanup_interval:
            return

        # Remove expired entries
        expired_keys = []
        for key, (_, timestamp, ttl, _, _) in self._cache.items():
            if current_time - timestamp > ttl:
                expired_keys.append(key)

        for key in expired_keys:
            self._remove_from_cache(key)

        self._last_cleanup = current_time

        if expired_keys:
            logger.debug(f"Cleaned up {len(expired_keys)} expired cache entries")

    def get_stats(self) -> dict[str, Any]:
        """Get cache statistics."""
        self._cleanup_if_needed()
        total_requests = self._hits + self._misses
        hit_rate = self._hits / total_requests * 100 if total_requests > 0 else 0

        memory_mb = self._current_memory_bytes / (1024 * 1024)

        return {
            "size": len(self._cache),
            "max_size": self.max_size,
            "hits": self._hits,
            "misses": self._misses,
            "hit_rate": f"{hit_rate:.2f}%",
            "evictions": self._evictions,
            "memory_mb": f"{memory_mb:.2f}",
            "max_memory_mb": (f"{self.max_memory_mb:.2f}" if self.max_memory_mb else None),
            "tags": len(self._tag_index),
        }

    def clear(self) -> None:
        """Clear all cache entries."""
        self._cache.clear()
        self._tag_index.clear()
        self._current_memory_bytes = 0
        self._hits = 0
        self._misses = 0
        self._evictions = 0


# Global cache instance
_response_cache: ResponseCache | None = None


def get_response_cache() -> ResponseCache:
    """Get or create global response cache instance."""
    global _response_cache
    if _response_cache is None:
        _response_cache = ResponseCache()
    return _response_cache


def set_response_cache(cache: ResponseCache) -> None:
    """Set global response cache instance."""
    global _response_cache
    _response_cache = cache


def _cache_key_auth_segment(request: Request) -> str:
    """
    Isolate cached GET responses by auth mode and credential presence.

    Without this, a 200 from an anonymous request could be served from cache
    when VOICESTUDIO_REQUIRE_AUTH=true should return 401 (GAP-057).
    """
    from backend.api.middleware import auth_middleware as am

    ar = str(am.AUTH_REQUIRED)
    if request.headers.get("X-API-Key"):
        cred = "k"
    elif request.headers.get("Authorization"):
        cred = "b"
    else:
        cred = "0"
    return f"{ar}|{cred}"


async def response_cache_middleware(request: Request, call_next: Callable) -> Response:
    """
    Middleware for caching GET endpoint responses.

    Args:
        request: FastAPI request
        call_next: Next middleware/route handler

    Returns:
        Cached or fresh response
    """
    # Only cache GET requests
    if request.method != "GET":
        resp: Response = await call_next(request)
        return resp

    # Skip caching for certain paths
    skip_paths = [
        "/health",
        "/api/health",
        "/docs",
        "/openapi.json",
        "/redoc",
    ]
    if any(request.url.path.startswith(path) for path in skip_paths):
        resp2: Response = await call_next(request)
        return resp2

    cache = get_response_cache()
    base_key = cache._generate_cache_key(request)
    cache_key = hashlib.md5(
        f"{base_key}|{_cache_key_auth_segment(request)}".encode(),
    ).hexdigest()

    # Try to get cached response
    cached_response = cache.get(cache_key)
    if cached_response is not None:
        logger.debug(f"Cache hit for {request.url.path}")
        # Return cached response
        return JSONResponse(
            content=cached_response["content"],
            status_code=cached_response["status_code"],
            headers={
                **cached_response.get("headers", {}),
                "X-Cache": "HIT",
                # First 8 chars for debugging
                "X-Cache-Key": cache_key[:8],
            },
        )

    # Cache miss, process request
    logger.debug(f"Cache miss for {request.url.path}")
    response: Response = await call_next(request)

    # Only cache successful responses
    if response.status_code == 200:
        # Get response body
        response_body = b""
        body_iter = getattr(response, "body_iterator", None)
        if body_iter is None:
            return response
        async for chunk in body_iter:
            response_body += chunk

        # Parse JSON if possible
        try:
            content = json.loads(response_body.decode())
        except (json.JSONDecodeError, UnicodeDecodeError):
            # Not JSON, skip caching
            return Response(
                content=response_body,
                status_code=response.status_code,
                headers=dict(response.headers),
            )

        # Determine TTL from headers or use default
        cache_control = request.headers.get("Cache-Control", "")
        ttl = cache.default_ttl

        # Parse Cache-Control header for max-age
        if "max-age" in cache_control:
            try:
                max_age_str = cache_control.split("max-age=")[1].split(",")[0].strip()
                max_age = int(max_age_str)
                ttl = max_age
            except (ValueError, IndexError):
                ...

        # Extract tags from path for invalidation
        tags = []
        path = request.url.path
        if path.startswith("/api/"):
            # Extract resource type from path (e.g., "/api/profiles" -> "profiles")
            parts = path.split("/")
            if len(parts) >= 3:
                tags.append(parts[2])  # e.g., "profiles", "voice", "projects"

        # Cache response
        cache.set(
            cache_key,
            {
                "content": content,
                "status_code": response.status_code,
                "headers": dict(response.headers),
            },
            ttl=ttl,
            tags=tags,
        )

        # Return response with cache headers
        return JSONResponse(
            content=content,
            status_code=response.status_code,
            headers={
                **dict(response.headers),
                "X-Cache": "MISS",
                "X-Cache-Key": cache_key[:8],
            },
        )

    return response


def cache_response(ttl: int | None = None):
    """
    Decorator for caching specific endpoint responses.

    Args:
        ttl: TTL in seconds (uses default if None)

    Usage:
        @app.get("/api/endpoint")
        @cache_response(ttl=600)
        async def my_endpoint():
            return {"data": "value"}
    """

    def decorator(func: Callable) -> Callable:
        async def wrapper(*args, **kwargs):
            # Get request from kwargs or args
            request = kwargs.get("request") or (
                args[0] if args and isinstance(args[0], Request) else None
            )

            if request is None:
                # No request, can't cache
                return await func(*args, **kwargs)

            cache = get_response_cache()
            cache_key = cache._generate_cache_key(request)

            # Try cache
            cached = cache.get(cache_key)
            if cached is not None:
                return JSONResponse(
                    content=cached["content"],
                    status_code=cached["status_code"],
                    headers={**cached.get("headers", {}), "X-Cache": "HIT"},
                )

            # Call function
            response = await func(*args, **kwargs)

            # Cache if successful
            if isinstance(response, JSONResponse) and response.status_code == 200:
                cache.set(
                    cache_key,
                    {
                        "content": (response.body.decode() if hasattr(response, "body") else None),
                        "status_code": response.status_code,
                        "headers": dict(response.headers),
                    },
                    ttl=ttl,
                )

            return response

        return wrapper

    return decorator
