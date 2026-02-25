"""
Enhanced Resource Manager

Improved resource management with:
- Better GPU memory management
- Enhanced VRAM tracking
- Resource prediction
- Improved job queuing
- Comprehensive resource monitoring
- Historical resource usage tracking
"""

from __future__ import annotations

import logging
import threading
import time
from collections import defaultdict, deque
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Any

from .resource_manager import (
    JobPriority,
    ResourceManager,
    ResourceRequirement,
)

logger = logging.getLogger(__name__)


@dataclass
class ResourceUsage:
    """Resource usage snapshot."""

    timestamp: datetime
    vram_used_gb: float
    vram_available_gb: float
    ram_used_gb: float
    cpu_usage_percent: float
    active_jobs: int
    queued_jobs: int


@dataclass
class ResourcePrediction:
    """Resource usage prediction."""

    predicted_vram_gb: float
    predicted_ram_gb: float
    confidence: float  # 0.0 to 1.0
    prediction_window_seconds: float


class EnhancedResourceManager(ResourceManager):
    """
    Enhanced resource manager with improved tracking, prediction, and monitoring.

    Enhancements:
    - Historical resource usage tracking
    - Resource usage prediction
    - Better VRAM fragmentation handling
    - Improved job queuing with resource awareness
    - Comprehensive monitoring and statistics
    - Resource usage alerts
    """

    def __init__(
        self,
        vram_headroom_gb: float = 1.0,
        history_window_seconds: float = 3600.0,  # 1 hour
        prediction_enabled: bool = True,
        monitoring_interval: float = 5.0,
    ):
        """
        Initialize enhanced resource manager.

        Args:
            vram_headroom_gb: Safety headroom for VRAM allocation
            history_window_seconds: Time window for historical tracking
            prediction_enabled: Enable resource prediction
            monitoring_interval: Resource monitoring interval in seconds
        """
        super().__init__(vram_headroom_gb)

        self.history_window_seconds = history_window_seconds
        self.prediction_enabled = prediction_enabled
        self.monitoring_interval = monitoring_interval

        # Historical resource usage (circular buffer)
        self.resource_history: deque = deque(maxlen=1000)

        # Resource usage by engine
        self.engine_resource_usage: dict[str, list[ResourceUsage]] = defaultdict(list)

        # Resource predictions
        self.resource_predictions: dict[str, ResourcePrediction] = {}

        # VRAM fragmentation tracking
        self.vram_fragmentation: float = 0.0  # 0.0 to 1.0

        # VRAM allocation tracking (TD-013)
        self._vram_allocations: dict[str, dict[str, Any]] = {}
        self.alert_callbacks: list = []

        # Resource alerts
        self.resource_alerts: list[dict[str, Any]] = []
        self.alert_thresholds = {
            "vram_usage_percent": 90.0,
            "ram_usage_percent": 85.0,
            "cpu_usage_percent": 90.0,
        }

        # Statistics
        self.stats = {
            "total_jobs_submitted": 0,
            "total_jobs_completed": 0,
            "total_jobs_failed": 0,
            "average_job_duration": 0.0,
            "peak_vram_usage_gb": 0.0,
            "peak_ram_usage_gb": 0.0,
            "resource_predictions_made": 0,
            "resource_alerts_triggered": 0,
        }

        # Monitoring thread
        self.monitoring_thread: threading.Thread | None = None
        self._start_monitoring()

    def _start_monitoring(self):
        """Start resource monitoring thread."""

        def monitoring_loop():
            while self.running:
                try:
                    self._collect_resource_usage()
                    self._check_resource_alerts()
                    if self.prediction_enabled:
                        self._update_predictions()
                    time.sleep(self.monitoring_interval)
                except Exception as e:
                    # Guard logging to prevent I/O errors during shutdown
                    if self.running:
                        try:
                            logger.error(f"Error in resource monitoring: {e}")
                        # Best effort - failure is acceptable here
                        except (ValueError, OSError):
                            pass
                    time.sleep(self.monitoring_interval)

        self.monitoring_thread = threading.Thread(target=monitoring_loop, daemon=True)
        self.monitoring_thread.start()

    def _collect_resource_usage(self):
        """Collect current resource usage snapshot."""
        with self.lock:
            # Get GPU info
            gpu_info = self.gpu_monitor.get_vram_info()

            # Get RAM usage (simplified - would use psutil in production)
            try:
                import psutil

                ram_info = psutil.virtual_memory()
                ram_used_gb = ram_info.used / (1024**3)
            except ImportError:
                ram_used_gb = 0.0

            # Get CPU usage
            try:
                import psutil

                cpu_usage_percent = psutil.cpu_percent(interval=0.1)
            except ImportError:
                cpu_usage_percent = 0.0

            # Count jobs
            active_jobs = len(self.active_jobs)
            queued_jobs = (
                self.realtime_queue.qsize()
                + self.interactive_queue.qsize()
                + self.batch_queue.qsize()
            )

            # Create usage snapshot
            usage = ResourceUsage(
                timestamp=datetime.now(),
                vram_used_gb=gpu_info.get("used_gb", 0.0),
                vram_available_gb=gpu_info.get("available_gb", 0.0),
                ram_used_gb=ram_used_gb,
                cpu_usage_percent=cpu_usage_percent,
                active_jobs=active_jobs,
                queued_jobs=queued_jobs,
            )

            # Add to history
            self.resource_history.append(usage)

            # Update peak usage
            if usage.vram_used_gb > self.stats["peak_vram_usage_gb"]:
                self.stats["peak_vram_usage_gb"] = usage.vram_used_gb
            if usage.ram_used_gb > self.stats["peak_ram_usage_gb"]:
                self.stats["peak_ram_usage_gb"] = usage.ram_used_gb

    def _check_resource_alerts(self):
        """Check for resource usage alerts."""
        if not self.resource_history:
            return

        latest = self.resource_history[-1]
        gpu_info = self.gpu_monitor.get_vram_info()
        total_vram = gpu_info.get("total_gb", 0.0)

        # Check VRAM usage
        if total_vram > 0:
            vram_usage_percent = (latest.vram_used_gb / total_vram) * 100.0
            if vram_usage_percent >= self.alert_thresholds["vram_usage_percent"]:
                self._trigger_alert(
                    "vram_high",
                    f"VRAM usage is {vram_usage_percent:.1f}%",
                    {"vram_usage_percent": vram_usage_percent},
                )

        # Check RAM usage (if available)
        try:
            import psutil

            ram_info = psutil.virtual_memory()
            ram_usage_percent = ram_info.percent
            if ram_usage_percent >= self.alert_thresholds["ram_usage_percent"]:
                self._trigger_alert(
                    "ram_high",
                    f"RAM usage is {ram_usage_percent:.1f}%",
                    {"ram_usage_percent": ram_usage_percent},
                )
        except ImportError:
            ...

        # Check CPU usage
        if latest.cpu_usage_percent >= self.alert_thresholds["cpu_usage_percent"]:
            self._trigger_alert(
                "cpu_high",
                f"CPU usage is {latest.cpu_usage_percent:.1f}%",
                {"cpu_usage_percent": latest.cpu_usage_percent},
            )

    def _trigger_alert(self, alert_type: str, message: str, data: dict[str, Any]):
        """Trigger a resource alert."""
        alert = {
            "type": alert_type,
            "message": message,
            "timestamp": datetime.now().isoformat(),
            "data": data,
        }
        self.resource_alerts.append(alert)
        self.stats["resource_alerts_triggered"] += 1

        # Keep only recent alerts (last 100)
        if len(self.resource_alerts) > 100:
            self.resource_alerts = self.resource_alerts[-100:]

        # Guard logging to prevent I/O errors during shutdown
        if self.running:
            try:
                logger.warning(f"Resource alert: {message}")
            except (ValueError, OSError):
                pass  # Ignore logging errors during teardown

    def _update_predictions(self):
        """Update resource usage predictions."""
        if len(self.resource_history) < 10:
            return  # Need enough history

        # Simple linear regression for prediction
        recent_history = list(self.resource_history)[-60:]  # Last 5 minutes

        if len(recent_history) < 10:
            return

        # Predict VRAM usage
        vram_values = [u.vram_used_gb for u in recent_history]
        if len(vram_values) >= 2:
            # Simple trend prediction
            trend = (vram_values[-1] - vram_values[0]) / len(vram_values)
            predicted_vram = vram_values[-1] + (trend * 10)  # Predict 10 steps ahead

            # Calculate confidence based on variance
            mean_vram = sum(vram_values) / len(vram_values)
            variance = sum((v - mean_vram) ** 2 for v in vram_values) / len(vram_values)
            confidence = max(0.0, min(1.0, 1.0 - (variance / (mean_vram + 1.0))))

            self.resource_predictions["vram"] = ResourcePrediction(
                predicted_vram_gb=predicted_vram,
                predicted_ram_gb=0.0,  # Would calculate similarly
                confidence=confidence,
                prediction_window_seconds=self.monitoring_interval * 10,
            )

            self.stats["resource_predictions_made"] += 1

    def predict_resource_usage(
        self, job_requirements: ResourceRequirement, window_seconds: float = 60.0
    ) -> ResourcePrediction:
        """
        Predict resource usage if job is executed.

        Args:
            job_requirements: Job resource requirements
            window_seconds: Prediction time window

        Returns:
            Resource prediction
        """
        if not self.resource_history:
            # No history, return current + requirements
            gpu_info = self.gpu_monitor.get_vram_info()
            current_vram = gpu_info.get("used_gb", 0.0)

            return ResourcePrediction(
                predicted_vram_gb=current_vram + job_requirements.vram_gb,
                predicted_ram_gb=job_requirements.ram_gb,
                confidence=0.5,  # Low confidence without history
                prediction_window_seconds=window_seconds,
            )

        # Use recent history for prediction
        latest = self.resource_history[-1]

        # Simple prediction: current + requirements + trend
        predicted_vram = latest.vram_used_gb + job_requirements.vram_gb

        # Add trend if available
        if "vram" in self.resource_predictions:
            trend = self.resource_predictions["vram"].predicted_vram_gb - latest.vram_used_gb
            predicted_vram += trend * 0.1  # Dampened trend

        # Calculate confidence
        confidence = 0.7  # Base confidence
        if len(self.resource_history) > 50:
            confidence = 0.9  # Higher confidence with more history

        return ResourcePrediction(
            predicted_vram_gb=predicted_vram,
            predicted_ram_gb=latest.ram_used_gb + job_requirements.ram_gb,
            confidence=confidence,
            prediction_window_seconds=window_seconds,
        )

    def get_resource_statistics(self) -> dict[str, Any]:
        """
        Get comprehensive resource statistics.

        Returns:
            Dictionary with resource statistics
        """
        with self.lock:
            gpu_info = self.gpu_monitor.get_vram_info()

            # Calculate averages from history
            if self.resource_history:
                recent = list(self.resource_history)[-100:]  # Last 100 samples
                avg_vram = sum(u.vram_used_gb for u in recent) / len(recent)
                avg_ram = sum(u.ram_used_gb for u in recent) / len(recent)
                avg_cpu = sum(u.cpu_usage_percent for u in recent) / len(recent)
            else:
                avg_vram = 0.0
                avg_ram = 0.0
                avg_cpu = 0.0

            # Calculate job statistics
            total_duration = 0.0
            completed_count = 0
            for job in self.job_history:
                if job.completed_at and job.started_at:
                    duration = (job.completed_at - job.started_at).total_seconds()
                    total_duration += duration
                    completed_count += 1

            avg_duration = total_duration / completed_count if completed_count > 0 else 0.0

            return {
                "gpu": {
                    "has_gpu": gpu_info.get("has_gpu", False),
                    "total_vram_gb": gpu_info.get("total_gb", 0.0),
                    "used_vram_gb": gpu_info.get("used_gb", 0.0),
                    "available_vram_gb": gpu_info.get("available_gb", 0.0),
                    "average_vram_gb": avg_vram,
                    "peak_vram_gb": self.stats["peak_vram_usage_gb"],
                },
                "ram": {
                    "average_ram_gb": avg_ram,
                    "peak_ram_gb": self.stats["peak_ram_usage_gb"],
                },
                "cpu": {
                    "average_cpu_percent": avg_cpu,
                },
                "jobs": {
                    "total_submitted": self.stats["total_jobs_submitted"],
                    "total_completed": self.stats["total_jobs_completed"],
                    "total_failed": self.stats["total_jobs_failed"],
                    "active": len(self.active_jobs),
                    "queued": (
                        self.realtime_queue.qsize()
                        + self.interactive_queue.qsize()
                        + self.batch_queue.qsize()
                    ),
                    "average_duration_seconds": avg_duration,
                },
                "predictions": {
                    "enabled": self.prediction_enabled,
                    "predictions_made": self.stats["resource_predictions_made"],
                },
                "alerts": {
                    "total_triggered": self.stats["resource_alerts_triggered"],
                    "recent": self.resource_alerts[-10:] if self.resource_alerts else [],
                },
            }

    def submit_job(
        self,
        job_id: str,
        engine_id: str,
        task: str,
        priority: JobPriority,
        requirements: ResourceRequirement,
        payload: dict[str, Any],
        callback: Any | None = None,
    ) -> bool:
        """
        Submit job with enhanced resource checking and prediction.
        """
        # Predict resource usage
        if self.prediction_enabled:
            prediction = self.predict_resource_usage(requirements)
            gpu_info = self.gpu_monitor.get_vram_info()
            total_vram = gpu_info.get("total_gb", 0.0)

            # Check if prediction exceeds capacity
            if total_vram > 0 and prediction.predicted_vram_gb > total_vram * 0.95:
                logger.warning(
                    f"Job {job_id} predicted to exceed VRAM capacity "
                    f"({prediction.predicted_vram_gb:.2f}GB / {total_vram:.2f}GB)"
                )
                # Still submit, but with lower priority or warning

        # Call parent implementation
        result = super().submit_job(
            job_id, engine_id, task, priority, requirements, payload, callback
        )

        if result:
            self.stats["total_jobs_submitted"] += 1

        return result

    def complete_job(self, job_id: str, success: bool = True, error: str | None = None):
        """Complete job with enhanced statistics tracking."""
        super().complete_job(job_id, success, error)

        with self.lock:
            if success:
                self.stats["total_jobs_completed"] += 1
            else:
                self.stats["total_jobs_failed"] += 1

    def get_resource_history(self, window_seconds: float | None = None) -> list[ResourceUsage]:
        """
        Get resource usage history.

        Args:
            window_seconds: Time window (None = all history)

        Returns:
            List of resource usage snapshots
        """
        if window_seconds is None:
            return list(self.resource_history)

        cutoff = datetime.now() - timedelta(seconds=window_seconds)
        return [usage for usage in self.resource_history if usage.timestamp >= cutoff]

    def request_vram_allocation(
        self, engine_id: str, required_vram_gb: float
    ) -> bool:
        """
        Request VRAM allocation for an engine. If insufficient VRAM is available,
        evict the least-recently-used engine to make room.

        Returns True if allocation succeeded (possibly after eviction).
        """
        with self.lock:
            available = self._get_available_vram_gb()
            if available >= required_vram_gb:
                self._vram_allocations[engine_id] = {
                    "allocated_gb": required_vram_gb,
                    "last_used": time.time(),
                }
                logger.info(
                    f"VRAM allocated: {engine_id} = {required_vram_gb:.1f}GB "
                    f"(available: {available - required_vram_gb:.1f}GB)"
                )
                return True

            freed = self._evict_lru_engines(required_vram_gb - available)
            available = self._get_available_vram_gb()
            if available >= required_vram_gb:
                self._vram_allocations[engine_id] = {
                    "allocated_gb": required_vram_gb,
                    "last_used": time.time(),
                }
                logger.info(
                    f"VRAM allocated after eviction: {engine_id} = {required_vram_gb:.1f}GB "
                    f"(freed {freed:.1f}GB)"
                )
                return True

            logger.warning(
                f"VRAM allocation failed: {engine_id} needs {required_vram_gb:.1f}GB, "
                f"only {available:.1f}GB available after eviction"
            )
            return False

    def release_vram_allocation(self, engine_id: str) -> None:
        """Release VRAM allocation for an engine."""
        with self.lock:
            if engine_id in self._vram_allocations:
                freed = self._vram_allocations[engine_id]["allocated_gb"]
                del self._vram_allocations[engine_id]
                logger.info(f"VRAM released: {engine_id} = {freed:.1f}GB")

    def touch_engine(self, engine_id: str) -> None:
        """Mark an engine as recently used (prevents LRU eviction)."""
        with self.lock:
            if engine_id in self._vram_allocations:
                self._vram_allocations[engine_id]["last_used"] = time.time()

    def _get_available_vram_gb(self) -> float:
        """Get available VRAM in GB."""
        try:
            import subprocess as sp

            result = sp.run(
                ["nvidia-smi", "--query-gpu=memory.free", "--format=csv,noheader,nounits"],
                capture_output=True,
                text=True,
                timeout=5,
            )
            if result.returncode == 0 and result.stdout.strip():
                free_mb = float(result.stdout.strip().split("\n")[0])
                return free_mb / 1024.0
        except Exception:
            pass
        total_allocated = sum(
            a["allocated_gb"] for a in self._vram_allocations.values()
        )
        return max(0.0, 8.0 - total_allocated)

    def _evict_lru_engines(self, needed_gb: float) -> float:
        """Evict least-recently-used engines until enough VRAM is freed."""
        if not self._vram_allocations:
            return 0.0

        sorted_engines = sorted(
            self._vram_allocations.items(),
            key=lambda x: x[1]["last_used"],
        )

        freed = 0.0
        evicted = []
        for engine_id, alloc in sorted_engines:
            if freed >= needed_gb:
                break
            freed += alloc["allocated_gb"]
            evicted.append(engine_id)

        for engine_id in evicted:
            del self._vram_allocations[engine_id]
            logger.info(f"VRAM eviction: {engine_id} evicted (LRU)")
            for cb in self.alert_callbacks:
                try:
                    cb("engine_evicted", f"Engine {engine_id} evicted from GPU (LRU)", {"engine_id": engine_id})
                except Exception:
                    pass

        return freed

    def get_vram_allocation_status(self) -> dict[str, Any]:
        """Get current VRAM allocation status."""
        with self.lock:
            return {
                "allocations": {
                    eid: {"gb": a["allocated_gb"], "last_used": a["last_used"]}
                    for eid, a in self._vram_allocations.items()
                },
                "total_allocated_gb": sum(
                    a["allocated_gb"] for a in self._vram_allocations.values()
                ),
                "available_gb": self._get_available_vram_gb(),
            }

    def shutdown(self):
        """Shutdown enhanced resource manager."""
        self.running = False
        if self.monitoring_thread:
            self.monitoring_thread.join(timeout=5.0)


# Factory function
def create_enhanced_resource_manager(
    vram_headroom_gb: float = 1.0,
    prediction_enabled: bool = True,
    monitoring_interval: float = 5.0,
) -> EnhancedResourceManager:
    """
    Create enhanced resource manager.

    Args:
        vram_headroom_gb: VRAM headroom in GB
        prediction_enabled: Enable resource prediction
        monitoring_interval: Monitoring interval in seconds

    Returns:
        EnhancedResourceManager instance
    """
    return EnhancedResourceManager(
        vram_headroom_gb=vram_headroom_gb,
        prediction_enabled=prediction_enabled,
        monitoring_interval=monitoring_interval,
    )


# Export
__all__ = [
    "EnhancedResourceManager",
    "ResourcePrediction",
    "ResourceUsage",
    "create_enhanced_resource_manager",
]
