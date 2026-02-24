"""
Circuit Breaker Recovery Under Load.

Phase 8 WS5: Verify circuit breaker opens/closes under load.
"""

from __future__ import annotations

import asyncio
import time

import pytest

from backend.services.circuit_breaker import (
    CircuitBreaker,
    CircuitBreakerOpenError,
    CircuitState,
)


@pytest.fixture
def breaker():
    """Short recovery timeout for fast tests."""
    return CircuitBreaker(
        name="test_breaker",
        failure_threshold=3,
        recovery_timeout=0.3,
        half_open_max_calls=2,
    )


class TestCircuitBreakerUnderLoad:
    """Circuit breaker behavior under simulated load."""

    def test_opens_after_consecutive_failures(self, breaker):
        """Circuit opens after failure_threshold consecutive failures."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state == CircuitState.OPEN
        assert not breaker.allow_request()

    def test_rejects_requests_when_open(self, breaker):
        """When open, allow_request returns False."""
        for _ in range(3):
            breaker.record_failure()
        for _ in range(5):
            assert not breaker.allow_request()

    def test_transitions_to_half_open_after_timeout(self, breaker):
        """After recovery_timeout, transitions to half-open."""
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.4)
        assert breaker.allow_request()
        assert breaker.state == CircuitState.HALF_OPEN

    def test_closes_after_success_in_half_open(self, breaker):
        """Success in half-open transitions to closed."""
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.4)
        breaker.allow_request()
        breaker.record_success()
        assert breaker.state == CircuitState.CLOSED

    @pytest.mark.asyncio
    async def test_async_context_manager_raises_when_open(self, breaker):
        """Async context manager raises CircuitBreakerOpenError when open."""
        for _ in range(3):
            breaker.record_failure()
        with pytest.raises(CircuitBreakerOpenError):
            async with breaker():
                pass
