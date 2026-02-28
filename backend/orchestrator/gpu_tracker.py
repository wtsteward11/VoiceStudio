"""
GPU Utilization Tracker — Phase X-A

Lightweight wrapper around existing GPU memory detection for
orchestration-aware scheduling decisions.
"""

from __future__ import annotations

import logging
import threading
import time
from collections import deque
from dataclasses import dataclass, field
from typing import Any

logger = logging.getLogger(__name__)


@dataclass
class GpuSnapshot:
    """Point-in-time GPU utilization reading."""

    timestamp: float = field(default_factory=time.time)
    total_mb: float = 0.0
    used_mb: float = 0.0
    free_mb: float = 0.0
    utilization_pct: float = 0.0


class GpuTracker:
    """
    Periodic GPU utilization tracker with ring-buffer history.

    Provides can_schedule() predicate for the job scheduler.
    """

    def __init__(
        self,
        poll_interval_s: float = 5.0,
        history_size: int = 100,
        schedule_threshold: float = 0.85,
    ) -> None:
        self._poll_interval = poll_interval_s
        self._history: deque[GpuSnapshot] = deque(maxlen=history_size)
        self._schedule_threshold = schedule_threshold
        self._running = False
        self._thread: threading.Thread | None = None
        self._lock = threading.Lock()
        self._gpu_available = self._detect_gpu()

    def _detect_gpu(self) -> bool:
        try:
            import torch
            return torch.cuda.is_available()
        except ImportError:
            return False

    def start(self) -> None:
        if self._running or not self._gpu_available:
            return
        self._running = True
        self._thread = threading.Thread(target=self._poll_loop, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        self._running = False
        if self._thread:
            self._thread.join(timeout=self._poll_interval + 1)

    def _poll_loop(self) -> None:
        while self._running:
            try:
                snapshot = self._take_snapshot()
                with self._lock:
                    self._history.append(snapshot)
            except Exception:
                logger.debug("GPU snapshot failed", exc_info=True)
            time.sleep(self._poll_interval)

    def _take_snapshot(self) -> GpuSnapshot:
        try:
            import torch

            if not torch.cuda.is_available():
                return GpuSnapshot()

            props = torch.cuda.get_device_properties(0)
            total = props.total_memory / (1024 * 1024)
            used = torch.cuda.memory_allocated(0) / (1024 * 1024)
            return GpuSnapshot(
                total_mb=round(total, 1),
                used_mb=round(used, 1),
                free_mb=round(total - used, 1),
                utilization_pct=round(used / total * 100, 1) if total > 0 else 0.0,
            )
        except Exception:
            return GpuSnapshot()

    @property
    def latest(self) -> GpuSnapshot:
        with self._lock:
            return self._history[-1] if self._history else GpuSnapshot()

    @property
    def gpu_available(self) -> bool:
        return self._gpu_available

    def can_schedule(self, vram_required_mb: float = 0) -> bool:
        """Check if GPU has capacity for a new job."""
        if not self._gpu_available:
            return True

        snap = self.latest
        if snap.total_mb == 0:
            return True

        usage_ratio = snap.used_mb / snap.total_mb
        if usage_ratio >= self._schedule_threshold:
            return False

        if vram_required_mb > 0 and snap.free_mb < vram_required_mb:
            return False

        return True

    def get_history(self) -> list[dict[str, Any]]:
        with self._lock:
            return [
                {
                    "timestamp": s.timestamp,
                    "total_mb": s.total_mb,
                    "used_mb": s.used_mb,
                    "free_mb": s.free_mb,
                    "utilization_pct": s.utilization_pct,
                }
                for s in self._history
            ]


_tracker_instance: GpuTracker | None = None


def get_gpu_tracker() -> GpuTracker:
    global _tracker_instance
    if _tracker_instance is None:
        _tracker_instance = GpuTracker()
        _tracker_instance.start()
    return _tracker_instance
