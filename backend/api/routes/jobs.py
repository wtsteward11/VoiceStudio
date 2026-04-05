"""
Job Progress Routes

Unified job progress monitoring for all job types.
Supports batch jobs, training jobs, synthesis jobs, etc.

Backend-Frontend Integration Plan - Phase 1:
Migrated from in-memory storage to database-backed JobRepository.
"""

from __future__ import annotations

import logging
from datetime import datetime
from typing import Optional, cast

from fastapi import APIRouter, Depends, HTTPException, Query
from pydantic import BaseModel, ConfigDict, Field

from backend.data.repositories.job_repository import (
    JobEntity,
    JobRepository,
    get_job_repository,
)
from backend.data.repositories.job_repository import JobStatus as RepoJobStatus
from backend.data.repositories.job_repository import JobType as RepoJobType
from backend.data.repository_base import QueryOptions

from ..middleware.auth_middleware import require_auth_if_enabled
from ..optimization import cache_response, invalidate_api_response_cache

logger = logging.getLogger(__name__)


def _invalidate_jobs_cache() -> None:
    """Clear response cache so /api/jobs and related cached routes refresh."""
    try:
        invalidate_api_response_cache()
    except Exception as e:
        logger.debug("Jobs cache invalidation skipped: %s", e)

router = APIRouter(
    prefix="/api/jobs",
    tags=["jobs"],
    dependencies=[Depends(require_auth_if_enabled)],
)


def get_repo() -> JobRepository:
    """Dependency injection for JobRepository."""
    return cast(JobRepository, get_job_repository())


class JobType(str):
    """Job type enumeration."""

    BATCH = "batch"
    TRAINING = "training"
    SYNTHESIS = "synthesis"
    EXPORT = "export"
    IMPORT = "import"
    DOWNLOAD = "download"
    OTHER = "other"


class JobStatus(str):
    """Job status enumeration."""

    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"
    PAUSED = "paused"


class JobProgress(BaseModel):
    """Unified job progress information."""

    id: str
    name: str
    type: str  # JobType
    status: str  # JobStatus
    progress: float  # 0.0 to 1.0
    current_step: str | None = None
    total_steps: int | None = None
    current_step_index: int | None = None
    created: str  # ISO datetime string
    started: str | None = None
    completed: str | None = None
    estimated_time_remaining: int | None = None  # seconds
    error_message: str | None = None
    result_id: str | None = None
    metadata: dict = {}


class JobSummary(BaseModel):
    """Summary of all jobs."""

    total: int
    pending: int
    running: int
    completed: int
    failed: int
    cancelled: int
    by_type: dict[str, int] = {}


class JobInfo(BaseModel):
    """Individual job info for queue display."""

    model_config = ConfigDict(populate_by_name=True)

    job_id: str = Field(serialization_alias="JobId")
    job_type: str = Field(serialization_alias="JobType")
    status: str = Field(serialization_alias="Status")
    progress: float = Field(serialization_alias="Progress")
    start_time: str = Field(serialization_alias="StartTime")


class JobQueueResponse(BaseModel):
    """Job queue status response matching frontend expectations."""

    model_config = ConfigDict(populate_by_name=True)

    queued: int = Field(serialization_alias="Queued")
    running: int = Field(serialization_alias="Running")
    completed: int = Field(serialization_alias="Completed")
    failed: int = Field(serialization_alias="Failed")
    active_jobs: list[JobInfo] = Field(default=[], serialization_alias="ActiveJobs")


def _entity_to_progress(entity: JobEntity) -> JobProgress:
    """Convert JobEntity to JobProgress response model."""
    return JobProgress(
        id=entity.id,
        name=entity.name,
        type=entity.job_type,
        status=entity.status,
        progress=entity.progress,
        current_step=entity.current_step,
        total_steps=entity.total_steps,
        current_step_index=entity.current_step_index,
        created=(
            entity.created_at.isoformat()
            if isinstance(entity.created_at, datetime)
            else str(entity.created_at)
        ),
        started=entity.started_at,
        completed=entity.completed_at,
        estimated_time_remaining=entity.estimated_time_remaining,
        error_message=entity.error,
        result_id=entity.result_id,
        metadata=entity.get_metadata(),
    )


@router.get("", response_model=list[JobProgress])
@cache_response(ttl=5)  # Cache for 5 seconds (job status changes frequently)
async def get_jobs(
    job_type: str | None = Query(None),
    status: str | None = Query(None),
    limit: int = Query(100, ge=1, le=1000),
    repo: JobRepository = Depends(get_repo),
):
    """Get all jobs, optionally filtered."""
    filters = {}
    if job_type:
        filters["job_type"] = job_type
    if status:
        filters["status"] = status

    options = QueryOptions(
        limit=limit,
        order_by="created_at",
        order_desc=True,
    )

    if filters:
        entities = await repo.find(filters, options)
    else:
        entities = await repo.get_all(options)

    return [_entity_to_progress(e) for e in entities]


@router.get("/summary", response_model=JobSummary)
@cache_response(ttl=10)  # Cache for 10 seconds (summary updates frequently)
async def get_job_summary(
    repo: JobRepository = Depends(get_repo),
):
    """Get summary of all jobs."""
    summary = await repo.get_summary()
    by_type = await repo.get_by_type_count()

    return JobSummary(
        total=summary["total"],
        pending=summary["pending"],
        running=summary["running"],
        completed=summary["completed"],
        failed=summary["failed"],
        cancelled=summary["cancelled"],
        by_type=by_type,
    )


@router.get("/status", response_model=JobQueueResponse, response_model_by_alias=True)
@cache_response(ttl=5)  # Cache for 5 seconds
async def get_job_queue_status(
    repo: JobRepository = Depends(get_repo),
):
    """
    Get job queue status for DiagnosticsView dashboard.

    Returns counts and active job list matching frontend JobQueueResponse format.
    """
    summary = await repo.get_summary()
    active_entities = await repo.get_active_jobs()

    # Convert to JobInfo models
    active_jobs = []
    for entity in active_entities[:20]:  # Limit to 20 active jobs
        active_jobs.append(
            JobInfo(
                job_id=entity.id,
                job_type=entity.job_type,
                status=entity.status,
                progress=entity.progress,
                start_time=entity.started_at
                or (
                    entity.created_at.isoformat()
                    if isinstance(entity.created_at, datetime)
                    else str(entity.created_at)
                ),
            )
        )

    return JobQueueResponse(
        queued=summary["pending"],
        running=summary["running"],
        completed=summary["completed"],
        failed=summary["failed"],
        active_jobs=active_jobs,
    )


# GAP-API-001: Alias for frontend compatibility with JobGateway
@router.get("/queue/status", response_model=JobQueueResponse, response_model_by_alias=True)
@cache_response(ttl=5)
async def get_job_queue_status_alias(
    repo: JobRepository = Depends(get_repo),
):
    """Alias for /status to match frontend JobGateway expectations."""
    return await get_job_queue_status(repo)


@router.get("/{job_id}", response_model=JobProgress)
@cache_response(ttl=5)  # Cache for 5 seconds (job status changes frequently)
async def get_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Get a specific job."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    return _entity_to_progress(entity)


@router.post("/{job_id}/retry")
async def retry_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Retry a failed job."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    if entity.status != RepoJobStatus.FAILED.value:
        raise HTTPException(
            status_code=400,
            detail=f"Cannot retry job with status: {entity.status}. Only failed jobs can be retried.",
        )

    await repo.update(
        job_id,
        {
            "status": RepoJobStatus.PENDING.value,
            "error": None,
            "progress": 0.0,
            "started_at": None,
            "completed_at": None,
        },
    )
    logger.info(f"Job {job_id} queued for retry")
    _invalidate_jobs_cache()
    if entity.job_type == RepoJobType.DOWNLOAD.value:
        from backend.services.model_download_service import schedule_model_download

        schedule_model_download(job_id)
    return {"success": True, "message": f"Job {job_id} queued for retry"}


@router.post("/history/clear")
async def clear_job_history(
    repo: JobRepository = Depends(get_repo),
):
    """Clear completed and failed jobs from history."""
    cleared_count = await repo.clear_completed()
    logger.info(f"Cleared {cleared_count} jobs from history")
    _invalidate_jobs_cache()
    return {"success": True, "cleared_count": cleared_count}


@router.post("/{job_id}/cancel")
async def cancel_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Cancel a job."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    if entity.status in (
        RepoJobStatus.COMPLETED.value,
        RepoJobStatus.FAILED.value,
        RepoJobStatus.CANCELLED.value,
    ):
        raise HTTPException(
            status_code=400,
            detail=f"Cannot cancel job with status: {entity.status}",
        )

    await repo.mark_cancelled(job_id)
    _invalidate_jobs_cache()
    return {"success": True, "message": "Job cancelled"}


@router.post("/{job_id}/pause")
async def pause_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Pause a running job."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    if entity.status != RepoJobStatus.RUNNING.value:
        raise HTTPException(status_code=400, detail="Can only pause running jobs")

    await repo.update(job_id, {"status": RepoJobStatus.PAUSED.value})
    _invalidate_jobs_cache()
    return {"success": True, "message": "Job paused"}


@router.post("/{job_id}/resume")
async def resume_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Resume a paused job."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    if entity.status != RepoJobStatus.PAUSED.value:
        raise HTTPException(status_code=400, detail="Can only resume paused jobs")

    await repo.update(job_id, {"status": RepoJobStatus.RUNNING.value})
    _invalidate_jobs_cache()
    return {"success": True, "message": "Job resumed"}


@router.delete("/{job_id}")
async def delete_job(
    job_id: str,
    repo: JobRepository = Depends(get_repo),
):
    """Delete a job (only if completed, failed, or cancelled)."""
    entity = await repo.get_by_id(job_id)
    if not entity:
        raise HTTPException(status_code=404, detail="Job not found")

    if entity.status in (
        RepoJobStatus.PENDING.value,
        RepoJobStatus.RUNNING.value,
        RepoJobStatus.PAUSED.value,
    ):
        raise HTTPException(
            status_code=400,
            detail="Cannot delete active job. Cancel it first.",
        )

    await repo.delete(job_id, soft=True)
    _invalidate_jobs_cache()
    return {"success": True, "message": "Job deleted"}


@router.delete("")
async def clear_completed_jobs(
    repo: JobRepository = Depends(get_repo),
):
    """Clear all completed, failed, and cancelled jobs."""
    deleted_count = await repo.clear_completed()
    _invalidate_jobs_cache()
    return {"success": True, "deleted_count": deleted_count}


# === Helper function to create jobs (used by other modules) ===


async def create_job(
    job_id: str,
    job_type: str,
    name: str,
    metadata: dict | None = None,
    user_id: str | None = None,
) -> JobEntity:
    """
    Create a new job in the database.

    Called by other services to register new jobs.
    """
    repo = get_job_repository()

    entity = JobEntity(
        id=job_id,
        job_type=job_type,
        name=name,
        status=RepoJobStatus.PENDING.value,
        progress=0.0,
        metadata="{}" if metadata is None else __import__("json").dumps(metadata),
        user_id=user_id,
        created_at=datetime.now(),
        updated_at=datetime.now(),
    )

    created = cast(JobEntity, await repo.create(entity))
    _invalidate_jobs_cache()
    return created


async def update_job_progress(
    job_id: str,
    progress: float,
    current_step: str | None = None,
    current_step_index: int | None = None,
) -> JobEntity | None:
    """Update job progress (does not invalidate full API cache — TTL covers polling)."""
    repo = get_job_repository()
    return cast(
        Optional[JobEntity],
        await repo.update_progress(job_id, progress, current_step, current_step_index),
    )


async def complete_job(
    job_id: str,
    result_path: str | None = None,
    result_id: str | None = None,
) -> JobEntity | None:
    """Mark job as completed."""
    repo = get_job_repository()
    entity = cast(
        Optional[JobEntity],
        await repo.mark_completed(job_id, result_path, result_id),
    )
    _invalidate_jobs_cache()
    return entity


async def fail_job(
    job_id: str,
    error: str,
) -> JobEntity | None:
    """Mark job as failed."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_failed(job_id, error))
    _invalidate_jobs_cache()
    return entity


async def mark_job_running(job_id: str) -> JobEntity | None:
    """Mark a pending job as running (canonical store)."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_started(job_id))
    _invalidate_jobs_cache()
    return entity


async def cancel_canonical_job(job_id: str) -> JobEntity | None:
    """Mark a job cancelled in the canonical store (used by adapters e.g. batch)."""
    repo = get_job_repository()
    entity = cast(Optional[JobEntity], await repo.mark_cancelled(job_id))
    _invalidate_jobs_cache()
    return entity


async def soft_delete_canonical_job(job_id: str) -> bool:
    """Soft-delete a job row if present (e.g. when a domain-specific job is removed)."""
    repo = get_job_repository()
    existing = await repo.get_by_id(job_id)
    if not existing:
        return False
    await repo.delete(job_id, soft=True)
    _invalidate_jobs_cache()
    return True
