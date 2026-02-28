"""
Circuit Breaker Pattern Implementation (TD-014)

Provides failure isolation for engine calls to prevent cascading failures.
Based on the pattern described in Release It! by Michael Nygard.

States:
- CLOSED: Normal operation, requests pass through
- OPEN: Circuit is tripped, requests fail fast
- HALF_OPEN: Testing if service has recovered

Usage:
    breaker = CircuitBreaker(name="xtts_v2", failure_threshold=3, recovery_timeout=60.0)

    async with breaker:
        result = await engine.synthesize(text, voice)

    # Or manually:
    if breaker.allow_request():
        try:
            result = await engine.synthesize(text, voice)
            breaker.record_success()
        except Exception as e:
            breaker.record_failure()
            raise
    else:
        raise CircuitBreakerOpenError(breaker.name)
"""

from __future__ import annotations

import asyncio
import logging
import time
from collections.abc import Callable
from contextlib import asynccontextmanager
from dataclasses import dataclass
from enum import Enum, auto
from typing import TypeVar

logger = logging.getLogger(__name__)

T = TypeVar("T")


class CircuitState(Enum):
    """Circuit breaker states."""

    CLOSED = auto()  # Normal operation
    OPEN = auto()  # Failing fast
    HALF_OPEN = auto()  # Testing recovery


class CircuitBreakerError(Exception):
    """Base class for circuit breaker errors."""

    pass


class CircuitBreakerOpenError(CircuitBreakerError):
    """Raised when circuit is open and requests are blocked."""

    def __init__(self, breaker_name: str, time_until_retry: float = 0.0):
        self.breaker_name = breaker_name
        self.time_until_retry = time_until_retry
        super().__init__(
            f"Circuit breaker '{breaker_name}' is OPEN. " f"Retry in {time_until_retry:.1f}s"
        )


@dataclass
class CircuitBreakerConfig:
    """Configuration for circuit breaker behavior."""

    failure_threshold: int = 3  # Failures before opening
    success_threshold: int = 2  # Successes to close from half-open
    recovery_timeout: float = 60.0  # Seconds before trying half-open
    half_open_max_calls: int = 3  # Max concurrent calls in half-open


@dataclass
class CircuitBreakerStats:
    """Statistics for circuit breaker monitoring."""

    name: str
    state: CircuitState = CircuitState.CLOSED
    failure_count: int = 0
    success_count: int = 0
    last_failure_time: float | None = None
    last_success_time: float | None = None
    open_count: int = 0  # Times circuit was opened
    total_calls: int = 0
    total_failures: int = 0
    total_blocked: int = 0  # Calls blocked by open circuit


@dataclass
class CircuitBreakerMetrics:
    """
    GAP-I24: Enhanced metrics for circuit breaker dashboard.

    Includes computed fields like failure_rate and state change tracking.
    """

    name: str
    state: str  # CLOSED, OPEN, HALF_OPEN
    failure_count: int
    success_count: int
    total_calls: int
    blocked_requests: int
    last_failure_time: float | None
    last_state_change: float | None
    failure_rate: float  # failures / total_calls (0.0-1.0)
    config: CircuitBreakerConfig | None = None  # Optional config exposure


class CircuitBreaker:
    """
    Circuit breaker implementation with thread-safe state management.

    Features:
    - Configurable failure/success thresholds
    - Automatic recovery after timeout
    - Half-open state for gradual recovery
    - Statistics tracking for monitoring
    """

    def __init__(
        self,
        name: str,
        failure_threshold: int = 3,
        success_threshold: int = 2,
        recovery_timeout: float = 60.0,
        half_open_max_calls: int = 3,
        on_state_change: Callable[[str, CircuitState, CircuitState], None] | None = None,
    ):
        self.name = name
        self.config = CircuitBreakerConfig(
            failure_threshold=failure_threshold,
            success_threshold=success_threshold,
            recovery_timeout=recovery_timeout,
            half_open_max_calls=half_open_max_calls,
        )
        self._on_state_change = on_state_change

        # State
        self._state = CircuitState.CLOSED
        self._failure_count = 0
        self._success_count = 0
        self._last_failure_time: float | None = None
        self._last_success_time: float | None = None
        self._last_state_change: float | None = None  # GAP-I24: Track state transitions
        self._half_open_calls = 0

        # Stats
        self._open_count = 0
        self._total_calls = 0
        self._total_failures = 0
        self._total_blocked = 0

        # Thread safety
        self._lock = asyncio.Lock()

    @property
    def state(self) -> CircuitState:
        """Current circuit state."""
        return self._state

    @property
    def is_closed(self) -> bool:
        """True if circuit is closed (normal operation)."""
        return self._state == CircuitState.CLOSED

    @property
    def is_open(self) -> bool:
        """True if circuit is open (failing fast)."""
        return self._state == CircuitState.OPEN

    @property
    def is_half_open(self) -> bool:
        """True if circuit is half-open (testing recovery)."""
        return self._state == CircuitState.HALF_OPEN

    def _set_state(self, new_state: CircuitState) -> None:
        """Change state and notify callback."""
        if self._state != new_state:
            old_state = self._state
            self._state = new_state
            self._last_state_change = time.monotonic()  # GAP-I24: Track state change time

            if new_state == CircuitState.OPEN:
                self._open_count += 1
                logger.warning(
                    "Circuit breaker '%s' OPENED after %d failures", self.name, self._failure_count
                )
            elif new_state == CircuitState.HALF_OPEN:
                logger.info("Circuit breaker '%s' entering HALF_OPEN state", self.name)
                self._half_open_calls = 0
            elif new_state == CircuitState.CLOSED:
                logger.info("Circuit breaker '%s' CLOSED (recovered)", self.name)

            if self._on_state_change:
                try:
                    self._on_state_change(self.name, old_state, new_state)
                except Exception as e:
                    logger.warning("State change callback error: %s", e)

    def _should_attempt_reset(self) -> bool:
        """Check if we should try half-open after timeout."""
        if self._state != CircuitState.OPEN:
            return False
        if self._last_failure_time is None:
            return True
        elapsed = time.monotonic() - self._last_failure_time
        return elapsed >= self.config.recovery_timeout

    def allow_request(self) -> bool:
        """
        Check if a request should be allowed.

        Returns True if request can proceed, False if circuit is open.
        Note: This is a non-blocking check. For async code, use the context manager.
        """
        if self._state == CircuitState.CLOSED:
            return True

        if self._state == CircuitState.OPEN:
            if self._should_attempt_reset():
                self._set_state(CircuitState.HALF_OPEN)
                self._half_open_calls += 1  # First call entering half-open
                return True
            self._total_blocked += 1
            return False

        # HALF_OPEN: limit concurrent calls
        if self._half_open_calls < self.config.half_open_max_calls:
            self._half_open_calls += 1  # Track in-flight calls for concurrency limiting
            return True

        self._total_blocked += 1
        return False

    def record_success(self) -> None:
        """Record a successful call."""
        self._total_calls += 1
        self._last_success_time = time.monotonic()  # GAP-I24: Track success time

        if self._state == CircuitState.HALF_OPEN:
            # Decrement in-flight counter (allow_request incremented it)
            if self._half_open_calls > 0:
                self._half_open_calls -= 1
            self._success_count += 1
            if self._success_count >= self.config.success_threshold:
                self._failure_count = 0
                self._success_count = 0
                self._set_state(CircuitState.CLOSED)
        elif self._state == CircuitState.CLOSED:
            # Reset failure count on success
            if self._failure_count > 0:
                self._failure_count = 0

    def record_failure(self) -> None:
        """Record a failed call."""
        self._total_calls += 1
        self._total_failures += 1
        self._failure_count += 1
        self._last_failure_time = time.monotonic()

        if self._state == CircuitState.HALF_OPEN:
            # Decrement in-flight counter (allow_request incremented it)
            if self._half_open_calls > 0:
                self._half_open_calls -= 1
            # Any failure in half-open re-opens circuit
            self._set_state(CircuitState.OPEN)
        elif self._state == CircuitState.CLOSED:
            if self._failure_count >= self.config.failure_threshold:
                self._set_state(CircuitState.OPEN)

    def time_until_retry(self) -> float:
        """Seconds until circuit may transition to half-open."""
        if self._state != CircuitState.OPEN:
            return 0.0
        if self._last_failure_time is None:
            return 0.0
        elapsed = time.monotonic() - self._last_failure_time
        remaining = self.config.recovery_timeout - elapsed
        return max(0.0, remaining)

    def reset(self) -> None:
        """Force reset to closed state (manual recovery)."""
        self._state = CircuitState.CLOSED
        self._failure_count = 0
        self._success_count = 0
        self._half_open_calls = 0
        logger.info("Circuit breaker '%s' manually reset", self.name)

    def get_stats(self) -> CircuitBreakerStats:
        """Get current statistics."""
        return CircuitBreakerStats(
            name=self.name,
            state=self._state,
            failure_count=self._failure_count,
            success_count=self._success_count,
            last_failure_time=self._last_failure_time,
            last_success_time=self._last_success_time,
            open_count=self._open_count,
            total_calls=self._total_calls,
            total_failures=self._total_failures,
            total_blocked=self._total_blocked,
        )

    def get_metrics(self) -> CircuitBreakerMetrics:
        """
        GAP-I24: Get enhanced metrics for dashboard visualization.

        Returns:
            CircuitBreakerMetrics with computed failure rate and state change tracking.
        """
        total = max(self._total_calls, 1)  # Avoid division by zero
        failure_rate = self._total_failures / total

        return CircuitBreakerMetrics(
            name=self.name,
            state=self._state.name,
            failure_count=self._failure_count,
            success_count=self._success_count,
            total_calls=self._total_calls,
            blocked_requests=self._total_blocked,
            last_failure_time=self._last_failure_time,
            last_state_change=self._last_state_change,
            failure_rate=failure_rate,
            config=self.config,
        )

    @asynccontextmanager
    async def __call__(self):
        """
        Async context manager for protected calls.

        Usage:
            async with breaker():
                result = await risky_operation()
        """
        async with self._lock:
            if not self.allow_request():
                raise CircuitBreakerOpenError(self.name, self.time_until_retry())
            # Note: allow_request() now increments _half_open_calls when in HALF_OPEN state

        try:
            yield
            self.record_success()
        except Exception:
            self.record_failure()
            raise

    async def execute(self, func: Callable[..., T], *args, **kwargs) -> T:
        """
        Execute a function with circuit breaker protection.

        Usage:
            result = await breaker.execute(engine.synthesize, text, voice=voice)
        """
        async with self():
            if asyncio.iscoroutinefunction(func):
                return await func(*args, **kwargs)
            else:
                return func(*args, **kwargs)


class CircuitBreakerRegistry:
    """
    Registry for managing multiple circuit breakers.

    Provides centralized access to circuit breakers by name (e.g., engine ID).
    """

    def __init__(
        self,
        default_failure_threshold: int = 3,
        default_recovery_timeout: float = 60.0,
    ):
        self._breakers: dict[str, CircuitBreaker] = {}
        self._default_failure_threshold = default_failure_threshold
        self._default_recovery_timeout = default_recovery_timeout
        self._lock = asyncio.Lock()

    def get(self, name: str) -> CircuitBreaker:
        """Get or create a circuit breaker for the given name."""
        if name not in self._breakers:
            self._breakers[name] = CircuitBreaker(
                name=name,
                failure_threshold=self._default_failure_threshold,
                recovery_timeout=self._default_recovery_timeout,
            )
        return self._breakers[name]

    def get_all_stats(self) -> dict[str, CircuitBreakerStats]:
        """Get stats for all circuit breakers."""
        return {name: breaker.get_stats() for name, breaker in self._breakers.items()}

    def get_all_metrics(self) -> list[CircuitBreakerMetrics]:
        """GAP-I24: Get metrics for all circuit breakers (for dashboard)."""
        return [breaker.get_metrics() for breaker in self._breakers.values()]

    def get_summary(self) -> dict[str, int]:
        """GAP-I24: Get summary counts by state."""
        metrics = self.get_all_metrics()
        return {
            "total": len(metrics),
            "open": sum(1 for m in metrics if m.state == "OPEN"),
            "half_open": sum(1 for m in metrics if m.state == "HALF_OPEN"),
            "closed": sum(1 for m in metrics if m.state == "CLOSED"),
        }

    def reset(self, name: str) -> bool:
        """Reset a specific circuit breaker. Returns True if found."""
        if name in self._breakers:
            self._breakers[name].reset()
            return True
        return False

    def reset_all(self) -> None:
        """Reset all circuit breakers."""
        for breaker in self._breakers.values():
            breaker.reset()


# Global registry for engine circuit breakers
_engine_breakers = CircuitBreakerRegistry(
    default_failure_threshold=3,
    default_recovery_timeout=60.0,
)


def get_engine_breaker(engine_id: str) -> CircuitBreaker:
    """Get circuit breaker for an engine by ID."""
    return _engine_breakers.get(engine_id)


def get_engine_breaker_stats() -> dict[str, CircuitBreakerStats]:
    """Get all engine circuit breaker stats."""
    return _engine_breakers.get_all_stats()


def get_engine_breaker_metrics() -> list[CircuitBreakerMetrics]:
    """GAP-I24: Get all engine circuit breaker metrics for dashboard."""
    return _engine_breakers.get_all_metrics()


def get_engine_breaker_summary() -> dict[str, int]:
    """GAP-I24: Get summary counts of circuit breaker states."""
    return _engine_breakers.get_summary()


def reset_engine_breaker(engine_id: str) -> bool:
    """Reset a specific engine's circuit breaker."""
    return _engine_breakers.reset(engine_id)
