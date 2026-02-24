"""Circuit breaker state transition integration tests.

Validates CLOSED -> OPEN -> HALF_OPEN -> CLOSED recovery cycle.
"""

from __future__ import annotations

import time

import pytest

from backend.services.circuit_breaker import CircuitBreaker


@pytest.fixture
def breaker():
    """Create a circuit breaker with short recovery timeout for testing."""
    return CircuitBreaker(
        failure_threshold=3,
        recovery_timeout=0.5,
        half_open_max_calls=2,
    )


class TestStateTransitions:
    """Test circuit breaker state machine transitions."""

    def test_initial_state_is_closed(self, breaker: CircuitBreaker):
        assert breaker.state == "closed"

    def test_closed_to_open_after_threshold(self, breaker: CircuitBreaker):
        """CLOSED -> OPEN after failure_threshold consecutive failures."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state == "open"

    def test_open_rejects_calls(self, breaker: CircuitBreaker):
        """OPEN state should indicate calls should be rejected."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state == "open"
        assert not breaker.allow_request()

    def test_open_to_half_open_after_timeout(self, breaker: CircuitBreaker):
        """OPEN -> HALF_OPEN after recovery_timeout elapses."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state == "open"

        time.sleep(0.6)
        assert breaker.allow_request()
        assert breaker.state == "half_open"

    def test_half_open_to_closed_on_success(self, breaker: CircuitBreaker):
        """HALF_OPEN -> CLOSED after successful call."""
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.6)
        breaker.allow_request()

        breaker.record_success()
        assert breaker.state == "closed"

    def test_half_open_to_open_on_failure(self, breaker: CircuitBreaker):
        """HALF_OPEN -> OPEN if call fails during half-open."""
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.6)
        breaker.allow_request()

        breaker.record_failure()
        assert breaker.state == "open"

    def test_success_resets_failure_count(self, breaker: CircuitBreaker):
        """Success in CLOSED state resets consecutive failure count."""
        breaker.record_failure()
        breaker.record_failure()
        breaker.record_success()

        breaker.record_failure()
        assert breaker.state == "closed"

    def test_statistics_tracked(self, breaker: CircuitBreaker):
        """Circuit breaker should track call statistics."""
        breaker.record_success()
        breaker.record_failure()
        stats = breaker.get_stats()
        assert stats["total_successes"] >= 1
        assert stats["total_failures"] >= 1
