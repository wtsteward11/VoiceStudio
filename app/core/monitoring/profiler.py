"""
Performance Profiling System

Provides comprehensive performance profiling utilities:
- Function execution time tracking
- Memory usage profiling
- Call stack profiling
- Performance statistics and reporting
"""

from __future__ import annotations

import functools
import logging
import time
from collections.abc import Callable
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

try:
    import psutil

    HAS_PSUTIL = True
except ImportError:
    HAS_PSUTIL = False
    psutil = None

# Try importing torch for GPU memory tracking
torch: Any = None
HAS_TORCH = False
try:
    import torch as _torch

    torch = _torch
    HAS_TORCH = True
except ImportError:  # ALLOWED: bare except - optional torch
    pass

logger = logging.getLogger(__name__)


@dataclass
class ProfileEntry:
    """Profile entry for a function call."""

    function_name: str
    call_count: int = 0
    total_time: float = 0.0
    min_time: float = float("inf")
    max_time: float = 0.0
    avg_time: float = 0.0
    total_memory_delta: float = 0.0
    max_memory_delta: float = 0.0
    total_gpu_memory_delta: float = 0.0
    max_gpu_memory_delta: float = 0.0
    last_called: datetime | None = None
    errors: int = 0
    call_stack: list[str] = field(default_factory=list)

    def update(
        self,
        execution_time: float,
        memory_delta: float = 0.0,
        gpu_memory_delta: float = 0.0,
    ):
        """Update profile entry with new execution data."""
        self.call_count += 1
        self.total_time += execution_time
        self.min_time = min(self.min_time, execution_time)
        self.max_time = max(self.max_time, execution_time)
        self.avg_time = self.total_time / self.call_count
        self.total_memory_delta += memory_delta
        self.max_memory_delta = max(self.max_memory_delta, memory_delta)
        self.total_gpu_memory_delta += gpu_memory_delta
        self.max_gpu_memory_delta = max(self.max_gpu_memory_delta, gpu_memory_delta)
        self.last_called = datetime.now()

    def record_error(self):
        """Record an error in the profile entry."""
        self.errors += 1


class PerformanceProfiler:
    """
    Performance profiler for tracking function execution times and memory usage.

    Features:
    - Function execution time tracking
    - Memory usage profiling
    - Call stack tracking
    - Error tracking
    - Statistics and reporting
    """

    def __init__(
        self,
        enabled: bool = True,
        slow_threshold_seconds: float = 1.0,
        warn_on_slow: bool = True,
    ):
        """
        Initialize performance profiler.

        Args:
            enabled: Whether profiling is enabled
            slow_threshold_seconds: Threshold for slow function warnings
            warn_on_slow: Whether to log warnings for slow functions
        """
        self.enabled = enabled
        self.slow_threshold_seconds = slow_threshold_seconds
        self.warn_on_slow = warn_on_slow
        self._profiles: dict[str, ProfileEntry] = {}
        self._call_stack: list[str] = []
        self._process = None

        if HAS_PSUTIL:
            try:
                import os

                self._process = psutil.Process(os.getpid())
            except Exception as e:
                logger.debug(f"Failed to get process: {e}")

    def _get_memory_usage(self) -> float:
        """Get current memory usage in MB."""
        if not HAS_PSUTIL or not self._process:
            return 0.0
        try:
            return float(self._process.memory_info().rss / (1024**2))
        except Exception:
            return 0.0

    def _get_gpu_memory_usage(self) -> float:
        """Get current GPU memory usage in MB."""
        if not HAS_TORCH or not torch.cuda.is_available():
            return 0.0
        try:
            return float(torch.cuda.memory_allocated(0) / (1024**2))
        except Exception:
            return 0.0

    def profile_function(
        self,
        name: str | None = None,
        track_memory: bool = True,
        track_gpu_memory: bool = True,
    ):
        """
        Decorator to profile a function (enhanced).

        Args:
            name: Custom name for the function (default: function name)
            track_memory: Whether to track CPU memory usage
            track_gpu_memory: Whether to track GPU memory usage

        Returns:
            Decorated function
        """

        def decorator(func: Callable) -> Callable:
            func_name = name or f"{func.__module__}.{func.__name__}"

            @functools.wraps(func)
            def wrapper(*args, **kwargs):
                if not self.enabled:
                    return func(*args, **kwargs)

                # Track call stack
                self._call_stack.append(func_name)
                start_time = time.perf_counter()
                start_memory = self._get_memory_usage() if track_memory else 0.0
                start_gpu_memory = self._get_gpu_memory_usage() if track_gpu_memory else 0.0

                try:
                    result = func(*args, **kwargs)
                    execution_time = time.perf_counter() - start_time
                    end_memory = self._get_memory_usage() if track_memory else 0.0
                    end_gpu_memory = self._get_gpu_memory_usage() if track_gpu_memory else 0.0
                    memory_delta = end_memory - start_memory
                    gpu_memory_delta = end_gpu_memory - start_gpu_memory

                    # Update profile
                    if func_name not in self._profiles:
                        self._profiles[func_name] = ProfileEntry(function_name=func_name)
                    self._profiles[func_name].update(execution_time, memory_delta, gpu_memory_delta)

                    # Warn on slow functions
                    if self.warn_on_slow and execution_time > self.slow_threshold_seconds:
                        logger.warning(
                            f"Slow function detected: {func_name} took "
                            f"{execution_time:.3f}s (threshold: "
                            f"{self.slow_threshold_seconds}s)"
                        )

                    return result
                except Exception:
                    execution_time = time.perf_counter() - start_time
                    if func_name not in self._profiles:
                        self._profiles[func_name] = ProfileEntry(function_name=func_name)
                    self._profiles[func_name].update(execution_time, 0.0, 0.0)
                    self._profiles[func_name].record_error()
                    raise
                finally:
                    if self._call_stack:
                        self._call_stack.pop()

            return wrapper

        return decorator

    @contextmanager
    def profile_context(
        self,
        name: str,
        track_memory: bool = True,
        track_gpu_memory: bool = True,
    ):
        """
        Context manager for profiling code blocks (enhanced).

        Args:
            name: Name for the code block
            track_memory: Whether to track CPU memory usage
            track_gpu_memory: Whether to track GPU memory usage

        Yields:
            None
        """
        if not self.enabled:
            yield
            return

        self._call_stack.append(name)
        start_time = time.perf_counter()
        start_memory = self._get_memory_usage() if track_memory else 0.0
        start_gpu_memory = self._get_gpu_memory_usage() if track_gpu_memory else 0.0

        try:
            yield
            execution_time = time.perf_counter() - start_time
            end_memory = self._get_memory_usage() if track_memory else 0.0
            end_gpu_memory = self._get_gpu_memory_usage() if track_gpu_memory else 0.0
            memory_delta = end_memory - start_memory
            gpu_memory_delta = end_gpu_memory - start_gpu_memory

            if name not in self._profiles:
                self._profiles[name] = ProfileEntry(function_name=name)
            self._profiles[name].update(execution_time, memory_delta, gpu_memory_delta)

            # Warn on slow functions
            if self.warn_on_slow and execution_time > self.slow_threshold_seconds:
                logger.warning(
                    f"Slow code block detected: {name} took "
                    f"{execution_time:.3f}s (threshold: "
                    f"{self.slow_threshold_seconds}s)"
                )
        except Exception:
            execution_time = time.perf_counter() - start_time
            if name not in self._profiles:
                self._profiles[name] = ProfileEntry(function_name=name)
            self._profiles[name].update(execution_time, 0.0, 0.0)
            self._profiles[name].record_error()
            raise
        finally:
            if self._call_stack:
                self._call_stack.pop()

    def get_profile(self, function_name: str) -> ProfileEntry | None:
        """Get profile entry for a function."""
        return self._profiles.get(function_name)

    def get_all_profiles(self) -> dict[str, ProfileEntry]:
        """Get all profile entries."""
        return self._profiles.copy()

    def get_stats(self) -> dict[str, Any]:
        """Get profiling statistics."""
        if not self._profiles:
            return {
                "enabled": self.enabled,
                "total_functions": 0,
                "total_calls": 0,
                "total_time": 0.0,
            }

        total_calls = sum(p.call_count for p in self._profiles.values())
        total_time = sum(p.total_time for p in self._profiles.values())
        total_errors = sum(p.errors for p in self._profiles.values())

        # Top functions by total time
        top_by_time = sorted(
            self._profiles.items(),
            key=lambda x: x[1].total_time,
            reverse=True,
        )[:10]

        # Top functions by call count
        top_by_calls = sorted(
            self._profiles.items(),
            key=lambda x: x[1].call_count,
            reverse=True,
        )[:10]

        # Top functions by average time
        top_by_avg = sorted(
            self._profiles.items(),
            key=lambda x: x[1].avg_time,
            reverse=True,
        )[:10]

        return {
            "enabled": self.enabled,
            "total_functions": len(self._profiles),
            "total_calls": total_calls,
            "total_time": total_time,
            "total_time_seconds": total_time,
            "total_errors": total_errors,
            "error_rate": total_errors / total_calls if total_calls > 0 else 0.0,
            "top_by_total_time": [
                {
                    "function": name,
                    "calls": entry.call_count,
                    "total_time": entry.total_time,
                    "avg_time": entry.avg_time,
                    "min_time": entry.min_time,
                    "max_time": entry.max_time,
                }
                for name, entry in top_by_time
            ],
            "top_by_calls": [
                {
                    "function": name,
                    "calls": entry.call_count,
                    "total_time": entry.total_time,
                    "avg_time": entry.avg_time,
                }
                for name, entry in top_by_calls
            ],
            "top_by_avg_time": [
                {
                    "function": name,
                    "calls": entry.call_count,
                    "avg_time": entry.avg_time,
                    "total_time": entry.total_time,
                }
                for name, entry in top_by_avg
            ],
        }

    def get_detailed_stats(self) -> dict[str, Any]:
        """Get detailed profiling statistics with all functions."""
        stats = self.get_stats()
        stats["all_functions"] = {
            name: {
                "call_count": entry.call_count,
                "total_time": entry.total_time,
                "avg_time": entry.avg_time,
                "min_time": entry.min_time,
                "max_time": entry.max_time,
                "total_memory_delta_mb": entry.total_memory_delta,
                "max_memory_delta_mb": entry.max_memory_delta,
                "total_gpu_memory_delta_mb": entry.total_gpu_memory_delta,
                "max_gpu_memory_delta_mb": entry.max_gpu_memory_delta,
                "errors": entry.errors,
                "error_rate": (entry.errors / entry.call_count if entry.call_count > 0 else 0.0),
                "last_called": (entry.last_called.isoformat() if entry.last_called else None),
            }
            for name, entry in self._profiles.items()
        }
        return stats

    def reset(self):
        """Reset all profiling data."""
        self._profiles.clear()
        self._call_stack.clear()
        logger.info("Performance profiler reset")

    def enable(self):
        """Enable profiling."""
        self.enabled = True
        logger.info("Performance profiler enabled")

    def disable(self):
        """Disable profiling."""
        self.enabled = False
        logger.info("Performance profiler disabled")


# Global profiler instance
_profiler: PerformanceProfiler | None = None


def get_profiler() -> PerformanceProfiler:
    """Get the global performance profiler instance."""
    global _profiler
    if _profiler is None:
        _profiler = PerformanceProfiler()
    return _profiler
