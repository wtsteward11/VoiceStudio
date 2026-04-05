"""
API Response Optimization Utilities

Provides caching, compression, pagination, and async processing
for FastAPI endpoints to achieve 50%+ response time improvement.
"""

from __future__ import annotations

import functools
import gzip
import hashlib
import inspect
import json
import logging
import time
from collections import OrderedDict
from collections.abc import Callable
from typing import Any

from fastapi import Request, Response
from fastapi.responses import JSONResponse
from pydantic import BaseModel as PydanticBaseModel
from starlette.middleware.base import BaseHTTPMiddleware

logger = logging.getLogger(__name__)

# Response cache (LRU cache with TTL)
_RESPONSE_CACHE: OrderedDict = OrderedDict()
_MAX_CACHE_SIZE = 1000  # Maximum number of cached responses
_CACHE_TTL = 300  # Default TTL in seconds (5 minutes)


class ResponseCache:
    """LRU cache for API responses with TTL."""

    def __init__(self, max_size: int = 1000, default_ttl: int = 300):
        """
        Initialize response cache.

        Args:
            max_size: Maximum number of cached responses
            default_ttl: Default TTL in seconds
        """
        self.cache: OrderedDict = OrderedDict()
        self.max_size = max_size
        self.default_ttl = default_ttl
        self.timestamps: dict[str, float] = {}
        self.ttls: dict[str, int] = {}

    def _generate_key(self, path: str, query_params: str, body: bytes | None = None) -> str:
        """Generate cache key from request."""
        key_data = f"{path}?{query_params}"
        if body:
            key_data += body.hex()
        return hashlib.md5(key_data.encode()).hexdigest()

    def get(self, key: str) -> tuple[Any, dict[str, str]] | None:
        """
        Get cached response if available and not expired.

        Args:
            key: Cache key

        Returns:
            Tuple of (response_data, headers) or None if not found/expired
        """
        if key not in self.cache:
            return None

        # Check TTL
        if key in self.timestamps:
            age = time.time() - self.timestamps[key]
            ttl = self.ttls.get(key, self.default_ttl)
            if age > ttl:
                # Expired, remove
                del self.cache[key]
                del self.timestamps[key]
                if key in self.ttls:
                    del self.ttls[key]
                return None

        # Move to end (most recently used)
        self.cache.move_to_end(key)
        entry: tuple[Any, dict[str, str]] = self.cache[key]
        return entry

    def set(
        self,
        key: str,
        response_data: Any,
        headers: dict[str, str],
        ttl: int | None = None,
    ):
        """
        Cache response.

        Args:
            key: Cache key
            response_data: Response data to cache
            headers: Response headers
            ttl: Optional TTL override
        """
        entry_ttl = int(ttl) if ttl is not None else self.default_ttl

        # Add/update entry
        self.cache[key] = (response_data, headers)
        self.timestamps[key] = time.time()
        self.ttls[key] = entry_ttl
        self.cache.move_to_end(key)

        # Evict oldest if cache full
        if len(self.cache) > self.max_size:
            oldest_key = next(iter(self.cache))
            del self.cache[oldest_key]
            if oldest_key in self.timestamps:
                del self.timestamps[oldest_key]
            if oldest_key in self.ttls:
                del self.ttls[oldest_key]

    def clear(self):
        """Clear all cached responses."""
        self.cache.clear()
        self.timestamps.clear()
        self.ttls.clear()


# Global response cache instance
_response_cache = ResponseCache(max_size=_MAX_CACHE_SIZE, default_ttl=_CACHE_TTL)


def invalidate_api_response_cache() -> None:
    """Clear all entries (used after mutating project/track persistence)."""
    _response_cache.clear()


def cache_response(ttl: int = 300, key_func: Callable | None = None):
    """
    Decorator to cache API responses.

    Args:
        ttl: Time to live in seconds
        key_func: Optional function to generate cache key from request

    Example:
        @router.get("/profiles")
        @cache_response(ttl=60)
        def list_profiles():
            return profiles
    """

    def decorator(func: Callable):
        @functools.wraps(func)
        async def wrapper(*args, **kwargs):
            # Try to locate a Request instance (kwargs or anywhere in args)
            request = kwargs.get("request")
            if request is None:
                for arg in args:
                    if isinstance(arg, Request):
                        request = arg
                        break

            # Generate cache key (supports endpoints without a Request parameter)
            if key_func:
                cache_key = key_func(request, *args, **kwargs)
            elif request is not None:
                query_string = str(request.query_params)
                cache_key = _response_cache._generate_key(request.url.path, query_string)
            else:
                try:
                    payload = json.dumps(
                        {"args": args, "kwargs": kwargs},
                        default=str,
                        sort_keys=True,
                    ).encode("utf-8")
                except Exception:
                    payload = repr((args, kwargs)).encode("utf-8")
                cache_key = _response_cache._generate_key(
                    f"{func.__module__}.{func.__qualname__}",
                    "",
                    body=payload,
                )

            # Check cache
            cached = _response_cache.get(cache_key)
            if cached:
                response_data, headers = cached
                logger.debug(f"Cache hit for {cache_key[:16]}...")
                return JSONResponse(content=response_data, headers={**headers, "X-Cache": "HIT"})

            # Call endpoint (sync or async)
            if inspect.iscoroutinefunction(func):
                result = await func(*args, **kwargs)
            else:
                result = func(*args, **kwargs)

            # Only cache JSON-serializable payloads (including nested pydantic models).
            if isinstance(result, Response) and not isinstance(result, JSONResponse):
                return result

            def _to_jsonable(value: Any) -> Any:
                if isinstance(value, PydanticBaseModel):
                    return value.model_dump(mode="json")
                if isinstance(value, dict):
                    return {k: _to_jsonable(v) for k, v in value.items()}
                if isinstance(value, list):
                    return [_to_jsonable(v) for v in value]
                if isinstance(value, tuple):
                    return [_to_jsonable(v) for v in value]
                return value

            headers: dict[str, str] = {}

            if isinstance(result, JSONResponse):
                try:
                    response_data = _to_jsonable(json.loads(result.body))
                except Exception:
                    return result
                headers = dict(result.headers)
            else:
                response_data = _to_jsonable(result)

            try:
                json.dumps(response_data)
            except TypeError:
                return result

            _response_cache.set(cache_key, response_data, headers, ttl=ttl)
            logger.debug(f"Cached response for {cache_key[:16]}...")

            return JSONResponse(
                content=response_data,
                headers={**headers, "X-Cache": "MISS"},
            )

        return wrapper

    return decorator


class CompressionMiddleware(BaseHTTPMiddleware):
    """Middleware to compress large responses."""

    def __init__(self, app, min_size: int = 1024):
        """
        Initialize compression middleware.

        Args:
            app: FastAPI application
            min_size: Minimum response size in bytes to compress
        """
        super().__init__(app)
        self.min_size = min_size

    async def dispatch(self, request: Request, call_next):
        """Compress response if large enough."""
        response = await call_next(request)

        # Only compress if response is large enough
        if hasattr(response, "body") and len(response.body) > self.min_size:
            # Check if client accepts gzip
            accept_encoding = request.headers.get("accept-encoding", "")
            if "gzip" in accept_encoding:
                compressed = gzip.compress(response.body)
                response.body = compressed
                response.headers["Content-Encoding"] = "gzip"
                response.headers["Content-Length"] = str(len(compressed))
                logger.debug(
                    f"Compressed response: {len(response.body)} -> {len(compressed)} bytes"
                )

        return response


class PaginationParams:
    """Pagination parameters for list endpoints."""

    def __init__(self, page: int = 1, page_size: int = 50, max_page_size: int = 1000):
        """
        Initialize pagination parameters.

        Args:
            page: Page number (1-indexed)
            page_size: Items per page
            max_page_size: Maximum allowed page size
        """
        self.page = max(1, page)
        self.page_size = min(max(1, page_size), max_page_size)
        self.skip = (self.page - 1) * self.page_size
        self.limit = self.page_size

    def paginate(self, items: list[Any]) -> dict[str, Any]:
        """
        Paginate a list of items.

        Args:
            items: List of items to paginate

        Returns:
            Dictionary with paginated results and metadata
        """
        total = len(items)
        paginated_items = items[self.skip : self.skip + self.limit]

        return {
            "items": paginated_items,
            "pagination": {
                "page": self.page,
                "page_size": self.page_size,
                "total": total,
                "pages": (total + self.page_size - 1) // self.page_size,
                "has_next": self.skip + self.limit < total,
                "has_prev": self.page > 1,
            },
        }


def optimize_json_serialization(data: Any) -> str:
    """
    Optimize JSON serialization by using orjson if available.

    Args:
        data: Data to serialize

    Returns:
        JSON string
    """
    try:
        import orjson

        result: str = orjson.dumps(data).decode("utf-8")
        return result
    except ImportError:
        # Fallback to standard json with optimizations
        return json.dumps(data, separators=(",", ":"), ensure_ascii=False)


class AsyncTaskManager:
    """Manager for async/long-running tasks."""

    def __init__(self):
        """Initialize async task manager."""
        self.tasks: dict[str, dict[str, Any]] = {}

    def create_task(self, task_id: str, task_func: Callable, *args, **kwargs) -> str:
        """
        Create and start an async task.

        Args:
            task_id: Unique task ID
            task_func: Function to execute
            *args: Function arguments
            **kwargs: Function keyword arguments

        Returns:
            Task ID
        """
        import asyncio

        async def run_task():
            try:
                result = (
                    await task_func(*args, **kwargs)
                    if hasattr(task_func, "__await__")
                    else task_func(*args, **kwargs)
                )
                self.tasks[task_id] = {
                    "status": "completed",
                    "result": result,
                    "completed_at": time.time(),
                }
            except Exception as e:
                self.tasks[task_id] = {
                    "status": "failed",
                    "error": str(e),
                    "failed_at": time.time(),
                }

        self.tasks[task_id] = {"status": "running", "started_at": time.time()}
        asyncio.create_task(run_task())
        return task_id

    def get_task_status(self, task_id: str) -> dict[str, Any] | None:
        """
        Get task status.

        Args:
            task_id: Task ID

        Returns:
            Task status dictionary or None if not found
        """
        return self.tasks.get(task_id)

    def get_result(self, task_id: str) -> Any | None:
        """
        Get task result if completed.

        Args:
            task_id: Task ID

        Returns:
            Task result or None if not completed
        """
        task = self.tasks.get(task_id)
        if task and task.get("status") == "completed":
            return task.get("result")
        return None


# Global async task manager
_async_task_manager = AsyncTaskManager()


def async_task(task_func: Callable):
    """
    Decorator to run endpoint as async task.

    Args:
        task_func: Function to run asynchronously

    Example:
        @router.post("/long-operation")
        @async_task
        def long_operation():
            # Long running operation
            return result
    """

    @functools.wraps(task_func)
    async def wrapper(*args, **kwargs):
        import uuid

        task_id = str(uuid.uuid4())
        _async_task_manager.create_task(task_id, task_func, *args, **kwargs)

        return {
            "task_id": task_id,
            "status": "queued",
            "message": "Task queued for processing",
        }

    return wrapper


def get_pagination_params(request: Request, default_page_size: int = 50) -> PaginationParams:
    """
    Extract pagination parameters from request.

    Args:
        request: FastAPI request
        default_page_size: Default page size

    Returns:
        PaginationParams object
    """
    page = int(request.query_params.get("page", 1))
    page_size = int(request.query_params.get("page_size", default_page_size))
    return PaginationParams(page=page, page_size=page_size)


# Export utilities
__all__ = [
    "AsyncTaskManager",
    "CompressionMiddleware",
    "PaginationParams",
    "ResponseCache",
    "_async_task_manager",
    "_response_cache",
    "async_task",
    "cache_response",
    "get_pagination_params",
    "invalidate_api_response_cache",
    "optimize_json_serialization",
]
