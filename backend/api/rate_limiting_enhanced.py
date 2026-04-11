"""
Enhanced Rate Limiting and Throttling

Features:
- Sliding window algorithm
- Per-endpoint rate limiting
- Throttling for resource-intensive operations
- Rate limit headers
- Configuration management
- Rate limit monitoring
"""

from __future__ import annotations

import asyncio
import logging
import threading
import time
from collections import defaultdict, deque
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

from fastapi import Request, Response, status
from starlette.middleware.base import BaseHTTPMiddleware

logger = logging.getLogger(__name__)


@dataclass
class RateLimitConfig:
    """Rate limit configuration for an endpoint."""

    requests_per_second: float = 10.0
    requests_per_minute: float = 60.0
    requests_per_hour: float = 1000.0
    burst_size: int = 20  # Allow burst of requests
    window_seconds: float = 60.0  # Sliding window size


@dataclass
class RateLimitStats:
    """Statistics for rate limiting."""

    total_requests: int = 0
    allowed_requests: int = 0
    blocked_requests: int = 0
    rate_limit_hits: int = 0
    throttle_applications: int = 0
    last_reset: datetime = field(default_factory=datetime.now)


class SlidingWindowRateLimiter:
    """
    Sliding window rate limiter.

    Uses a sliding window algorithm for more accurate rate limiting.
    """

    def __init__(self, config: RateLimitConfig):
        """
        Initialize sliding window rate limiter.

        Args:
            config: Rate limit configuration
        """
        self.config = config
        self.windows: dict[str, deque] = {}  # key -> deque of timestamps
        self.lock = threading.Lock()

    def check_rate_limit(
        self, key: str, current_time: float | None = None
    ) -> tuple[bool, dict[str, Any]]:
        """
        Check if request is within rate limit.

        Args:
            key: Rate limit key (e.g., IP address or user ID)
            current_time: Current timestamp (for testing)

        Returns:
            Tuple of (is_allowed, rate_limit_info)
        """
        if current_time is None:
            current_time = time.time()

        with self.lock:
            # Get or create window for this key
            if key not in self.windows:
                self.windows[key] = deque()

            window = self.windows[key]

            # Remove old entries outside the window
            window_start = current_time - self.config.window_seconds
            while window and window[0] < window_start:
                window.popleft()

            # Check if within limits
            len(window)

            # Check per-second limit
            if self.config.requests_per_second > 0:
                recent_requests = sum(1 for t in window if t > current_time - 1.0)
                if recent_requests >= self.config.requests_per_second:
                    return False, {
                        "allowed": False,
                        "reason": "requests_per_second",
                        "limit": self.config.requests_per_second,
                        "current": recent_requests,
                        "retry_after": 1.0,
                    }

            # Check per-minute limit
            if self.config.requests_per_minute > 0:
                recent_requests = sum(1 for t in window if t > current_time - 60.0)
                if recent_requests >= self.config.requests_per_minute:
                    return False, {
                        "allowed": False,
                        "reason": "requests_per_minute",
                        "limit": self.config.requests_per_minute,
                        "current": recent_requests,
                        "retry_after": 60.0 - (current_time - window[0]) if window else 0.0,
                    }

            # Check per-hour limit
            if self.config.requests_per_hour > 0:
                recent_requests = sum(1 for t in window if t > current_time - 3600.0)
                if recent_requests >= self.config.requests_per_hour:
                    return False, {
                        "allowed": False,
                        "reason": "requests_per_hour",
                        "limit": self.config.requests_per_hour,
                        "current": recent_requests,
                        "retry_after": 3600.0 - (current_time - window[0]) if window else 0.0,
                    }

            # Check burst limit
            if self.config.burst_size > 0:
                recent_requests = sum(1 for t in window if t > current_time - 1.0)
                if recent_requests >= self.config.burst_size:
                    return False, {
                        "allowed": False,
                        "reason": "burst_size",
                        "limit": self.config.burst_size,
                        "current": recent_requests,
                        "retry_after": 1.0,
                    }

            # Request is allowed, add to window
            window.append(current_time)

            # Calculate remaining requests
            remaining_second = max(
                0,
                int(
                    self.config.requests_per_second
                    - sum(1 for t in window if t > current_time - 1.0)
                ),
            )
            remaining_minute = max(
                0,
                int(
                    self.config.requests_per_minute
                    - sum(1 for t in window if t > current_time - 60.0)
                ),
            )
            remaining_hour = max(
                0,
                int(
                    self.config.requests_per_hour
                    - sum(1 for t in window if t > current_time - 3600.0)
                ),
            )

            return True, {
                "allowed": True,
                "remaining_second": remaining_second,
                "remaining_minute": remaining_minute,
                "remaining_hour": remaining_hour,
                "reset_after": self.config.window_seconds,
            }

    def cleanup_old_entries(self, max_age_seconds: float = 3600.0):
        """Clean up old entries."""
        current_time = time.time()
        cutoff_time = current_time - max_age_seconds

        with self.lock:
            keys_to_remove = []
            for key, window in self.windows.items():
                # Remove old entries
                while window and window[0] < cutoff_time:
                    window.popleft()

                # Remove empty windows
                if not window:
                    keys_to_remove.append(key)

            for key in keys_to_remove:
                del self.windows[key]


class Throttler:
    """
    Throttler for resource-intensive operations.

    Implements delay-based throttling to prevent resource exhaustion.
    """

    def __init__(self, min_delay_seconds: float = 0.1, max_concurrent: int = 10):
        """
        Initialize throttler.

        Args:
            min_delay_seconds: Minimum delay between requests
            max_concurrent: Maximum concurrent requests
        """
        self.min_delay_seconds = min_delay_seconds
        self.max_concurrent = max_concurrent
        self.last_request_time: dict[str, float] = {}
        self.active_requests: dict[str, int] = defaultdict(int)
        self.lock = threading.Lock()

    def throttle(self, key: str) -> float | None:
        """
        Apply throttling.

        Args:
            key: Throttle key

        Returns:
            Delay in seconds if throttling is needed, None otherwise
        """
        current_time = time.time()

        with self.lock:
            # Check concurrent requests
            if self.active_requests[key] >= self.max_concurrent:
                return 1.0  # Wait 1 second

            # Check minimum delay
            if key in self.last_request_time:
                time_since_last = current_time - self.last_request_time[key]
                if time_since_last < self.min_delay_seconds:
                    return self.min_delay_seconds - time_since_last

            # Update tracking
            self.last_request_time[key] = current_time
            self.active_requests[key] += 1

            return None

    def release(self, key: str):
        """Release throttle lock."""
        with self.lock:
            if key in self.active_requests:
                self.active_requests[key] = max(0, self.active_requests[key] - 1)


class EnhancedRateLimiter:
    """
    Enhanced rate limiter with sliding window and throttling.

    Features:
    - Sliding window algorithm
    - Per-endpoint configuration
    - Throttling support
    - Rate limit headers
    - Statistics tracking
    """

    def __init__(self):
        """Initialize enhanced rate limiter."""
        # Default configuration
        self.default_config = RateLimitConfig(
            requests_per_second=10.0,
            requests_per_minute=60.0,
            requests_per_hour=1000.0,
            burst_size=20,
        )

        # Per-endpoint configurations
        self.endpoint_configs: dict[str, RateLimitConfig] = {
            "/api/voice/synthesize": RateLimitConfig(
                requests_per_second=2.0,
                requests_per_minute=30.0,
                requests_per_hour=500.0,
                burst_size=5,
            ),
            "/api/training/start": RateLimitConfig(
                requests_per_second=0.1,
                requests_per_minute=1.0,
                requests_per_hour=10.0,
                burst_size=1,
            ),
            "/api/batch/submit": RateLimitConfig(
                requests_per_second=1.0,
                requests_per_minute=10.0,
                requests_per_hour=100.0,
                burst_size=3,
            ),
        }

        # Rate limiters per endpoint
        self.limiters: dict[str, SlidingWindowRateLimiter] = {}
        self.throttlers: dict[str, Throttler] = {}

        # Statistics
        self.stats = RateLimitStats()
        self.stats_lock = threading.Lock()

        # Initialize limiters
        self._initialize_limiters()

    def _initialize_limiters(self):
        """Initialize rate limiters for all endpoints."""
        # Default limiter
        self.limiters["default"] = SlidingWindowRateLimiter(self.default_config)

        # Endpoint-specific limiters
        for endpoint, config in self.endpoint_configs.items():
            self.limiters[endpoint] = SlidingWindowRateLimiter(config)
            self.throttlers[endpoint] = Throttler(min_delay_seconds=0.5, max_concurrent=5)

    def get_limiter(self, endpoint: str) -> SlidingWindowRateLimiter:
        """
        Get rate limiter for endpoint.

        Args:
            endpoint: Endpoint path

        Returns:
            Rate limiter instance
        """
        # Check for exact match
        if endpoint in self.limiters:
            return self.limiters[endpoint]

        # Check for prefix match
        for config_endpoint, limiter in self.limiters.items():
            if config_endpoint != "default" and endpoint.startswith(config_endpoint):
                return limiter

        # Use default
        return self.limiters["default"]

    def get_throttler(self, endpoint: str) -> Throttler | None:
        """
        Get throttler for endpoint.

        Args:
            endpoint: Endpoint path

        Returns:
            Throttler instance or None
        """
        return self.throttlers.get(endpoint)

    def check_rate_limit(self, request: Request) -> tuple[bool, dict[str, Any], float | None]:
        """
        Check rate limit for request.

        Args:
            request: FastAPI request

        Returns:
            Tuple of (is_allowed, rate_limit_info, throttle_delay)
        """
        # Get client identifier
        client_id = self._get_client_id(request)
        endpoint = request.url.path

        # Get limiter for endpoint
        limiter = self.get_limiter(endpoint)

        # Check rate limit
        is_allowed, rate_limit_info = limiter.check_rate_limit(client_id)

        # Check throttling
        throttle_delay = None
        throttler = self.get_throttler(endpoint)
        if throttler:
            throttle_delay = throttler.throttle(client_id)

        # Update statistics
        with self.stats_lock:
            self.stats.total_requests += 1
            if is_allowed:
                self.stats.allowed_requests += 1
            else:
                self.stats.blocked_requests += 1
                self.stats.rate_limit_hits += 1
            if throttle_delay:
                self.stats.throttle_applications += 1

        return is_allowed, rate_limit_info, throttle_delay

    def _get_client_id(self, request: Request) -> str:
        """
        Get client identifier for rate limiting.

        Args:
            request: FastAPI request

        Returns:
            Client identifier
        """
        # Try to get user ID from headers (if authenticated)
        user_id = request.headers.get("X-User-ID")
        if user_id:
            return f"user:{user_id}"

        # Fall back to IP address
        client_ip = request.client.host if request.client else "unknown"
        return f"ip:{client_ip}"

    def add_rate_limit_headers(self, response: Response, rate_limit_info: dict[str, Any]):
        """
        Add rate limit headers to response.

        Args:
            response: FastAPI response
            rate_limit_info: Rate limit information
        """
        if rate_limit_info.get("allowed"):
            response.headers["X-RateLimit-Limit-Second"] = str(int(rate_limit_info.get("limit", 0)))
            response.headers["X-RateLimit-Remaining-Second"] = str(
                rate_limit_info.get("remaining_second", 0)
            )
            response.headers["X-RateLimit-Remaining-Minute"] = str(
                rate_limit_info.get("remaining_minute", 0)
            )
            response.headers["X-RateLimit-Remaining-Hour"] = str(
                rate_limit_info.get("remaining_hour", 0)
            )
            response.headers["X-RateLimit-Reset"] = str(
                int(time.time() + rate_limit_info.get("reset_after", 60))
            )
        else:
            retry_after = rate_limit_info.get("retry_after", 60)
            response.headers["Retry-After"] = str(int(retry_after))
            response.headers["X-RateLimit-Limit"] = str(int(rate_limit_info.get("limit", 0)))
            response.headers["X-RateLimit-Remaining"] = "0"

    def get_stats(self) -> dict[str, Any]:
        """Get rate limiting statistics."""
        with self.stats_lock:
            return {
                "total_requests": self.stats.total_requests,
                "allowed_requests": self.stats.allowed_requests,
                "blocked_requests": self.stats.blocked_requests,
                "rate_limit_hits": self.stats.rate_limit_hits,
                "throttle_applications": self.stats.throttle_applications,
                "block_rate": (
                    self.stats.blocked_requests / self.stats.total_requests
                    if self.stats.total_requests > 0
                    else 0.0
                ),
                "last_reset": self.stats.last_reset.isoformat(),
            }

    def cleanup_old_entries(self):
        """Clean up old rate limit entries."""
        for limiter in self.limiters.values():
            limiter.cleanup_old_entries()


# Global rate limiter instance
_enhanced_rate_limiter = EnhancedRateLimiter()


class RateLimitMiddleware(BaseHTTPMiddleware):
    """Middleware for enhanced rate limiting."""

    def __init__(self, app, skip_paths: list[str] | None = None):
        """
        Initialize rate limit middleware.

        Args:
            app: FastAPI application
            skip_paths: Paths to skip rate limiting
        """
        super().__init__(app)
        self.skip_paths = skip_paths or ["/health", "/api/health", "/", "/docs", "/openapi.json"]
        self.rate_limiter = _enhanced_rate_limiter

    # Loopback hosts (localhost desktop mode) - exempt for high-frequency paths
    # "testclient": Starlette TestClient ASGI transport host; treated as localhost-equivalent.
    _LOOPBACK_HOSTS = frozenset({"127.0.0.1", "localhost", "::1", "testclient"})

    async def dispatch(self, request: Request, call_next):
        """Process request with rate limiting."""
        path = request.url.path
        if path in self.skip_paths:
            return await call_next(request)

        client_host = request.client.host if request.client else ""
        try:
            from backend.core.settings import settings

            localhost_exempt = getattr(settings, "rate_limit_localhost_exempt", True)
        except ImportError:
            localhost_exempt = True
        # Desktop mode: exempt localhost entirely when configured.
        # Rate limiting localhost only harms UX (Import, bulk ops) without protecting against abuse.
        # See: repeated "Rate limit exceeded: requests_per_minute" + "Import Failed" reports.
        if localhost_exempt and client_host in self._LOOPBACK_HOSTS:
            return await call_next(request)

        # Check rate limit
        is_allowed, rate_limit_info, throttle_delay = self.rate_limiter.check_rate_limit(request)

        # Apply throttling delay if needed
        if throttle_delay and throttle_delay > 0:
            await asyncio.sleep(throttle_delay)

        # Check if rate limited
        if not is_allowed:
            response = Response(
                content=f"Rate limit exceeded: {rate_limit_info.get('reason', 'unknown')}",
                status_code=status.HTTP_429_TOO_MANY_REQUESTS,
            )
            self.rate_limiter.add_rate_limit_headers(response, rate_limit_info)
            return response

        # Process request
        response = await call_next(request)

        # Add rate limit headers
        self.rate_limiter.add_rate_limit_headers(response, rate_limit_info)

        return response


# Export
__all__ = [
    "EnhancedRateLimiter",
    "RateLimitConfig",
    "RateLimitMiddleware",
    "RateLimitStats",
    "SlidingWindowRateLimiter",
    "Throttler",
]
