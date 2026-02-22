"""
Engine Performance Metrics Collection
Track synthesis times, cache hit rates, and other performance metrics per engine

Compatible with:
- Python 3.10+
- All VoiceStudio engines
"""

from __future__ import annotations

import logging
import time
from collections import defaultdict
from collections.abc import Callable
from contextlib import contextmanager
from threading import Lock, RLock
from typing import Any

_get_metrics_collector: Callable[..., Any] | None = None
HAS_METRICS = False

try:
    from app.core.monitoring.metrics import get_metrics_collector

    _get_metrics_collector = get_metrics_collector
    HAS_METRICS = True
except ImportError:
    pass

logger = logging.getLogger(__name__)


class EnginePerformanceMetrics:
    """
    Performance metrics collector for engines.

    Tracks:
    - Synthesis times per engine
    - Cache hit rates
    - Error rates
    - Memory usage
    - Throughput
    """

    def __init__(self):
        """Initialize engine performance metrics collector."""
        self._lock = RLock()

        # Per-engine metrics
        self._synthesis_times: dict[str, list] = defaultdict(list)
        self._cache_hits: dict[str, int] = defaultdict(int)
        self._cache_misses: dict[str, int] = defaultdict(int)
        self._errors: dict[str, int] = defaultdict(int)
        self._total_requests: dict[str, int] = defaultdict(int)

        # Timing data (last 1000 operations per engine)
        self._max_timing_history = 1000

        # Metrics collector integration
        self._metrics_collector = None
        if HAS_METRICS:
            try:
                self._metrics_collector = (
                    _get_metrics_collector() if _get_metrics_collector is not None else None
                )
            except Exception as e:
                logger.warning(f"Failed to get metrics collector: {e}")

    def record_synthesis_time(self, engine_name: str, duration: float, cached: bool = False):
        """
        Record synthesis time for an engine.

        Args:
            engine_name: Engine name
            duration: Synthesis duration in seconds
            cached: Whether result was from cache
        """
        with self._lock:
            # Store timing data
            self._synthesis_times[engine_name].append(duration)
            if len(self._synthesis_times[engine_name]) > self._max_timing_history:
                self._synthesis_times[engine_name] = self._synthesis_times[engine_name][
                    -self._max_timing_history :
                ]

            # Record cache hit/miss
            if cached:
                self._cache_hits[engine_name] += 1
            else:
                self._cache_misses[engine_name] += 1

            self._total_requests[engine_name] += 1

            # Record to metrics collector if available
            if self._metrics_collector:
                self._metrics_collector.timer(
                    f"engine.{engine_name}.synthesis_time",
                    duration,
                    tags={"engine": engine_name, "cached": str(cached)},
                )
                if cached:
                    self._metrics_collector.increment(
                        f"engine.{engine_name}.cache_hits",
                        tags={"engine": engine_name},
                    )
                else:
                    self._metrics_collector.increment(
                        f"engine.{engine_name}.cache_misses",
                        tags={"engine": engine_name},
                    )

    def record_error(self, engine_name: str, error_type: str = "unknown"):
        """
        Record an error for an engine.

        Args:
            engine_name: Engine name
            error_type: Type of error
        """
        with self._lock:
            self._errors[engine_name] += 1

            if self._metrics_collector:
                self._metrics_collector.increment(
                    f"engine.{engine_name}.errors",
                    tags={"engine": engine_name, "error_type": error_type},
                )

    def record_cache_hit(self, engine_name: str):
        """Record a cache hit for an engine."""
        with self._lock:
            self._cache_hits[engine_name] += 1

            if self._metrics_collector:
                self._metrics_collector.increment(
                    f"engine.{engine_name}.cache_hits",
                    tags={"engine": engine_name},
                )

    def record_cache_miss(self, engine_name: str):
        """Record a cache miss for an engine."""
        with self._lock:
            self._cache_misses[engine_name] += 1

            if self._metrics_collector:
                self._metrics_collector.increment(
                    f"engine.{engine_name}.cache_misses",
                    tags={"engine": engine_name},
                )

    def get_engine_stats(self, engine_name: str) -> dict[str, Any]:
        """
        Get performance statistics for an engine.

        Args:
            engine_name: Engine name

        Returns:
            Dictionary with engine statistics
        """
        with self._lock:
            synthesis_times = self._synthesis_times.get(engine_name, [])
            cache_hits = self._cache_hits.get(engine_name, 0)
            cache_misses = self._cache_misses.get(engine_name, 0)
            errors = self._errors.get(engine_name, 0)
            total_requests = self._total_requests.get(engine_name, 0)

            total_cache_requests = cache_hits + cache_misses
            cache_hit_rate = (
                (cache_hits / total_cache_requests * 100) if total_cache_requests > 0 else 0.0
            )

            error_rate = (errors / total_requests * 100) if total_requests > 0 else 0.0

            stats = {
                "engine_name": engine_name,
                "total_requests": total_requests,
                "cache_hits": cache_hits,
                "cache_misses": cache_misses,
                "cache_hit_rate": f"{cache_hit_rate:.2f}%",
                "errors": errors,
                "error_rate": f"{error_rate:.2f}%",
            }

            # Add synthesis time statistics
            if synthesis_times:
                sorted_times = sorted(synthesis_times)
                stats["synthesis_times"] = {
                    "count": len(synthesis_times),
                    "min": min(synthesis_times),
                    "max": max(synthesis_times),
                    "mean": sum(synthesis_times) / len(synthesis_times),
                    "p50": sorted_times[len(synthesis_times) // 2],
                    "p95": (
                        sorted_times[int(len(synthesis_times) * 0.95)]
                        if len(synthesis_times) > 0
                        else 0.0
                    ),
                    "p99": (
                        sorted_times[int(len(synthesis_times) * 0.99)]
                        if len(synthesis_times) > 0
                        else 0.0
                    ),
                }
            else:
                stats["synthesis_times"] = None

            return stats

    def get_all_stats(self) -> dict[str, Any]:
        """
        Get performance statistics for all engines.

        Returns:
            Dictionary with statistics for all engines
        """
        with self._lock:
            all_engines: set[str] = set()
            all_engines.update(self._synthesis_times.keys())
            all_engines.update(self._cache_hits.keys())
            all_engines.update(self._cache_misses.keys())
            all_engines.update(self._errors.keys())
            all_engines.update(self._total_requests.keys())

            return {engine_name: self.get_engine_stats(engine_name) for engine_name in all_engines}

    def get_summary(self) -> dict[str, Any]:
        """
        Get summary statistics across all engines.

        Returns:
            Dictionary with summary statistics
        """
        with self._lock:
            all_stats = self.get_all_stats()

            total_requests = sum(stats.get("total_requests", 0) for stats in all_stats.values())
            total_cache_hits = sum(stats.get("cache_hits", 0) for stats in all_stats.values())
            total_cache_misses = sum(stats.get("cache_misses", 0) for stats in all_stats.values())
            total_errors = sum(stats.get("errors", 0) for stats in all_stats.values())

            total_cache_requests = total_cache_hits + total_cache_misses
            overall_cache_hit_rate = (
                (total_cache_hits / total_cache_requests * 100) if total_cache_requests > 0 else 0.0
            )

            overall_error_rate = (
                (total_errors / total_requests * 100) if total_requests > 0 else 0.0
            )

            return {
                "total_engines": len(all_stats),
                "total_requests": total_requests,
                "total_cache_hits": total_cache_hits,
                "total_cache_misses": total_cache_misses,
                "overall_cache_hit_rate": f"{overall_cache_hit_rate:.2f}%",
                "total_errors": total_errors,
                "overall_error_rate": f"{overall_error_rate:.2f}%",
                "engines": all_stats,
            }

    def clear(self, engine_name: str | None = None):
        """
        Clear metrics for an engine or all engines.

        Args:
            engine_name: Engine name to clear (None = clear all)
        """
        with self._lock:
            if engine_name:
                self._synthesis_times.pop(engine_name, None)
                self._cache_hits.pop(engine_name, None)
                self._cache_misses.pop(engine_name, None)
                self._errors.pop(engine_name, None)
                self._total_requests.pop(engine_name, None)
            else:
                self._synthesis_times.clear()
                self._cache_hits.clear()
                self._cache_misses.clear()
                self._errors.clear()
                self._total_requests.clear()

    @contextmanager
    def time_synthesis(self, engine_name: str, cached: bool = False):
        """
        Context manager for timing synthesis operations.

        Args:
            engine_name: Engine name
            cached: Whether result is from cache

        Usage:
            with metrics.time_synthesis("xtts", cached=False):
                audio = engine.synthesize(...)
        """
        start_time = time.time()
        try:
            yield
        finally:
            duration = time.time() - start_time
            self.record_synthesis_time(engine_name, duration, cached=cached)


# Global engine performance metrics instance
_engine_metrics: EnginePerformanceMetrics | None = None
_metrics_lock = Lock()


def get_engine_metrics() -> EnginePerformanceMetrics:
    """Get or create global engine performance metrics instance."""
    global _engine_metrics
    with _metrics_lock:
        if _engine_metrics is None:
            _engine_metrics = EnginePerformanceMetrics()
        return _engine_metrics


def set_engine_metrics(metrics: EnginePerformanceMetrics):
    """Set global engine performance metrics instance."""
    global _engine_metrics
    with _metrics_lock:
        _engine_metrics = metrics


# Export
__all__ = [
    "EnginePerformanceMetrics",
    "get_engine_metrics",
    "set_engine_metrics",
]
