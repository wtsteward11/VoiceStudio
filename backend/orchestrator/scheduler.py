"""
Job Scheduler Service — Phase X-A

Orchestration-aware job scheduling with GPU utilization tracking,
priority promotion, and concurrency limits.

Extends the concepts from EnhancedJobQueue with orchestrator-specific logic.
"""

from __future__ import annotations

import logging
import threading
import time
from collections import OrderedDict
from dataclasses import dataclass, field
from enum import Enum
from typing import Any

from .gpu_tracker import GpuTracker, get_gpu_tracker
from .schemas import OrchestrationPriority, OrchestrationRequest

logger = logging.getLogger(__name__)


class ScheduledJobStatus(str, Enum):
    WAITING = "waiting"
    SCHEDULED = "scheduled"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


@dataclass
class ScheduledJob:
    """A job managed by the scheduler."""

    job_id: str
    request: OrchestrationRequest
    priority: OrchestrationPriority
    status: ScheduledJobStatus = ScheduledJobStatus.WAITING
    queued_at: float = field(default_factory=time.time)
    started_at: float | None = None
    completed_at: float | None = None
    engine_id: str | None = None
    estimated_duration_ms: float | None = None


class JobScheduler:
    """
    GPU-aware job scheduler with priority promotion and concurrency limits.

    Responsibilities:
    - Queue jobs by priority (realtime > interactive > batch)
    - Promote waiting jobs after configured threshold
    - Enforce concurrency limits per engine
    - Defer jobs when GPU memory is insufficient
    - Estimate wait times from historical durations
    """

    def __init__(
        self,
        max_concurrent: int = 2,
        promotion_threshold_s: float = 60.0,
        gpu_tracker: GpuTracker | None = None,
    ) -> None:
        self._max_concurrent = max_concurrent
        self._promotion_threshold = promotion_threshold_s
        self._gpu_tracker = gpu_tracker or get_gpu_tracker()
        self._queue: OrderedDict[str, ScheduledJob] = OrderedDict()
        self._running: dict[str, ScheduledJob] = {}
        self._completed: OrderedDict[str, ScheduledJob] = OrderedDict()
        self._lock = threading.Lock()
        self._avg_durations: dict[str, float] = {}

    def submit(self, job_id: str, request: OrchestrationRequest) -> ScheduledJob:
        job = ScheduledJob(
            job_id=job_id,
            request=request,
            priority=request.priority,
        )
        with self._lock:
            self._queue[job_id] = job
        return job

    def try_schedule_next(self) -> ScheduledJob | None:
        """
        Attempt to schedule the highest-priority waiting job.

        Returns the job if scheduling succeeds, None otherwise.
        """
        self._promote_stale_jobs()

        with self._lock:
            if len(self._running) >= self._max_concurrent:
                return None

            if not self._gpu_tracker.can_schedule():
                logger.debug("GPU at capacity, deferring scheduling")
                return None

            best = self._pick_next()
            if best is None:
                return None

            best.status = ScheduledJobStatus.RUNNING
            best.started_at = time.time()
            del self._queue[best.job_id]
            self._running[best.job_id] = best
            return best

    def mark_completed(
        self, job_id: str, duration_ms: float | None = None, engine_id: str | None = None
    ) -> None:
        with self._lock:
            job = self._running.pop(job_id, None)
            if job is None:
                return
            job.status = ScheduledJobStatus.COMPLETED
            job.completed_at = time.time()
            job.engine_id = engine_id

            if duration_ms and engine_id:
                prev = self._avg_durations.get(engine_id, duration_ms)
                self._avg_durations[engine_id] = (prev + duration_ms) / 2

            self._completed[job_id] = job
            if len(self._completed) > 200:
                self._completed.popitem(last=False)

    def mark_failed(self, job_id: str) -> None:
        with self._lock:
            job = self._running.pop(job_id, None)
            if job:
                job.status = ScheduledJobStatus.FAILED
                job.completed_at = time.time()
                self._completed[job_id] = job

    def cancel(self, job_id: str) -> bool:
        with self._lock:
            job = self._queue.pop(job_id, None) or self._running.pop(job_id, None)
            if job:
                job.status = ScheduledJobStatus.CANCELLED
                return True
            return False

    def get_queue_depth(self) -> int:
        with self._lock:
            return len(self._queue)

    def get_running_count(self) -> int:
        with self._lock:
            return len(self._running)

    def estimate_wait(self, job_id: str) -> float | None:
        """Estimate wait time in milliseconds based on queue position and averages."""
        with self._lock:
            if job_id not in self._queue:
                return None
            position = list(self._queue.keys()).index(job_id)
            avg = sum(self._avg_durations.values()) / max(len(self._avg_durations), 1)
            if avg == 0:
                avg = 5000.0
            return position * avg / max(self._max_concurrent, 1)

    def get_status(self) -> dict[str, Any]:
        with self._lock:
            return {
                "queue_depth": len(self._queue),
                "running": len(self._running),
                "max_concurrent": self._max_concurrent,
                "gpu_available": self._gpu_tracker.gpu_available,
                "can_schedule": self._gpu_tracker.can_schedule(),
                "avg_durations": dict(self._avg_durations),
            }

    def _pick_next(self) -> ScheduledJob | None:
        """Pick the highest-priority job from the queue."""
        priority_order = {
            OrchestrationPriority.REALTIME: 0,
            OrchestrationPriority.INTERACTIVE: 1,
            OrchestrationPriority.BATCH: 2,
        }
        candidates = sorted(
            self._queue.values(),
            key=lambda j: (priority_order.get(j.priority, 99), j.queued_at),
        )
        return candidates[0] if candidates else None

    def _promote_stale_jobs(self) -> None:
        """Promote jobs waiting longer than the threshold."""
        now = time.time()
        with self._lock:
            for job in self._queue.values():
                wait = now - job.queued_at
                if (
                    wait > self._promotion_threshold
                    and job.priority == OrchestrationPriority.BATCH
                ):
                    job.priority = OrchestrationPriority.INTERACTIVE
                    logger.info(
                        "Promoted job %s from BATCH to INTERACTIVE (waited %.0fs)",
                        job.job_id,
                        wait,
                    )


_scheduler_instance: JobScheduler | None = None


def get_job_scheduler() -> JobScheduler:
    global _scheduler_instance
    if _scheduler_instance is None:
        _scheduler_instance = JobScheduler()
    return _scheduler_instance
