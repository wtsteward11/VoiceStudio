"""
RED Metrics - Per-Context Rate, Error, Duration.

Task 3.1: Observability formalization.
Tracks request rate, error count, and duration percentiles per bounded context.
"""

from __future__ import annotations

import logging
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Any

logger = logging.getLogger(__name__)

# Bounded contexts from ADR-047
CONTEXTS = (
    "voice",
    "audio",
    "project",
    "ml",
    "platform",
    "plugins",
    "media",
)


@dataclass
class ContextMetrics:
    """RED metrics for a single context."""

    context: str
    request_count: int = 0
    error_count: int = 0
    duration_samples: deque = field(default_factory=lambda: deque(maxlen=1000))

    def record(self, duration_sec: float, is_error: bool = False) -> None:
        """Record a request."""
        self.request_count += 1
        if is_error:
            self.error_count += 1
        self.duration_samples.append(duration_sec)

    def rate_per_minute(self, window_sec: float = 60.0) -> float:
        """Requests per minute (extrapolated from count)."""
        if window_sec <= 0:
            return 0.0
        return (self.request_count / window_sec) * 60.0

    def error_rate(self) -> float:
        """Error rate (0.0-1.0)."""
        if self.request_count == 0:
            return 0.0
        return self.error_count / self.request_count

    def duration_p95_ms(self) -> float | None:
        """95th percentile duration in milliseconds."""
        if not self.duration_samples:
            return None
        sorted_durations = sorted(self.duration_samples)
        idx = int(len(sorted_durations) * 0.95) - 1
        idx = max(0, idx)
        return sorted_durations[idx] * 1000.0

    def to_dict(self) -> dict[str, Any]:
        """Export as dict for API."""
        return {
            "context": self.context,
            "request_count": self.request_count,
            "error_count": self.error_count,
            "error_rate": round(self.error_rate(), 4),
            "duration_p95_ms": (
                round(self.duration_p95_ms(), 2) if self.duration_p95_ms() is not None else None
            ),
        }


class REDMetricsCollector:
    """Collects RED metrics per context."""

    def __init__(self) -> None:
        self._metrics: dict[str, ContextMetrics] = {
            ctx: ContextMetrics(context=ctx) for ctx in CONTEXTS
        }
        self._lock = threading.RLock()
        self._start_time = time.monotonic()

    def record(
        self,
        context: str,
        duration_sec: float,
        is_error: bool = False,
    ) -> None:
        """Record a request for a context."""
        with self._lock:
            m = self._metrics.get(context)
            if m is None:
                m = ContextMetrics(context=context)
                self._metrics[context] = m
            m.record(duration_sec, is_error)

    def get_all(self) -> dict[str, dict[str, Any]]:
        """Get all context metrics as dict."""
        with self._lock:
            return {k: v.to_dict() for k, v in self._metrics.items()}

    def get_context(self, context: str) -> dict[str, Any] | None:
        """Get metrics for a single context."""
        with self._lock:
            m = self._metrics.get(context)
            return m.to_dict() if m else None

    def reset(self) -> None:
        """Reset all metrics."""
        with self._lock:
            self._metrics = {ctx: ContextMetrics(context=ctx) for ctx in CONTEXTS}
            self._start_time = time.monotonic()


_collector: REDMetricsCollector | None = None


def get_red_metrics() -> REDMetricsCollector:
    """Get the global RED metrics collector."""
    global _collector
    if _collector is None:
        _collector = REDMetricsCollector()
    return _collector
