"""
Simple Rate Limiting for VoiceStudio Backend API

Provides basic rate limiting to prevent abuse.
For production, consider using a more robust solution like slowapi or redis-based rate limiting.
"""

from __future__ import annotations

import time
from collections import defaultdict

from fastapi import HTTPException, Request, status

# In-memory rate limit storage
# In production, use Redis or similar for distributed systems
_rate_limit_store: dict[str, dict[str, tuple[float, int]]] = defaultdict(dict)


class RateLimiter:
    """Simple rate limiter for API endpoints."""

    def __init__(self, requests_per_minute: int = 60, requests_per_hour: int = 1000):
        """
        Initialize rate limiter.

        Args:
            requests_per_minute: Maximum requests per minute per IP
            requests_per_hour: Maximum requests per hour per IP
        """
        self.requests_per_minute = requests_per_minute
        self.requests_per_hour = requests_per_hour

    def check_rate_limit(self, request: Request) -> None:
        """
        Check if request exceeds rate limit.

        Raises:
            HTTPException: If rate limit is exceeded
        """
        # Get client IP
        client_ip = request.client.host if request.client else "unknown"

        # Get current time
        current_time = time.time()

        # Get or create rate limit data for this IP
        ip_data = _rate_limit_store[client_ip]

        # Clean old entries (older than 1 hour)
        minute_key = "minute"
        hour_key = "hour"

        # Check minute limit
        if minute_key in ip_data:
            last_time, count = ip_data[minute_key]
            if current_time - last_time < 60:  # Within last minute
                if count >= self.requests_per_minute:
                    raise HTTPException(
                        status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                        detail=f"Rate limit exceeded: {self.requests_per_minute} requests per minute",
                    )
                ip_data[minute_key] = (last_time, count + 1)
            else:
                # Reset minute counter
                ip_data[minute_key] = (current_time, 1)
        else:
            ip_data[minute_key] = (current_time, 1)

        # Check hour limit
        if hour_key in ip_data:
            last_time, count = ip_data[hour_key]
            if current_time - last_time < 3600:  # Within last hour
                if count >= self.requests_per_hour:
                    raise HTTPException(
                        status_code=status.HTTP_429_TOO_MANY_REQUESTS,
                        detail=f"Rate limit exceeded: {self.requests_per_hour} requests per hour",
                    )
                ip_data[hour_key] = (last_time, count + 1)
            else:
                # Reset hour counter
                ip_data[hour_key] = (current_time, 1)
        else:
            ip_data[hour_key] = (current_time, 1)

    def cleanup_old_entries(self, max_age_seconds: int = 3600):
        """Clean up old rate limit entries."""
        current_time = time.time()
        ips_to_remove = []

        for ip, ip_data in _rate_limit_store.items():
            # Check if all entries are old
            all_old = True
            for _key, (last_time, _) in ip_data.items():
                if current_time - last_time < max_age_seconds:
                    all_old = False
                    break

            if all_old:
                ips_to_remove.append(ip)

        for ip in ips_to_remove:
            del _rate_limit_store[ip]


# Default rate limiter instance
default_rate_limiter = RateLimiter(requests_per_minute=60, requests_per_hour=1000)

# Rate limiters for specific endpoints
synthesis_rate_limiter = RateLimiter(
    requests_per_minute=30,  # Lower limit for synthesis (more resource-intensive)
    requests_per_hour=500,
)

training_rate_limiter = RateLimiter(
    requests_per_minute=10, requests_per_hour=50  # Very low limit for training
)


# Loopback hosts (desktop mode) - exempt when configured
_LOOPBACK_HOSTS = frozenset({"127.0.0.1", "localhost", "::1"})


def rate_limit_middleware(request: Request, call_next):
    """Middleware to apply rate limiting."""
    # Skip rate limiting for health checks
    if request.url.path in ["/health", "/api/health", "/"]:
        return call_next(request)

    # Desktop mode: exempt localhost when configured (same as enhanced limiter)
    client_host = request.client.host if request.client else ""
    try:
        from backend.core.settings import settings

        localhost_exempt = getattr(settings, "rate_limit_localhost_exempt", True)
    except ImportError:
        localhost_exempt = True
    if localhost_exempt and client_host in _LOOPBACK_HOSTS:
        return call_next(request)

    # Apply default rate limiting
    try:
        default_rate_limiter.check_rate_limit(request)
    except HTTPException:
        raise

    # Cleanup old entries periodically (every 100 requests, roughly)
    import random

    if random.random() < 0.01:  # 1% chance
        default_rate_limiter.cleanup_old_entries()

    return call_next(request)
