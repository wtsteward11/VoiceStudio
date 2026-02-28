"""
Batch Processing Service

Phase 13.2: Batch Optimization
Optimized batch processing for large-scale synthesis jobs.

Features:
- Queue-based job management
- Priority scheduling
- Parallel processing
- Progress tracking
- Result aggregation
"""

from __future__ import annotations

import asyncio
import contextlib
import logging
import time
import uuid
from collections.abc import Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import Enum
from pathlib import Path
from typing import Any

logger = logging.getLogger(__name__)


class JobStatus(Enum):
    """Batch job status."""

    PENDING = "pending"
    QUEUED = "queued"
    RUNNING = "running"
    PAUSED = "paused"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


class JobPriority(Enum):
    """Job priority levels."""

    LOW = 0
    NORMAL = 1
    HIGH = 2
    URGENT = 3


@dataclass
class BatchItem:
    """Individual item in a batch job."""

    item_id: str
    text: str
    voice_id: str
    options: dict[str, Any] = field(default_factory=dict)
    status: JobStatus = JobStatus.PENDING
    output_path: str | None = None
    error_message: str | None = None
    processing_time_ms: float = 0

    def to_dict(self) -> dict[str, Any]:
        return {
            "item_id": self.item_id,
            "text": self.text[:50] + "..." if len(self.text) > 50 else self.text,
            "voice_id": self.voice_id,
            "status": self.status.value,
            "output_path": self.output_path,
            "error_message": self.error_message,
            "processing_time_ms": self.processing_time_ms,
        }


@dataclass
class BatchJob:
    """Batch synthesis job."""

    job_id: str
    name: str
    items: list[BatchItem]
    priority: JobPriority
    status: JobStatus
    created_at: datetime
    started_at: datetime | None = None
    completed_at: datetime | None = None
    output_directory: str | None = None
    max_concurrent: int = 4
    retry_failed: bool = True
    max_retries: int = 3
    metadata: dict[str, Any] = field(default_factory=dict)

    @property
    def total_items(self) -> int:
        return len(self.items)

    @property
    def completed_items(self) -> int:
        return sum(1 for item in self.items if item.status == JobStatus.COMPLETED)

    @property
    def failed_items(self) -> int:
        return sum(1 for item in self.items if item.status == JobStatus.FAILED)

    @property
    def progress(self) -> float:
        if self.total_items == 0:
            return 0.0
        return (self.completed_items + self.failed_items) / self.total_items

    @property
    def total_processing_time_ms(self) -> float:
        return sum(item.processing_time_ms for item in self.items)

    def to_dict(self) -> dict[str, Any]:
        return {
            "job_id": self.job_id,
            "name": self.name,
            "priority": self.priority.value,
            "status": self.status.value,
            "total_items": self.total_items,
            "completed_items": self.completed_items,
            "failed_items": self.failed_items,
            "progress": self.progress,
            "created_at": self.created_at.isoformat(),
            "started_at": self.started_at.isoformat() if self.started_at else None,
            "completed_at": self.completed_at.isoformat() if self.completed_at else None,
            "output_directory": self.output_directory,
            "total_processing_time_ms": self.total_processing_time_ms,
        }


@dataclass
class BatchResult:
    """Result of batch processing."""

    job_id: str
    success: bool
    total_items: int
    completed_items: int
    failed_items: int
    total_time_seconds: float
    average_time_per_item_ms: float
    output_directory: str | None
    errors: list[dict[str, str]]

    def to_dict(self) -> dict[str, Any]:
        return {
            "job_id": self.job_id,
            "success": self.success,
            "total_items": self.total_items,
            "completed_items": self.completed_items,
            "failed_items": self.failed_items,
            "total_time_seconds": self.total_time_seconds,
            "average_time_per_item_ms": self.average_time_per_item_ms,
            "output_directory": self.output_directory,
            "errors": self.errors,
        }


# Type for synthesis callback
SynthesisCallback = Callable[[str, str, dict[str, Any]], bytes]


class BatchProcessor:
    """
    Batch processing service.

    Phase 13.2: Batch Optimization

    Features:
    - Priority queue management
    - Concurrent processing with limits
    - Progress callbacks
    - Error handling and retries
    """

    def __init__(
        self,
        max_concurrent_jobs: int = 2,
        max_concurrent_items: int = 4,
    ):
        self._max_concurrent_jobs = max_concurrent_jobs
        self._max_concurrent_items = max_concurrent_items
        self._jobs: dict[str, BatchJob] = {}
        self._job_queue: asyncio.PriorityQueue = asyncio.PriorityQueue()
        self._active_jobs: dict[str, asyncio.Task] = {}
        self._synthesizer: SynthesisCallback | None = None
        self._initialized = False
        self._processing_task: asyncio.Task | None = None
        self._progress_callbacks: list[Callable[[str, float], None]] = []

        logger.info("BatchProcessor created")

    async def initialize(
        self,
        synthesizer: SynthesisCallback,
    ) -> bool:
        """Initialize the batch processor."""
        if self._initialized:
            return True

        self._synthesizer = synthesizer
        self._processing_task = asyncio.create_task(self._process_queue())
        self._initialized = True

        logger.info("BatchProcessor initialized")
        return True

    async def create_job(
        self,
        name: str,
        items: list[dict[str, Any]],
        priority: JobPriority = JobPriority.NORMAL,
        output_directory: str | None = None,
        max_concurrent: int = 4,
    ) -> BatchJob:
        """
        Create a new batch job.

        Args:
            name: Job name
            items: List of items with 'text', 'voice_id', and optional 'options'
            priority: Job priority
            output_directory: Directory for output files
            max_concurrent: Maximum concurrent items

        Returns:
            Created BatchJob
        """
        job_id = f"batch_{uuid.uuid4().hex[:8]}"

        # Create batch items
        batch_items = []
        for i, item in enumerate(items):
            batch_items.append(
                BatchItem(
                    item_id=f"{job_id}_item_{i}",
                    text=item.get("text", ""),
                    voice_id=item.get("voice_id", ""),
                    options=item.get("options", {}),
                )
            )

        job = BatchJob(
            job_id=job_id,
            name=name,
            items=batch_items,
            priority=priority,
            status=JobStatus.PENDING,
            created_at=datetime.now(),
            output_directory=output_directory,
            max_concurrent=min(max_concurrent, self._max_concurrent_items),
        )

        self._jobs[job_id] = job
        logger.info(f"Created batch job: {job_id} with {len(batch_items)} items")

        return job

    async def submit_job(self, job_id: str) -> bool:
        """Submit a job for processing."""
        if job_id not in self._jobs:
            return False

        job = self._jobs[job_id]

        if job.status != JobStatus.PENDING:
            logger.warning(f"Job {job_id} is not in PENDING state")
            return False

        job.status = JobStatus.QUEUED

        # Add to priority queue (negative priority for max-heap behavior)
        await self._job_queue.put((-job.priority.value, time.time(), job_id))

        logger.info(f"Submitted job {job_id} to queue")
        return True

    async def cancel_job(self, job_id: str) -> bool:
        """Cancel a job."""
        if job_id not in self._jobs:
            return False

        job = self._jobs[job_id]

        if job.status in (JobStatus.COMPLETED, JobStatus.CANCELLED):
            return False

        # Cancel active task if running
        if job_id in self._active_jobs:
            self._active_jobs[job_id].cancel()

        job.status = JobStatus.CANCELLED
        logger.info(f"Cancelled job {job_id}")

        return True

    async def pause_job(self, job_id: str) -> bool:
        """Pause a running job."""
        if job_id not in self._jobs:
            return False

        job = self._jobs[job_id]

        if job.status != JobStatus.RUNNING:
            return False

        job.status = JobStatus.PAUSED
        logger.info(f"Paused job {job_id}")

        return True

    async def resume_job(self, job_id: str) -> bool:
        """Resume a paused job."""
        if job_id not in self._jobs:
            return False

        job = self._jobs[job_id]

        if job.status != JobStatus.PAUSED:
            return False

        job.status = JobStatus.QUEUED
        await self._job_queue.put((-job.priority.value, time.time(), job_id))

        logger.info(f"Resumed job {job_id}")
        return True

    def get_job(self, job_id: str) -> BatchJob | None:
        """Get a job by ID."""
        return self._jobs.get(job_id)

    def list_jobs(
        self,
        status: JobStatus | None = None,
    ) -> list[BatchJob]:
        """List jobs with optional status filter."""
        jobs = list(self._jobs.values())
        if status:
            jobs = [j for j in jobs if j.status == status]
        return jobs

    def get_job_items(
        self,
        job_id: str,
        status: JobStatus | None = None,
    ) -> list[BatchItem]:
        """Get items from a job."""
        job = self._jobs.get(job_id)
        if not job:
            return []

        items = job.items
        if status:
            items = [i for i in items if i.status == status]

        return items

    def add_progress_callback(self, callback: Callable[[str, float], None]):
        """Add a progress callback."""
        self._progress_callbacks.append(callback)

    def remove_progress_callback(self, callback: Callable[[str, float], None]):
        """Remove a progress callback."""
        if callback in self._progress_callbacks:
            self._progress_callbacks.remove(callback)

    async def _process_queue(self):
        """Main queue processing loop."""
        while True:
            try:
                # Check if we can start more jobs
                while len(self._active_jobs) < self._max_concurrent_jobs:
                    try:
                        # Get next job from queue (with timeout)
                        _priority, _timestamp, job_id = await asyncio.wait_for(
                            self._job_queue.get(),
                            timeout=1.0,
                        )
                    except asyncio.TimeoutError:
                        break

                    if job_id not in self._jobs:
                        continue

                    job = self._jobs[job_id]

                    if job.status != JobStatus.QUEUED:
                        continue

                    # Start processing job
                    task = asyncio.create_task(self._process_job(job_id))
                    self._active_jobs[job_id] = task

                # Clean up completed tasks
                completed = [job_id for job_id, task in self._active_jobs.items() if task.done()]
                for job_id in completed:
                    del self._active_jobs[job_id]

                await asyncio.sleep(0.1)

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Queue processing error: {e}")
                await asyncio.sleep(1.0)

    async def _process_job(self, job_id: str):
        """Process a batch job."""
        job = self._jobs.get(job_id)
        if not job:
            return

        job.status = JobStatus.RUNNING
        job.started_at = datetime.now()

        logger.info(f"Started processing job {job_id}")

        try:
            # Get pending items
            pending_items = [item for item in job.items if item.status == JobStatus.PENDING]

            # Process items concurrently with semaphore
            semaphore = asyncio.Semaphore(job.max_concurrent)

            async def process_with_limit(item: BatchItem):
                async with semaphore:
                    await self._process_item(job, item)

            tasks = [process_with_limit(item) for item in pending_items]
            await asyncio.gather(*tasks, return_exceptions=True)

            # Update job status
            if all(item.status == JobStatus.COMPLETED for item in job.items):
                job.status = JobStatus.COMPLETED
            elif any(item.status == JobStatus.FAILED for item in job.items):
                job.status = JobStatus.COMPLETED  # Completed with some failures

            job.completed_at = datetime.now()

            logger.info(
                f"Job {job_id} completed: {job.completed_items}/{job.total_items} succeeded"
            )

        except asyncio.CancelledError:
            job.status = JobStatus.CANCELLED
            logger.info(f"Job {job_id} was cancelled")

        except Exception as e:
            job.status = JobStatus.FAILED
            logger.error(f"Job {job_id} failed: {e}")

    async def _process_item(self, job: BatchJob, item: BatchItem):
        """Process a single batch item."""
        if job.status == JobStatus.PAUSED:
            return

        item.status = JobStatus.RUNNING
        start_time = time.perf_counter()

        retries = 0
        while retries <= job.max_retries:
            try:
                # Call synthesizer
                if self._synthesizer:
                    audio_data = self._synthesizer(
                        item.text,
                        item.voice_id,
                        item.options,
                    )

                    # Save output
                    if job.output_directory:
                        output_path = Path(job.output_directory) / f"{item.item_id}.wav"
                        output_path.parent.mkdir(parents=True, exist_ok=True)

                        with open(output_path, "wb") as f:
                            f.write(audio_data)

                        item.output_path = str(output_path)

                item.status = JobStatus.COMPLETED
                item.processing_time_ms = (time.perf_counter() - start_time) * 1000

                # Notify progress
                self._notify_progress(job)

                return

            except Exception as e:
                retries += 1
                if retries > job.max_retries:
                    item.status = JobStatus.FAILED
                    item.error_message = str(e)
                    item.processing_time_ms = (time.perf_counter() - start_time) * 1000
                    logger.warning(f"Item {item.item_id} failed after {retries} retries: {e}")
                else:
                    await asyncio.sleep(0.5 * retries)  # Exponential backoff

    def _notify_progress(self, job: BatchJob):
        """Notify progress callbacks."""
        for callback in self._progress_callbacks:
            try:
                callback(job.job_id, job.progress)
            except Exception as e:
                logger.warning(f"Progress callback error: {e}")

    async def get_job_result(self, job_id: str) -> BatchResult | None:
        """Get the result of a completed job."""
        job = self._jobs.get(job_id)
        if not job or job.status not in (JobStatus.COMPLETED, JobStatus.FAILED):
            return None

        errors = [
            {"item_id": item.item_id, "error": item.error_message or "Unknown"}
            for item in job.items
            if item.status == JobStatus.FAILED
        ]

        total_time = 0.0
        if job.started_at and job.completed_at:
            total_time = (job.completed_at - job.started_at).total_seconds()

        avg_time = 0.0
        if job.completed_items > 0:
            avg_time = job.total_processing_time_ms / job.completed_items

        return BatchResult(
            job_id=job_id,
            success=job.failed_items == 0,
            total_items=job.total_items,
            completed_items=job.completed_items,
            failed_items=job.failed_items,
            total_time_seconds=total_time,
            average_time_per_item_ms=avg_time,
            output_directory=job.output_directory,
            errors=errors,
        )

    async def cleanup(self):
        """Cleanup resources."""
        # Cancel processing task
        if self._processing_task:
            self._processing_task.cancel()
            with contextlib.suppress(asyncio.CancelledError):
                await self._processing_task

        # Cancel active jobs
        for task in self._active_jobs.values():
            task.cancel()

        self._jobs.clear()
        self._active_jobs.clear()

        logger.info("BatchProcessor cleaned up")


# Singleton instance
_batch_processor: BatchProcessor | None = None


def get_batch_processor() -> BatchProcessor:
    """Get or create the batch processor singleton."""
    global _batch_processor
    if _batch_processor is None:
        _batch_processor = BatchProcessor()
    return _batch_processor
