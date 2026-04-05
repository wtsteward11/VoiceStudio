"""
Job Repository.

Backend-Frontend Integration Plan - Phase 1.
Replaces in-memory _jobs dict in backend/api/routes/jobs.py.
"""

from __future__ import annotations

import json
import logging
from dataclasses import dataclass
from datetime import datetime
from enum import Enum
from typing import Any

from backend.data.repository_base import (
    BaseEntity,
    BaseRepository,
    ConnectionConfig,
    QueryOptions,
)

logger = logging.getLogger(__name__)


class JobStatus(str, Enum):
    """Job status enumeration."""

    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    PAUSED = "paused"


class JobType(str, Enum):
    """Job type enumeration."""

    BATCH = "batch"
    TRAINING = "training"
    SYNTHESIS = "synthesis"
    EXPORT = "export"
    IMPORT = "import"
    DOWNLOAD = "download"
    TRANSCRIPTION = "transcription"
    DEEPFAKE = "deepfake"
    OTHER = "other"


@dataclass
class JobEntity(BaseEntity):
    """
    Job entity for persistent storage.

    Maps to job_history table.
    """

    job_type: str = JobType.OTHER.value
    name: str = ""
    status: str = JobStatus.PENDING.value
    progress: float = 0.0
    current_step: str | None = None
    current_step_index: int | None = None
    total_steps: int | None = None
    error: str | None = None
    result_path: str | None = None
    result_id: str | None = None
    estimated_time_remaining: int | None = None
    metadata: str = "{}"  # JSON string
    user_id: str | None = None
    started_at: str | None = None
    completed_at: str | None = None

    def get_metadata(self) -> dict[str, Any]:
        """Parse metadata JSON."""
        try:
            return json.loads(self.metadata) if self.metadata else {}
        except json.JSONDecodeError:
            return {}

    def set_metadata(self, data: dict[str, Any]) -> None:
        """Set metadata as JSON string."""
        self.metadata = json.dumps(data)


class JobRepository(BaseRepository[JobEntity]):
    """
    Repository for job persistence.

    Replaces the in-memory _jobs dict with database-backed storage.
    """

    def __init__(self, config: ConnectionConfig | None = None):
        super().__init__(
            entity_type=JobEntity,
            table_name="job_history",
            config=config,
        )

    def _entity_to_dict(self, entity: JobEntity) -> dict[str, Any]:
        """Convert JobEntity to database row dict."""
        return {
            "id": entity.id,
            "job_type": entity.job_type,
            "name": getattr(entity, "name", ""),
            "status": entity.status,
            "progress": entity.progress,
            "current_step": entity.current_step,
            "current_step_index": entity.current_step_index,
            "total_steps": entity.total_steps,
            "error": entity.error,
            "result_path": entity.result_path,
            "result_id": entity.result_id,
            "estimated_time_remaining": entity.estimated_time_remaining,
            "metadata": entity.metadata,
            "user_id": entity.user_id,
            "created_at": (
                entity.created_at.isoformat()
                if isinstance(entity.created_at, datetime)
                else entity.created_at
            ),
            "updated_at": (
                entity.updated_at.isoformat()
                if isinstance(entity.updated_at, datetime)
                else entity.updated_at
            ),
            "started_at": entity.started_at,
            "completed_at": entity.completed_at,
            "deleted_at": entity.deleted_at.isoformat() if entity.deleted_at else None,
        }

    def _row_to_entity(self, row: dict[str, Any]) -> JobEntity:
        """Convert database row to JobEntity."""
        return JobEntity(
            id=row["id"],
            job_type=row.get("job_type", JobType.OTHER.value),
            name=row.get("name", ""),
            status=row.get("status", JobStatus.PENDING.value),
            progress=row.get("progress", 0.0),
            current_step=row.get("current_step"),
            current_step_index=row.get("current_step_index"),
            total_steps=row.get("total_steps"),
            error=row.get("error"),
            result_path=row.get("result_path"),
            result_id=row.get("result_id"),
            estimated_time_remaining=row.get("estimated_time_remaining"),
            metadata=row.get("metadata", "{}"),
            user_id=row.get("user_id"),
            created_at=(
                datetime.fromisoformat(row["created_at"])
                if row.get("created_at")
                else datetime.now()
            ),
            updated_at=(
                datetime.fromisoformat(row["updated_at"])
                if row.get("updated_at")
                else datetime.now()
            ),
            started_at=row.get("started_at"),
            completed_at=row.get("completed_at"),
            deleted_at=datetime.fromisoformat(row["deleted_at"]) if row.get("deleted_at") else None,
        )

    async def get_by_status(
        self,
        status: JobStatus,
        options: QueryOptions | None = None,
    ) -> list[JobEntity]:
        """Get jobs by status."""
        return await self.find({"status": status.value}, options)

    async def get_by_type(
        self,
        job_type: JobType,
        options: QueryOptions | None = None,
    ) -> list[JobEntity]:
        """Get jobs by type."""
        return await self.find({"job_type": job_type.value}, options)

    async def get_active_jobs(self) -> list[JobEntity]:
        """Get all active (pending, running, paused) jobs."""
        await self.connect()

        query = f"""
            SELECT * FROM {self.table_name}
            WHERE status IN ('pending', 'running', 'paused')
            AND deleted_at IS NULL
            ORDER BY created_at DESC
        """

        async with self._connection.execute(query) as cursor:
            rows = await cursor.fetchall()
            return [self._row_to_entity(dict(row)) for row in rows]

    async def get_completed_jobs(
        self,
        limit: int = 100,
    ) -> list[JobEntity]:
        """Get completed jobs (for history)."""
        return await self.find(
            {"status": JobStatus.COMPLETED.value},
            QueryOptions(limit=limit, order_by="completed_at", order_desc=True),
        )

    async def update_progress(
        self,
        job_id: str,
        progress: float,
        current_step: str | None = None,
        current_step_index: int | None = None,
    ) -> JobEntity | None:
        """Update job progress efficiently."""
        data: dict[str, Any] = {"progress": progress}
        if current_step is not None:
            data["current_step"] = current_step
        if current_step_index is not None:
            data["current_step_index"] = current_step_index
        return await self.update(job_id, data)

    async def mark_started(self, job_id: str) -> JobEntity | None:
        """Mark job as started."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.RUNNING.value,
                "started_at": datetime.now().isoformat(),
            },
        )

    async def mark_completed(
        self,
        job_id: str,
        result_path: str | None = None,
        result_id: str | None = None,
    ) -> JobEntity | None:
        """Mark job as completed."""
        data: dict[str, Any] = {
            "status": JobStatus.COMPLETED.value,
            "progress": 1.0,
            "completed_at": datetime.now().isoformat(),
        }
        if result_path:
            data["result_path"] = result_path
        if result_id:
            data["result_id"] = result_id
        return await self.update(job_id, data)

    async def mark_failed(
        self,
        job_id: str,
        error: str,
    ) -> JobEntity | None:
        """Mark job as failed."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.FAILED.value,
                "error": error,
                "completed_at": datetime.now().isoformat(),
            },
        )

    async def mark_cancelled(self, job_id: str) -> JobEntity | None:
        """Mark job as cancelled."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.CANCELLED.value,
                "completed_at": datetime.now().isoformat(),
            },
        )

    async def get_summary(self) -> dict[str, Any]:
        """Get job summary statistics."""
        await self.connect()

        query = f"""
            SELECT
                COUNT(*) as total,
                SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) as pending,
                SUM(CASE WHEN status = 'running' THEN 1 ELSE 0 END) as running,
                SUM(CASE WHEN status = 'completed' THEN 1 ELSE 0 END) as completed,
                SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) as failed,
                SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END) as cancelled
            FROM {self.table_name}
            WHERE deleted_at IS NULL
        """

        async with self._connection.execute(query) as cursor:
            row = await cursor.fetchone()
            if row:
                return {
                    "total": row[0] or 0,
                    "pending": row[1] or 0,
                    "running": row[2] or 0,
                    "completed": row[3] or 0,
                    "failed": row[4] or 0,
                    "cancelled": row[5] or 0,
                }

        return {"total": 0, "pending": 0, "running": 0, "completed": 0, "failed": 0, "cancelled": 0}

    async def get_by_type_count(self) -> dict[str, int]:
        """Get count of jobs by type."""
        await self.connect()

        query = f"""
            SELECT job_type, COUNT(*) as count
            FROM {self.table_name}
            WHERE deleted_at IS NULL
            GROUP BY job_type
        """

        async with self._connection.execute(query) as cursor:
            rows = await cursor.fetchall()
            return {row[0]: row[1] for row in rows}

    async def clear_completed(self) -> int:
        """Clear completed, failed, and cancelled jobs. Returns count deleted."""
        await self.connect()

        # Soft delete completed jobs
        query = f"""
            UPDATE {self.table_name}
            SET deleted_at = ?
            WHERE status IN ('completed', 'failed', 'cancelled')
            AND deleted_at IS NULL
        """

        await self._connection.execute(query, (datetime.now().isoformat(),))
        await self._connection.commit()

        # Get count of affected rows
        return int(self._connection.total_changes)


# In-memory fallback repository for graceful degradation
class InMemoryJobRepository:
    """
    In-memory fallback repository for when database is unavailable.

    Provides the same interface as JobRepository but stores data in memory.
    Used for graceful degradation when database connection fails.
    """

    def __init__(self):
        self._jobs: dict[str, JobEntity] = {}
        self._is_fallback = True
        logger.info("Using InMemoryJobRepository fallback (database unavailable)")

    def _active_jobs(self) -> list[JobEntity]:
        """Jobs not soft-deleted."""
        return [j for j in self._jobs.values() if j.deleted_at is None]

    async def find_all(self, options: QueryOptions | None = None) -> list[JobEntity]:
        """Get all jobs (excluding soft-deleted)."""
        jobs = self._active_jobs()
        if options:
            if options.order_by:
                reverse = options.order_desc if options.order_desc else False
                jobs.sort(key=lambda j: getattr(j, options.order_by or "", ""), reverse=reverse)
            if options.limit:
                jobs = jobs[: options.limit]
        return jobs

    async def get_all(self, options: QueryOptions | None = None) -> list[JobEntity]:
        """Get all jobs (alias for find_all for route compatibility)."""
        return await self.find_all(options)

    async def find(
        self, filters: dict[str, Any], options: QueryOptions | None = None
    ) -> list[JobEntity]:
        """Find jobs matching filters (excluding soft-deleted)."""
        jobs = [
            job
            for job in self._active_jobs()
            if all(getattr(job, k, None) == v for k, v in filters.items())
        ]
        if options and options.limit:
            jobs = jobs[: options.limit]
        return jobs

    async def get_by_id(self, job_id: str) -> JobEntity | None:
        """Get job by ID (returns None if soft-deleted)."""
        job = self._jobs.get(job_id)
        return job if job and job.deleted_at is None else None

    async def create(self, entity: JobEntity) -> JobEntity:
        """Create a new job."""
        self._jobs[entity.id] = entity
        return entity

    async def update(self, job_id: str, data: dict[str, Any]) -> JobEntity | None:
        """Update a job."""
        if job_id not in self._jobs:
            return None
        job = self._jobs[job_id]
        for key, value in data.items():
            if hasattr(job, key):
                setattr(job, key, value)
        job.updated_at = datetime.now()
        return job

    async def delete(self, job_id: str, soft: bool = True) -> bool:
        """Delete a job (soft or hard)."""
        if job_id not in self._jobs:
            return False
        if soft:
            job = self._jobs[job_id]
            job.deleted_at = datetime.now()
            return True
        del self._jobs[job_id]
        return True

    async def get_by_status(
        self, status: JobStatus, options: QueryOptions | None = None
    ) -> list[JobEntity]:
        """Get jobs by status."""
        return await self.find({"status": status.value}, options)

    async def get_by_type(
        self, job_type: JobType, options: QueryOptions | None = None
    ) -> list[JobEntity]:
        """Get jobs by type."""
        return await self.find({"job_type": job_type.value}, options)

    async def get_active_jobs(self) -> list[JobEntity]:
        """Get all active jobs."""
        return [
            job
            for job in self._jobs.values()
            if job.status
            in (JobStatus.PENDING.value, JobStatus.RUNNING.value, JobStatus.PAUSED.value)
            and job.deleted_at is None
        ]

    async def get_completed_jobs(self, limit: int = 100) -> list[JobEntity]:
        """Get completed jobs."""
        jobs = [job for job in self._jobs.values() if job.status == JobStatus.COMPLETED.value]
        jobs.sort(key=lambda j: j.completed_at or "", reverse=True)
        return jobs[:limit]

    async def update_progress(
        self,
        job_id: str,
        progress: float,
        current_step: str | None = None,
        current_step_index: int | None = None,
    ) -> JobEntity | None:
        """Update job progress."""
        data: dict[str, Any] = {"progress": progress}
        if current_step is not None:
            data["current_step"] = current_step
        if current_step_index is not None:
            data["current_step_index"] = current_step_index
        return await self.update(job_id, data)

    async def mark_started(self, job_id: str) -> JobEntity | None:
        """Mark job as started."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.RUNNING.value,
                "started_at": datetime.now().isoformat(),
            },
        )

    async def mark_completed(
        self,
        job_id: str,
        result_path: str | None = None,
        result_id: str | None = None,
    ) -> JobEntity | None:
        """Mark job as completed."""
        data = {
            "status": JobStatus.COMPLETED.value,
            "progress": 1.0,
            "completed_at": datetime.now().isoformat(),
        }
        if result_path:
            data["result_path"] = result_path
        if result_id:
            data["result_id"] = result_id
        return await self.update(job_id, data)

    async def mark_failed(self, job_id: str, error: str) -> JobEntity | None:
        """Mark job as failed."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.FAILED.value,
                "error": error,
                "completed_at": datetime.now().isoformat(),
            },
        )

    async def mark_cancelled(self, job_id: str) -> JobEntity | None:
        """Mark job as cancelled."""
        return await self.update(
            job_id,
            {
                "status": JobStatus.CANCELLED.value,
                "completed_at": datetime.now().isoformat(),
            },
        )

    async def get_summary(self) -> dict[str, Any]:
        """Get job summary statistics."""
        active_jobs = [j for j in self._jobs.values() if j.deleted_at is None]
        return {
            "total": len(active_jobs),
            "pending": sum(1 for j in active_jobs if j.status == JobStatus.PENDING.value),
            "running": sum(1 for j in active_jobs if j.status == JobStatus.RUNNING.value),
            "completed": sum(1 for j in active_jobs if j.status == JobStatus.COMPLETED.value),
            "failed": sum(1 for j in active_jobs if j.status == JobStatus.FAILED.value),
            "cancelled": sum(1 for j in active_jobs if j.status == JobStatus.CANCELLED.value),
        }

    async def get_by_type_count(self) -> dict[str, int]:
        """Get count of jobs by type."""
        counts: dict[str, int] = {}
        for job in self._jobs.values():
            if job.deleted_at is None:
                counts[job.job_type] = counts.get(job.job_type, 0) + 1
        return counts

    async def clear_completed(self) -> int:
        """Clear completed jobs."""
        to_delete = [
            job_id
            for job_id, job in self._jobs.items()
            if job.status
            in (JobStatus.COMPLETED.value, JobStatus.FAILED.value, JobStatus.CANCELLED.value)
        ]
        for job_id in to_delete:
            del self._jobs[job_id]
        return len(to_delete)

    async def connect(self) -> None:
        """No-op for in-memory repository."""
        pass

    async def disconnect(self) -> None:
        """No-op for in-memory repository."""
        pass


# Singleton instance for dependency injection
_job_repository: Any | None = None  # Can be JobRepository or InMemoryJobRepository
_job_repository_init_attempted: bool = False


def get_job_repository() -> Any:
    """
    Get or create JobRepository singleton with graceful fallback.

    If database initialization fails, falls back to InMemoryJobRepository
    to allow the API to function without database persistence.
    """
    global _job_repository, _job_repository_init_attempted

    if _job_repository is not None:
        return _job_repository

    if _job_repository_init_attempted:
        # Already tried and failed, return in-memory fallback
        _job_repository = InMemoryJobRepository()
        return _job_repository

    _job_repository_init_attempted = True

    try:
        repo = JobRepository()
        # Test connection by trying to connect
        import asyncio

        try:
            loop = asyncio.get_event_loop()
            if loop.is_running():
                # Can't await in sync context with running loop, use fallback
                logger.debug("Event loop running, using lazy database validation")
                _job_repository = repo
            else:
                loop.run_until_complete(repo.connect())
                _job_repository = repo
                logger.info("JobRepository initialized successfully with database")
        except RuntimeError:
            # No event loop, create one for initialization
            asyncio.run(repo.connect())
            _job_repository = repo
            logger.info("JobRepository initialized successfully with database")
    except Exception as e:
        logger.warning(f"JobRepository database init failed, using in-memory fallback: {e}")
        _job_repository = InMemoryJobRepository()

    return _job_repository


def reset_job_repository() -> None:
    """Reset the repository singleton (for testing)."""
    global _job_repository, _job_repository_init_attempted
    _job_repository = None
    _job_repository_init_attempted = False
