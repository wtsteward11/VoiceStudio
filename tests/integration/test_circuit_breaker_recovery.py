"""Circuit breaker state transition integration tests.

Validates CLOSED -> OPEN -> HALF_OPEN -> CLOSED recovery cycle.

Aligned with the current ``backend.services.circuit_breaker.CircuitBreaker`` API:

- ``CircuitBreaker`` requires ``name`` as a positional argument.
- ``state`` returns a ``CircuitState`` enum (CLOSED / OPEN / HALF_OPEN), not a
  string.
- ``get_stats()`` returns a ``CircuitBreakerStats`` dataclass with attribute
  access (e.g. ``stats.total_failures``), not a dict.
"""

from __future__ import annotations

import time

import pytest

from backend.services.circuit_breaker import CircuitBreaker, CircuitState


@pytest.fixture
def breaker() -> CircuitBreaker:
    """Create a circuit breaker with short recovery timeout for testing."""
    return CircuitBreaker(
        name="test-recovery",
        failure_threshold=3,
        recovery_timeout=0.5,
        half_open_max_calls=2,
    )


class TestStateTransitions:
    """Test circuit breaker state machine transitions."""

    def test_initial_state_is_closed(self, breaker: CircuitBreaker) -> None:
        assert breaker.state is CircuitState.CLOSED

    def test_closed_to_open_after_threshold(self, breaker: CircuitBreaker) -> None:
        """CLOSED -> OPEN after failure_threshold consecutive failures."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state is CircuitState.OPEN

    def test_open_rejects_calls(self, breaker: CircuitBreaker) -> None:
        """OPEN state should indicate calls should be rejected."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state is CircuitState.OPEN
        assert not breaker.allow_request()

    def test_open_to_half_open_after_timeout(self, breaker: CircuitBreaker) -> None:
        """OPEN -> HALF_OPEN after recovery_timeout elapses."""
        for _ in range(3):
            breaker.record_failure()
        assert breaker.state is CircuitState.OPEN

        time.sleep(0.6)
        assert breaker.allow_request()
        assert breaker.state is CircuitState.HALF_OPEN

    def test_half_open_to_closed_on_success(self, breaker: CircuitBreaker) -> None:
        """HALF_OPEN -> CLOSED after success_threshold successful calls.

        Default ``success_threshold`` is 2; ``record_success`` only transitions
        back to CLOSED once that count is reached.
        """
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.6)
        # Two HALF_OPEN admissions + two successes (default success_threshold=2)
        assert breaker.allow_request()
        breaker.record_success()
        assert breaker.allow_request()
        breaker.record_success()
        assert breaker.state is CircuitState.CLOSED

    def test_half_open_to_open_on_failure(self, breaker: CircuitBreaker) -> None:
        """HALF_OPEN -> OPEN if call fails during half-open."""
        for _ in range(3):
            breaker.record_failure()
        time.sleep(0.6)
        breaker.allow_request()

        breaker.record_failure()
        assert breaker.state is CircuitState.OPEN

    def test_success_resets_failure_count(self, breaker: CircuitBreaker) -> None:
        """Success in CLOSED state resets consecutive failure count."""
        breaker.record_failure()
        breaker.record_failure()
        breaker.record_success()

        breaker.record_failure()
        assert breaker.state is CircuitState.CLOSED

    def test_statistics_tracked(self, breaker: CircuitBreaker) -> None:
        """Circuit breaker should track call statistics via the dataclass."""
        breaker.record_success()
        breaker.record_failure()
        stats = breaker.get_stats()
        # ``CircuitBreakerStats`` is a dataclass; access fields, not keys.
        assert stats.total_calls >= 2
        assert stats.total_failures >= 1
