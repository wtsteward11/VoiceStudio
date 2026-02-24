"""
Tests for circuit breaker pattern implementation (TD-014).
"""

import time

import pytest

from backend.services.circuit_breaker import (
    CircuitBreaker,
    CircuitBreakerOpenError,
    CircuitBreakerRegistry,
    CircuitState,
    get_engine_breaker,
    reset_engine_breaker,
)


class TestCircuitBreaker:
    """Tests for CircuitBreaker class."""

    def test_initial_state_is_closed(self):
        """Circuit breaker starts in CLOSED state."""
        breaker = CircuitBreaker(name="test_engine")
        assert breaker.is_closed
        assert breaker.state == CircuitState.CLOSED

    def test_allows_requests_when_closed(self):
        """Requests are allowed when circuit is closed."""
        breaker = CircuitBreaker(name="test_engine")
        assert breaker.allow_request()

    def test_opens_after_failure_threshold(self):
        """Circuit opens after reaching failure threshold."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=3)

        # Record failures
        breaker.record_failure()
        assert breaker.is_closed
        breaker.record_failure()
        assert breaker.is_closed
        breaker.record_failure()  # Third failure should open

        assert breaker.is_open
        assert not breaker.allow_request()

    def test_success_resets_failure_count(self):
        """A success resets the failure count."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=3)

        breaker.record_failure()
        breaker.record_failure()
        breaker.record_success()  # Reset
        breaker.record_failure()
        breaker.record_failure()

        assert breaker.is_closed  # Should still be closed (only 2 failures after reset)

    def test_half_open_after_timeout(self):
        """Circuit transitions to HALF_OPEN after recovery timeout."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=2, recovery_timeout=0.1)

        # Open the circuit
        breaker.record_failure()
        breaker.record_failure()
        assert breaker.is_open

        # Wait for timeout
        time.sleep(0.15)

        # Should transition to half-open on next request check
        assert breaker.allow_request()
        assert breaker.is_half_open

    def test_closes_after_success_in_half_open(self):
        """Circuit closes after successful calls in HALF_OPEN."""
        breaker = CircuitBreaker(
            name="test_engine", failure_threshold=2, success_threshold=2, recovery_timeout=0.1
        )

        # Open and wait for half-open
        breaker.record_failure()
        breaker.record_failure()
        time.sleep(0.15)
        breaker.allow_request()  # Transition to half-open

        assert breaker.is_half_open

        # Record successes
        breaker.record_success()
        assert breaker.is_half_open  # Still half-open (need 2)
        breaker.record_success()

        assert breaker.is_closed  # Now closed

    def test_reopens_on_failure_in_half_open(self):
        """Circuit reopens if failure occurs in HALF_OPEN."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=2, recovery_timeout=0.1)

        # Open and wait for half-open
        breaker.record_failure()
        breaker.record_failure()
        time.sleep(0.15)
        breaker.allow_request()

        assert breaker.is_half_open

        # Record failure - should reopen
        breaker.record_failure()
        assert breaker.is_open

    def test_manual_reset(self):
        """Circuit can be manually reset to closed."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=2)

        # Open the circuit
        breaker.record_failure()
        breaker.record_failure()
        assert breaker.is_open

        # Manual reset
        breaker.reset()
        assert breaker.is_closed
        assert breaker.allow_request()

    def test_time_until_retry(self):
        """time_until_retry returns correct remaining time."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=2, recovery_timeout=1.0)

        # When closed, should be 0
        assert breaker.time_until_retry() == 0.0

        # Open the circuit
        breaker.record_failure()
        breaker.record_failure()

        # Should have approximately 1 second until retry
        remaining = breaker.time_until_retry()
        assert 0.9 <= remaining <= 1.0

    def test_stats_tracking(self):
        """Statistics are tracked correctly."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=3)

        breaker.record_success()
        breaker.record_failure()
        breaker.record_success()

        stats = breaker.get_stats()
        assert stats.name == "test_engine"
        assert stats.total_calls == 3
        assert stats.total_failures == 1
        assert stats.state == CircuitState.CLOSED

    def test_half_open_concurrency_limit_synchronous(self):
        """Synchronous allow_request() enforces half-open concurrency limit."""
        breaker = CircuitBreaker(
            name="test_engine",
            failure_threshold=2,
            recovery_timeout=0.05,
            half_open_max_calls=2,  # Only allow 2 concurrent calls in half-open
        )

        # Open the circuit
        breaker.record_failure()
        breaker.record_failure()
        assert breaker.is_open

        # Wait for recovery timeout (generous margin to avoid timing flakiness under load)
        time.sleep(0.15)

        # First call - should allow and increment counter
        assert breaker.allow_request()
        assert breaker.is_half_open
        assert breaker._half_open_calls == 1

        # Second call - should allow
        assert breaker.allow_request()
        assert breaker._half_open_calls == 2

        # Third call - should be blocked (at limit)
        assert not breaker.allow_request()
        assert breaker._half_open_calls == 2  # Still at 2

        # Complete first call successfully - should decrement counter
        breaker.record_success()
        assert breaker._half_open_calls == 1

        # Now another call should be allowed
        assert breaker.allow_request()
        assert breaker._half_open_calls == 2


class TestCircuitBreakerAsync:
    """Async tests for circuit breaker context manager."""

    @pytest.mark.asyncio
    async def test_context_manager_success(self):
        """Context manager records success on normal completion."""
        breaker = CircuitBreaker(name="test_engine")

        async with breaker():
            pass  # Simulated successful call

        stats = breaker.get_stats()
        assert stats.total_calls == 1
        assert stats.total_failures == 0

    @pytest.mark.asyncio
    async def test_context_manager_failure(self):
        """Context manager records failure on exception."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=3)

        with pytest.raises(ValueError):
            async with breaker():
                raise ValueError("Simulated failure")

        stats = breaker.get_stats()
        assert stats.total_calls == 1
        assert stats.total_failures == 1
        assert stats.failure_count == 1

    @pytest.mark.asyncio
    async def test_context_manager_blocks_when_open(self):
        """Context manager raises when circuit is open."""
        breaker = CircuitBreaker(name="test_engine", failure_threshold=2)

        # Open the circuit
        breaker.record_failure()
        breaker.record_failure()

        with pytest.raises(CircuitBreakerOpenError) as exc_info:
            async with breaker():
                pass

        assert "test_engine" in str(exc_info.value)
        assert "OPEN" in str(exc_info.value)

    @pytest.mark.asyncio
    async def test_execute_helper(self):
        """execute() helper works with both sync and async functions."""
        breaker = CircuitBreaker(name="test_engine")

        async def async_func(x):
            return x * 2

        def sync_func(x):
            return x + 1

        result1 = await breaker.execute(async_func, 5)
        assert result1 == 10

        result2 = await breaker.execute(sync_func, 5)
        assert result2 == 6


class TestCircuitBreakerRegistry:
    """Tests for CircuitBreakerRegistry."""

    def test_get_creates_new_breaker(self):
        """get() creates a new breaker if one doesn't exist."""
        registry = CircuitBreakerRegistry()

        breaker = registry.get("engine_a")
        assert breaker.name == "engine_a"

    def test_get_returns_same_breaker(self):
        """get() returns the same breaker for the same name."""
        registry = CircuitBreakerRegistry()

        breaker1 = registry.get("engine_a")
        breaker2 = registry.get("engine_a")

        assert breaker1 is breaker2

    def test_reset_single(self):
        """reset() resets a specific breaker."""
        registry = CircuitBreakerRegistry(default_failure_threshold=2)

        breaker = registry.get("engine_a")
        breaker.record_failure()
        breaker.record_failure()
        assert breaker.is_open

        registry.reset("engine_a")
        assert breaker.is_closed

    def test_reset_all(self):
        """reset_all() resets all breakers."""
        registry = CircuitBreakerRegistry(default_failure_threshold=2)

        breaker_a = registry.get("engine_a")
        breaker_b = registry.get("engine_b")

        breaker_a.record_failure()
        breaker_a.record_failure()
        breaker_b.record_failure()
        breaker_b.record_failure()

        assert breaker_a.is_open
        assert breaker_b.is_open

        registry.reset_all()

        assert breaker_a.is_closed
        assert breaker_b.is_closed

    def test_get_all_stats(self):
        """get_all_stats() returns stats for all breakers."""
        registry = CircuitBreakerRegistry()

        registry.get("engine_a").record_success()
        registry.get("engine_b").record_failure()

        stats = registry.get_all_stats()

        assert "engine_a" in stats
        assert "engine_b" in stats
        assert stats["engine_a"].total_calls == 1
        assert stats["engine_b"].total_failures == 1


class TestGlobalRegistry:
    """Tests for global engine breaker functions."""

    def test_get_engine_breaker(self):
        """get_engine_breaker() returns a breaker."""
        breaker = get_engine_breaker("xtts_v2")
        assert breaker.name == "xtts_v2"

    def test_reset_engine_breaker(self):
        """reset_engine_breaker() resets a breaker."""
        breaker = get_engine_breaker("test_reset_engine")
        breaker._state = CircuitState.OPEN

        result = reset_engine_breaker("test_reset_engine")
        assert result is True
        assert breaker.is_closed
