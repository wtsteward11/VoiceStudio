"""
Resource Usage Analytics.

Task 1.2.5: Track and report resource patterns.
Collects and analyzes resource usage for optimization insights.
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import statistics
from collections import deque
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from enum import Enum
from typing import Any

logger = logging.getLogger(__name__)

# Optional dependencies - import once at module level
_psutil = None
_torch = None
_imports_checked = False


def _check_imports() -> None:
    """Check optional imports once at module load."""
    global _psutil, _torch, _imports_checked
    if _imports_checked:
        return
    _imports_checked = True

    try:
        import psutil

        _psutil = psutil
    except ImportError:
        logger.debug("psutil not available for resource sampling")

    try:
        import torch

        _torch = torch
    except ImportError:
        logger.debug("PyTorch not available for GPU sampling")


class ResourceType(Enum):
    """Types of tracked resources."""

    CPU = "cpu"
    MEMORY = "memory"
    GPU = "gpu"
    GPU_MEMORY = "gpu_memory"
    DISK_IO = "disk_io"
    NETWORK = "network"


@dataclass
class ResourceSample:
    """A single resource sample."""

    timestamp: datetime
    resource_type: ResourceType
    value: float
    unit: str
    metadata: dict[str, Any] = field(default_factory=dict)


@dataclass
class ResourceStats:
    """Statistics for a resource over a time period."""

    resource_type: ResourceType
    period_start: datetime
    period_end: datetime
    sample_count: int
    min_value: float
    max_value: float
    avg_value: float
    median_value: float
    std_dev: float
    percentile_95: float
    percentile_99: float
    unit: str


@dataclass
class AnalyticsConfig:
    """Configuration for resource analytics."""

    sample_interval_seconds: float = 10.0
    retention_hours: int = 24
    aggregation_periods: list[int] = field(default_factory=lambda: [60, 300, 3600])  # 1m, 5m, 1h
    enable_predictions: bool = True


class ResourceAnalytics:
    """
    Tracks and analyzes resource usage patterns.

    Features:
    - Real-time resource sampling
    - Historical data retention
    - Statistical analysis
    - Trend detection
    - Anomaly detection
    - Usage predictions

    Thread Safety:
    - All public methods are thread-safe via asyncio.Lock
    - Use async context manager for batch operations
    """

    def __init__(self, config: AnalyticsConfig | None = None):
        self.config = config or AnalyticsConfig()

        # Calculate max samples based on retention and interval
        max_samples = (
            int((self.config.retention_hours * 3600) / self.config.sample_interval_seconds) + 100
        )  # Buffer for safety

        self._samples: dict[ResourceType, deque[ResourceSample]] = {
            rt: deque(maxlen=max_samples) for rt in ResourceType
        }
        self._running = False
        self._task: asyncio.Task | None = None
        self._lock = asyncio.Lock()

        # Check imports on first instantiation
        _check_imports()

    async def start(self) -> None:
        """Start resource tracking."""
        self._running = True
        self._task = asyncio.create_task(self._sample_loop())
        logger.info("Resource analytics started")

    async def stop(self) -> None:
        """Stop resource tracking."""
        self._running = False
        if self._task:
            self._task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._task
        logger.info("Resource analytics stopped")

    async def _sample_loop(self) -> None:
        """Main sampling loop."""
        while self._running:
            try:
                await self._collect_samples()
                await self._cleanup_old_samples()
                await asyncio.sleep(self.config.sample_interval_seconds)
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Sample collection error: {e}")
                await asyncio.sleep(1.0)

    async def _collect_samples(self) -> None:
        """Collect resource samples using pre-imported modules."""
        now = datetime.now()

        async with self._lock:
            # CPU & Memory (using pre-imported psutil)
            if _psutil is not None:
                try:
                    cpu_percent = _psutil.cpu_percent()
                    self._samples[ResourceType.CPU].append(
                        ResourceSample(
                            timestamp=now,
                            resource_type=ResourceType.CPU,
                            value=cpu_percent,
                            unit="%",
                        )
                    )

                    mem = _psutil.virtual_memory()
                    self._samples[ResourceType.MEMORY].append(
                        ResourceSample(
                            timestamp=now,
                            resource_type=ResourceType.MEMORY,
                            value=mem.percent,
                            unit="%",
                            metadata={"used_gb": mem.used / 1e9, "total_gb": mem.total / 1e9},
                        )
                    )
                except Exception as e:
                    logger.debug("psutil sampling error: %s", e)

            # GPU Memory (using pre-imported torch)
            if _torch is not None:
                try:
                    if _torch.cuda.is_available():
                        for i in range(_torch.cuda.device_count()):
                            allocated = _torch.cuda.memory_allocated(i)
                            total = _torch.cuda.get_device_properties(i).total_memory
                            percent = (allocated / total) * 100 if total > 0 else 0

                            self._samples[ResourceType.GPU_MEMORY].append(
                                ResourceSample(
                                    timestamp=now,
                                    resource_type=ResourceType.GPU_MEMORY,
                                    value=percent,
                                    unit="%",
                                    metadata={"device": i, "used_gb": allocated / 1e9},
                                )
                            )
                except Exception as e:
                    logger.debug("GPU sampling error: %s", e)

    async def _cleanup_old_samples(self) -> None:
        """Remove samples older than retention period.

        Note: deque maxlen provides automatic size-based cleanup.
        This method handles time-based cleanup for partial retention.
        """
        cutoff = datetime.now() - timedelta(hours=self.config.retention_hours)

        async with self._lock:
            for resource_type in ResourceType:
                # Remove from left (oldest) while older than cutoff
                samples = self._samples[resource_type]
                while samples and samples[0].timestamp < cutoff:
                    samples.popleft()

    async def add_sample(
        self,
        resource_type: ResourceType,
        value: float,
        unit: str = "",
        metadata: dict[str, Any] | None = None,
    ) -> None:
        """Manually add a resource sample (thread-safe)."""
        sample = ResourceSample(
            timestamp=datetime.now(),
            resource_type=resource_type,
            value=value,
            unit=unit,
            metadata=metadata or {},
        )
        async with self._lock:
            self._samples[resource_type].append(sample)

    def add_sample_sync(
        self,
        resource_type: ResourceType,
        value: float,
        unit: str = "",
        metadata: dict[str, Any] | None = None,
    ) -> None:
        """Synchronously add a sample (use only when lock not needed)."""
        sample = ResourceSample(
            timestamp=datetime.now(),
            resource_type=resource_type,
            value=value,
            unit=unit,
            metadata=metadata or {},
        )
        self._samples[resource_type].append(sample)

    def get_stats(
        self,
        resource_type: ResourceType,
        period_seconds: int = 3600,
    ) -> ResourceStats | None:
        """Get statistics for a resource over a time period."""
        cutoff = datetime.now() - timedelta(seconds=period_seconds)
        # Create a snapshot of samples to avoid issues during iteration
        samples = [s for s in self._samples[resource_type] if s.timestamp > cutoff]

        if len(samples) < 2:
            return None

        values = [s.value for s in samples]
        sorted_values = sorted(values)
        n = len(sorted_values)

        # Use sorted list for min/max (O(1) instead of O(n))
        # Fix percentile calculation with proper index bounds
        p95_idx = min(int(n * 0.95), n - 1)
        p99_idx = min(int(n * 0.99), n - 1)

        return ResourceStats(
            resource_type=resource_type,
            period_start=samples[0].timestamp,
            period_end=samples[-1].timestamp,
            sample_count=n,
            min_value=sorted_values[0],
            max_value=sorted_values[-1],
            avg_value=statistics.mean(values),
            median_value=statistics.median(sorted_values),
            std_dev=statistics.stdev(values) if n > 1 else 0,
            percentile_95=sorted_values[p95_idx],
            percentile_99=sorted_values[p99_idx],
            unit=samples[0].unit,
        )

    def get_trend(
        self,
        resource_type: ResourceType,
        period_seconds: int = 3600,
    ) -> dict[str, Any] | None:
        """
        Analyze trend for a resource.

        Returns direction (up/down/stable) and rate of change per minute.
        The slope is normalized to change per minute for consistent comparison.
        """
        cutoff = datetime.now() - timedelta(seconds=period_seconds)
        samples = [s for s in self._samples[resource_type] if s.timestamp > cutoff]

        if len(samples) < 10:
            return None

        values = [s.value for s in samples]

        # Simple linear regression (slope is change per sample)
        n = len(values)
        x_mean = (n - 1) / 2
        y_mean = sum(values) / n

        numerator = sum((i - x_mean) * (v - y_mean) for i, v in enumerate(values))
        denominator = sum((i - x_mean) ** 2 for i in range(n))

        slope_per_sample = numerator / denominator if denominator != 0 else 0

        # Normalize slope to change per minute for consistent interpretation
        samples_per_minute = 60 / self.config.sample_interval_seconds
        slope_per_minute = slope_per_sample * samples_per_minute

        # Determine direction based on normalized slope
        # Threshold: 0.5% change per minute is considered significant
        if abs(slope_per_minute) < 0.5:
            direction = "stable"
        elif slope_per_minute > 0:
            direction = "increasing"
        else:
            direction = "decreasing"

        return {
            "direction": direction,
            "slope": round(slope_per_sample, 4),
            "slope_per_minute": round(slope_per_minute, 4),
            "sample_count": n,
            "period_seconds": period_seconds,
            "current_value": values[-1],
            "starting_value": values[0],
            "change_total": round(values[-1] - values[0], 2),
        }

    def detect_anomalies(
        self,
        resource_type: ResourceType,
        threshold_std: float = 2.0,
    ) -> list[ResourceSample]:
        """
        Detect anomalous samples (values beyond threshold standard deviations).
        """
        samples = self._samples[resource_type]

        if len(samples) < 10:
            return []

        values = [s.value for s in samples]
        mean = statistics.mean(values)
        std = statistics.stdev(values)

        if std == 0:
            return []

        return [s for s in samples if abs(s.value - mean) > threshold_std * std]

    def get_peak_usage_times(
        self,
        resource_type: ResourceType,
        bucket_minutes: int = 60,
    ) -> dict[int, float]:
        """
        Get average usage by time bucket.

        Args:
            resource_type: Type of resource to analyze
            bucket_minutes: Size of time bucket (60=hourly, 30=half-hourly, etc.)

        Returns:
            Dict mapping bucket index to average usage.
            For 60-minute buckets: 0-23 (hours)
            For 30-minute buckets: 0-47 (half-hours)
        """
        buckets_per_day = (24 * 60) // bucket_minutes
        by_bucket: dict[int, list[float]] = {b: [] for b in range(buckets_per_day)}

        for sample in self._samples[resource_type]:
            # Calculate bucket index based on time of day
            minutes_of_day = sample.timestamp.hour * 60 + sample.timestamp.minute
            bucket_idx = minutes_of_day // bucket_minutes
            by_bucket[bucket_idx].append(sample.value)

        return {
            bucket: statistics.mean(values) if values else 0.0
            for bucket, values in by_bucket.items()
        }

    def predict_usage(
        self,
        resource_type: ResourceType,
        minutes_ahead: int = 30,
    ) -> float | None:
        """
        Predict resource usage for the near future.

        Uses simple trend extrapolation.
        """
        trend = self.get_trend(resource_type, period_seconds=1800)
        if not trend:
            return None

        # Extrapolate based on slope
        samples_per_minute = 60 / self.config.sample_interval_seconds
        predicted_change = trend["slope"] * samples_per_minute * minutes_ahead

        predicted = trend["current_value"] + predicted_change
        return max(0, min(100, predicted))  # Clamp to 0-100

    def get_report(self) -> dict[str, Any]:
        """Generate a comprehensive analytics report."""
        report = {
            "generated_at": datetime.now().isoformat(),
            "retention_hours": self.config.retention_hours,
            "resources": {},
        }

        for resource_type in ResourceType:
            samples = self._samples[resource_type]
            if not samples:
                continue

            stats_1h = self.get_stats(resource_type, 3600)
            stats_24h = self.get_stats(resource_type, 86400)
            trend = self.get_trend(resource_type)
            anomalies = self.detect_anomalies(resource_type)
            prediction = self.predict_usage(resource_type)

            report["resources"][resource_type.value] = {
                "sample_count": len(samples),
                "current_value": samples[-1].value if samples else None,
                "stats_1h": (
                    {
                        "min": round(stats_1h.min_value, 2),
                        "max": round(stats_1h.max_value, 2),
                        "avg": round(stats_1h.avg_value, 2),
                        "p95": round(stats_1h.percentile_95, 2),
                    }
                    if stats_1h
                    else None
                ),
                "stats_24h": (
                    {
                        "min": round(stats_24h.min_value, 2),
                        "max": round(stats_24h.max_value, 2),
                        "avg": round(stats_24h.avg_value, 2),
                        "p95": round(stats_24h.percentile_95, 2),
                    }
                    if stats_24h
                    else None
                ),
                "trend": trend,
                "anomaly_count": len(anomalies),
                "prediction_30m": round(prediction, 2) if prediction else None,
            }

        return report


# Global analytics instance
_analytics: ResourceAnalytics | None = None


def get_resource_analytics() -> ResourceAnalytics:
    """Get or create the global resource analytics."""
    global _analytics
    if _analytics is None:
        _analytics = ResourceAnalytics()
    return _analytics
