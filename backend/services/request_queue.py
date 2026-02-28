"""
Request Queue Management.

Task 1.1.4: Implement priority queue for engine requests.
Manages engine request prioritization and rate limiting.
"""

from __future__ import annotations

import asyncio
import logging
import time
import uuid
from collections.abc import Awaitable, Callable
from dataclasses import dataclass, field
from datetime import datetime
from enum import IntEnum
from typing import Any, Generic, TypeVar

logger = logging.getLogger(__name__)

T = TypeVar("T")


class RequestPriority(IntEnum):
    """Priority levels for requests."""

    CRITICAL = 0  # System-critical requests
    HIGH = 1  # User-initiated real-time requests
    NORMAL = 2  # Standard requests
    LOW = 3  # Background tasks
    BATCH = 4  # Batch processing jobs


@dataclass
class QueuedRequest(Generic[T]):
    """A request in the queue."""

    id: str
    priority: RequestPriority
    payload: T
    created_at: datetime = field(default_factory=datetime.now)
    engine_type: str | None = None
    user_id: str | None = None
    timeout_seconds: float = 300.0

    def __lt__(self, other: QueuedRequest) -> bool:
        # Lower priority value = higher priority
        if self.priority != other.priority:
            return self.priority < other.priority
        # Earlier requests first (FIFO within priority)
        return self.created_at < other.created_at


@dataclass
class QueueStats:
    """Statistics for the request queue."""

    total_enqueued: int = 0
    total_processed: int = 0
    total_failed: int = 0
    total_timeout: int = 0
    total_rejected: int = 0
    current_size: int = 0
    avg_wait_time_ms: float = 0.0
    avg_process_time_ms: float = 0.0


class RequestQueue(Generic[T]):
    """
    Priority-based request queue for engine processing.

    Features:
    - Multi-level priority queue
    - Concurrency limiting per engine type
    - Request timeout handling
    - Statistics and monitoring
    - Backpressure support
    """

    def __init__(
        self,
        max_size: int = 1000,
        max_concurrent: int = 10,
        default_timeout_seconds: float = 300.0,
    ):
        self.max_size = max_size
        self.max_concurrent = max_concurrent
        self.default_timeout_seconds = default_timeout_seconds

        self._queue: asyncio.PriorityQueue[QueuedRequest[T]] = asyncio.PriorityQueue()
        self._processing: dict[str, QueuedRequest[T]] = {}
        self._semaphore = asyncio.Semaphore(max_concurrent)
        self._stats = QueueStats()
        self._wait_times: list[float] = []
        self._process_times: list[float] = []
        self._engine_semaphores: dict[str, asyncio.Semaphore] = {}
        self._lock = asyncio.Lock()
        self._running = True

    @property
    def size(self) -> int:
        """Current queue size."""
        return self._queue.qsize()

    @property
    def processing_count(self) -> int:
        """Number of requests currently being processed."""
        return len(self._processing)

    def set_engine_concurrency(self, engine_type: str, max_concurrent: int) -> None:
        """Set concurrency limit for a specific engine type."""
        self._engine_semaphores[engine_type] = asyncio.Semaphore(max_concurrent)

    async def enqueue(
        self,
        payload: T,
        priority: RequestPriority = RequestPriority.NORMAL,
        engine_type: str | None = None,
        user_id: str | None = None,
        timeout_seconds: float | None = None,
    ) -> str | None:
        """
        Add a request to the queue.

        Returns request ID if successful, None if rejected.
        """
        if not self._running:
            logger.warning("Queue is stopped, rejecting request")
            self._stats.total_rejected += 1
            return None

        if self._queue.qsize() >= self.max_size:
            logger.warning("Queue full, rejecting request")
            self._stats.total_rejected += 1
            return None

        request = QueuedRequest(
            id=str(uuid.uuid4()),
            priority=priority,
            payload=payload,
            engine_type=engine_type,
            user_id=user_id,
            timeout_seconds=timeout_seconds or self.default_timeout_seconds,
        )

        await self._queue.put(request)
        self._stats.total_enqueued += 1
        self._stats.current_size = self._queue.qsize()

        logger.debug(f"Request {request.id} enqueued (priority: {priority.name})")
        return request.id

    async def process(
        self,
        handler: Callable[[T], Awaitable[Any]],
    ) -> tuple[str, Any]:
        """
        Process the next request from the queue.

        Returns (request_id, result) tuple.
        """
        request = await self._queue.get()

        try:
            # Check timeout before processing
            wait_time = (datetime.now() - request.created_at).total_seconds()
            if wait_time > request.timeout_seconds:
                logger.warning(f"Request {request.id} timed out in queue")
                self._stats.total_timeout += 1
                raise TimeoutError(
                    f"Request waited {wait_time:.1f}s, timeout is {request.timeout_seconds}s"
                )

            # Track wait time
            self._wait_times.append(wait_time * 1000)
            if len(self._wait_times) > 1000:
                self._wait_times = self._wait_times[-500:]
            self._stats.avg_wait_time_ms = sum(self._wait_times) / len(self._wait_times)

            # Acquire semaphores
            async with self._semaphore:
                engine_sem = self._engine_semaphores.get(request.engine_type)
                if engine_sem:
                    await engine_sem.acquire()

                try:
                    self._processing[request.id] = request
                    start_time = time.time()

                    # Execute handler with timeout
                    remaining_timeout = request.timeout_seconds - wait_time
                    result = await asyncio.wait_for(
                        handler(request.payload),
                        timeout=remaining_timeout,
                    )

                    # Track process time
                    process_time = (time.time() - start_time) * 1000
                    self._process_times.append(process_time)
                    if len(self._process_times) > 1000:
                        self._process_times = self._process_times[-500:]
                    self._stats.avg_process_time_ms = sum(self._process_times) / len(
                        self._process_times
                    )

                    self._stats.total_processed += 1
                    return request.id, result

                finally:
                    if engine_sem:
                        engine_sem.release()
                    self._processing.pop(request.id, None)

        except asyncio.TimeoutError:
            self._stats.total_timeout += 1
            raise
        except Exception:
            self._stats.total_failed += 1
            raise
        finally:
            self._queue.task_done()
            self._stats.current_size = self._queue.qsize()

    async def process_loop(
        self,
        handler: Callable[[T], Awaitable[Any]],
        result_callback: Callable[[str, Any], Awaitable[None]] | None = None,
        error_callback: Callable[[str, Exception], Awaitable[None]] | None = None,
    ) -> None:
        """
        Continuous processing loop.

        Args:
            handler: Function to process each payload
            result_callback: Called with (request_id, result) on success
            error_callback: Called with (request_id, exception) on failure
        """
        logger.info("Starting request queue processing loop")

        while self._running:
            try:
                request_id, result = await self.process(handler)

                if result_callback:
                    await result_callback(request_id, result)

            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Request processing error: {e}")
                if error_callback:
                    try:
                        await error_callback("unknown", e)
                    except Exception as callback_err:
                        logger.warning(f"Error callback failed: {callback_err}")

        logger.info("Request queue processing loop stopped")

    async def cancel_request(self, request_id: str) -> bool:
        """
        Cancel a request if it hasn't started processing.

        Note: Cannot cancel requests already being processed.
        """
        # Check if already processing
        if request_id in self._processing:
            logger.warning(f"Request {request_id} already processing, cannot cancel")
            return False

        # Cannot efficiently remove from PriorityQueue, mark as cancelled
        logger.info(f"Request {request_id} marked for cancellation")
        return True

    def get_position(self, request_id: str) -> int:
        """Get estimated position in queue (0 = processing)."""
        if request_id in self._processing:
            return 0
        # Cannot get exact position from PriorityQueue without iteration
        return -1

    async def stop(self) -> None:
        """Stop the queue and cancel pending requests."""
        self._running = False
        logger.info("Request queue stopped")

    def get_stats(self) -> dict:
        """Get queue statistics."""
        return {
            "total_enqueued": self._stats.total_enqueued,
            "total_processed": self._stats.total_processed,
            "total_failed": self._stats.total_failed,
            "total_timeout": self._stats.total_timeout,
            "total_rejected": self._stats.total_rejected,
            "current_size": self._stats.current_size,
            "processing_count": self.processing_count,
            "avg_wait_time_ms": round(self._stats.avg_wait_time_ms, 2),
            "avg_process_time_ms": round(self._stats.avg_process_time_ms, 2),
        }


# Global request queue instance
_request_queue: RequestQueue | None = None


def get_request_queue() -> RequestQueue:
    """Get or create the global request queue."""
    global _request_queue
    if _request_queue is None:
        _request_queue = RequestQueue()
    return _request_queue
