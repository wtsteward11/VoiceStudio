"""
Enhanced Job Queue System

Improved job queue with:
- Priority-based scheduling
- Job batching
- Enhanced status tracking
- Job cancellation
- Retry logic
- Job dependencies
- WebSocket notifications
- Progress tracking
"""

from __future__ import annotations

import logging
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from queue import PriorityQueue
from typing import Any, Optional, cast

from .resource_manager import (
    Job,
    JobPriority,
    JobStatus,
    ResourceManager,
    ResourceRequirement,
)

logger = logging.getLogger(__name__)

# WebSocket notification callback type
WebSocketNotifier = Optional[Callable[[str, dict[str, Any]], None]]


class RetryPolicy(Enum):
    """Retry policy for failed jobs."""

    NONE = "none"  # No retry
    IMMEDIATE = "immediate"  # Retry immediately
    EXPONENTIAL = "exponential"  # Exponential backoff
    FIXED = "fixed"  # Fixed delay


@dataclass
class JobBatch:
    """Batch of jobs to process together."""

    batch_id: str
    jobs: list[Job]
    created_at: datetime = field(default_factory=datetime.now)
    started_at: datetime | None = None
    completed_at: datetime | None = None
    status: str = "pending"  # pending, running, completed, failed
    progress: float = 0.0  # 0.0 to 1.0
    total_jobs: int = 0
    completed_jobs: int = 0
    failed_jobs: int = 0


class EnhancedJobQueue:
    """
    Enhanced job queue with batching, retry logic, and improved tracking.

    Features:
    - Priority-based scheduling
    - Job batching
    - Retry logic with configurable policies
    - Job dependencies
    - Enhanced status tracking
    - Job cancellation
    - Batch processing
    """

    def __init__(
        self,
        resource_manager: ResourceManager | None = None,
        max_retries: int = 3,
        default_retry_policy: RetryPolicy = RetryPolicy.EXPONENTIAL,
        batch_size: int = 10,
        enable_batching: bool = True,
        websocket_notifier: WebSocketNotifier = None,
    ):
        """
        Initialize enhanced job queue.

        Args:
            resource_manager: Resource manager instance
            max_retries: Maximum retry attempts
            default_retry_policy: Default retry policy
            batch_size: Default batch size
            enable_batching: Enable job batching
            websocket_notifier: Optional WebSocket notification callback
        """
        self.resource_manager = resource_manager
        self.max_retries = max_retries
        self.default_retry_policy = default_retry_policy
        self.batch_size = batch_size
        self.enable_batching = enable_batching
        self.websocket_notifier = websocket_notifier

        # Priority queues (inherited from ResourceManager if available)
        if resource_manager:
            self.realtime_queue = resource_manager.realtime_queue
            self.interactive_queue = resource_manager.interactive_queue
            self.batch_queue = resource_manager.batch_queue
        else:
            self.realtime_queue = PriorityQueue()
            self.interactive_queue = PriorityQueue()
            self.batch_queue = PriorityQueue()

        # Job batches
        self.job_batches: dict[str, JobBatch] = {}
        self.pending_batches: list[JobBatch] = []

        # Job tracking
        self.jobs: dict[str, Job] = {}
        self.active_jobs: dict[str, Job] = {}
        self.job_dependencies: dict[str, set[str]] = {}  # job_id -> set of dependency job_ids
        self.job_retries: dict[str, int] = {}  # job_id -> retry count
        # job_id -> next retry timestamp
        self.job_retry_times: dict[str, float] = {}
        # job_id -> progress (0.0 to 1.0)
        self.job_progress: dict[str, float] = {}
        # job_id -> metadata
        self.job_metadata: dict[str, dict[str, Any]] = {}

        # Threading
        self.lock = threading.Lock()
        self.running = True

        # Statistics
        self.stats = {
            "total_submitted": 0,
            "total_completed": 0,
            "total_failed": 0,
            "total_cancelled": 0,
            "total_retried": 0,
            "batches_created": 0,
            "batches_completed": 0,
        }

    def submit_job(
        self,
        job_id: str,
        engine_id: str,
        task: str,
        priority: JobPriority,
        requirements: ResourceRequirement,
        payload: dict[str, Any],
        callback: Callable | None = None,
        dependencies: list[str] | None = None,
        retry_policy: RetryPolicy | None = None,
        batch_id: str | None = None,
    ) -> bool:
        """
        Submit a job to the queue.

        Args:
            job_id: Unique job identifier
            engine_id: Engine to execute the job
            task: Task type
            priority: Job priority
            requirements: Resource requirements
            payload: Job payload
            callback: Optional callback function
            dependencies: List of job IDs that must complete first
            retry_policy: Retry policy for this job
            batch_id: Optional batch ID to group jobs

        Returns:
            True if job was accepted, False otherwise
        """
        with self.lock:
            # Create job
            job = Job(
                job_id=job_id,
                engine_id=engine_id,
                task=task,
                priority=priority,
                requirements=requirements,
                payload=payload,
                callback=callback,
            )

            # Store job
            self.jobs[job_id] = job

            # Set dependencies
            if dependencies:
                self.job_dependencies[job_id] = set(dependencies)

            # Set retry policy
            if retry_policy is None:
                retry_policy = self.default_retry_policy
            job.payload["_retry_policy"] = retry_policy.value

            # Add to batch if specified
            if batch_id and self.enable_batching:
                if batch_id not in self.job_batches:
                    batch = JobBatch(
                        batch_id=batch_id,
                        jobs=[],
                    )
                    self.job_batches[batch_id] = batch
                    self.pending_batches.append(batch)
                    self.stats["batches_created"] += 1

                self.job_batches[batch_id].jobs.append(job)
                logger.debug(f"Added job {job_id} to batch {batch_id}")
            else:
                # Queue job directly
                self._queue_job(job)

            self.stats["total_submitted"] += 1
            logger.info(f"Job {job_id} submitted (priority: {priority.name})")

            # Send WebSocket notification
            self._notify_websocket(
                "job.submitted",
                {
                    "job_id": job_id,
                    "status": "queued",
                    "priority": priority.name,
                    "engine_id": engine_id,
                    "task": task,
                },
            )

            return True

    def _queue_job(self, job: Job):
        """Queue a job based on priority."""
        priority = job.priority
        if priority == JobPriority.REALTIME:
            self.realtime_queue.put((priority.value, time.time(), job))
        elif priority == JobPriority.INTERACTIVE:
            self.interactive_queue.put((priority.value, time.time(), job))
        else:  # BATCH
            self.batch_queue.put((priority.value, time.time(), job))

    def get_next_job(self, check_dependencies: bool = True) -> Job | None:
        """
        Get next job to execute.

        Args:
            check_dependencies: Check if dependencies are met

        Returns:
            Next job or None if no jobs available
        """
        with self.lock:
            # Process batches first if enabled
            if self.enable_batching and self.pending_batches:
                for batch in self.pending_batches:
                    if batch.status == "pending":
                        # Check if batch is ready (all dependencies met)
                        ready_jobs = [
                            job
                            for job in batch.jobs
                            if not check_dependencies or self._check_dependencies(job.job_id)
                        ]
                        if ready_jobs:
                            # Return first ready job from batch
                            batch.status = "running"
                            batch.started_at = datetime.now()
                            job = ready_jobs[0]
                            self.active_jobs[job.job_id] = job
                            return job

            # Try queues in priority order
            queues = [
                (self.realtime_queue, JobPriority.REALTIME),
                (self.interactive_queue, JobPriority.INTERACTIVE),
                (self.batch_queue, JobPriority.BATCH),
            ]

            for queue, priority in queues:
                if not queue.empty():
                    try:
                        _, _, job = queue.get_nowait()

                        # Check dependencies
                        if check_dependencies and not self._check_dependencies(job.job_id):
                            # Dependencies not met, put back
                            queue.put((priority.value, time.time(), job))
                            continue

                        # Check retry timing
                        if job.job_id in self.job_retry_times:
                            if time.time() < self.job_retry_times[job.job_id]:
                                # Not ready for retry yet, put back
                                queue.put((priority.value, time.time(), job))
                                continue

                        # Use resource manager if available
                        if self.resource_manager:
                            # Check resource availability
                            gpu_monitor = self.resource_manager.gpu_monitor
                            if not gpu_monitor.has_sufficient_vram(
                                job.requirements.vram_gb,
                                self.resource_manager.vram_headroom_gb,
                            ):
                                # Insufficient resources, put back
                                queue.put((priority.value, time.time(), job))
                                continue

                        # Job is ready
                        self.active_jobs[job.job_id] = job
                        return cast(Job, job)

                    except Exception as e:
                        logger.error(f"Error getting job from queue: {e}")
                        continue

            return None

    def _check_dependencies(self, job_id: str) -> bool:
        """
        Check if job dependencies are met.

        Args:
            job_id: Job ID

        Returns:
            True if all dependencies are met, False otherwise
        """
        if job_id not in self.job_dependencies:
            return True  # No dependencies

        dependencies = self.job_dependencies[job_id]

        for dep_id in dependencies:
            if dep_id not in self.jobs:
                return False  # Dependency job not found

            dep_job = self.jobs[dep_id]
            if dep_job.status != JobStatus.COMPLETED:
                return False  # Dependency not completed

        return True  # All dependencies met

    def start_job(self, job: Job):
        """Mark job as started."""
        with self.lock:
            job.status = JobStatus.RUNNING
            job.started_at = datetime.now()
            self.active_jobs[job.job_id] = job
            self.job_progress[job.job_id] = 0.0

            # Use resource manager if available
            if self.resource_manager:
                self.resource_manager.start_job(job)

            logger.info(f"Job {job.job_id} started")

            # Send WebSocket notification
            self._notify_websocket(
                "job.started",
                {
                    "job_id": job.job_id,
                    "status": "running",
                    "started_at": job.started_at.isoformat(),
                    "progress": 0.0,
                },
            )

    def update_job_progress(
        self,
        job_id: str,
        progress: float,
        metadata: dict[str, Any] | None = None,
    ):
        """
        Update job progress.

        Args:
            job_id: Job ID
            progress: Progress value (0.0 to 1.0)
            metadata: Optional metadata to include
        """
        with self.lock:
            if job_id not in self.active_jobs:
                return

            self.job_progress[job_id] = max(0.0, min(1.0, progress))

            if metadata:
                if job_id not in self.job_metadata:
                    self.job_metadata[job_id] = {}
                self.job_metadata[job_id].update(metadata)

            # Send WebSocket notification
            self._notify_websocket(
                "job.progress",
                {
                    "job_id": job_id,
                    "progress": self.job_progress[job_id],
                    "metadata": self.job_metadata.get(job_id, {}),
                },
            )

    def complete_job(
        self,
        job_id: str,
        success: bool = True,
        error: str | None = None,
        result: dict[str, Any] | None = None,
    ):
        """
        Mark job as completed.

        Args:
            job_id: Job ID
            success: Whether job succeeded
            error: Error message if failed
            result: Optional result data
        """
        with self.lock:
            if job_id not in self.active_jobs:
                logger.warning(f"Job {job_id} not found in active jobs")
                return

            job = self.active_jobs.pop(job_id)
            job.completed_at = datetime.now()

            # Set progress to 100% on completion
            self.job_progress[job_id] = 1.0

            if success:
                job.status = JobStatus.COMPLETED
                self.stats["total_completed"] += 1

                # Check if job is part of a batch
                self._update_batch_status(job_id)

                # Use resource manager if available
                if self.resource_manager:
                    self.resource_manager.complete_job(job_id, success=True)

                # Send WebSocket notification
                self._notify_websocket(
                    "job.completed",
                    {
                        "job_id": job_id,
                        "status": "completed",
                        "completed_at": (job.completed_at.isoformat()),
                        "progress": 1.0,
                        "result": result or {},
                    },
                )

            else:
                # Check retry policy
                retry_policy_str = job.payload.get("_retry_policy", "none")
                retry_policy = RetryPolicy(retry_policy_str)

                retry_count = self.job_retries.get(job_id, 0)

                if retry_policy != RetryPolicy.NONE and retry_count < self.max_retries:
                    # Retry job
                    self._schedule_retry(job, retry_policy, retry_count)
                    return  # Don't mark as failed yet

                # No more retries, mark as failed
                job.status = JobStatus.FAILED
                job.error = error
                self.stats["total_failed"] += 1

                # Check if job is part of a batch
                self._update_batch_status(job_id)

                # Use resource manager if available
                if self.resource_manager:
                    self.resource_manager.complete_job(job_id, success=False, error=error)

                # Send WebSocket notification
                self._notify_websocket(
                    "job.failed",
                    {
                        "job_id": job_id,
                        "status": "failed",
                        "completed_at": (job.completed_at.isoformat()),
                        "error": error,
                        "retry_count": (self.job_retries.get(job_id, 0)),
                    },
                )

    def _schedule_retry(self, job: Job, retry_policy: RetryPolicy, retry_count: int):
        """Schedule job retry based on policy."""
        retry_count += 1
        self.job_retries[job.job_id] = retry_count
        self.stats["total_retried"] += 1

        # Calculate retry delay
        if retry_policy == RetryPolicy.IMMEDIATE:
            delay = 0.0
        elif retry_policy == RetryPolicy.EXPONENTIAL:
            delay = min(2**retry_count, 300)  # Max 5 minutes
        elif retry_policy == RetryPolicy.FIXED:
            delay = 5.0  # 5 seconds
        else:
            delay = 0.0

        # Schedule retry
        retry_time = time.time() + delay
        self.job_retry_times[job.job_id] = retry_time

        # Reset job status and requeue
        job.status = JobStatus.QUEUED
        job.started_at = None
        job.completed_at = None
        job.error = None

        # Requeue job
        self._queue_job(job)

        logger.info(
            f"Job {job.job_id} scheduled for retry "
            f"{retry_count}/{self.max_retries} "
            f"in {delay:.1f}s (policy: {retry_policy.value})"
        )

    def _update_batch_status(self, job_id: str):
        """Update batch status when job completes."""
        for batch in self.job_batches.values():
            if any(j.job_id == job_id for j in batch.jobs):
                # Update batch progress
                batch.total_jobs = len(batch.jobs)
                batch.completed_jobs = sum(1 for j in batch.jobs if j.status == JobStatus.COMPLETED)
                batch.failed_jobs = sum(1 for j in batch.jobs if j.status == JobStatus.FAILED)

                if batch.total_jobs > 0:
                    batch.progress = (batch.completed_jobs + batch.failed_jobs) / batch.total_jobs

                # Send batch progress notification
                self._notify_websocket(
                    "batch.progress",
                    {
                        "batch_id": batch.batch_id,
                        "progress": batch.progress,
                        "total_jobs": batch.total_jobs,
                        "completed_jobs": batch.completed_jobs,
                        "failed_jobs": batch.failed_jobs,
                    },
                )

                # Check if all jobs in batch are complete
                all_complete = all(
                    j.status
                    in [
                        JobStatus.COMPLETED,
                        JobStatus.FAILED,
                        JobStatus.CANCELLED,
                    ]
                    for j in batch.jobs
                )

                if all_complete:
                    batch.completed_at = datetime.now()
                    batch.status = "completed"
                    self.stats["batches_completed"] += 1
                    if batch in self.pending_batches:
                        self.pending_batches.remove(batch)
                    logger.info(f"Batch {batch.batch_id} completed")

                    # Send batch completion notification
                self._notify_websocket(
                    "batch.completed",
                    {
                        "batch_id": batch.batch_id,
                        "status": "completed",
                        "completed_at": (
                            batch.completed_at.isoformat() if batch.completed_at else None
                        ),
                        "total_jobs": batch.total_jobs,
                        "completed_jobs": batch.completed_jobs,
                        "failed_jobs": batch.failed_jobs,
                    },
                )

    def cancel_job(self, job_id: str) -> bool:
        """
        Cancel a job.

        Args:
            job_id: Job ID to cancel

        Returns:
            True if cancelled, False if not found
        """
        with self.lock:
            # Check active jobs
            if job_id in self.active_jobs:
                job = self.active_jobs[job_id]
                job.status = JobStatus.CANCELLED
                job.completed_at = datetime.now()
                job.error = "Cancelled by user"
                self.active_jobs.pop(job_id)
                self.stats["total_cancelled"] += 1

                # Use resource manager if available
                if self.resource_manager:
                    self.resource_manager.complete_job(
                        job_id, success=False, error="Cancelled by user"
                    )

                logger.info(f"Job {job_id} cancelled")

                # Send WebSocket notification
                self._notify_websocket(
                    "job.cancelled",
                    {
                        "job_id": job_id,
                        "status": "cancelled",
                        "cancelled_at": (
                            job.completed_at.isoformat()
                            if job.completed_at
                            else datetime.now().isoformat()
                        ),
                    },
                )

                return True

            # Check queued jobs (would need to search queues)
            if job_id in self.jobs:
                job = self.jobs[job_id]
                job.status = JobStatus.CANCELLED
                self.stats["total_cancelled"] += 1
                logger.info(f"Job {job_id} cancelled (was queued)")

                # Send WebSocket notification
                self._notify_websocket(
                    "job.cancelled",
                    {
                        "job_id": job_id,
                        "status": "cancelled",
                        "cancelled_at": datetime.now().isoformat(),
                    },
                )

                return True

            logger.warning(f"Job {job_id} not found for cancellation")
            return False

    def get_job_status(self, job_id: str) -> dict[str, Any] | None:
        """
        Get job status information.

        Args:
            job_id: Job ID

        Returns:
            Job status dictionary or None if not found
        """
        with self.lock:
            if job_id not in self.jobs:
                return None

            job = self.jobs[job_id]
            retry_count = self.job_retries.get(job_id, 0)

            status: dict[str, Any] = {
                "job_id": job_id,
                "status": job.status.value,
                "priority": job.priority.name,
                "engine_id": job.engine_id,
                "task": job.task,
                "created_at": (job.created_at.isoformat() if job.created_at else None),
                "started_at": (job.started_at.isoformat() if job.started_at else None),
                "completed_at": (job.completed_at.isoformat() if job.completed_at else None),
                "error": job.error,
                "retry_count": retry_count,
                "dependencies": list(self.job_dependencies.get(job_id, set())),
            }

            # Add retry info
            if job_id in self.job_retry_times:
                retry_time = self.job_retry_times[job_id]
                if time.time() < retry_time:
                    status["next_retry"] = datetime.fromtimestamp(retry_time).isoformat()

            # Add progress info
            if job_id in self.job_progress:
                status["progress"] = self.job_progress[job_id]

            # Add metadata
            if job_id in self.job_metadata:
                status["metadata"] = self.job_metadata[job_id]

            return status

    def _notify_websocket(self, event_type: str, data: dict[str, Any]):
        """
        Send WebSocket notification if notifier is available.

        Args:
            event_type: Event type (e.g., "job.completed", "job.progress")
            data: Event data
        """
        if self.websocket_notifier:
            try:
                self.websocket_notifier(event_type, data)
            except Exception as e:
                logger.warning(f"WebSocket notification failed: {e}")

    def get_queue_stats(self) -> dict[str, Any]:
        """
        Get queue statistics.

        Returns:
            Dictionary with queue statistics
        """
        with self.lock:
            return {
                "queued_jobs": {
                    "realtime": self.realtime_queue.qsize(),
                    "interactive": self.interactive_queue.qsize(),
                    "batch": self.batch_queue.qsize(),
                    "total": (
                        self.realtime_queue.qsize()
                        + self.interactive_queue.qsize()
                        + self.batch_queue.qsize()
                    ),
                },
                "active_jobs": len(self.active_jobs),
                "batches": {
                    "total": len(self.job_batches),
                    "pending": len(self.pending_batches),
                    "running": sum(1 for b in self.job_batches.values() if b.status == "running"),
                    "completed": sum(
                        1 for b in self.job_batches.values() if b.status == "completed"
                    ),
                },
                "statistics": self.stats.copy(),
            }

    def create_batch(self, batch_id: str, job_ids: list[str] | None = None) -> JobBatch:
        """
        Create a new job batch.

        Args:
            batch_id: Batch identifier
            job_ids: Optional list of job IDs to add to batch

        Returns:
            Created JobBatch
        """
        with self.lock:
            batch = JobBatch(batch_id=batch_id, jobs=[])
            self.job_batches[batch_id] = batch
            self.pending_batches.append(batch)
            self.stats["batches_created"] += 1

            # Add jobs to batch if specified
            if job_ids:
                for job_id in job_ids:
                    if job_id in self.jobs:
                        batch.jobs.append(self.jobs[job_id])

            logger.info(f"Created batch {batch_id} with {len(batch.jobs)} jobs")

            # Send batch creation notification
            self._notify_websocket(
                "batch.created",
                {
                    "batch_id": batch_id,
                    "total_jobs": len(batch.jobs),
                    "status": "pending",
                },
            )

            return batch


# Factory function
def create_enhanced_job_queue(
    resource_manager: ResourceManager | None = None,
    max_retries: int = 3,
    enable_batching: bool = True,
    websocket_notifier: WebSocketNotifier = None,
) -> EnhancedJobQueue:
    """
    Create enhanced job queue.

    Args:
        resource_manager: Resource manager instance
        max_retries: Maximum retry attempts
        enable_batching: Enable job batching
        websocket_notifier: Optional WebSocket notification callback

    Returns:
        EnhancedJobQueue instance
    """
    return EnhancedJobQueue(
        resource_manager=resource_manager,
        max_retries=max_retries,
        enable_batching=enable_batching,
        websocket_notifier=websocket_notifier,
    )


# Export
__all__ = [
    "EnhancedJobQueue",
    "JobBatch",
    "RetryPolicy",
    "create_enhanced_job_queue",
]
