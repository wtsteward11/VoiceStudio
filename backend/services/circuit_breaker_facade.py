"""
Circuit breaker facade for routes (GAP-008).

Routes must import from this service-layer facade instead of backend.core.circuit_breaker
to respect the architecture boundary. The facade re-exports engine circuit breaker utilities.
"""

from __future__ import annotations

from backend.core.circuit_breaker import (
    CircuitBreakerOpenError,
    get_engine_breaker,
    get_engine_breaker_metrics,
    get_engine_breaker_stats,
    get_engine_breaker_summary,
    reset_engine_breaker,
)

__all__ = [
    "CircuitBreakerOpenError",
    "get_engine_breaker",
    "get_engine_breaker_metrics",
    "get_engine_breaker_stats",
    "get_engine_breaker_summary",
    "reset_engine_breaker",
]
